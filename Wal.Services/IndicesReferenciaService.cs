using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis;
using Wal.Services.Dtos;

namespace Wal.Services;

public class IndicesReferenciaService(
    AppDbContext db,
    IBancoCentralClient bancoCentral,
    IYahooFinanceClient yahoo,
    ILogger<IndicesReferenciaService> logger) : IIndicesReferenciaService
{
    /// <summary>Mesmo início da carga histórica dos ativos.</summary>
    public static readonly DateOnly InicioHistorico = new(2020, 1, 1);

    // Códigos das séries no SGS do Banco Central
    private const int SgsCdi = 12;
    private const int SgsIpca = 433;
    private const int SgsPtaxVenda = 1;

    public async Task SincronizarAsync(CancellationToken cancellationToken = default)
    {
        foreach (var indice in Enum.GetValues<IndiceReferencia>())
        {
            try
            {
                var valores = await BaixarAsync(indice, cancellationToken);
                var (inseridos, atualizados) = await GravarAsync(indice, valores, cancellationToken);
                logger.LogInformation("Índice {Indice}: +{Inseridos} / ~{Atualizados} ({Total} baixados).",
                    indice, inseridos, atualizados, valores.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha ao sincronizar o índice {Indice}; seguindo com os demais.", indice);
            }
        }
    }

    public async Task<List<PontoBenchmarkResponse>> ObterRentabilidadeAcumuladaAsync(
        Benchmark benchmark, DateOnly inicio, DateOnly fim, decimal taxaRealAnual = 0)
    {
        var indices = CalculoBenchmark.IndicesNecessarios(benchmark);
        // Um mês antes: o IPCA vem no 1º dia do mês, e os índices de preço precisam do último valor antes do início
        var desde = new DateOnly(inicio.Year, inicio.Month, 1).AddMonths(-1);
        var valores = await db.CotacoesIndices.AsNoTracking()
            .Where(c => indices.Contains(c.Indice) && c.Data >= desde && c.Data <= fim)
            .ToListAsync();

        var series = valores.GroupBy(v => v.Indice).ToDictionary(g => g.Key, g => g.ToList());
        return CalculoBenchmark.Acumular(benchmark, inicio, fim, series, taxaRealAnual);
    }

    public async Task<List<SituacaoIndiceResponse>> ObterSituacaoAsync()
    {
        var resumo = await db.CotacoesIndices.AsNoTracking()
            .GroupBy(c => c.Indice)
            .Select(g => new { Indice = g.Key, Primeira = g.Min(c => c.Data), Ultima = g.Max(c => c.Data), Quantidade = g.Count() })
            .ToListAsync();

        var situacao = new List<SituacaoIndiceResponse>();
        foreach (var indice in Enum.GetValues<IndiceReferencia>())
        {
            var r = resumo.FirstOrDefault(x => x.Indice == indice);
            decimal? ultimoValor = r is null
                ? null
                : await db.CotacoesIndices.AsNoTracking()
                    .Where(c => c.Indice == indice && c.Data == r.Ultima)
                    .Select(c => (decimal?)c.Valor)
                    .FirstOrDefaultAsync();
            situacao.Add(new SituacaoIndiceResponse(indice, r?.Primeira, r?.Ultima, ultimoValor, r?.Quantidade ?? 0));
        }
        return situacao;
    }

    // Sempre o histórico inteiro: são poucas requisições, e o preço ajustado do DIVO11 muda para trás
    // a cada provento — baixar só os dias recentes deixaria um degrau na série.
    private async Task<List<(DateOnly Data, decimal Valor)>> BaixarAsync(IndiceReferencia indice, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var period1 = new DateTimeOffset(InicioHistorico.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

        async Task<List<(DateOnly, decimal)>> Sgs(int codigo, decimal divisor) =>
            (await bancoCentral.ObterSerieAsync(codigo, InicioHistorico, hoje, ct))
            .Select(v => (v.Data, v.Valor / divisor)).ToList();

        async Task<List<(DateOnly, decimal)>> Yahoo(string simbolo, bool ajustado) =>
            (await yahoo.ObterDadosSimboloAsync(simbolo, period1)).HistoricalDataPrice
            .Select(p => (DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(p.Date).UtcDateTime),
                          ajustado ? p.AdjClose ?? p.Close : p.Close))
            .ToList();

        return indice switch
        {
            IndiceReferencia.CDI => await Sgs(SgsCdi, 100),
            IndiceReferencia.IPCA => await Sgs(SgsIpca, 100),
            IndiceReferencia.PTAX => await Sgs(SgsPtaxVenda, 1),
            IndiceReferencia.IBOV => await Yahoo("^BVSP", ajustado: false),
            IndiceReferencia.SP500 => await Yahoo("^GSPC", ajustado: false),
            // IDIV não está no Yahoo; o DIVO11 o replica, e o preço ajustado inclui os proventos reinvestidos
            IndiceReferencia.IDIV => await Yahoo("DIVO11.SA", ajustado: true),
            _ => []
        };
    }

    private async Task<(int Inseridos, int Atualizados)> GravarAsync(
        IndiceReferencia indice, List<(DateOnly Data, decimal Valor)> valores, CancellationToken ct)
    {
        if (valores.Count == 0) return (0, 0);

        var existentes = await db.CotacoesIndices
            .Where(c => c.Indice == indice)
            .ToDictionaryAsync(c => c.Data, ct);

        int inseridos = 0, atualizados = 0;
        // Uma linha por data: a fonte pode repetir o dia (o Yahoo às vezes manda o pregão corrente duas vezes)
        foreach (var (data, valor) in valores.GroupBy(v => v.Data).Select(g => g.Last()))
        {
            if (existentes.TryGetValue(data, out var existente))
            {
                if (existente.Valor == valor) continue;
                existente.Valor = valor;
                existente.DataImportacao = DateTime.UtcNow;
                atualizados++;
            }
            else
            {
                db.CotacoesIndices.Add(new CotacaoIndice { Indice = indice, Data = data, Valor = valor });
                inseridos++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (inseridos, atualizados);
    }
}

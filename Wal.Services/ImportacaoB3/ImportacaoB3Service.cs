using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.Irpf;

namespace Wal.Services.ImportacaoB3;

/// <summary>
/// Corrige os proventos do app com o extrato de Movimentação da B3: primeiro uma prévia do que vai mudar, depois
/// a aplicação (com backup do banco antes). Ver <see cref="ConciliacaoProventosB3"/>.
/// </summary>
public interface IImportacaoB3Service
{
    Task<ResultadoOperacao<PreviaImportacaoB3>> PreviaAsync(IReadOnlyList<(string Nome, Stream Conteudo)> arquivos);

    /// <summary>Refaz a conciliação com o banco atual e só aplica se ela continuar igual à da prévia.</summary>
    Task<ResultadoOperacao<ResumoImportacaoB3>> AplicarAsync(PreviaImportacaoB3 previa);
}

public class ImportacaoB3Service(AppDbContext db, ILogger<ImportacaoB3Service> logger) : IImportacaoB3Service
{
    private static readonly Dictionary<string, TipoProvento> TiposProvento = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dividendo"] = TipoProvento.Dividendo,
        ["Juros Sobre Capital Próprio"] = TipoProvento.JCP,
        ["Rendimento"] = TipoProvento.Rendimento
    };

    private const string Liquidacao = "Transferência - Liquidação";

    public async Task<ResultadoOperacao<PreviaImportacaoB3>> PreviaAsync(IReadOnlyList<(string Nome, Stream Conteudo)> arquivos)
    {
        if (arquivos.Count == 0)
            return ResultadoOperacao<PreviaImportacaoB3>.Falha("Escolha ao menos um arquivo.", TipoErroOperacao.Validacao);

        var porArquivo = new List<List<ExtratoMovimentacaoB3.Linha>>();
        foreach (var (nome, conteudo) in arquivos)
        {
            try
            {
                porArquivo.Add(ExtratoMovimentacaoB3.Ler(conteudo));
            }
            catch (ExtratoMovimentacaoB3.FormatoInvalidoException e)
            {
                return ResultadoOperacao<PreviaImportacaoB3>.Falha($"{nome}: {e.Message}", TipoErroOperacao.Validacao);
            }
        }

        var linhas = SemRepeticaoEntreArquivos(porArquivo);
        if (linhas.Count == 0)
            return ResultadoOperacao<PreviaImportacaoB3>.Falha("O extrato não tem nenhuma movimentação.", TipoErroOperacao.Validacao);

        var conciliacao = await ConciliarAsync(linhas);
        return ResultadoOperacao<PreviaImportacaoB3>.Ok(new PreviaImportacaoB3
        {
            Inicio = conciliacao.Inicio,
            Fim = conciliacao.Fim,
            Arquivos = arquivos.Select(a => a.Nome).ToList(),
            Itens = conciliacao.Itens.Select(i => ParaResponse(i, conciliacao.Tickers)).ToList(),
            Informativos = linhas
                .Where(l => !EhCreditoDeProvento(l) && l.Movimentacao != Liquidacao)
                .OrderBy(l => l.Data)
                .Select(l => new LinhaInformativaB3(l.Data, l.Movimentacao, l.Ticker, l.Quantidade, l.Valor))
                .ToList(),
            Liquidacoes = linhas.Count(l => l.Movimentacao == Liquidacao),
            TickersNaoCadastrados = conciliacao.NaoCadastrados,
            Linhas = linhas
        });
    }

    public async Task<ResultadoOperacao<ResumoImportacaoB3>> AplicarAsync(PreviaImportacaoB3 previa)
    {
        var conciliacao = await ConciliarAsync(previa.Linhas);
        var aplicaveis = conciliacao.Itens.Where(i => i.Aplicavel).ToList();
        if (Assinatura(aplicaveis) != Assinatura(previa.Itens.Where(i => i.Aplicavel)))
            return ResultadoOperacao<ResumoImportacaoB3>.Falha(
                "Os proventos mudaram desde a prévia. Gere a prévia de novo.", TipoErroOperacao.Conflito);
        if (aplicaveis.Count == 0)
            return ResultadoOperacao<ResumoImportacaoB3>.Ok(new ResumoImportacaoB3(0, 0, 0, 0, null));

        var backup = await FazerBackupAsync();

        await using var transacao = await db.Database.BeginTransactionAsync();
        var ids = aplicaveis.Where(i => i.Provento is not null).Select(i => i.Provento!.Id).ToHashSet();
        var proventos = await db.Proventos.Include(p => p.Pagamentos).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        int criados = 0, ocultados = 0, parcelas = 0;
        var novosPorChave = new Dictionary<(int, DateOnly, TipoProvento, DateOnly?), (Provento Provento, decimal Quantidade)>();

        foreach (var item in aplicaveis)
        {
            switch (item.Acao)
            {
                case AcaoImportacaoB3.Substituir or AcaoImportacaoB3.Parcial or AcaoImportacaoB3.OcultarDuplicado:
                    proventos[item.Provento!.Id].Oculto = true;
                    ocultados++;
                    break;
                case AcaoImportacaoB3.AdicionarParcela:
                    foreach (var c in item.Creditos)
                        proventos[item.Provento!.Id].Pagamentos.Add(Parcela(c));
                    parcelas += item.Creditos.Count;
                    break;
            }

            foreach (var novo in item.Novos)
            {
                var chave = (item.AtivoId, novo.Parcelas[0].Credito.Data, novo.Tipo, novo.DataCom);
                Provento existente;
                if (novosPorChave.TryGetValue(chave, out var criado))
                {
                    // Mesmo provento (mesma chave única): soma ao que já foi criado nesta importação
                    existente = criado.Provento;
                    existente.ValorPorAcao += novo.ValorPorAcao * novo.Quantidade / criado.Quantidade;
                }
                else
                {
                    existente = new Provento
                    {
                        AtivoId = item.AtivoId,
                        TipoProvento = novo.Tipo,
                        Origem = OrigemDado.ExtratoB3,
                        DataCom = novo.DataCom,
                        DataPagamento = novo.Parcelas[0].Credito.Data,
                        ValorPorAcao = novo.ValorPorAcao,
                        DataImportacao = DateTime.UtcNow
                    };
                    novosPorChave[chave] = (existente, novo.Quantidade);
                    db.Proventos.Add(existente);
                    criados++;
                }
                foreach (var c in novo.Parcelas) existente.Pagamentos.Add(Parcela(c));
                parcelas += novo.Parcelas.Count;
            }
        }

        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        logger.LogInformation(
            "Extrato B3 aplicado: {Alteracoes} alterações, {Criados} proventos criados, {Ocultados} ocultados, {Parcelas} parcelas. Backup: {Backup}",
            aplicaveis.Count, criados, ocultados, parcelas, backup ?? "-");
        return ResultadoOperacao<ResumoImportacaoB3>.Ok(new ResumoImportacaoB3(aplicaveis.Count, criados, ocultados, parcelas, backup));
    }

    private static PagamentoProvento Parcela(ConciliacaoProventosB3.CreditoAvaliado c) => new()
    {
        Data = c.Credito.Data,
        Quantidade = c.Credito.Quantidade,
        ValorBruto = c.ValorBruto,
        ValorLiquido = c.Credito.ValorLiquido
    };

    private record Resultado(
        DateOnly Inicio, DateOnly Fim, List<ConciliacaoProventosB3.Item> Itens, Dictionary<int, string> Tickers, List<string> NaoCadastrados);

    private async Task<Resultado> ConciliarAsync(IReadOnlyList<ExtratoMovimentacaoB3.Linha> linhas)
    {
        var inicio = linhas.Min(l => l.Data);
        var fim = linhas.Max(l => l.Data);
        var ativos = await db.Ativos.AsNoTracking().ToListAsync();
        var porTicker = ativos.ToDictionary(a => a.Ticker, StringComparer.OrdinalIgnoreCase);
        var tickers = ativos.ToDictionary(a => a.Id, a => a.Ticker);

        var motor = await MotorCarteira.CalcularAsync(db);
        var deProventos = linhas.Where(EhCreditoDeProvento).ToList();
        var naoCadastrados = deProventos.Select(l => l.Ticker).Where(t => !porTicker.ContainsKey(t)).Distinct().Order().ToList();
        var creditos = deProventos
            .Where(l => porTicker.ContainsKey(l.Ticker))
            .Select(l =>
            {
                var ativoId = porTicker[l.Ticker].Id;
                return new ConciliacaoProventosB3.Credito(
                    l.NumeroLinha, ativoId, l.Ticker, l.Data, TiposProvento[l.Movimentacao], l.Quantidade,
                    l.PrecoUnitario, l.Valor!.Value, motor.Conversao.FatorRegistro(ativoId, l.Data));
            })
            .ToList();

        var proventos = await ProventosDoAppAsync(motor, inicio.AddMonths(-12), fim);
        var itens = ConciliacaoProventosB3.Conciliar(creditos, proventos, inicio, fim)
            .Select(i => i.Ticker.Length > 0 ? i : i with { Ticker = tickers.GetValueOrDefault(i.AtivoId, "?") })
            .ToList();
        return new Resultado(inicio, fim, itens, tickers, naoCadastrados);
    }

    /// <summary>
    /// Proventos não ocultos com data ex no intervalo, como o app os enxerga: o valor esperado é o do
    /// MotorCarteira (que já põe o Yahoo na base certa) e a quantidade com direito é a registrada.
    /// </summary>
    private async Task<List<ConciliacaoProventosB3.ProventoApp>> ProventosDoAppAsync(MotorCarteira motor, DateOnly desde, DateOnly ate)
    {
        var recebidos = motor.ProventosRecebidos.Where(r => r.DataEx >= desde && r.DataEx <= ate).ToList();
        if (recebidos.Count == 0) return [];

        var ids = recebidos.Select(r => r.ProventoId).ToHashSet();
        var registros = await db.Proventos.AsNoTracking().Include(p => p.Pagamentos)
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var ativoIds = recebidos.Select(r => r.AtivoId).ToHashSet();
        var movimentacoes = (await db.Movimentacoes.AsNoTracking().Where(m => ativoIds.Contains(m.AtivoId)).ToListAsync())
            .ToLookup(m => m.AtivoId);
        var pregoes = (await db.CotacoesHistoricas.AsNoTracking()
                .Where(c => ativoIds.Contains(c.AtivoId) && c.Data >= desde.AddDays(-15))
                .Select(c => new { c.AtivoId, c.Data }).ToListAsync())
            .GroupBy(c => c.AtivoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateOnly>)g.Select(c => c.Data).Order().ToList());

        return recebidos.Select(r =>
        {
            var registro = registros[r.ProventoId];
            var quantidade = movimentacoes[r.AtivoId].Where(m => m.Data < r.DataEx)
                .Sum(m => m.Tipo == TipoMovimentacao.Compra ? m.Quantidade : -m.Quantidade);
            var dataCom = r.Origem == OrigemDado.YahooFinance
                ? RegrasIrpf.DataComPelaDataEx(r.DataEx, pregoes.GetValueOrDefault(r.AtivoId) ?? [])
                : registro.DataCom ?? r.DataEx.AddDays(-1);
            var semAjuste = r.Origem == OrigemDado.YahooFinance ? quantidade * registro.ValorPorAcao : (decimal?)null;
            return new ConciliacaoProventosB3.ProventoApp(
                r.ProventoId, r.AtivoId, r.Origem, r.Tipo, r.DataEx, dataCom, quantidade, r.Valor, semAjuste,
                registro.Pagamentos.Select(p => new ConciliacaoProventosB3.ParcelaExistente(p.Data, p.ValorBruto, p.ValorLiquido)).ToList(),
                motor.Conversao.FatorRegistro(r.AtivoId, dataCom));
        }).ToList();
    }

    private static bool EhCreditoDeProvento(ExtratoMovimentacaoB3.Linha l) =>
        l.Credito && TiposProvento.ContainsKey(l.Movimentacao) && l.Valor is > 0 && l.Quantidade > 0;

    // Arquivos com períodos sobrepostos repetem linhas: de cada linha idêntica fica o maior número de
    // ocorrências num mesmo arquivo (duas parcelas iguais no mesmo dia continuam sendo duas)
    private static List<ExtratoMovimentacaoB3.Linha> SemRepeticaoEntreArquivos(List<List<ExtratoMovimentacaoB3.Linha>> arquivos)
    {
        if (arquivos.Count == 1) return arquivos[0];
        static object Chave(ExtratoMovimentacaoB3.Linha l) =>
            (l.Credito, l.Data, l.Movimentacao, l.Ticker, l.Quantidade, l.PrecoUnitario, l.Valor);
        var resultado = new List<ExtratoMovimentacaoB3.Linha>();
        foreach (var grupo in arquivos.SelectMany((linhas, i) => linhas.Select(l => (Arquivo: i, Linha: l))).GroupBy(x => Chave(x.Linha)))
        {
            var maisOcorrencias = grupo.GroupBy(x => x.Arquivo).MaxBy(g => g.Count())!;
            resultado.AddRange(maisOcorrencias.Select(x => x.Linha));
        }
        return resultado;
    }

    private static string Assinatura(IEnumerable<ConciliacaoProventosB3.Item> itens) =>
        string.Join(";", itens.Select(i => $"{i.Acao}:{i.Provento?.Id}:{string.Join(",", i.Creditos.Select(c => c.Credito.Linha + "@" + c.Credito.Data.DayNumber))}").Order());

    private static string Assinatura(IEnumerable<ItemImportacaoB3> itens) =>
        string.Join(";", itens.Select(i => $"{i.Acao}:{i.ProventoId}:{string.Join(",", i.Creditos.Select(c => c.Linha + "@" + c.Data.DayNumber))}").Order());

    /// <summary>Cópia consistente do banco (VACUUM INTO) ao lado dele, antes de mexer nos proventos.</summary>
    internal async Task<string?> FazerBackupAsync()
    {
        var arquivo = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrEmpty(arquivo) || arquivo.Contains(":memory:", StringComparison.OrdinalIgnoreCase)) return null;

        var pasta = Path.GetDirectoryName(Path.GetFullPath(arquivo))!;
        var nome = Path.GetFileNameWithoutExtension(arquivo);
        var destino = Path.Combine(pasta, $"{nome}.backup-antes-importacao-b3-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        // Caminho como parâmetro (o SQLite aceita em VACUUM INTO), não montado dentro do SQL
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {destino}");
        return destino;
    }

    private static ItemImportacaoB3 ParaResponse(ConciliacaoProventosB3.Item i, Dictionary<int, string> tickers) => new(
        i.Acao,
        i.Ticker.Length > 0 ? i.Ticker : tickers.GetValueOrDefault(i.AtivoId, "?"),
        i.Provento?.Id,
        i.Provento?.DataEx,
        i.Provento?.DataCom,
        i.ValorEsperado,
        i.ValorRecebidoBruto,
        i.Creditos.Select(c => new CreditoB3Response(
            c.Credito.Linha, c.Credito.Data, c.Credito.Tipo, c.Credito.Quantidade, c.Credito.ValorLiquido, c.Aliquota, c.ValorBruto)).ToList(),
        i.Novos.Select(n => $"{NomeTipo(n.Tipo)} com data com {(n.DataCom is DateOnly d ? d.ToString("dd/MM/yyyy") : "a informar")}, " +
                            $"{n.Quantidade:0.####} × {n.ValorPorAcao:0.######} por ação, {n.Parcelas.Count} parcela(s)").ToList(),
        i.Detalhe,
        i.Aplicavel);

    private static string NomeTipo(TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => "JCP",
        TipoProvento.Rendimento => "Rendimento",
        _ => "Dividendo"
    };
}

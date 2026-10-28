using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Enums;
using Wal.Services.Dtos;

namespace Wal.Services;

public class AnaliseCarteiraService(
    AppDbContext db,
    ICarteiraService carteira,
    IIndicesReferenciaService indices) : IAnaliseCarteiraService
{
    private const int DiasUteisNoAno = 252;

    public async Task<DashboardResponse?> ObterDashboardAsync()
    {
        // A data final da série é conhecida antes de calcular, e a contribuição precisa da foto do início dos 12 meses
        if (await DataFimSerieAsync() is not DateOnly fim) return null;
        var base12Meses = fim.AddMonths(-12);

        var motor = await MotorCarteira.CalcularAsync(db, [base12Meses]);
        if (motor.Pontos.Count == 0) return null;

        var pontos = motor.Pontos;
        var cdi = await CdiAcumuladoAsync(pontos[0].Data, fim);
        var tickers = await TickersAsync();
        var inicioProventos = new DateOnly(fim.Year, fim.Month, 1).AddMonths(-11);

        decimal aplicado = 0;
        var serie = pontos.Select(p =>
        {
            aplicado += p.Compras - p.Vendas;
            return new PontoSerieResponse(p.Data, p.Valor, aplicado, p.RentabilidadeAcumulada);
        }).ToList();

        return new DashboardResponse(
            serie,
            ResumoPeriodos(pontos, cdi),
            ResultadoMensal(pontos, cdi),
            Drawdown(pontos),
            Contribuicao(motor, base12Meses, fim, tickers),
            ProventosPorPeriodo(motor, inicioProventos, fim, AgrupamentoPeriodo.Mes),
            MontarPosicoes(motor, await carteira.ObterPosicoesAsync()));
    }

    public async Task<PainelPosicoesResponse> ObterPainelPosicoesAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        var registradas = await carteira.ObterPosicoesAsync();
        var abertas = MontarPosicoes(motor, registradas);
        var ativos = await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var encerradas = Encerradas(motor, registradas, ativos);

        var fim = motor.Pontos.Count > 0 ? motor.Pontos[^1].Data : DateOnly.FromDateTime(DateTime.Today);
        var desde = fim.AddDays(-30);
        var ids = abertas.Select(p => p.AtivoId).ToList();
        var tendencia = (await db.CotacoesHistoricas.AsNoTracking()
                .Where(c => ids.Contains(c.AtivoId) && c.Data > desde)
                .Select(c => new { c.AtivoId, c.Data, c.Fechamento })
                .ToListAsync())
            .GroupBy(c => c.AtivoId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Data).Select(c => c.Fechamento).ToList());

        return new PainelPosicoesResponse(abertas, encerradas, tendencia);
    }

    public async Task<DetalheAtivoResponse?> ObterDetalheAtivoAsync(int ativoId)
    {
        var ativo = await db.Ativos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == ativoId);
        if (ativo is null) return null;

        var motor = await MotorCarteira.CalcularAsync(db, ativoSerie: ativoId);
        var cotacoes = await db.CotacoesHistoricas.AsNoTracking()
            .Where(c => c.AtivoId == ativoId)
            .OrderBy(c => c.Data)
            .ToListAsync();

        PregaoResponse? pregao = null;
        if (cotacoes.Count > 0)
        {
            var ultima = cotacoes[^1];
            var anterior = cotacoes.Count > 1 ? cotacoes[^2] : null;
            decimal? variacao = anterior is null ? null : ultima.Fechamento - anterior.Fechamento;
            var ano = cotacoes.Where(c => c.Data > ultima.Data.AddYears(-1)).ToList();
            pregao = new PregaoResponse(
                ultima.Data, ultima.Abertura, ultima.Maxima, ultima.Minima, ultima.Fechamento, ultima.Volume,
                variacao, anterior is { Fechamento: not 0 } ? variacao / anterior.Fechamento : null,
                ano.Min(c => c.Minima), ano.Max(c => c.Maxima));
        }

        var serieAtivo = motor.SerieAtivo.ToDictionary(p => p.Data);
        var serie = cotacoes.Select(c =>
        {
            var s = serieAtivo.GetValueOrDefault(c.Data);
            return new PontoAtivoResponse(c.Data, c.Fechamento, s?.Valor, s?.Aplicado);
        }).ToList();

        var registradas = await carteira.ObterPosicoesAsync();
        var posicao = MontarPosicoes(motor, registradas).FirstOrDefault(p => p.AtivoId == ativoId);
        var encerrada = posicao is null
            ? Encerradas(motor, registradas, new Dictionary<int, Domain.Entities.Ativo> { [ativo.Id] = ativo }).FirstOrDefault()
            : null;

        var recebidos = motor.ProventosRecebidos.Where(p => p.AtivoId == ativoId).ToList();
        var proventos = recebidos
            .OrderByDescending(p => p.DataPagamento)
            .Select(p => ParaResponse(p, ativo.Ticker))
            .ToList();
        var proventosPorAno = recebidos.Count == 0
            ? []
            : ProventosPorPeriodo(motor.ProventosRecebidos.Where(p => p.AtivoId == ativoId),
                new DateOnly(recebidos.Min(p => p.DataPagamento).Year, 1, 1),
                new DateOnly(recebidos.Max(p => p.DataPagamento).Year, 12, 31),
                AgrupamentoPeriodo.Ano);

        var movimentacoes = (await db.Movimentacoes.AsNoTracking()
                .Where(m => m.AtivoId == ativoId)
                .ToListAsync())
            .OrderByDescending(m => m.Data).ThenByDescending(m => m.Id)
            .Select(m => new MovimentacaoResponse(m.Id, m.AtivoId, ativo.Ticker, m.Tipo, m.Data, m.Quantidade, m.PrecoUnitario, m.Taxas))
            .ToList();

        // Um evento por data: fontes diferentes podem gravar o mesmo desdobramento
        var eventos = (await db.EventosCorporativos.AsNoTracking()
                .Where(e => e.AtivoId == ativoId)
                .ToListAsync())
            .GroupBy(e => e.Data)
            .Select(g => g.OrderBy(e => e.Origem).First())
            .OrderByDescending(e => e.Data)
            .Select(e => new EventoCorporativoResponse(e.Data, e.Tipo, e.Numerador, e.Denominador))
            .ToList();

        return new DetalheAtivoResponse(
            new AtivoResponse(ativo.Id, ativo.Ticker, ativo.Nome, ativo.Tipo, ativo.EstaAtivo, ativo.DataCadastro),
            pregao, posicao, encerrada, serie, proventos, proventosPorAno, movimentacoes, eventos);
    }

    public async Task<List<ResumoPeriodoResponse>> ObterResumoPeriodosAsync()
    {
        var pontos = (await MotorCarteira.CalcularAsync(db)).Pontos;
        if (pontos.Count == 0) return [];
        return ResumoPeriodos(pontos, await CdiAcumuladoAsync(pontos[0].Data, pontos[^1].Data));
    }

    public async Task<List<ResultadoMensalResponse>> ObterResultadoMensalAsync()
    {
        var pontos = (await MotorCarteira.CalcularAsync(db)).Pontos;
        if (pontos.Count == 0) return [];
        return ResultadoMensal(pontos, await CdiAcumuladoAsync(pontos[0].Data, pontos[^1].Data));
    }

    public async Task<DrawdownResponse> ObterDrawdownAsync() =>
        Drawdown((await MotorCarteira.CalcularAsync(db)).Pontos);

    public async Task<List<ContribuicaoAtivoResponse>> ObterContribuicaoPorAtivoAsync(DateOnly inicio, DateOnly fim)
    {
        if (fim < inicio) return [];
        var dataBase = inicio.AddDays(-1);
        var motor = await MotorCarteira.CalcularAsync(db, [dataBase, fim]);
        if (motor.Pontos.Count == 0) return [];
        return Contribuicao(motor, dataBase, fim, await TickersAsync());
    }

    public async Task<List<PosicaoDetalhadaResponse>> ObterPosicoesDetalhadasAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        return MontarPosicoes(motor, await carteira.ObterPosicoesAsync());
    }

    public async Task<List<PosicaoEncerradaResponse>> ObterPosicoesEncerradasAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        var ativos = await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id);
        return Encerradas(motor, await carteira.ObterPosicoesAsync(), ativos);
    }

    /// <summary>Ativos com compras que não estão entre as posições abertas, com o resultado realizado.</summary>
    private static List<PosicaoEncerradaResponse> Encerradas(
        MotorCarteira motor, List<PosicaoAtivoResponse> registradas, IReadOnlyDictionary<int, Domain.Entities.Ativo> ativos)
    {
        // Encerrada pela quantidade registrada (a mesma da tela de Posições), não pela convertida do motor
        var abertas = registradas.Select(p => p.AtivoId).ToHashSet();

        return motor.PorAtivo
            .Where(kv => !abertas.Contains(kv.Key) && kv.Value.Compras > 0 && ativos.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var t = kv.Value;
                var ativo = ativos[kv.Key];
                var resultado = t.Vendas + t.Proventos - t.Compras;
                return new PosicaoEncerradaResponse(
                    kv.Key, ativo.Ticker, ativo.Nome, ativo.Tipo, t.PrimeiraCompra, t.UltimaVenda,
                    t.Compras, t.Vendas, t.Proventos, resultado, resultado / t.Compras);
            })
            .OrderByDescending(e => e.UltimaVenda)
            .ToList();
    }

    public async Task<List<ProventoRecebidoResponse>> ObterProventosRecebidosAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        var tickers = await TickersAsync();
        return motor.ProventosRecebidos
            .OrderByDescending(p => p.DataPagamento).ThenBy(p => tickers.GetValueOrDefault(p.AtivoId))
            .Select(p => ParaResponse(p, tickers.GetValueOrDefault(p.AtivoId, "?")))
            .ToList();
    }

    public async Task<List<ProventosPeriodoResponse>> ObterProventosPorPeriodoAsync(
        DateOnly inicio, DateOnly fim, AgrupamentoPeriodo agrupamento)
    {
        if (fim < inicio) return [];
        return ProventosPorPeriodo(await MotorCarteira.CalcularAsync(db), inicio, fim, agrupamento);
    }

    public async Task<List<PagadorProventosResponse>> ObterPagadoresAsync(DateOnly inicio, DateOnly fim)
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        var posicoes = MontarPosicoes(motor, await carteira.ObterPosicoesAsync());
        var ativos = await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id);
        return Pagadores(motor, posicoes, ativos, inicio, fim);
    }

    public async Task<PainelEventosResponse?> ObterPainelEventosAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        if (motor.Pontos.Count == 0) return null;

        var fim = motor.Pontos[^1].Data;
        var ativos = await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var posicoes = MontarPosicoes(motor, await carteira.ObterPosicoesAsync());

        var proventos = motor.ProventosRecebidos
            .OrderByDescending(p => p.DataPagamento).ThenBy(p => ativos.GetValueOrDefault(p.AtivoId)?.Ticker)
            .Select(p => ParaResponse(p, ativos.GetValueOrDefault(p.AtivoId)?.Ticker ?? "?"))
            .ToList();

        // Um evento por ativo e data: fontes diferentes podem gravar o mesmo desdobramento
        var eventos = (await db.EventosCorporativos.AsNoTracking().ToListAsync())
            .GroupBy(e => (e.AtivoId, e.Data))
            .Select(g => g.OrderBy(e => e.Origem).First())
            .Where(e => ativos.ContainsKey(e.AtivoId))
            .OrderByDescending(e => e.Data).ThenBy(e => ativos[e.AtivoId].Ticker)
            .Select(e => new EventoCorporativoAtivoResponse(e.AtivoId, ativos[e.AtivoId].Ticker, e.Data, e.Tipo, e.Numerador, e.Denominador))
            .ToList();

        return new PainelEventosResponse(
            fim, proventos, Pagadores(motor, posicoes, ativos, fim.AddMonths(-12).AddDays(1), fim), eventos);
    }

    private static List<PagadorProventosResponse> Pagadores(
        MotorCarteira motor, List<PosicaoDetalhadaResponse> abertas, IReadOnlyDictionary<int, Domain.Entities.Ativo> ativos,
        DateOnly inicio, DateOnly fim)
    {
        var posicoes = abertas.ToDictionary(p => p.AtivoId);
        var porAtivo = motor.ProventosRecebidos
            .Where(p => p.DataPagamento >= inicio && p.DataPagamento <= fim)
            .GroupBy(p => p.AtivoId)
            .Select(g => (AtivoId: g.Key, Recebido: g.Sum(p => p.Valor)))
            .Where(x => x.Recebido > 0)
            .ToList();
        var total = porAtivo.Sum(x => x.Recebido);

        return porAtivo
            .Select(x =>
            {
                var aberta = posicoes.GetValueOrDefault(x.AtivoId);
                var ativo = ativos.GetValueOrDefault(x.AtivoId);
                return new PagadorProventosResponse(
                    x.AtivoId, ativo?.Ticker ?? "?", ativo?.Nome ?? "", x.Recebido, x.Recebido / total,
                    aberta is null, aberta?.DividendYield12Meses, aberta?.YieldOnCost12Meses);
            })
            .OrderByDescending(p => p.Recebido)
            .ToList();
    }

    private static ProventoRecebidoResponse ParaResponse(MotorCarteira.ProventoRecebido p, string ticker) =>
        new(p.ProventoId, p.AtivoId, ticker, p.Tipo, p.Origem, p.DataCom, p.DataPagamento,
            p.DataPagamentoEstimada, p.Quantidade, p.ValorPorAcao, p.Valor, p.Corrigido);

    // ── Cálculos sobre a série (sem banco) ───────────────────────────────

    private static List<ResumoPeriodoResponse> ResumoPeriodos(List<MotorCarteira.Ponto> pontos, Dictionary<DateOnly, decimal>? cdi)
    {
        var fim = pontos[^1].Data;
        return Enum.GetValues<PeriodoResumo>()
            .Select(periodo => Resumir(periodo, pontos, IndiceBase(pontos, DataBase(periodo, fim)), pontos.Count - 1, cdi))
            .ToList();
    }

    private static List<ResultadoMensalResponse> ResultadoMensal(List<MotorCarteira.Ponto> pontos, Dictionary<DateOnly, decimal>? cdi)
    {
        var meses = new List<ResultadoMensalResponse>();
        var indiceBase = -1;
        for (var i = 0; i < pontos.Count; i++)
        {
            var ultimoDoMes = i == pontos.Count - 1 || pontos[i + 1].Data.Month != pontos[i].Data.Month;
            if (!ultimoDoMes) continue;

            var r = Resumir(PeriodoResumo.Mes, pontos, indiceBase, i, cdi);
            meses.Add(new ResultadoMensalResponse(pontos[i].Data.Year, pontos[i].Data.Month, r.Rentabilidade, r.Resultado, r.Cdi));
            indiceBase = i;
        }
        return meses;
    }

    private static DrawdownResponse Drawdown(List<MotorCarteira.Ponto> pontos)
    {
        var serie = new List<PontoDrawdownResponse>(pontos.Count);
        decimal pico = 1, maximo = 0;
        DateOnly? dataMaximo = null;
        foreach (var p in pontos)
        {
            var cota = 1 + p.RentabilidadeAcumulada;
            if (cota > pico) pico = cota;
            var queda = cota / pico - 1;
            serie.Add(new PontoDrawdownResponse(p.Data, queda));
            if (queda < maximo)
            {
                maximo = queda;
                dataMaximo = p.Data;
            }
        }
        return new DrawdownResponse(serie, maximo, dataMaximo);
    }

    /// <summary>Resultado de cada ativo entre a foto da data base (exclusive) e o fim (inclusive).</summary>
    private static List<ContribuicaoAtivoResponse> Contribuicao(
        MotorCarteira motor, DateOnly dataBase, DateOnly fim, Dictionary<int, string> tickers)
    {
        // Fim depois da série: vale o último dia calculado
        var dataFim = fim > motor.Pontos[^1].Data ? motor.Pontos[^1].Data : fim;

        return motor.PorAtivo.Keys
            .Select(id =>
            {
                var a = motor.FotoEm(dataBase, id);
                var b = motor.FotoEm(dataFim, id);
                var resultado = (b.Valor - a.Valor) - (b.Compras - a.Compras) + (b.Vendas - a.Vendas) + (b.Proventos - a.Proventos);
                return new ContribuicaoAtivoResponse(id, tickers.GetValueOrDefault(id, "?"), resultado);
            })
            .Where(c => Math.Abs(c.Resultado) >= 0.005m)
            .OrderByDescending(c => c.Resultado)
            .ToList();
    }

    private static List<ProventosPeriodoResponse> ProventosPorPeriodo(
        MotorCarteira motor, DateOnly inicio, DateOnly fim, AgrupamentoPeriodo agrupamento) =>
        ProventosPorPeriodo(motor.ProventosRecebidos, inicio, fim, agrupamento);

    private static List<ProventosPeriodoResponse> ProventosPorPeriodo(
        IEnumerable<MotorCarteira.ProventoRecebido> recebidos, DateOnly inicio, DateOnly fim, AgrupamentoPeriodo agrupamento)
    {
        DateOnly Grupo(DateOnly d) => agrupamento == AgrupamentoPeriodo.Mes ? new DateOnly(d.Year, d.Month, 1) : new DateOnly(d.Year, 1, 1);
        var porGrupo = recebidos
            .Where(p => p.DataPagamento >= inicio && p.DataPagamento <= fim)
            .ToLookup(p => Grupo(p.DataPagamento));

        var resultado = new List<ProventosPeriodoResponse>();
        for (var g = Grupo(inicio); g <= fim; g = agrupamento == AgrupamentoPeriodo.Mes ? g.AddMonths(1) : g.AddYears(1))
        {
            var doGrupo = porGrupo[g].ToList();
            decimal Soma(TipoProvento tipo) => doGrupo.Where(p => p.Tipo == tipo).Sum(p => p.Valor);
            resultado.Add(new ProventosPeriodoResponse(g, Soma(TipoProvento.Dividendo), Soma(TipoProvento.JCP), Soma(TipoProvento.Rendimento)));
        }
        return resultado;
    }

    private static List<PosicaoDetalhadaResponse> MontarPosicoes(MotorCarteira motor, List<PosicaoAtivoResponse> posicoes)
    {
        var fim = motor.Pontos.Count > 0 ? motor.Pontos[^1].Data : DateOnly.FromDateTime(DateTime.Today);
        var inicio12Meses = fim.AddMonths(-12);
        var recebidos12Meses = motor.ProventosRecebidos
            .Where(p => p.DataPagamento > inicio12Meses && p.DataPagamento <= fim)
            .GroupBy(p => p.AtivoId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var proventos12Meses = recebidos12Meses.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => p.Valor));
        // DY e YoC por ação (soma do valor por ação ÷ preço), como as corretoras: dividir o total recebido pelo
        // valor de hoje inflaria o DY de quem tinha mais ações durante o ano e vendeu parte
        var porAcao12Meses = recebidos12Meses.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => p.ValorPorAcao));
        var valorTotal = posicoes.Sum(p => p.ValorPosicao ?? 0);

        return posicoes.Select(p =>
        {
            var totais = motor.PorAtivo.GetValueOrDefault(p.AtivoId);
            var custo = p.Quantidade * p.PrecoMedio;
            var prov12 = proventos12Meses.GetValueOrDefault(p.AtivoId);
            var porAcao12 = porAcao12Meses.GetValueOrDefault(p.AtivoId);
            return new PosicaoDetalhadaResponse(
                p.AtivoId, p.Ticker, p.Nome, p.Tipo, p.Quantidade, p.PrecoMedio, custo,
                p.CotacaoAtual, p.VariacaoDiaPercentual, p.ValorPosicao,
                p.ValorPosicao is decimal v && valorTotal > 0 ? v / valorTotal : null,
                p.RentabilidadePercentual,
                totais?.Proventos ?? 0,
                totais?.Resultado,
                totais is { Compras: > 0 } ? totais.Resultado / totais.Compras : null,
                prov12,
                p.CotacaoAtual is > 0 ? porAcao12 / p.CotacaoAtual : null,
                p.PrecoMedio > 0 ? porAcao12 / p.PrecoMedio : null);
        }).ToList();
    }

    // Base de cada período: o último dia antes dele começar. Nulo = desde o início.
    private static DateOnly? DataBase(PeriodoResumo periodo, DateOnly fim) => periodo switch
    {
        PeriodoResumo.Mes => new DateOnly(fim.Year, fim.Month, 1).AddDays(-1),
        PeriodoResumo.Ano => new DateOnly(fim.Year - 1, 12, 31),
        PeriodoResumo.DozeMeses => fim.AddMonths(-12),
        PeriodoResumo.VinteQuatroMeses => fim.AddMonths(-24),
        _ => null
    };

    // Índice do último ponto até a data base; -1 quando a carteira é mais nova que o período (= desde o início)
    private static int IndiceBase(List<MotorCarteira.Ponto> pontos, DateOnly? dataBase) =>
        dataBase is DateOnly d && d >= pontos[0].Data ? pontos.FindLastIndex(p => p.Data <= d) : -1;

    /// <summary>Métricas da janela (indiceBase, indiceFim]; indiceBase -1 = antes do primeiro dia (cota 1, valor 0).</summary>
    private static ResumoPeriodoResponse Resumir(
        PeriodoResumo periodo, List<MotorCarteira.Ponto> pontos, int indiceBase, int indiceFim, Dictionary<DateOnly, decimal>? cdi)
    {
        var cotaBase = indiceBase >= 0 ? 1 + pontos[indiceBase].RentabilidadeAcumulada : 1m;
        var valorBase = indiceBase >= 0 ? pontos[indiceBase].Valor : 0m;

        decimal fluxos = 0, pico = cotaBase, drawdown = 0, cotaAnterior = cotaBase;
        var retornosDiasUteis = new List<double>();
        for (var i = indiceBase + 1; i <= indiceFim; i++)
        {
            var p = pontos[i];
            fluxos += p.Vendas + p.Proventos - p.Compras;
            var cota = 1 + p.RentabilidadeAcumulada;
            // Fim de semana repete a sexta; contar esses zeros reduziria a volatilidade
            if (p.Data.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && cotaAnterior != 0)
                retornosDiasUteis.Add((double)(cota / cotaAnterior - 1));
            cotaAnterior = cota;
            if (cota > pico) pico = cota;
            if (cota / pico - 1 < drawdown) drawdown = cota / pico - 1;
        }

        var fim = pontos[indiceFim];
        var rentabilidade = (1 + fim.RentabilidadeAcumulada) / cotaBase - 1;
        var resultado = fim.Valor - valorBase + fluxos;
        var inicio = indiceBase >= 0 ? pontos[indiceBase].Data.AddDays(1) : pontos[0].Data;

        decimal? cdiPeriodo = null;
        var dataBaseCdi = indiceBase >= 0 ? pontos[indiceBase].Data : pontos[0].Data;
        if (cdi is not null && cdi.TryGetValue(dataBaseCdi, out var cdiBase) && cdi.TryGetValue(fim.Data, out var cdiFim))
            cdiPeriodo = (1 + cdiFim) / (1 + cdiBase) - 1;

        var n = retornosDiasUteis.Count;
        decimal? volatilidade = null, sharpe = null;
        if (n >= 2)
        {
            var media = retornosDiasUteis.Average();
            var variancia = retornosDiasUteis.Sum(r => (r - media) * (r - media)) / (n - 1);
            var vol = Math.Sqrt(variancia) * Math.Sqrt(DiasUteisNoAno);
            volatilidade = (decimal)vol;
            if (vol > 0 && cdiPeriodo is decimal c)
            {
                var anos = (double)DiasUteisNoAno / n;
                var retornoAnual = Math.Pow(1 + (double)rentabilidade, anos) - 1;
                var cdiAnual = Math.Pow(1 + (double)c, anos) - 1;
                sharpe = (decimal)((retornoAnual - cdiAnual) / vol);
            }
        }

        return new ResumoPeriodoResponse(
            periodo, inicio, fim.Data, rentabilidade, resultado, cdiPeriodo,
            cdiPeriodo is > 0 ? rentabilidade / cdiPeriodo : null,
            volatilidade, sharpe, drawdown);
    }

    // ── Acesso a dados ───────────────────────────────────────────────────

    // Mesma regra do motor: última cotação dos ativos com movimentação, ou a primeira movimentação
    private async Task<DateOnly?> DataFimSerieAsync()
    {
        var primeira = await db.Movimentacoes.AsNoTracking().MinAsync(m => (DateOnly?)m.Data);
        if (primeira is null) return null;

        var ativoIds = db.Movimentacoes.Select(m => m.AtivoId).Distinct();
        var ultimaCotacao = await db.CotacoesHistoricas.AsNoTracking()
            .Where(c => ativoIds.Contains(c.AtivoId))
            .MaxAsync(c => (DateOnly?)c.Data);
        return ultimaCotacao is DateOnly u && u > primeira ? u : primeira;
    }

    private async Task<Dictionary<DateOnly, decimal>?> CdiAcumuladoAsync(DateOnly inicio, DateOnly fim)
    {
        var serie = await indices.ObterRentabilidadeAcumuladaAsync(Benchmark.CDI, inicio, fim);
        return serie.Count == 0 ? null : serie.ToDictionary(p => p.Data, p => p.RentabilidadeAcumulada);
    }

    private async Task<Dictionary<int, string>> TickersAsync() =>
        await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Ticker);
}

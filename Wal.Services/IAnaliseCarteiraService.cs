using Wal.Services.Dtos;

namespace Wal.Services;

/// <summary>Métricas e visões derivadas da série diária da carteira (Dashboard, Posições, Eventos).</summary>
public interface IAnaliseCarteiraService
{
    /// <summary>Série, resumo, resultado mensal, drawdown, contribuição e proventos de 12 meses e posições — um cálculo só. Nulo sem movimentações.</summary>
    Task<DashboardResponse?> ObterDashboardAsync();

    /// <summary>Posições abertas e encerradas e a tendência de 30 dias das abertas — um cálculo só.</summary>
    Task<PainelPosicoesResponse> ObterPainelPosicoesAsync();

    /// <summary>Pregão, posição (ou resultado realizado), série diária, proventos, transações e eventos de um ativo. Nulo se não existe.</summary>
    Task<DetalheAtivoResponse?> ObterDetalheAtivoAsync(int ativoId);

    /// <summary>Proventos recebidos, pagadores dos últimos 12 meses e eventos corporativos — um cálculo só. Nulo sem movimentações.</summary>
    Task<PainelEventosResponse?> ObterPainelEventosAsync();

    /// <summary>Mês, ano, 12 meses, 24 meses e desde o início, terminando na cotação mais recente.</summary>
    Task<List<ResumoPeriodoResponse>> ObterResumoPeriodosAsync();

    /// <summary>Rentabilidade, resultado e CDI de cada mês desde a primeira movimentação.</summary>
    Task<List<ResultadoMensalResponse>> ObterResultadoMensalAsync();

    /// <summary>Queda em relação ao maior valor anterior da cota, dia a dia, e a maior queda.</summary>
    Task<DrawdownResponse> ObterDrawdownAsync();

    /// <summary>Resultado de cada ativo entre as datas (inclusive), do maior para o menor. Ativos sem resultado ficam de fora.</summary>
    Task<List<ContribuicaoAtivoResponse>> ObterContribuicaoPorAtivoAsync(DateOnly inicio, DateOnly fim);

    /// <summary>Posições abertas com valorização, resultado total, proventos, DY e yield on cost.</summary>
    Task<List<PosicaoDetalhadaResponse>> ObterPosicoesDetalhadasAsync();

    /// <summary>Ativos que já foram vendidos por inteiro, com o resultado realizado.</summary>
    Task<List<PosicaoEncerradaResponse>> ObterPosicoesEncerradasAsync();

    /// <summary>Cada provento recebido, do pagamento mais recente para o mais antigo.</summary>
    Task<List<ProventoRecebidoResponse>> ObterProventosRecebidosAsync();

    /// <summary>Proventos por mês ou ano (pela data de pagamento) e tipo, incluindo períodos sem nada.</summary>
    Task<List<ProventosPeriodoResponse>> ObterProventosPorPeriodoAsync(DateOnly inicio, DateOnly fim, AgrupamentoPeriodo agrupamento);

    /// <summary>Quanto cada ativo pagou entre as datas, do maior para o menor.</summary>
    Task<List<PagadorProventosResponse>> ObterPagadoresAsync(DateOnly inicio, DateOnly fim);
}

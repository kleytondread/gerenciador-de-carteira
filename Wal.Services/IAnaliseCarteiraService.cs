using Wal.Services.Dtos;

namespace Wal.Services;

/// <summary>Métricas e visões derivadas da série diária da carteira (Dashboard, Posições, Eventos).</summary>
public interface IAnaliseCarteiraService
{
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

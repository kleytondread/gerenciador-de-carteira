using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

public enum PeriodoResumo { Mes, Ano, DozeMeses, VinteQuatroMeses, Inicio }

public enum AgrupamentoPeriodo { Mes, Ano }

/// <summary>Uma coluna do "Resumo do portfólio". Frações (0,1 = 10%).</summary>
/// <param name="Inicio">Primeiro dia do período (o dia anterior é a base).</param>
/// <param name="Resultado">Variação do patrimônio descontando aportes e vendas, com proventos.</param>
/// <param name="Cdi">Rentabilidade do CDI no mesmo período; nula sem dados de CDI.</param>
/// <param name="PercentualCdi">Rentabilidade ÷ CDI.</param>
/// <param name="Volatilidade">Desvio-padrão dos retornos dos dias úteis, anualizado (× √252).</param>
/// <param name="Sharpe">(retorno anualizado − CDI anualizado) ÷ volatilidade.</param>
/// <param name="DrawdownMaximo">Maior queda a partir de um pico dentro do período (≤ 0).</param>
public record ResumoPeriodoResponse(
    PeriodoResumo Periodo,
    DateOnly Inicio,
    DateOnly Fim,
    decimal Rentabilidade,
    decimal Resultado,
    decimal? Cdi,
    decimal? PercentualCdi,
    decimal? Volatilidade,
    decimal? Sharpe,
    decimal DrawdownMaximo);

/// <param name="Valor">Patrimônio no dia.</param>
/// <param name="ValorAplicado">Dinheiro posto na carteira até o dia: compras − vendas, acumulado.</param>
/// <param name="RentabilidadeAcumulada">Desde o início, pelo método de cotas, como fração.</param>
public record PontoSerieResponse(DateOnly Data, decimal Valor, decimal ValorAplicado, decimal RentabilidadeAcumulada);

/// <summary>Tudo o que o Dashboard mostra, calculado de uma vez sobre a mesma série.</summary>
/// <param name="Contribuicao12Meses">Soma igual ao resultado de 12 meses do resumo.</param>
/// <param name="Proventos12Meses">Um item por mês, do 11º mês anterior até o mês da última cotação.</param>
public record DashboardResponse(
    List<PontoSerieResponse> Serie,
    List<ResumoPeriodoResponse> Resumo,
    List<ResultadoMensalResponse> ResultadoMensal,
    DrawdownResponse Drawdown,
    List<ContribuicaoAtivoResponse> Contribuicao12Meses,
    List<ProventosPeriodoResponse> Proventos12Meses,
    List<PosicaoDetalhadaResponse> Posicoes);

public record ResultadoMensalResponse(int Ano, int Mes, decimal Rentabilidade, decimal Resultado, decimal? Cdi);

public record PontoDrawdownResponse(DateOnly Data, decimal Drawdown);

public record DrawdownResponse(List<PontoDrawdownResponse> Pontos, decimal Maximo, DateOnly? DataMaximo);

/// <summary>Quanto o ativo somou ao resultado da carteira no intervalo, em R$.</summary>
public record ContribuicaoAtivoResponse(int AtivoId, string Ticker, decimal Resultado);

/// <param name="Custo">Quantidade × preço médio.</param>
/// <param name="Valorizacao">Cotação sobre o preço médio, sem proventos.</param>
/// <param name="ProventosRecebidos">Proventos brutos desde a primeira compra.</param>
/// <param name="ResultadoTotal">Valor atual + vendas + proventos − compras, desde a primeira compra.</param>
/// <param name="ResultadoTotalPercentual">Resultado total ÷ total comprado.</param>
/// <param name="DividendYield12Meses">Proventos dos últimos 12 meses ÷ valor atual.</param>
/// <param name="YieldOnCost12Meses">Proventos dos últimos 12 meses ÷ custo.</param>
public record PosicaoDetalhadaResponse(
    int AtivoId,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    decimal Quantidade,
    decimal PrecoMedio,
    decimal Custo,
    decimal? Cotacao,
    decimal? VariacaoDia,
    decimal? Valor,
    decimal? Peso,
    decimal? Valorizacao,
    decimal ProventosRecebidos,
    decimal? ResultadoTotal,
    decimal? ResultadoTotalPercentual,
    decimal Proventos12Meses,
    decimal? DividendYield12Meses,
    decimal? YieldOnCost12Meses);

/// <param name="Resultado">Vendido + proventos − investido.</param>
/// <param name="ResultadoPercentual">Resultado ÷ investido.</param>
public record PosicaoEncerradaResponse(
    int AtivoId,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    DateOnly? PrimeiraCompra,
    DateOnly? UltimaVenda,
    decimal Investido,
    decimal Vendido,
    decimal Proventos,
    decimal Resultado,
    decimal? ResultadoPercentual);

/// <param name="DataPagamentoEstimada">O Yahoo só informa a data ex; a de pagamento é aproximada.</param>
/// <param name="Quantidade">Quantidade com direito na data ex (na base de preço da fonte do provento).</param>
public record ProventoRecebidoResponse(
    int AtivoId,
    string Ticker,
    TipoProvento Tipo,
    OrigemDado Origem,
    DateOnly? DataCom,
    DateOnly DataPagamento,
    bool DataPagamentoEstimada,
    decimal Quantidade,
    decimal ValorPorAcao,
    decimal Valor);

/// <param name="Inicio">1º dia do mês ou do ano do grupo.</param>
public record ProventosPeriodoResponse(DateOnly Inicio, decimal Dividendos, decimal Jcp, decimal Rendimentos)
{
    public decimal Total => Dividendos + Jcp + Rendimentos;
}

/// <param name="Percentual">Fração do total recebido no intervalo.</param>
/// <param name="Encerrada">O ativo não está mais na carteira (sem DY nem YoC).</param>
public record PagadorProventosResponse(
    int AtivoId,
    string Ticker,
    string Nome,
    decimal Recebido,
    decimal Percentual,
    bool Encerrada,
    decimal? DividendYield12Meses,
    decimal? YieldOnCost12Meses);

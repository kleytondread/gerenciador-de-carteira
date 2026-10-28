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

/// <summary>Tela de Posições: abertas, encerradas e os fechamentos dos últimos 30 dias de cada aberta.</summary>
public record PainelPosicoesResponse(
    List<PosicaoDetalhadaResponse> Abertas,
    List<PosicaoEncerradaResponse> Encerradas,
    Dictionary<int, List<decimal>> Tendencia30Dias);

/// <summary>Último pregão do ativo e a faixa de 52 semanas (mínima e máxima dos pregões).</summary>
public record PregaoResponse(
    DateOnly Data,
    decimal Abertura,
    decimal Maxima,
    decimal Minima,
    decimal Fechamento,
    long Volume,
    decimal? VariacaoReais,
    decimal? VariacaoPercentual,
    decimal? Minima52Semanas,
    decimal? Maxima52Semanas);

/// <summary>Um pregão do ativo; valor da posição e aplicado ficam nulos antes da primeira compra.</summary>
public record PontoAtivoResponse(DateOnly Data, decimal Fechamento, decimal? ValorPosicao, decimal? ValorAplicado);

public record EventoCorporativoResponse(DateOnly Data, TipoEventoCorporativo Tipo, decimal Numerador, decimal Denominador)
{
    /// <summary>Variação da quantidade: 0,1 = +10% (bonificação 11:10), −0,9 = grupamento 1:10.</summary>
    public decimal Variacao => Numerador / Denominador - 1;
}

public record EventoCorporativoAtivoResponse(int AtivoId, string Ticker, DateOnly Data, TipoEventoCorporativo Tipo, decimal Numerador, decimal Denominador)
{
    public decimal Variacao => Numerador / Denominador - 1;
}

/// <summary>Tela de Eventos: todos os proventos recebidos, pagadores de 12 meses e eventos corporativos.</summary>
/// <param name="DataReferencia">Última cotação da carteira: fim dos períodos (12 meses, 24 meses...).</param>
public record PainelEventosResponse(
    DateOnly DataReferencia,
    List<ProventoRecebidoResponse> Proventos,
    List<PagadorProventosResponse> Pagadores12Meses,
    List<EventoCorporativoAtivoResponse> EventosCorporativos);

/// <summary>Dinheiro que entrou (compras, com taxas) e saiu (vendas, sem taxas) da carteira num mês. Bonificações não contam.</summary>
public record FluxoMensalResponse(DateOnly Mes, decimal Compras, decimal Vendas)
{
    public decimal AporteLiquido => Compras - Vendas;
}

/// <summary>Tela de Transações: todas as movimentações (da mais recente para a mais antiga) e o fluxo de cada mês.</summary>
/// <param name="Bonificacoes">Ids das compras que são bonificação (ações recebidas, não dinheiro novo).</param>
public record PainelTransacoesResponse(
    List<MovimentacaoResponse> Transacoes,
    List<FluxoMensalResponse> FluxoMensal,
    IReadOnlySet<int> Bonificacoes);

/// <summary>Tudo o que a tela de detalhe de um ativo mostra.</summary>
/// <param name="Posicao">Nula quando o ativo não está mais (ou nunca esteve) na carteira.</param>
/// <param name="Encerrada">Resultado realizado, quando o ativo já foi vendido por inteiro.</param>
/// <param name="ProventosPorAno">Pela data de pagamento, do primeiro ao último ano com provento.</param>
public record DetalheAtivoResponse(
    AtivoResponse Ativo,
    PregaoResponse? Pregao,
    PosicaoDetalhadaResponse? Posicao,
    PosicaoEncerradaResponse? Encerrada,
    List<PontoAtivoResponse> Serie,
    List<ProventoRecebidoResponse> Proventos,
    List<ProventosPeriodoResponse> ProventosPorAno,
    List<MovimentacaoResponse> Movimentacoes,
    List<EventoCorporativoResponse> EventosCorporativos);

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
/// <param name="DividendYield12Meses">Proventos por ação dos últimos 12 meses ÷ cotação atual.</param>
/// <param name="YieldOnCost12Meses">Proventos por ação dos últimos 12 meses ÷ preço médio.</param>
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
    decimal? YieldOnCost12Meses,
    CategoriaAlocacao Categoria);

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

/// <param name="DataPagamentoEstimada">O Yahoo só informa a data ex; a de pagamento é aproximada até ser corrigida.</param>
/// <param name="Quantidade">Quantidade com direito na data ex (na base de preço da fonte do provento).</param>
/// <param name="Corrigido">Tipo ou data de pagamento foram corrigidos pelo usuário.</param>
public record ProventoRecebidoResponse(
    int ProventoId,
    int AtivoId,
    string Ticker,
    TipoProvento Tipo,
    OrigemDado Origem,
    DateOnly? DataCom,
    DateOnly DataPagamento,
    bool DataPagamentoEstimada,
    decimal Quantidade,
    decimal ValorPorAcao,
    decimal Valor,
    bool Corrigido);

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

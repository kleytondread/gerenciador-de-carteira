namespace Wal.Services.Dtos;

public record PosicaoAtivoResponse(
    int AtivoId,
    string Ticker,
    string Nome,
    decimal Quantidade,
    decimal PrecoMedio,
    decimal? CotacaoAtual,
    decimal? VariacaoDiaPercentual,
    decimal? ValorPosicao,
    decimal? RentabilidadePercentual,
    Wal.Domain.Enums.TipoAtivo Tipo,
    Wal.Domain.Enums.CategoriaAlocacao Categoria);

/// <param name="ValorInvestido">Custo das posições abertas (quantidade x preço médio).</param>
/// <param name="ValorAtual">Valor de mercado das posições abertas.</param>
/// <param name="RentabilidadeReais">
/// Resultado desde o início: valor atual + vendas + proventos - compras. Inclui ganhos realizados e
/// ativos já zerados; bonificações não contam como compra.
/// </param>
/// <param name="RentabilidadePercentual">
/// Rentabilidade acumulada desde o início pelo método de cotas, como fração — o último ponto do gráfico.
/// </param>
public record ResumoCarteiraResponse(
    decimal ValorInvestido,
    decimal ValorAtual,
    decimal RentabilidadeReais,
    decimal? RentabilidadePercentual);

/// <param name="ValorCarteira">Valor de mercado da carteira no dia (patrimônio, inclui aportes).</param>
/// <param name="RentabilidadeAcumulada">
/// Rentabilidade total ponderada no tempo desde a primeira movimentação, como fração (0,1 = 10%).
/// Desconta compras e vendas, então aportes não contam como ganho. Inclui proventos pelo valor bruto, na data ex.
/// </param>
public record PontoRentabilidadeResponse(DateOnly Data, decimal ValorCarteira, decimal RentabilidadeAcumulada);

/// <summary>Números da barra superior, iguais em todas as telas.</summary>
/// <param name="Patrimonio">Valor de mercado das posições abertas.</param>
/// <param name="Resultado">Resultado desde o início em R$ (mesma conta de <see cref="ResumoCarteiraResponse.RentabilidadeReais"/>).</param>
/// <param name="ResultadoPercentual">Rentabilidade acumulada pelo método de cotas, como fração.</param>
/// <param name="VariacaoDiaReais">Variação das posições abertas entre os dois últimos pregões de cada ativo.</param>
/// <param name="VariacaoDiaPercentual">Variação do dia sobre o valor das posições no pregão anterior, como fração.</param>
/// <param name="DataPrimeiraMovimentacao">Início da carteira.</param>
/// <param name="DataUltimaCotacao">Pregão mais recente com cotação no banco.</param>
public record CabecalhoCarteiraResponse(
    decimal Patrimonio,
    decimal Resultado,
    decimal? ResultadoPercentual,
    decimal? VariacaoDiaReais,
    decimal? VariacaoDiaPercentual,
    DateOnly? DataPrimeiraMovimentacao,
    DateOnly? DataUltimaCotacao);

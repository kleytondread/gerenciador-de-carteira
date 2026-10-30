using Wal.Services.ImportacaoB3;
using static Wal.Tests.Services.ExtratoB3TestHelper;

namespace Wal.Tests.Services;

public class ExtratoMovimentacaoB3Tests
{
    [Fact]
    public void Ler_DataEmTextoNumerosEHifen()
    {
        using var arquivo = Planilha(
        [
            new("Credito", "20/03/2025", "Juros Sobre Capital Próprio", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.343m, 237.33m),
            new("Credito", "28/05/2025", "Atualização", "ITSA4 - ITAUSA S.A.", 3, null, null),
            new("Debito", "22/10/2026", "Transferência - Liquidação", "TSLA34 - TESLA INC   ", 24, 50.1m, 1202.4m)
        ]);

        var linhas = ExtratoMovimentacaoB3.Ler(arquivo);

        Assert.Equal(3, linhas.Count);
        var jcp = linhas[0];
        Assert.True(jcp.Credito);
        Assert.Equal(new DateOnly(2025, 3, 20), jcp.Data);
        Assert.Equal("BBAS3", jcp.Ticker);
        Assert.Equal(815, jcp.Quantidade);
        Assert.Equal(0.343m, jcp.PrecoUnitario);
        Assert.Equal(237.33m, jcp.Valor);

        Assert.Null(linhas[1].PrecoUnitario);
        Assert.Null(linhas[1].Valor);
        Assert.False(linhas[2].Credito);
        Assert.Equal("TSLA34", linhas[2].Ticker);
    }

    [Fact]
    public void Ler_SemAsColunasDoExtrato_Recusa()
    {
        using var arquivo = Planilha([new("Credito", "20/03/2025", "Dividendo", "BBAS3 - BB", 1, 1, 1)], semColunaValor: true);

        var erro = Assert.Throws<ExtratoMovimentacaoB3.FormatoInvalidoException>(() => ExtratoMovimentacaoB3.Ler(arquivo));
        Assert.Contains("Valor da Operação", erro.Message);
    }

    [Fact]
    public void Ler_ArquivoQueNaoEhExcel_Recusa()
    {
        using var arquivo = new MemoryStream("não é planilha"u8.ToArray());

        Assert.Throws<ExtratoMovimentacaoB3.FormatoInvalidoException>(() => ExtratoMovimentacaoB3.Ler(arquivo));
    }

    [Theory]
    [InlineData("B3SA3 - B3 S.A. - BRASIL, BOLSA, BALCÃO", "B3SA3")]
    [InlineData("CDB - CDBA264SLB2 - ITAU UNIBANCO S.A.", "CDB")]
    [InlineData("KLBN11", "KLBN11")]
    public void TickerDoProduto_CortaNoPrimeiroSeparador(string produto, string ticker) =>
        Assert.Equal(ticker, ExtratoMovimentacaoB3.TickerDoProduto(produto));
}

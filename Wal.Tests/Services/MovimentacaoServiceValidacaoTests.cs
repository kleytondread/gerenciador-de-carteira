using Microsoft.Extensions.Logging.Abstractions;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

/// <summary>Validações de quantidade/preço/taxas/data (correções da revisão de telas).</summary>
public class MovimentacaoServiceValidacaoTests : SqliteInMemoryTestBase
{
    private readonly MovimentacaoService sut;
    private readonly int ativoId;

    public MovimentacaoServiceValidacaoTests()
    {
        sut = new MovimentacaoService(Db, new PosicaoService(Db), NullLogger<MovimentacaoService>.Instance);

        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
        ativoId = ativo.Id;
    }

    [Theory]
    [InlineData(0, 10, 0)]    // quantidade zero
    [InlineData(-5, 10, 0)]   // quantidade negativa
    [InlineData(10, 0, 0)]    // preço zero
    [InlineData(10, -1, 0)]   // preço negativo
    [InlineData(10, 10, -1)]  // taxas negativas
    public async Task CriarAsync_ValoresInvalidos_RetornaFalhaDeValidacaoENaoPersiste(
        decimal quantidade, decimal preco, decimal taxas)
    {
        var resultado = await sut.CriarAsync(new CriarMovimentacaoRequest(
            ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), quantidade, preco, taxas));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Empty(Db.Movimentacoes);
    }

    [Fact]
    public async Task CriarAsync_DataFutura_RetornaFalhaDeValidacao()
    {
        var amanha = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        var resultado = await sut.CriarAsync(new CriarMovimentacaoRequest(
            ativoId, TipoMovimentacao.Compra, amanha, 10, 10));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
    }

    [Fact]
    public async Task CriarAsync_DataDeHoje_Sucesso()
    {
        var resultado = await sut.CriarAsync(new CriarMovimentacaoRequest(
            ativoId, TipoMovimentacao.Compra, DateOnly.FromDateTime(DateTime.Today), 10, 10));

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public async Task EditarAsync_QuantidadeZero_RetornaFalhaENaoAltera()
    {
        var criada = await sut.CriarAsync(new CriarMovimentacaoRequest(
            ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10));

        var resultado = await sut.EditarAsync(criada.Valor!.Id,
            new EditarMovimentacaoRequest(TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 0, 10));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Equal(10, Db.Movimentacoes.Single().Quantidade);
    }
}

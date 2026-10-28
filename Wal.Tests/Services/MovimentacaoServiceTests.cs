using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class MovimentacaoServiceTests : SqliteInMemoryTestBase
{
    private readonly MovimentacaoService sut;
    private readonly int ativoId;

    public MovimentacaoServiceTests()
    {
        sut = new MovimentacaoService(Db, new PosicaoService(Db), NullLogger<MovimentacaoService>.Instance);

        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
        ativoId = ativo.Id;
    }

    [Fact]
    public async Task CriarAsync_AtivoInexistente_RetornaNaoEncontrado()
    {
        var request = new CriarMovimentacaoRequest(999, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10);

        var resultado = await sut.CriarAsync(request);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task CriarAsync_Compra_PersisteComTaxasDefaultZeroQuandoOmitido()
    {
        var request = new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 25);

        var resultado = await sut.CriarAsync(request);

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.Valor!.Taxas);
    }

    [Fact]
    public async Task CriarAsync_VendaMaiorQuePosicaoDisponivel_RetornaFalhaDeValidacaoENaoPersiste()
    {
        await sut.CriarAsync(new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 30));

        var resultado = await sut.CriarAsync(
            new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 150, 40));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Contains("100", resultado.Erro); // quantidade disponível deve aparecer na mensagem
        Assert.Single(await Db.Movimentacoes.ToListAsync());
    }

    [Fact]
    public async Task CriarAsync_VendaDentroDaPosicaoDisponivel_Persiste()
    {
        await sut.CriarAsync(new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 30));

        var resultado = await sut.CriarAsync(
            new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 40, 45));

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, await Db.Movimentacoes.CountAsync());
    }

    [Fact]
    public async Task CriarAsync_VendaConsideraApenasMovimentacoesAteADataInformada()
    {
        // Compra em 10/jan, venda tentada em 05/jan (antes da compra) — não deve considerar a compra futura.
        await sut.CriarAsync(new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 30));

        var resultado = await sut.CriarAsync(
            new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 5), 10, 40));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorAtivoId()
    {
        var outroAtivo = Fixtures.NovoAtivo("VALE3", "Vale ON");
        Db.Ativos.Add(outroAtivo);
        await Db.SaveChangesAsync();

        await sut.CriarAsync(new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10));
        await sut.CriarAsync(new CriarMovimentacaoRequest(outroAtivo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 5, 20));

        var doAtivo = await sut.ListarAsync(ativoId);
        var todas = await sut.ListarAsync();

        Assert.Single(doAtivo);
        Assert.Equal(ativoId, doAtivo[0].AtivoId);
        Assert.Equal(2, todas.Count);
    }

    [Fact]
    public async Task EditarAsync_MovimentacaoInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.EditarAsync(999,
            new EditarMovimentacaoRequest(TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task EditarAsync_ExcluiAPropriaMovimentacaoAoRevalidarPosicao()
    {
        // Compra 100, venda total de 100 (mesma movimentação sendo editada).
        await sut.CriarAsync(new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 30));
        var vendaCriada = await sut.CriarAsync(
            new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 100, 40));

        // Editar para 100 de novo deve ser permitido (a validação exclui a própria movimentação do cálculo de disponibilidade).
        var resultadoValido = await sut.EditarAsync(vendaCriada.Valor!.Id,
            new EditarMovimentacaoRequest(TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 100, 42));
        Assert.True(resultadoValido.Sucesso);

        // Editar para mais do que a compra original (150 > 100 disponível) deve falhar.
        var resultadoInvalido = await sut.EditarAsync(vendaCriada.Valor!.Id,
            new EditarMovimentacaoRequest(TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 150, 42));
        Assert.False(resultadoInvalido.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultadoInvalido.TipoErro);
    }

    [Fact]
    public async Task ExcluirAsync_MovimentacaoInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.ExcluirAsync(999);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task ExcluirAsync_Existente_RemoveDefinitivamenteDoBanco()
    {
        var criada = await sut.CriarAsync(
            new CriarMovimentacaoRequest(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10));

        var resultado = await sut.ExcluirAsync(criada.Valor!.Id);

        Assert.True(resultado.Sucesso);
        Assert.Empty(await Db.Movimentacoes.ToListAsync());
    }
}

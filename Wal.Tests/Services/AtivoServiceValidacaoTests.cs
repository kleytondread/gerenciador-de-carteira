using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.TesouroDireto;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

/// <summary>Validações de campos obrigatórios e reativação de ativo (correções da revisão de telas).</summary>
public class AtivoServiceValidacaoTests : SqliteInMemoryTestBase
{
    private readonly Mock<IAtivoJobScheduler> jobSchedulerMock = new();
    private readonly Mock<ITesouroDiretoService> tesouroMock = new();
    private readonly AtivoService sut;

    public AtivoServiceValidacaoTests()
    {
        sut = new AtivoService(Db, jobSchedulerMock.Object, tesouroMock.Object, NullLogger<AtivoService>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CriarAsync_NomeVazio_RetornaFalhaDeValidacaoESemEnfileirar(string nome)
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest("ITUB4", nome, TipoAtivo.Acao));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Empty(Db.Ativos);
        jobSchedulerMock.Verify(j => j.EnfileirarCargaHistoricaInicial(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_TipoForaDoEnum_RetornaFalhaDeValidacao()
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest("ITUB4", "Itaú", (TipoAtivo)99));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
    }

    [Fact]
    public async Task EditarAsync_NomeVazio_RetornaFalhaENaoAltera()
    {
        var ativo = Fixtures.NovoAtivo(nome: "Petrobras PN");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.EditarAsync(ativo.Id, new EditarAtivoRequest("  ", TipoAtivo.Acao));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Equal("Petrobras PN", Db.Ativos.Single().Nome);
    }

    [Fact]
    public async Task ReativarAsync_AtivoInativo_VoltaAFicarAtivo()
    {
        var ativo = Fixtures.NovoAtivo(estaAtivo: false);
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.ReativarAsync(ativo.Id);

        Assert.True(resultado.Sucesso);
        Assert.True(Db.Ativos.Single().EstaAtivo);
    }

    [Fact]
    public async Task ReativarAsync_IdInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.ReativarAsync(999);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }
}

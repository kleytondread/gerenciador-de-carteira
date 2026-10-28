using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Jobs;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Jobs;

public class AtivoSyncJobTests : SqliteInMemoryTestBase
{
    private readonly Mock<IDadosMercadoService> dadosMercadoServiceMock = new();
    private readonly AtivoSyncJob sut;

    public AtivoSyncJobTests()
    {
        sut = new AtivoSyncJob(Db, dadosMercadoServiceMock.Object, NullLogger<AtivoSyncJob>.Instance);
    }

    [Fact]
    public async Task SincronizarTodosAtivosAsync_SemAtivos_NaoLancaENaoChamaServicoExterno()
    {
        await sut.SincronizarTodosAtivosAsync();

        dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SincronizarTodosAtivosAsync_IgnoraAtivosInativos()
    {
        var ativo1 = Fixtures.NovoAtivo("PETR4", estaAtivo: true);
        var ativo2 = Fixtures.NovoAtivo("VALE3", estaAtivo: false);
        Db.Ativos.AddRange(ativo1, ativo2);
        await Db.SaveChangesAsync();

        await sut.SincronizarTodosAtivosAsync();

        dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(ativo1.Id), Times.Once);
        dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(ativo2.Id), Times.Never);
    }

    [Fact]
    public async Task SincronizarTodosAtivosAsync_FalhaEmUmAtivo_NaoInterrompeOProcessamentoDosDemais()
    {
        // Regra da spec (seção 4.3, "Tratamento de falha"): falha em um ativo não pode
        // derrubar a sincronização dos demais no mesmo ciclo.
        var comFalha = Fixtures.NovoAtivo("ZZZZ9", "Ticker inválido");
        var comSucesso = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        Db.Ativos.AddRange(comFalha, comSucesso);
        await Db.SaveChangesAsync();

        dadosMercadoServiceMock
            .Setup(s => s.SincronizarAtivoAsync(comFalha.Id))
            .ThrowsAsync(new HttpRequestException("Yahoo Finance indisponível"));
        dadosMercadoServiceMock
            .Setup(s => s.SincronizarAtivoAsync(comSucesso.Id))
            .Returns(Task.CompletedTask);

        // Não deve propagar a exceção do ativo com falha.
        await sut.SincronizarTodosAtivosAsync();

        dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(comFalha.Id), Times.Once);
        dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(comSucesso.Id), Times.Once);
    }

    [Fact]
    public async Task SincronizarTodosAtivosAsync_ProcessaTodosOsAtivosAtivosMesmoComMultiplasFalhas()
    {
        var ativos = Enumerable.Range(1, 5)
            .Select(i => Fixtures.NovoAtivo($"TEST{i}", $"Ativo {i}"))
            .ToList();
        Db.Ativos.AddRange(ativos);
        await Db.SaveChangesAsync();

        // Ativos de índice par falham, ímpares têm sucesso.
        for (var i = 0; i < ativos.Count; i++)
        {
            var setup = dadosMercadoServiceMock.Setup(s => s.SincronizarAtivoAsync(ativos[i].Id));
            if (i % 2 == 0)
                setup.ThrowsAsync(new InvalidOperationException("Falha simulada"));
            else
                setup.Returns(Task.CompletedTask);
        }

        await sut.SincronizarTodosAtivosAsync();

        foreach (var ativo in ativos)
            dadosMercadoServiceMock.Verify(s => s.SincronizarAtivoAsync(ativo.Id), Times.Once);
    }
}

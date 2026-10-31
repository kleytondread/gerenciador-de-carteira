using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;
using Wal.Jobs;
using Wal.Services.Common;

namespace Wal.Tests.Jobs;

public class AtivoJobSchedulerTests
{
    private readonly Mock<IBackgroundJobClient> hangfire = new();
    private readonly AtualizacoesDados atualizacoes = new();
    private readonly AtivoJobScheduler sut;

    public AtivoJobSchedulerTests()
    {
        hangfire.Setup(h => h.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("1");
        sut = new AtivoJobScheduler(hangfire.Object, atualizacoes);
    }

    [Fact]
    public void EnfileirarSincronizacaoGeral_JaEmAndamento_NaoEnfileiraDeNovo()
    {
        Assert.True(sut.EnfileirarSincronizacaoGeral());
        Assert.True(atualizacoes.Atualizando);
        Assert.False(sut.EnfileirarSincronizacaoGeral());
        hangfire.Verify(h => h.Create(It.Is<Job>(j => j.Type == typeof(AtualizacaoCompletaJob)), It.IsAny<IState>()), Times.Once);

        atualizacoes.ConcluirAtualizacaoCompleta();
        Assert.True(sut.EnfileirarSincronizacaoGeral());
    }

    [Fact]
    public void EnfileirarSincronizacaoGeral_FalhaAoEnfileirar_LiberaParaTentarDeNovo()
    {
        hangfire.Setup(h => h.Create(It.IsAny<Job>(), It.IsAny<IState>())).Throws(new InvalidOperationException("banco ocupado"));

        Assert.Throws<InvalidOperationException>(() => sut.EnfileirarSincronizacaoGeral());
        Assert.False(atualizacoes.Atualizando);
    }

    [Fact]
    public void EnfileirarSincronizacaoGeral_ComJobRodando_NaoEnfileira()
    {
        atualizacoes.JobIniciado(); // ex.: o job das 20h

        Assert.True(atualizacoes.Atualizando);
        Assert.False(sut.EnfileirarSincronizacaoGeral());

        var avisos = 0;
        atualizacoes.DadosAtualizados += () => avisos++;
        atualizacoes.JobConcluido(sucesso: true);

        Assert.False(atualizacoes.Atualizando);
        Assert.Equal(1, avisos);
        Assert.True(sut.EnfileirarSincronizacaoGeral());
    }

    [Fact]
    public void JobConcluido_ComErro_LiberaOBotaoSemAvisarDadosNovos()
    {
        var avisos = 0;
        atualizacoes.DadosAtualizados += () => avisos++;

        atualizacoes.JobIniciado();
        atualizacoes.JobConcluido(sucesso: false);

        Assert.False(atualizacoes.Atualizando);
        Assert.Equal(0, avisos);
    }
}

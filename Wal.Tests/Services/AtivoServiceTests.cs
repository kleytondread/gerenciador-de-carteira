using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.TesouroDireto;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class AtivoServiceTests : SqliteInMemoryTestBase
{
    private readonly Mock<IAtivoJobScheduler> jobSchedulerMock = new();
    private readonly Mock<ITesouroDiretoService> tesouroMock = new();
    private readonly AtivoService sut;

    public AtivoServiceTests()
    {
        sut = new AtivoService(Db, jobSchedulerMock.Object, tesouroMock.Object, NullLogger<AtivoService>.Instance);
    }

    [Theory]
    [InlineData("PETR4")]
    [InlineData("HGLG11")]
    [InlineData("petr4")] // deve normalizar para maiúsculas antes de validar
    [InlineData("B5P211")] // ETF com dígito na raiz (IT NOW IMA-B5 P2) — raiz B5P2 + 11
    public async Task CriarAsync_TickerFormatoValido_Sucesso(string ticker)
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest(ticker, "Nome Teste", TipoAtivo.Acao));

        Assert.True(resultado.Sucesso);
        Assert.Equal(ticker.ToUpperInvariant(), resultado.Valor!.Ticker);
    }

    [Theory]
    [InlineData("AB1")]        // menos de 4 letras
    [InlineData("ABCDE1")]     // 5 letras
    [InlineData("PETR")]       // sem dígito
    [InlineData("PETR123")]    // 3 dígitos
    [InlineData("1234")]       // sem letras
    [InlineData("5BP211")]     // raiz não pode começar com dígito
    [InlineData("B5P2")]       // só a raiz, sem o número final
    public async Task CriarAsync_TickerFormatoInvalido_RetornaFalhaDeValidacao(string ticker)
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest(ticker, "Nome Teste", TipoAtivo.Acao));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Empty(Db.Ativos);
    }

    [Fact]
    public async Task CriarAsync_TickerDuplicado_RetornaConflito()
    {
        await sut.CriarAsync(new CriarAtivoRequest("VALE3", "Vale ON", TipoAtivo.Acao));

        var resultado = await sut.CriarAsync(new CriarAtivoRequest("VALE3", "Vale ON de novo", TipoAtivo.Acao));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
        Assert.Single(Db.Ativos);
    }

    [Fact]
    public async Task CriarAsync_Sucesso_NasceAtivoComDataCadastroAutomatica()
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest("PETR4", "Petrobras PN", TipoAtivo.Acao));

        Assert.True(resultado.Sucesso);
        Assert.True(resultado.Valor!.EstaAtivo);
        Assert.True((DateTime.UtcNow - resultado.Valor.DataCadastro) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CriarAsync_Sucesso_EnfileiraCargaHistoricaInicialApenasUmaVez()
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest("PETR4", "Petrobras PN", TipoAtivo.Acao));

        jobSchedulerMock.Verify(
            s => s.EnfileirarCargaHistoricaInicial(resultado.Valor!.Id), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_TickerInvalido_NaoEnfileiraJob()
    {
        await sut.CriarAsync(new CriarAtivoRequest("AB1", "Nome", TipoAtivo.Acao));

        jobSchedulerMock.Verify(s => s.EnfileirarCargaHistoricaInicial(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_TickerDuplicado_NaoEnfileiraJobNovamente()
    {
        await sut.CriarAsync(new CriarAtivoRequest("VALE3", "Vale ON", TipoAtivo.Acao));
        jobSchedulerMock.Invocations.Clear();

        await sut.CriarAsync(new CriarAtivoRequest("VALE3", "Vale ON dup", TipoAtivo.Acao));

        jobSchedulerMock.Verify(s => s.EnfileirarCargaHistoricaInicial(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObterPorIdAsync_Existente_RetornaOAtivo()
    {
        var ativo = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.ObterPorIdAsync(ativo.Id);

        Assert.True(resultado.Sucesso);
        Assert.Equal("PETR4", resultado.Valor!.Ticker);
    }

    [Fact]
    public async Task ObterPorIdAsync_Inexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.ObterPorIdAsync(999);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorTickerOuNomeCaseInsensitive()
    {
        Db.Ativos.AddRange(
            Fixtures.NovoAtivo("PETR4", "Petrobras PN"),
            Fixtures.NovoAtivo("VALE3", "Vale ON"));
        await Db.SaveChangesAsync();

        var porTicker = await sut.ListarAsync(busca: "petr");
        var porNome = await sut.ListarAsync(busca: "vale");

        Assert.Single(porTicker);
        Assert.Equal("PETR4", porTicker[0].Ticker);
        Assert.Single(porNome);
        Assert.Equal("VALE3", porNome[0].Ticker);
    }

    [Fact]
    public async Task ListarAsync_IncluirInativosFalse_OmiteInativos()
    {
        Db.Ativos.AddRange(
            Fixtures.NovoAtivo("PETR4", estaAtivo: true),
            Fixtures.NovoAtivo("VALE3", estaAtivo: false));
        await Db.SaveChangesAsync();

        var comInativos = await sut.ListarAsync(incluirInativos: true);
        var semInativos = await sut.ListarAsync(incluirInativos: false);

        Assert.Equal(2, comInativos.Count);
        Assert.Single(semInativos);
        Assert.Equal("PETR4", semInativos[0].Ticker);
    }

    [Fact]
    public async Task EditarAsync_IdInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.EditarAsync(999, new EditarAtivoRequest("X", TipoAtivo.Acao));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task EditarAsync_AlteraApenasNomeETipo_TickerPermaneceIntacto()
    {
        var ativo = Fixtures.NovoAtivo("PETR4", "Petrobras PN", TipoAtivo.Acao);
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.EditarAsync(ativo.Id, new EditarAtivoRequest("Petrobras Preferencial", TipoAtivo.FII));

        Assert.True(resultado.Sucesso);
        Assert.Equal("Petrobras Preferencial", resultado.Valor!.Nome);
        Assert.Equal(TipoAtivo.FII, resultado.Valor.Tipo);
        Assert.Equal("PETR4", resultado.Valor.Ticker);
    }

    [Fact]
    public async Task ExcluirAsync_ComMovimentacoesAssociadas_RetornaConflitoENaoDesativa()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 10));
        await Db.SaveChangesAsync();

        var resultado = await sut.ExcluirAsync(ativo.Id);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
        Assert.True((await Db.Ativos.FindAsync(ativo.Id))!.EstaAtivo);
    }

    [Fact]
    public async Task ExcluirAsync_SemMovimentacoes_DesativaSemRemoverORegistro()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.ExcluirAsync(ativo.Id);

        Assert.True(resultado.Sucesso);
        var doBanco = await Db.Ativos.FindAsync(ativo.Id);
        Assert.NotNull(doBanco);
        Assert.False(doBanco!.EstaAtivo);
    }

    [Fact]
    public async Task ExcluirAsync_IdInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.ExcluirAsync(999);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task ObterCotacoesAsync_AtivoInexistente_RetornaNaoEncontrado()
    {
        var resultado = await sut.ObterCotacoesAsync(999, "Max");

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.NaoEncontrado, resultado.TipoErro);
    }

    [Fact]
    public async Task ObterCotacoesAsync_SemCotacoesPersistidas_RetornaListaVazia()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.ObterCotacoesAsync(ativo.Id, "Max");

        Assert.True(resultado.Sucesso);
        Assert.Empty(resultado.Valor!);
    }

    [Theory]
    [InlineData("1M", 31)]  // últimos 30 dias a partir da última cotação + o próprio dia da última cotação
    [InlineData("6M", 182)] // 2026-01-14 até 2026-07-14 inclusive
    [InlineData("YTD", 195)] // 2026-01-01 até 2026-07-14 inclusive
    [InlineData("24M", 366)] // pede 2 anos, mas a série só tem 366 dias
    [InlineData("Início", 366)] // histórico inteiro
    [InlineData(null, 366)] // ausente => histórico inteiro
    [InlineData("periodo-invalido", 366)] // inválido => histórico inteiro
    public async Task ObterCotacoesAsync_FiltraPorPeriodoAPartirDaUltimaCotacao(string? periodo, int diasEsperados)
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var ultimaData = new DateOnly(2026, 7, 14);
        for (var i = 0; i < 366; i++)
            Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, ultimaData.AddDays(-i), fechamento: 10 + i));
        await Db.SaveChangesAsync();

        var resultado = await sut.ObterCotacoesAsync(ativo.Id, periodo);

        Assert.True(resultado.Sucesso);
        Assert.Equal(diasEsperados, resultado.Valor!.Count);
    }

    // ---- Títulos do Tesouro ----

    private static readonly TituloTesouroResponse Ipca2045 = new(
        "Tesouro IPCA+", new DateOnly(2045, 5, 15), TitulosTesouro.Ipca, "Tesouro IPCA+ 2045", "IPCA+ 2045",
        new DateOnly(2026, 10, 23), 1457.55m, 7.10m);

    private void CatalogoTesouro(params TituloTesouroResponse[] titulos) =>
        tesouroMock.Setup(t => t.ListarTitulosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(titulos.ToList());

    [Fact]
    public async Task CriarAsync_Tesouro_GeraTickerENomeDoCatalogoEEnfileiraCarga()
    {
        CatalogoTesouro(Ipca2045);

        var resultado = await sut.CriarAsync(new CriarAtivoRequest("", "", TipoAtivo.Tesouro, "Tesouro IPCA+", new DateOnly(2045, 5, 15)));

        Assert.True(resultado.Sucesso);
        var ativo = Db.Ativos.Single();
        Assert.Equal("IPCA+ 2045", ativo.Ticker);
        Assert.Equal("Tesouro IPCA+ 2045", ativo.Nome);
        Assert.Equal("Tesouro IPCA+", ativo.TituloTesouro);
        Assert.Equal(new DateOnly(2045, 5, 15), ativo.VencimentoTesouro);
        jobSchedulerMock.Verify(j => j.EnfileirarCargaHistoricaInicial(ativo.Id), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_TesouroForaDoCatalogo_RetornaValidacao()
    {
        CatalogoTesouro(Ipca2045);

        var resultado = await sut.CriarAsync(new CriarAtivoRequest("", "", TipoAtivo.Tesouro, "Tesouro IPCA+", new DateOnly(2035, 5, 15)));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Empty(Db.Ativos);
    }

    [Fact]
    public async Task CriarAsync_TesouroSemTitulo_RetornaValidacaoSemConsultarCatalogo()
    {
        var resultado = await sut.CriarAsync(new CriarAtivoRequest("", "", TipoAtivo.Tesouro));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        tesouroMock.Verify(t => t.ListarTitulosAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_TesouroComCatalogoIndisponivel_ExplicaQueOArquivoNaoFoiBaixado()
    {
        CatalogoTesouro();

        var resultado = await sut.CriarAsync(new CriarAtivoRequest("", "", TipoAtivo.Tesouro, "Tesouro IPCA+", new DateOnly(2045, 5, 15)));

        Assert.False(resultado.Sucesso);
        Assert.Contains("indisponível", resultado.Erro);
    }

    [Fact]
    public async Task CriarAsync_TesouroJaCadastrado_RetornaConflito()
    {
        CatalogoTesouro(Ipca2045);
        var request = new CriarAtivoRequest("", "", TipoAtivo.Tesouro, "Tesouro IPCA+", new DateOnly(2045, 5, 15));
        await sut.CriarAsync(request);

        var resultado = await sut.CriarAsync(request);

        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
    }

    [Theory]
    [InlineData(TipoAtivo.Acao, TipoAtivo.Tesouro)]
    [InlineData(TipoAtivo.Tesouro, TipoAtivo.ETF)]
    public async Task EditarAsync_TrocaDeTipoEntreTesouroEAtivoDaB3_RetornaValidacao(TipoAtivo atual, TipoAtivo novo)
    {
        var ativo = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", atual);
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        var resultado = await sut.EditarAsync(ativo.Id, new EditarAtivoRequest("Outro nome", novo));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
    }
}

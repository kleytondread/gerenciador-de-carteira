using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.TesouroDireto;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class ProventoServiceTests : SqliteInMemoryTestBase
{
    private readonly ProventoService sut;
    private readonly Ativo ativo;

    private static DateOnly D(int mes, int dia) => new(2026, mes, dia);

    public ProventoServiceTests()
    {
        sut = new ProventoService(Db);
        ativo = Fixtures.NovoAtivo("ITSA4", "Itaúsa PN");
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
    }

    private async Task<Provento> ImportadoAsync(DateOnly dataEx, decimal valor)
    {
        var provento = Fixtures.NovoProvento(ativo.Id, dataEx, valor);
        Db.Proventos.Add(provento);
        await Db.SaveChangesAsync();
        return provento;
    }

    // Carteira com 10 ações desde 02/01 e cotação até 30/06, para os proventos serem recebidos
    private async Task<AnaliseCarteiraService> CarteiraAsync()
    {
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, D(1, 2), 10, 10));
        Db.CotacoesHistoricas.AddRange(Fixtures.NovaCotacao(ativo.Id, D(1, 2), 10), Fixtures.NovaCotacao(ativo.Id, D(6, 30), 10));
        await Db.SaveChangesAsync();
        var indices = new IndicesReferenciaService(Db, Mock.Of<IBancoCentralClient>(), Mock.Of<IYahooFinanceClient>(), NullLogger<IndicesReferenciaService>.Instance);
        return new AnaliseCarteiraService(Db, new CarteiraService(Db, new PosicaoService(Db)), indices);
    }

    [Fact]
    public async Task LancarAsync_CriaProventoManual()
    {
        var resultado = await sut.LancarAsync(new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 10), D(4, 1), 0.25m));

        Assert.True(resultado.Sucesso);
        var gravado = await Db.Proventos.SingleAsync();
        Assert.Equal(OrigemDado.Manual, gravado.Origem);
        Assert.Equal(TipoProvento.JCP, gravado.TipoProvento);
        Assert.False(resultado.Valor!.Importado);
    }

    [Theory]
    [InlineData(0, 4, 1, "maior que zero")]
    [InlineData(0.25, 3, 1, "antes da data com")]
    public async Task LancarAsync_DadosInvalidos_Recusa(double valor, int mesPagamento, int diaPagamento, string trecho)
    {
        var resultado = await sut.LancarAsync(
            new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 10), D(mesPagamento, diaPagamento), (decimal)valor));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Contains(trecho, resultado.Erro);
    }

    [Fact]
    public async Task LancarAsync_RepetidoNaMesmaDataETipo_RecusaComConflito()
    {
        await sut.LancarAsync(new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 10), D(4, 1), 0.25m));

        var resultado = await sut.LancarAsync(new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 12), D(4, 1), 0.10m));

        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
    }

    [Fact]
    public async Task EditarAsync_Importado_GuardaCorrecaoSemMexerNaChaveOriginal()
    {
        var importado = await ImportadoAsync(D(3, 10), 0.5m);

        var resultado = await sut.EditarAsync(importado.Id, new EditarProventoRequest(TipoProvento.JCP, D(4, 1), D(1, 1), 99m));

        Assert.True(resultado.Sucesso);
        await Db.Entry(importado).ReloadAsync();
        Assert.Equal(TipoProvento.Dividendo, importado.TipoProvento);   // chave original intacta
        Assert.Equal(D(3, 10), importado.DataPagamento);
        Assert.Equal(TipoProvento.JCP, importado.TipoCorrigido);
        Assert.Equal(D(4, 1), importado.DataPagamentoCorrigida);
        Assert.Equal(0.5m, importado.ValorPorAcao);                    // valor e data com vêm da fonte
        Assert.Equal(D(3, 10), importado.DataCom);
        Assert.Equal(TipoProvento.JCP, resultado.Valor!.Tipo);
    }

    [Fact]
    public async Task EditarAsync_ImportadoVoltandoAoOriginal_LimpaACorrecao()
    {
        var importado = await ImportadoAsync(D(3, 10), 0.5m);
        await sut.EditarAsync(importado.Id, new EditarProventoRequest(TipoProvento.JCP, D(4, 1)));

        await sut.EditarAsync(importado.Id, new EditarProventoRequest(TipoProvento.Dividendo, D(3, 10)));

        await Db.Entry(importado).ReloadAsync();
        Assert.Null(importado.TipoCorrigido);
        Assert.Null(importado.DataPagamentoCorrigida);
    }

    [Fact]
    public async Task EditarAsync_Manual_AlteraTudo()
    {
        var lancado = (await sut.LancarAsync(new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 10), D(4, 1), 0.25m))).Valor!;

        await sut.EditarAsync(lancado.Id, new EditarProventoRequest(TipoProvento.Dividendo, D(4, 15), D(3, 20), 0.30m));

        var gravado = await Db.Proventos.SingleAsync();
        Assert.Equal((TipoProvento.Dividendo, D(4, 15), D(3, 20), 0.30m),
            (gravado.TipoProvento, gravado.DataPagamento, gravado.DataCom!.Value, gravado.ValorPorAcao));
    }

    [Fact]
    public async Task Correcao_SobreviveASincronizacaoDoYahoo()
    {
        var dataEx = D(3, 10);
        var importado = await ImportadoAsync(dataEx, 0.5m);
        await sut.EditarAsync(importado.Id, new EditarProventoRequest(TipoProvento.JCP, D(4, 1)));

        // Sincronização trazendo o mesmo provento, com valor revisado
        var yahoo = new Mock<IYahooFinanceClient>();
        var unix = new DateTimeOffset(dataEx.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        yahoo.Setup(y => y.ObterDadosAsync("ITSA4", It.IsAny<long>()))
            .ReturnsAsync(new YahooQuoteResult { Symbol = "ITSA4", Dividends = [new YahooDividend { Date = unix, Amount = 0.55m }] });
        var sincronizacao = new DadosMercadoService(Db, yahoo.Object, Mock.Of<ITesouroDiretoService>(), NullLogger<DadosMercadoService>.Instance);
        await sincronizacao.SincronizarAtivoAsync(ativo.Id);

        var gravado = await Db.Proventos.AsNoTracking().SingleAsync();
        Assert.Equal(0.55m, gravado.ValorPorAcao);                // a fonte continua atualizando o valor
        Assert.Equal(TipoProvento.JCP, gravado.TipoCorrigido);    // e a correção continua lá
        Assert.Equal(D(4, 1), gravado.DataPagamentoCorrigida);
    }

    [Fact]
    public async Task ExcluirAsync_Importado_OcultaERestaurarTrazDeVolta()
    {
        var importado = await ImportadoAsync(D(3, 10), 0.5m);
        var analise = await CarteiraAsync();

        await sut.ExcluirAsync(importado.Id);

        Assert.True(await Db.Proventos.AnyAsync());               // não apaga: a sincronização recriaria
        Assert.Empty(await analise.ObterProventosRecebidosAsync());
        Assert.Single(await sut.ListarOcultosAsync());

        await sut.RestaurarAsync(importado.Id);

        Assert.Single(await analise.ObterProventosRecebidosAsync());
        Assert.Empty(await sut.ListarOcultosAsync());
    }

    [Fact]
    public async Task ExcluirAsync_Manual_Apaga()
    {
        var lancado = (await sut.LancarAsync(new LancarProventoRequest(ativo.Id, TipoProvento.JCP, D(3, 10), D(4, 1), 0.25m))).Valor!;

        await sut.ExcluirAsync(lancado.Id);

        Assert.False(await Db.Proventos.AnyAsync());
    }

    [Fact]
    public async Task ProventoCorrigido_AparecemComTipoEPagamentoCorrigidosNosRecebidos()
    {
        var importado = await ImportadoAsync(D(3, 10), 0.5m);
        await sut.EditarAsync(importado.Id, new EditarProventoRequest(TipoProvento.JCP, D(4, 1)));
        var analise = await CarteiraAsync();

        var recebido = Assert.Single(await analise.ObterProventosRecebidosAsync());

        Assert.Equal(importado.Id, recebido.ProventoId);
        Assert.Equal(TipoProvento.JCP, recebido.Tipo);
        Assert.Equal(D(4, 1), recebido.DataPagamento);
        Assert.False(recebido.DataPagamentoEstimada);
        Assert.True(recebido.Corrigido);
        Assert.Equal(5m, recebido.Valor);
    }

    [Fact]
    public async Task ObterPainelEventosAsync_ProventosPagadoresEEventos()
    {
        await ImportadoAsync(D(3, 10), 0.5m);
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, D(2, 2), 11, 10));
        await Db.SaveChangesAsync();
        var analise = await CarteiraAsync();

        var painel = await analise.ObterPainelEventosAsync();

        Assert.NotNull(painel);
        Assert.Equal(D(6, 30), painel.DataReferencia);
        Assert.Single(painel.Proventos);
        Assert.Equal(5m, Assert.Single(painel.Pagadores12Meses).Recebido);
        var evento = Assert.Single(painel.EventosCorporativos);
        Assert.Equal("ITSA4", evento.Ticker);
        Assert.Equal(TipoEventoCorporativo.Bonificacao, evento.Tipo);
    }
}

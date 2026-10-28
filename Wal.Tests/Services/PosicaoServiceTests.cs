using Wal.Domain.Enums;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class PosicaoServiceTests : SqliteInMemoryTestBase
{
    private readonly PosicaoService sut;
    private readonly int ativoId;

    public PosicaoServiceTests()
    {
        sut = new PosicaoService(Db);

        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
        ativoId = ativo.Id;
    }

    [Fact]
    public async Task SemMovimentacoes_RetornaQuantidadeEPrecoMedioZero()
    {
        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(0, posicao.Quantidade);
        Assert.Equal(0, posicao.PrecoMedio);
    }

    [Fact]
    public async Task CompraUnica_RetornaQuantidadeEPrecoDaCompra()
    {
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(
            ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), quantidade: 100, precoUnitario: 30));
        await Db.SaveChangesAsync();

        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(100, posicao.Quantidade);
        Assert.Equal(30, posicao.PrecoMedio);
    }

    [Fact]
    public async Task DuasCompras_CalculaPrecoMedioPonderado()
    {
        // 100 @ 30 + 100 @ 50 => 200 unidades, preço médio (100*30 + 100*50)/200 = 40
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 30),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 20), 100, 50));
        await Db.SaveChangesAsync();

        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(200, posicao.Quantidade);
        Assert.Equal(40, posicao.PrecoMedio);
    }

    [Fact]
    public async Task CompraSeguidaDeVendaParcial_QuantidadeReduzMasPrecoMedioNaoMuda()
    {
        // Regra clássica de preço médio: venda não altera o preço médio, só a quantidade.
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 30),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 20), 40, 999 /* preço de venda não afeta preço médio */));
        await Db.SaveChangesAsync();

        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(60, posicao.Quantidade);
        Assert.Equal(30, posicao.PrecoMedio);
    }

    [Fact]
    public async Task VendaTotalSeguidaDeNovaCompra_PrecoMedioRecalculadoDoZero()
    {
        // Zera a posição via venda total, depois compra de novo — preço médio antigo não deve "vazar".
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 5), 10, 100),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 10, 150),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 15), 5, 50));
        await Db.SaveChangesAsync();

        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(5, posicao.Quantidade);
        Assert.Equal(50, posicao.PrecoMedio);
    }

    [Fact]
    public async Task AteData_IgnoraMovimentacoesPosterioresADataInformada()
    {
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 30),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 2, 10), 50, 60));
        await Db.SaveChangesAsync();

        var posicaoAntes = await sut.CalcularPosicaoAsync(ativoId, ateData: new DateOnly(2026, 1, 31));
        var posicaoDepois = await sut.CalcularPosicaoAsync(ativoId, ateData: new DateOnly(2026, 2, 10));

        Assert.Equal(100, posicaoAntes.Quantidade);
        Assert.Equal(30, posicaoAntes.PrecoMedio);
        Assert.Equal(150, posicaoDepois.Quantidade);
    }

    [Fact]
    public async Task ExcluirMovimentacaoId_IgnoraAMovimentacaoInformadaNoCalculo()
    {
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 30));
        await Db.SaveChangesAsync();

        var vendaAExcluir = Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 20), 40, 999);
        Db.Movimentacoes.Add(vendaAExcluir);
        await Db.SaveChangesAsync();

        // Sem exclusão: 100 - 40 = 60
        var comVenda = await sut.CalcularPosicaoAsync(ativoId);
        // Excluindo a própria venda do cálculo (simula revalidação ao editá-la): volta a 100
        var semVenda = await sut.CalcularPosicaoAsync(ativoId, excluirMovimentacaoId: vendaAExcluir.Id);

        Assert.Equal(60, comVenda.Quantidade);
        Assert.Equal(100, semVenda.Quantidade);
    }

    [Fact]
    public async Task MovimentacoesNaMesmaData_SaoAplicadasNaOrdemDeInsercao()
    {
        // Duas movimentações no mesmo dia: a ordem de desempate é por Id (ordem de inserção).
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Compra, new DateOnly(2026, 1, 10), 100, 20),
            Fixtures.NovaMovimentacao(ativoId, TipoMovimentacao.Venda, new DateOnly(2026, 1, 10), 100, 999));
        await Db.SaveChangesAsync();

        var posicao = await sut.CalcularPosicaoAsync(ativoId);

        Assert.Equal(0, posicao.Quantidade);
    }
}

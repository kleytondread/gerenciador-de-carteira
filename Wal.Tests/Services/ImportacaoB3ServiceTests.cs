using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wal.Domain.Enums;
using Wal.Services.Dtos;
using Wal.Services.ImportacaoB3;
using Wal.Tests.TestHelpers;
using static Wal.Tests.Services.ExtratoB3TestHelper;

namespace Wal.Tests.Services;

public class ImportacaoB3ServiceTests : SqliteInMemoryTestBase
{
    private readonly ImportacaoB3Service sut;
    private readonly int bbas;

    public ImportacaoB3ServiceTests()
    {
        sut = new ImportacaoB3Service(Db, NullLogger<ImportacaoB3Service>.Instance);
        var ativo = Fixtures.NovoAtivo("BBAS3", "Banco do Brasil ON");
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
        bbas = ativo.Id;

        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(bbas, TipoMovimentacao.Compra, new DateOnly(2025, 1, 2), 815, 25));
        // Cotações no começo e no fim: o motor da carteira só anda até a última cotação
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(bbas, new DateOnly(2025, 1, 2), 25),
            Fixtures.NovaCotacao(bbas, new DateOnly(2025, 3, 11), 26),
            Fixtures.NovaCotacao(bbas, new DateOnly(2025, 12, 31), 27));
        // Yahoo: dividendo + JCP + atualização da data ex 12/03/2025 numa linha só, como "Dividendo"
        Db.Proventos.Add(Fixtures.NovoProvento(bbas, new DateOnly(2025, 3, 12), 521.94m / 815));
        Db.SaveChanges();
    }

    private static MemoryStream Extrato() => Planilha(
    [
        new("Credito", "21/03/2025", "Juros Sobre Capital Próprio", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.149m, 103.47m),
        new("Credito", "20/03/2025", "Dividendo", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.136m, 110.84m),
        new("Credito", "20/03/2025", "Juros Sobre Capital Próprio", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.343m, 237.33m),
        new("Credito", "20/03/2025", "Rendimento", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.003m, 2.24m),
        new("Credito", "20/03/2025", "Rendimento", "BBAS3 - BANCO DO BRASIL S/A", 815, 0.007m, 5.64m),
        new("Credito", "28/01/2025", "Leilão de Fração", "ITSA4 - ITAUSA S/A", 0.1m, 9.062m, 0.9m),
        new("Credito", "03/01/2025", "Transferência - Liquidação", "BBAS3 - BANCO DO BRASIL S/A", 815, 25m, 20375m),
        new("Credito", "14/03/2025", "Dividendo", "VALE3 - VALE S.A.", 291, 2.142m, 623.27m)
    ]);

    [Fact]
    public async Task Previa_MostraSubstituicaoInformativosENaoCadastrados()
    {
        await using var arquivo = Extrato();

        var previa = (await sut.PreviaAsync([("extrato.xlsx", arquivo)])).Valor!;

        Assert.Equal(new DateOnly(2025, 1, 3), previa.Inicio);
        Assert.Equal(new DateOnly(2025, 3, 21), previa.Fim);
        var item = Assert.Single(previa.Itens, i => i.Aplicavel);
        Assert.Equal(AcaoImportacaoB3.Substituir, item.Acao);
        Assert.Equal(3, item.NovosProventos.Count);
        Assert.Equal(["VALE3"], previa.TickersNaoCadastrados);
        Assert.Contains(previa.Informativos, l => l.Movimentacao == "Leilão de Fração");
        Assert.Equal(1, previa.Liquidacoes);
        // Nada foi gravado ainda
        Assert.False(await Db.Proventos.AnyAsync(p => p.Origem == OrigemDado.ExtratoB3));
    }

    [Fact]
    public async Task Aplicar_OcultaOYahooECriaUmProventoPorTipoComAsParcelas()
    {
        await using var arquivo = Extrato();
        var previa = (await sut.PreviaAsync([("extrato.xlsx", arquivo)])).Valor!;

        var resumo = await sut.AplicarAsync(previa);

        Assert.True(resumo.Sucesso, resumo.Erro);
        Assert.Null(resumo.Valor!.Backup); // banco em memória
        Assert.True((await Db.Proventos.SingleAsync(p => p.Origem == OrigemDado.YahooFinance)).Oculto);
        var novos = await Db.Proventos.AsNoTracking().Include(p => p.Pagamentos).Where(p => p.Origem == OrigemDado.ExtratoB3).ToListAsync();
        Assert.Equal(3, novos.Count);
        var jcp = novos.Single(p => p.TipoProvento == TipoProvento.JCP);
        Assert.Equal(new DateOnly(2025, 3, 11), jcp.DataCom);
        Assert.Equal(2, jcp.Pagamentos.Count);
        Assert.Equal(340.80m, jcp.Pagamentos.Sum(p => p.ValorLiquido));
        Assert.Equal(new DateOnly(2025, 3, 20), jcp.DataPagamento);
        Assert.Equal(110.84m, novos.Single(p => p.TipoProvento == TipoProvento.Dividendo).Pagamentos.Single().ValorLiquido);
    }

    [Fact]
    public async Task Reimportar_OMesmoExtrato_NaoAlteraNada()
    {
        await using (var arquivo = Extrato())
            await sut.AplicarAsync((await sut.PreviaAsync([("extrato.xlsx", arquivo)])).Valor!);

        await using var deNovo = Extrato();
        var previa = (await sut.PreviaAsync([("extrato.xlsx", deNovo)])).Valor!;

        Assert.Equal(0, previa.Alteracoes);
        Assert.Equal(3, previa.Itens.Count(i => i.Acao == AcaoImportacaoB3.Confirmado));
    }

    [Fact]
    public async Task DoisArquivosComPeriodoRepetido_NaoDuplicamCreditos()
    {
        await using var um = Extrato();
        await using var outro = Extrato();

        var previa = (await sut.PreviaAsync([("a.xlsx", um), ("b.xlsx", outro)])).Valor!;

        Assert.Equal(5, Assert.Single(previa.Itens, i => i.Aplicavel).Creditos.Count);
    }

    [Fact]
    public async Task Aplicar_ComBancoAlteradoDepoisDaPrevia_Recusa()
    {
        await using var arquivo = Extrato();
        var previa = (await sut.PreviaAsync([("extrato.xlsx", arquivo)])).Valor!;
        (await Db.Proventos.SingleAsync()).Oculto = true;
        await Db.SaveChangesAsync();

        var resultado = await sut.AplicarAsync(previa);

        Assert.False(resultado.Sucesso);
        Assert.Equal(Wal.Services.Common.TipoErroOperacao.Conflito, resultado.TipoErro);
    }
}

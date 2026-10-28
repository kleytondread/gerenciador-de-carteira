using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class CarteiraServiceTests : SqliteInMemoryTestBase
{
    private readonly CarteiraService sut;

    public CarteiraServiceTests()
    {
        sut = new CarteiraService(Db, new PosicaoService(Db));
    }

    [Fact]
    public async Task ObterPosicoesAsync_ExcluiAtivosComQuantidadeZerada()
    {
        var comPosicao = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        var zerado = Fixtures.NovoAtivo("VALE3", "Vale ON");
        Db.Ativos.AddRange(comPosicao, zerado);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(comPosicao.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 30),
            Fixtures.NovaMovimentacao(zerado.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 30),
            Fixtures.NovaMovimentacao(zerado.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 5), 10, 35));
        await Db.SaveChangesAsync();

        var posicoes = await sut.ObterPosicoesAsync();

        Assert.Single(posicoes);
        Assert.Equal("PETR4", posicoes[0].Ticker);
    }

    [Fact]
    public async Task ObterPosicoesAsync_SemCotacaoPersistida_RetornaCamposDeMercadoNulos()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 30));
        await Db.SaveChangesAsync();

        var posicoes = await sut.ObterPosicoesAsync();

        var posicao = Assert.Single(posicoes);
        Assert.Equal(10, posicao.Quantidade);
        Assert.Equal(30, posicao.PrecoMedio);
        Assert.Null(posicao.CotacaoAtual);
        Assert.Null(posicao.ValorPosicao);
        Assert.Null(posicao.RentabilidadePercentual);
        Assert.Null(posicao.VariacaoDiaPercentual);
    }

    [Fact]
    public async Task ObterPosicoesAsync_CalculaValorEVariacaoEDiaComAsDuasUltimasCotacoes()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 40));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 10), fechamento: 45),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 9), fechamento: 50));
        await Db.SaveChangesAsync();

        var posicao = Assert.Single(await sut.ObterPosicoesAsync());

        Assert.Equal(45, posicao.CotacaoAtual);
        Assert.Equal((45m - 50m) / 50m, posicao.VariacaoDiaPercentual);
        Assert.Equal(450, posicao.ValorPosicao);
        Assert.Equal((45m - 40m) / 40m, posicao.RentabilidadePercentual);
    }

    [Fact]
    public async Task ObterResumoAsync_SomaValorInvestidoEValorAtualDeTodasAsPosicoes()
    {
        var a1 = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        var a2 = Fixtures.NovoAtivo("VALE3", "Vale ON");
        Db.Ativos.AddRange(a1, a2);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(a1.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 40),  // investido 400
            Fixtures.NovaMovimentacao(a2.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 5, 100));  // investido 500
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(a1.Id, new DateOnly(2026, 1, 10), fechamento: 50), // valor atual 500
            Fixtures.NovaCotacao(a2.Id, new DateOnly(2026, 1, 10), fechamento: 90)); // valor atual 450
        await Db.SaveChangesAsync();

        var resumo = await sut.ObterResumoAsync();

        Assert.Equal(900, resumo.ValorInvestido);
        Assert.Equal(950, resumo.ValorAtual);
        Assert.Equal(50, resumo.RentabilidadeReais);
        Assert.Equal(50m / 900m, resumo.RentabilidadePercentual);
    }

    [Fact]
    public async Task ObterResumoAsync_ResultadoIncluiVendasProventosEAtivosZerados()
    {
        var aberto = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        var zerado = Fixtures.NovoAtivo("VALE3", "Vale ON");
        Db.Ativos.AddRange(aberto, zerado);
        await Db.SaveChangesAsync();

        // PETR4: compra 10 @ 20, vende 5 @ 30 (lucro realizado de 50), provento de R$ 1 nas 10 ações (R$ 10),
        //        5 restantes valem 25 hoje.
        // VALE3: compra 10 @ 50 e vende tudo a 40 (prejuízo realizado de 100); some das posições abertas.
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(aberto.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(aberto.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 3), 5, 30),
            Fixtures.NovaMovimentacao(zerado.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 50),
            Fixtures.NovaMovimentacao(zerado.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 2), 10, 40));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(aberto.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(aberto.Id, new DateOnly(2026, 1, 3), fechamento: 30),
            Fixtures.NovaCotacao(aberto.Id, new DateOnly(2026, 1, 4), fechamento: 25),
            Fixtures.NovaCotacao(zerado.Id, new DateOnly(2026, 1, 1), fechamento: 50),
            Fixtures.NovaCotacao(zerado.Id, new DateOnly(2026, 1, 2), fechamento: 40));
        Db.Proventos.Add(Fixtures.NovoProvento(aberto.Id, new DateOnly(2026, 1, 2), valorPorAcao: 1));
        await Db.SaveChangesAsync();

        var resumo = await sut.ObterResumoAsync();
        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(100, resumo.ValorInvestido);  // 5 x 20, só a posição aberta
        Assert.Equal(125, resumo.ValorAtual);      // 5 x 25
        // 125 (hoje) + 150 + 400 (vendas) + 10 (provento) - 200 - 500 (compras) = -15
        Assert.Equal(-15, resumo.RentabilidadeReais);
        Assert.Equal(pontos[^1].RentabilidadeAcumulada, resumo.RentabilidadePercentual); // o mesmo número do gráfico
    }

    [Fact]
    public async Task ObterResumoAsync_BonificacaoNaoContaComoCompra()
    {
        var ativo = Fixtures.NovoAtivo("EGIE3", "Engie");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Compra 100 @ 42 e recebe 40 ações bonificadas (lançadas a R$ 6 para o IR); hoje valem 30 cada
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 42),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 3), 40, 6));
        Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 30));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 3), 3.5m, 2.5m));
        await Db.SaveChangesAsync();

        var resumo = await sut.ObterResumoAsync();

        Assert.Equal(0, resumo.RentabilidadeReais); // 140 x 30 - 4200: os R$ 240 da bonificação não saíram do bolso
    }

    [Fact]
    public async Task ObterResumoAsync_CarteiraVazia_RentabilidadePercentualNulaSemDividirPorZero()
    {
        var resumo = await sut.ObterResumoAsync();

        Assert.Equal(0, resumo.ValorInvestido);
        Assert.Equal(0, resumo.ValorAtual);
        Assert.Equal(0, resumo.RentabilidadeReais);
        Assert.Null(resumo.RentabilidadePercentual);
    }

    [Fact]
    public async Task ObterCabecalhoAsync_SomaVariacaoDoDiaDasPosicoesEDevolveDatasDeReferencia()
    {
        var a1 = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        var a2 = Fixtures.NovoAtivo("VALE3", "Vale ON");
        Db.Ativos.AddRange(a1, a2);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(a1.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 2), 10, 40),
            Fixtures.NovaMovimentacao(a2.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 5), 5, 100));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(a1.Id, new DateOnly(2026, 1, 9), fechamento: 50),   // ontem 500
            Fixtures.NovaCotacao(a1.Id, new DateOnly(2026, 1, 12), fechamento: 55),  // hoje 550
            Fixtures.NovaCotacao(a2.Id, new DateOnly(2026, 1, 9), fechamento: 100),  // ontem 500
            Fixtures.NovaCotacao(a2.Id, new DateOnly(2026, 1, 12), fechamento: 90)); // hoje 450
        await Db.SaveChangesAsync();

        var cabecalho = await sut.ObterCabecalhoAsync();

        Assert.Equal(1000, cabecalho.Patrimonio);
        Assert.Equal(0, cabecalho.VariacaoDiaReais);       // +50 e -50
        Assert.Equal(0, cabecalho.VariacaoDiaPercentual);
        Assert.Equal(new DateOnly(2026, 1, 2), cabecalho.DataPrimeiraMovimentacao);
        Assert.Equal(new DateOnly(2026, 1, 12), cabecalho.DataUltimaCotacao);
    }

    [Fact]
    public async Task ObterCabecalhoAsync_VariacaoPercentualUsaValorDoPregaoAnterior()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 2), 10, 40));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 9), fechamento: 50),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 12), fechamento: 45));
        await Db.SaveChangesAsync();

        var cabecalho = await sut.ObterCabecalhoAsync();

        Assert.Equal(-50, cabecalho.VariacaoDiaReais);
        Assert.Equal(-0.1m, cabecalho.VariacaoDiaPercentual);
    }

    [Fact]
    public async Task ObterCabecalhoAsync_CarteiraVazia_SemVariacaoNemDatas()
    {
        var cabecalho = await sut.ObterCabecalhoAsync();

        Assert.Equal(0, cabecalho.Patrimonio);
        Assert.Null(cabecalho.VariacaoDiaReais);
        Assert.Null(cabecalho.VariacaoDiaPercentual);
        Assert.Null(cabecalho.DataPrimeiraMovimentacao);
        Assert.Null(cabecalho.DataUltimaCotacao);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_SemMovimentacoes_RetornaListaVazia()
    {
        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Empty(pontos);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_ComeçaNaDataDaPrimeiraMovimentacaoEAcumulaQuantidade()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Compra 10 em 01/jan, cotação 20 nesse dia; compra mais 10 em 03/jan (total 20), cotação 25.
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 3), 10, 25));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 22),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 25));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(3, pontos.Count); // 01, 02 e 03/jan
        Assert.Equal(new DateOnly(2026, 1, 1), pontos[0].Data);
        Assert.Equal(200, pontos[0].ValorCarteira);  // 10 * 20
        Assert.Equal(220, pontos[1].ValorCarteira);  // ainda 10 unidades, cotação sobe pra 22
        Assert.Equal(500, pontos[2].ValorCarteira);  // 20 unidades * 25
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_DiaSemPregao_UsaUltimaCotacaoConhecida()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Compra em sexta (01/jan); só há cotação de sexta e de segunda (04/jan) — sábado/domingo sem pregão.
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 4), fechamento: 30));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(4, pontos.Count); // 01 (sex), 02 (sáb), 03 (dom), 04 (seg)
        Assert.Equal(200, pontos[0].ValorCarteira); // sexta: cotação do dia
        Assert.Equal(200, pontos[1].ValorCarteira); // sábado: repete última cotação conhecida (sexta)
        Assert.Equal(200, pontos[2].ValorCarteira); // domingo: idem
        Assert.Equal(300, pontos[3].ValorCarteira); // segunda: nova cotação
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_VendaTotalZeraContribuicaoDoAtivoNosDiasSeguintes()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 2), 10, 25));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 25));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(200, pontos[0].ValorCarteira);
        Assert.Equal(0, pontos[1].ValorCarteira); // posição zerada não contribui mais para o valor da carteira
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_AporteNaoContaComoRentabilidade()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // 01: compra 10 @ 20 (fecha 20) | 02: fecha 22 (+10%) | 03: aporte de 10 @ 22 (fecha 22) | 04: fecha 24,2 (+10%)
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 3), 10, 22));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 22),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 22),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 4), fechamento: 24.2m));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada);
        Assert.Equal(0.1m, pontos[1].RentabilidadeAcumulada);
        Assert.Equal(440, pontos[2].ValorCarteira);              // patrimônio dobra com o aporte...
        Assert.Equal(0.1m, pontos[2].RentabilidadeAcumulada);    // ...mas a rentabilidade não muda
        Assert.Equal(484, pontos[3].ValorCarteira);
        Assert.Equal(0.21m, pontos[3].RentabilidadeAcumulada);   // 1,1 x 1,1 - 1
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_DiaDaCompraUsaPrecoPagoComoBase()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Comprou a 20 e o papel fechou a 21 no mesmo dia: +5% já no primeiro ponto
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20));
        Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 21));
        await Db.SaveChangesAsync();

        var ponto = Assert.Single(await sut.ObterRentabilidadeHistoricaAsync());

        Assert.Equal(0.05m, ponto.RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_VendaTotalRealizaGanhoEMantemRentabilidadeDepois()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 2), 10, 25));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 25),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 30));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(0.25m, pontos[1].RentabilidadeAcumulada); // vendeu a 25 o que comprou a 20
        Assert.Equal(0, pontos[2].ValorCarteira);
        Assert.Equal(0.25m, pontos[2].RentabilidadeAcumulada); // sem posição, a alta do papel não conta
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_AtivoSemCotacao_UsaPrecoDaUltimaMovimentacao()
    {
        var comCotacao = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        var semCotacao = Fixtures.NovoAtivo("SULA11", "SulAmérica");
        Db.Ativos.AddRange(comCotacao, semCotacao);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(comCotacao.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20),
            Fixtures.NovaMovimentacao(semCotacao.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 2), 5, 30));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(comCotacao.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(comCotacao.Id, new DateOnly(2026, 1, 2), fechamento: 20));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(350, pontos[1].ValorCarteira);           // 10 x 20 + 5 x 30 (preço da compra)
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);   // sem cotação, não vira perda de 100%
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_ProventoNaDataExCompensaQuedaDaCotacao()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Paga R$ 1 por ação com data ex em 02/jan; a cotação cai de 20 para 19 nesse dia
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 19));
        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 2), valorPorAcao: 1));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(190, pontos[1].ValorCarteira);          // patrimônio não inclui o provento
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);  // (190 + 10) / 200 - 1: a queda foi o provento
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_CompraNaDataExNaoRecebeProvento()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 2), 10, 20));
        Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 20));
        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 2), valorPorAcao: 1));
        await Db.SaveChangesAsync();

        var ponto = Assert.Single(await sut.ObterRentabilidadeHistoricaAsync());

        Assert.Equal(0m, ponto.RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_ProventoComDataCom_EntraNoDiaSeguinte()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Lançamento manual grava a data com (01/jan): quem tinha no fim de 01/jan recebe, e o ganho entra em 02/jan
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 19));
        Db.Proventos.Add(new Provento
        {
            AtivoId = ativo.Id, TipoProvento = TipoProvento.JCP, Origem = OrigemDado.Manual,
            DataCom = new DateOnly(2026, 1, 1), DataPagamento = new DateOnly(2026, 1, 20), ValorPorAcao = 1
        });
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada);
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_ProventoManualSomaAoDoYahoo()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Yahoo: R$ 1 com data ex 02/jan. Manual (ex.: correção monetária): R$ 0,50 com data com 02/jan (ex 03/jan)
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 20));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 19),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 19));
        Db.Proventos.AddRange(
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 2), valorPorAcao: 1),
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 2), valorPorAcao: 0.5m, tipo: TipoProvento.Rendimento, origem: OrigemDado.Manual));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);                          // Yahoo compensa a queda
        Assert.Equal(0.0263m, Math.Round(pontos[2].RentabilidadeAcumulada, 4));      // + R$ 5 sobre R$ 190
    }

    // ---- Conversão para a base de preço do Yahoo (desdobramentos, grupamentos e bonificações) ----

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_BonificacaoRegistrada_ConverteCompraAnteriorSemPerdaFalsa()
    {
        var ativo = Fixtures.NovoAtivo("EGIE3", "Engie");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Comprou 100 a R$ 42; o Yahoo mostra R$ 30 porque ajustou o histórico pela bonificação de 40% de 03/jan.
        // Em 03/jan entram as 40 ações bonificadas, lançadas com o custo de R$ 6 (para o IR).
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 42),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 3), 40, 6));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 30),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 30),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 3), fechamento: 30));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 3), 3.5m, 2.5m));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(4200, pontos[0].ValorCarteira);         // 140 (base nova) x 30 = o que valia de fato
        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada);  // sem conversão, daria -28,6% no dia da compra
        Assert.Equal(4200, pontos[2].ValorCarteira);
        Assert.Equal(0m, pontos[2].RentabilidadeAcumulada);  // a bonificação não é ganho nem aporte
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_DesdobramentoJaLancadoNaBaseNova_NaoConverteDeNovo()
    {
        var ativo = Fixtures.NovoAtivo("BBAS3", "Banco do Brasil");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Compra importada já multiplicada pelo desdobramento 2:1 de 02/jan (como no extrato ajustado)
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 200, 15));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 15),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 15));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 2), 2, 1));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(3000, pontos[1].ValorCarteira); // não vira 6000
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_EventoDepoisDaVenda_ConverteAsOperacoesAnteriores()
    {
        var ativo = Fixtures.NovoAtivo("ITSA4", "Itaúsa");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Comprou e vendeu a R$ 22; o Yahoo mostra R$ 20 por causa de uma bonificação de 10% em 05/jan
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 22),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Venda, new DateOnly(2026, 1, 2), 10, 22));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 20));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 5), 11, 10));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(220, pontos[0].ValorCarteira);         // 11 (base nova) x 20
        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada); // sem conversão, -9,1% no dia da compra
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_BonificacaoSoEmFracao_FracaoViraVenda()
    {
        var ativo = Fixtures.NovoAtivo("ITSA4", "Itaúsa");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // 10 ações e bonificação de 5% em 02/jan: 0,5 ação, vendida em leilão. Comprou a 21; o Yahoo mostra
        // 20 antes do evento (21 / 1,05) e 20 depois.
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 10, 21));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 20),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 20));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 2), 105, 100));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(210, pontos[0].ValorCarteira);         // 10,5 x 20
        Assert.Equal(200, pontos[1].ValorCarteira);         // volta a 10 ações; a fração virou R$ 10
        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada);
    }

    [Fact]
    public async Task ObterRentabilidadeHistoricaAsync_ProventoDoYahooUsaQuantidadeNaBaseNova()
    {
        var ativo = Fixtures.NovoAtivo("EGIE3", "Engie");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        // Provento do Yahoo de R$ 1 (já ajustado pela bonificação de 40%) com data ex 02/jan:
        // tem direito quem tinha 100 ações, que equivalem a 140 na base do Yahoo
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 1), 100, 42),
            Fixtures.NovaMovimentacao(ativo.Id, TipoMovimentacao.Compra, new DateOnly(2026, 1, 5), 40, 6));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 1), fechamento: 30),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 2), fechamento: 29));
        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 2), valorPorAcao: 1));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(ativo.Id, new DateOnly(2026, 1, 5), 3.5m, 2.5m));
        await Db.SaveChangesAsync();

        var pontos = await sut.ObterRentabilidadeHistoricaAsync();

        Assert.Equal(0m, pontos[1].RentabilidadeAcumulada); // (140 x 29 + 140) / 4200 - 1
    }
}

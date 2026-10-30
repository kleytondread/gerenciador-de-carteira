using Wal.Domain.Enums;
using Wal.Services.Dtos;
using Wal.Services.ImportacaoB3;
using static Wal.Services.ImportacaoB3.ConciliacaoProventosB3;

namespace Wal.Tests.Services;

/// <summary>Casos tirados do extrato real da B3 de 2025–2026 (valores e quantidades como vieram).</summary>
public class ConciliacaoProventosB3Tests
{
    private static readonly DateOnly Inicio = new(2025, 1, 2);
    private static readonly DateOnly Fim = new(2026, 10, 28);
    private int linha;

    private static DateOnly D(int ano, int mes, int dia) => new(ano, mes, dia);

    private Credito C(DateOnly data, TipoProvento tipo, decimal quantidade, decimal? preco, decimal liquido, int ativo = 1) =>
        new(++linha, ativo, "TEST3", data, tipo, quantidade, preco, liquido);

    private static ProventoApp Yahoo(int id, DateOnly dataEx, DateOnly dataCom, decimal quantidade, decimal bruto, decimal? semAjuste = null, int ativo = 1) =>
        new(id, ativo, OrigemDado.YahooFinance, TipoProvento.Dividendo, dataEx, dataCom, quantidade, bruto, semAjuste ?? bruto, []);

    private static List<Item> Aplicaveis(List<Item> itens) => itens.Where(i => i.Aplicavel).ToList();

    [Fact]
    public void JcpEmDuasLinhasNoMesmoDia_SubstituiPorUmJcpDaB3()
    {
        var itens = Conciliar(
            [C(D(2025, 6, 12), TipoProvento.JCP, 837, 0.334m, 237.81m), C(D(2025, 6, 12), TipoProvento.JCP, 837, 0.090m, 64.35m)],
            [Yahoo(322, D(2025, 6, 3), D(2025, 6, 2), 837, 355.48m)], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Substituir, item.Acao);
        var novo = Assert.Single(item.Novos);
        Assert.Equal(TipoProvento.JCP, novo.Tipo);
        Assert.Equal(D(2025, 6, 2), novo.DataCom);
        Assert.Equal(2, novo.Parcelas.Count);
        Assert.All(novo.Parcelas, p => Assert.Equal(0.15m, p.Aliquota));
        Assert.Equal(355.49m, novo.Parcelas.Sum(p => p.ValorBruto));
    }

    [Fact]
    public void LinhaDoYahooComDividendoJcpERendimento_ViraUmProventoPorTipo()
    {
        // BBAS3: o Yahoo somou tudo da data ex 12/03/2025; um provento antigo (já pago antes do extrato) com a
        // mesma quantidade não pode ficar com estes créditos
        var itens = Conciliar(
            [
                C(D(2025, 3, 20), TipoProvento.Dividendo, 815, 0.136m, 110.84m),
                C(D(2025, 3, 20), TipoProvento.JCP, 815, 0.343m, 237.33m),
                C(D(2025, 3, 20), TipoProvento.Rendimento, 815, 0.003m, 2.24m),
                C(D(2025, 3, 20), TipoProvento.Rendimento, 815, 0.007m, 5.64m),
                C(D(2025, 3, 21), TipoProvento.JCP, 815, 0.149m, 103.47m)
            ],
            [Yahoo(317, D(2024, 8, 22), D(2024, 8, 21), 815, 386.84m), Yahoo(321, D(2025, 3, 12), D(2025, 3, 11), 815, 521.94m)],
            Inicio, Fim);

        var item = Assert.Single(Aplicaveis(itens));
        Assert.Equal(AcaoImportacaoB3.Substituir, item.Acao);
        Assert.Equal(321, item.Provento!.Id);
        Assert.Equal(5, item.Creditos.Count);
        Assert.Equal([TipoProvento.Dividendo, TipoProvento.JCP, TipoProvento.Rendimento], item.Novos.Select(n => n.Tipo).Order());
        Assert.DoesNotContain(itens, i => i.Provento?.Id == 317);
    }

    [Fact]
    public void DesdobramentoLancadoNaBaseNova_ComparaAQuantidadeDaB3NaBaseDoApp()
    {
        // BBAS3, fev/2024: a B3 mostra 240 ações (antes do desdobramento 1:2 de abril/2024), mas as compras
        // antigas foram lançadas já na base nova (480). Os proventos manuais são os mesmos dos créditos.
        ProventoApp Manual(int id, TipoProvento tipo, decimal bruto) =>
            new(id, 1, OrigemDado.Manual, tipo, D(2024, 2, 22), D(2024, 2, 21), 480, bruto, null, [], FatorRegistro: 2);

        var itens = Conciliar(
            [C(D(2024, 2, 29), TipoProvento.Dividendo, 240, 0.221m, 52.99m), C(D(2024, 2, 29), TipoProvento.JCP, 240, 0.614m, 125.18m)],
            [Manual(924, TipoProvento.Dividendo, 53.00m), Manual(925, TipoProvento.JCP, 147.27m)],
            D(2024, 1, 2), D(2024, 12, 27));

        Assert.Equal(2, itens.Count);
        Assert.All(itens, i => Assert.Equal(AcaoImportacaoB3.Substituir, i.Acao));
        var dividendo = Assert.Single(itens.Single(i => i.Provento!.Id == 924).Novos);
        Assert.Equal(480, dividendo.Quantidade);
        Assert.Equal(52.99m / 480, dividendo.ValorPorAcao);
    }

    [Fact]
    public void ExtratoAntigoComPrecoEmDuasCasas_AceitaAAliquotaDoJcp()
    {
        // BBAS3, dez/2021: preço 0,17 (era 0,175) e 16 ações da época = 32 no app
        var credito = C(D(2021, 12, 30), TipoProvento.JCP, 16, 0.17m, 2.38m);
        var yahoo = Yahoo(298, D(2021, 12, 14), D(2021, 12, 13), 32, 2.80m) with { FatorRegistro = 2 };

        var item = Assert.Single(Conciliar([credito], [yahoo], D(2021, 6, 1), D(2023, 12, 29)));

        Assert.Equal(AcaoImportacaoB3.Substituir, item.Acao);
        Assert.Equal(2.80m, item.ValorRecebidoBruto);
    }

    [Fact]
    public void JcpMinusculo_AliquotaConsideraOLiquidoEmCentavos()
    {
        // MGLU3: 5 ações × 0,015 = 0,075 bruto, 0,06 líquido (0,064 antes de arredondar)
        Assert.True(AliquotaCompativel(C(D(2022, 5, 6), TipoProvento.JCP, 5, 0.015m, 0.06m), 0.15m));
    }

    [Fact]
    public void CreditosDoMesmoDiaQueNaoFecham_FicamAmbiguosEmVezDeParcelados()
    {
        // LEVE3: três dividendos em 26/05/2023 somam 228,94; o Yahoo diz 225,54 (1,5% a menos)
        var itens = Conciliar(
            [
                C(D(2023, 5, 26), TipoProvento.Dividendo, 65, 1.903m, 123.67m),
                C(D(2023, 5, 26), TipoProvento.Dividendo, 65, 1.368m, 88.94m),
                C(D(2023, 5, 26), TipoProvento.Dividendo, 65, 0.251m, 16.33m)
            ],
            [Yahoo(274, D(2023, 4, 28), D(2023, 4, 27), 65, 225.54m)], D(2021, 6, 1), D(2023, 12, 29));

        Assert.Empty(Aplicaveis(itens));
        Assert.All(itens, i => Assert.Equal(AcaoImportacaoB3.Ambiguo, i.Acao));
    }

    [Fact]
    public void QuantidadeDiferenteComProventoNaJanela_FicaAmbiguoEmVezDeCriar()
    {
        // HGLG11: a cota subscrita só virou cota depois da data com; a B3 pagou sobre 6, o app tem 7
        var manual = new ProventoApp(850, 1, OrigemDado.Manual, TipoProvento.Rendimento, D(2021, 9, 1), D(2021, 8, 31), 7, 6.60m, null, []);

        var item = Assert.Single(Conciliar([C(D(2021, 9, 15), TipoProvento.Rendimento, 6, 1.1m, 6.60m)], [manual], D(2021, 6, 1), D(2023, 12, 29)));

        Assert.Equal(AcaoImportacaoB3.Ambiguo, item.Acao);
        Assert.Equal(850, item.Provento!.Id);
        Assert.Contains("a B3 pagou sobre 6 ações e o app tem 7", item.Detalhe);
    }

    [Fact]
    public void CreditoSemProvento_CriaNaBaseDoApp()
    {
        var credito = C(D(2024, 2, 29), TipoProvento.Dividendo, 240, 0.221m, 52.99m) with { FatorRegistro = 2 };

        var item = Assert.Single(Conciliar([credito], [], D(2024, 1, 2), D(2024, 12, 27)));

        Assert.Equal(AcaoImportacaoB3.Criar, item.Acao);
        Assert.Equal(480, Assert.Single(item.Novos).Quantidade);
    }

    [Fact]
    public void PagamentoParceladoEmAndamento_ViraParcialComOTotalDeclarado()
    {
        var itens = Conciliar(
            [
                C(D(2026, 2, 27), TipoProvento.Dividendo, 198, 0.228m, 45.14m),
                C(D(2026, 5, 20), TipoProvento.Dividendo, 198, 0.228m, 45.14m),
                C(D(2026, 8, 19), TipoProvento.Dividendo, 198, 0.228m, 45.14m)
            ],
            [Yahoo(536, D(2025, 12, 16), D(2025, 12, 15), 198, 180.56m)], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Parcial, item.Acao);
        var novo = Assert.Single(item.Novos);
        Assert.Equal(3, novo.Parcelas.Count);
        Assert.Equal(180.56m, Math.Round(novo.ValorPorAcao * novo.Quantidade, 2)); // a 4ª parcela continua contando
    }

    [Fact]
    public void JcpsDeCreditosDiferentesPagosNoMesmoDia_VaoCadaUmParaOSeu()
    {
        // ITSA4 em 07/03/2025: JCP da data ex 12/12/2024 e dividendo + JCP da data ex 18/02/2025
        var itens = Conciliar(
            [
                C(D(2025, 3, 7), TipoProvento.JCP, 233.1m, 0.058m, 11.51m),
                C(D(2025, 3, 7), TipoProvento.JCP, 233, 0.101m, 20.02m),
                C(D(2025, 3, 7), TipoProvento.Dividendo, 233, 0.408m, 95.09m),
                C(D(2025, 4, 22), TipoProvento.Dividendo, 233, 0.092m, 21.49m)
            ],
            [Yahoo(408, D(2024, 12, 12), D(2024, 12, 11), 233, 13.48m), Yahoo(409, D(2025, 2, 18), D(2025, 2, 17), 233, 140.15m)],
            Inicio, Fim);

        var do408 = Assert.Single(itens, i => i.Provento?.Id == 408);
        Assert.Equal(AcaoImportacaoB3.Substituir, do408.Acao);
        Assert.Equal(11.51m, Assert.Single(do408.Creditos).Credito.ValorLiquido);
        var do409 = Assert.Single(itens, i => i.Provento?.Id == 409);
        Assert.Equal(AcaoImportacaoB3.Substituir, do409.Acao);
        Assert.Equal([20.02m, 21.49m, 95.09m], do409.Creditos.Select(c => c.Credito.ValorLiquido).Order());
    }

    [Fact]
    public void AliquotaDoJcp_SeparaParcelaDeCredito2025DeJcpDe2026()
    {
        // B3SA3: JCP extraordinário de 2025 (data ex 02/01/2026, IR 15%) em 4 parcelas + JCP de 2026 (IR 17,5%)
        var itens = Conciliar(
            [
                C(D(2026, 1, 12), TipoProvento.JCP, 308, 0.083m, 21.60m),
                C(D(2026, 1, 12), TipoProvento.JCP, 308, 0.074m, 19.50m),
                C(D(2026, 4, 13), TipoProvento.JCP, 308, 0.074m, 19.50m),
                C(D(2026, 4, 13), TipoProvento.JCP, 308, 0.074m, 18.89m),
                C(D(2026, 7, 7), TipoProvento.JCP, 308, 0.074m, 19.50m),
                C(D(2026, 10, 7), TipoProvento.JCP, 308, 0.074m, 19.50m)
            ],
            [Yahoo(450, D(2026, 1, 2), D(2025, 12, 30), 308, 117.18m), Yahoo(451, D(2026, 4, 1), D(2026, 3, 31), 308, 22.90m)],
            Inicio, Fim);

        var do451 = Assert.Single(itens, i => i.Provento?.Id == 451);
        var credito = Assert.Single(do451.Creditos);
        Assert.Equal(18.89m, credito.Credito.ValorLiquido);
        Assert.Equal(0.175m, credito.Aliquota);
        var do450 = Assert.Single(itens, i => i.Provento?.Id == 450);
        Assert.Equal(5, do450.Creditos.Count);
        Assert.All(do450.Creditos, c => Assert.Equal(0.15m, c.Aliquota));
    }

    [Fact]
    public void CreditoSemProventoNoApp_ViraNovoSemDataCom()
    {
        var itens = Conciliar([C(D(2026, 3, 6), TipoProvento.JCP, 326, 0.018m, 5.05m)], [], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Criar, item.Acao);
        Assert.Null(Assert.Single(item.Novos).DataCom);
        Assert.Equal(0.15m, item.Creditos[0].Aliquota); // inferida pelo extrato: crédito de 2025
    }

    [Fact]
    public void ValorQueNaoFecha_ComProventoPossivel_FicaAmbiguoENaoCriaDuplicado()
    {
        var itens = Conciliar([C(D(2025, 5, 27), TipoProvento.Dividendo, 246, 1.5m, 369.00m)],
            [Yahoo(204, D(2025, 5, 7), D(2025, 5, 6), 246, 213.94m)], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Ambiguo, item.Acao);
        Assert.False(item.Aplicavel);
    }

    [Fact]
    public void AtualizacaoSelicDentroDaTolerancia_Substitui()
    {
        // EGIE3: pago 9 meses depois da data ex, com atualização (+0,78%)
        var itens = Conciliar([C(D(2025, 5, 27), TipoProvento.Dividendo, 246, 1.143m, 281.23m)],
            [Yahoo(204, D(2024, 8, 22), D(2024, 8, 21), 246, 279.05m)], Inicio, Fim);

        Assert.Equal(AcaoImportacaoB3.Substituir, Assert.Single(itens).Acao);
    }

    [Fact]
    public void YahooSemAjuste_UsaOValorBrutoSemAjusteQuandoFecha()
    {
        var itens = Conciliar([C(D(2025, 12, 19), TipoProvento.Dividendo, 326, 0.775m, 252.76m)],
            [Yahoo(416, D(2025, 12, 10), D(2025, 12, 9), 326, 258.70m, semAjuste: 253.63m)], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Substituir, item.Acao);
        Assert.Equal(253.63m, item.ValorEsperado);
    }

    [Fact]
    public void ValorSemAjusteQueCoincideComCreditosDeOutros_NaoVenceOValorAjustado()
    {
        // ITSA4 2023 (bonificações todo ano): o sem ajuste do JCP de 23/06 (21,18) é a soma exata dos créditos de
        // dois outros proventos (4,94 + 16,24); o de 22/09 (21,66), a dos dois JCPs de 9,19 líquidos
        ProventoApp P(int id, DateOnly ex, decimal ajustado, decimal semAjuste) =>
            Yahoo(id, ex, ex.AddDays(-1), 210, ajustado, semAjuste);

        var itens = Conciliar(
            [
                C(D(2023, 7, 3), TipoProvento.JCP, 210, 0.024m, 4.20m),
                C(D(2023, 8, 25), TipoProvento.JCP, 210, 0.077m, 13.80m),
                C(D(2023, 8, 25), TipoProvento.JCP, 210, 0.114m, 20.42m),
                C(D(2023, 10, 2), TipoProvento.JCP, 210, 0.024m, 4.20m),
                C(D(2024, 3, 8), TipoProvento.JCP, 210, 0.052m, 9.19m),
                C(D(2024, 3, 8), TipoProvento.JCP, 210, 0.117m, 20.80m)
            ],
            [
                P(391, D(2023, 3, 24), 16.09m, 14.31m), P(392, D(2023, 6, 1), 4.90m, 4.36m), P(393, D(2023, 6, 23), 23.82m, 21.18m),
                P(394, D(2023, 7, 26), 10.72m, 9.53m), P(395, D(2023, 8, 18), 4.92m, 4.37m), P(396, D(2023, 9, 22), 24.36m, 21.66m)
            ],
            D(2021, 6, 1), D(2026, 10, 28));

        decimal Recebido(int id) => Assert.Single(itens, i => i.Provento?.Id == id && i.Acao == AcaoImportacaoB3.Substituir).ValorRecebidoBruto;
        Assert.Equal(16.24m, Recebido(391));
        Assert.Equal(4.94m, Recebido(392));
        Assert.Equal(24.02m, Recebido(393));
        Assert.Equal(10.81m, Recebido(394));
        Assert.Equal(4.94m, Recebido(395));
        Assert.Equal(24.47m, Recebido(396));
    }

    [Fact]
    public void ReimportacaoDoMesmoExtrato_ConfirmaENaoMudaNada()
    {
        var jaImportado = new ProventoApp(900, 1, OrigemDado.ExtratoB3, TipoProvento.JCP, D(2025, 6, 3), D(2025, 6, 2), 837, 355.49m, null,
            [new ParcelaExistente(D(2025, 6, 12), 279.78m, 237.81m), new ParcelaExistente(D(2025, 6, 12), 75.71m, 64.35m)]);

        var itens = Conciliar(
            [C(D(2025, 6, 12), TipoProvento.JCP, 837, 0.334m, 237.81m), C(D(2025, 6, 12), TipoProvento.JCP, 837, 0.090m, 64.35m)],
            [jaImportado], Inicio, Fim);

        var item = Assert.Single(itens);
        Assert.Equal(AcaoImportacaoB3.Confirmado, item.Acao);
        Assert.Empty(Aplicaveis(itens));
    }

    [Fact]
    public void YahooQueRepeteProventoJaImportado_EhOcultado()
    {
        var jaImportado = new ProventoApp(900, 1, OrigemDado.ExtratoB3, TipoProvento.Dividendo, D(2025, 8, 15), D(2025, 8, 14), 155, 301.02m, null,
            [new ParcelaExistente(D(2025, 8, 26), 301.02m, 301.02m)]);

        var itens = Conciliar([C(D(2025, 8, 26), TipoProvento.Dividendo, 155, 1.942m, 301.02m)],
            [jaImportado, Yahoo(506, D(2025, 8, 15), D(2025, 8, 14), 155, 301.02m)], Inicio, Fim);

        Assert.Contains(itens, i => i.Acao == AcaoImportacaoB3.Confirmado);
        var duplicado = Assert.Single(itens, i => i.Acao == AcaoImportacaoB3.OcultarDuplicado);
        Assert.Equal(506, duplicado.Provento!.Id);
    }

    [Fact]
    public void ProventoDoAppSemCreditoNoPeriodo_SoInforma()
    {
        var itens = Conciliar([C(D(2025, 3, 14), TipoProvento.Dividendo, 291, 2.142m, 623.27m)],
            [Yahoo(232, D(2025, 3, 10), D(2025, 3, 7), 291, 623.28m), Yahoo(235, D(2026, 8, 21), D(2026, 8, 20), 300, 163.26m)], Inicio, Fim);

        var semCredito = Assert.Single(itens, i => i.Acao == AcaoImportacaoB3.SemCreditoNaB3);
        Assert.Equal(235, semCredito.Provento!.Id);
        Assert.False(semCredito.Aplicavel);
    }

    [Theory]
    [InlineData(19.50, 0.074, true, false)]  // 15%
    [InlineData(18.89, 0.074, false, true)]  // 17,5%
    public void AliquotaCompativel_ConsideraOArredondamentoDoPreco(double liquido, double preco, bool quinze, bool dezessete)
    {
        var c = new Credito(1, 1, "B3SA3", D(2026, 4, 13), TipoProvento.JCP, 308, (decimal)preco, (decimal)liquido);

        Assert.Equal(quinze, AliquotaCompativel(c, 0.15m));
        Assert.Equal(dezessete, AliquotaCompativel(c, 0.175m));
    }
}

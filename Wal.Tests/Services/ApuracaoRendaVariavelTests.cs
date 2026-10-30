using Wal.Domain.Enums;
using Wal.Services.Irpf;
using static Wal.Services.Irpf.ApuracaoRendaVariavel;

namespace Wal.Tests.Services;

public class ApuracaoRendaVariavelTests
{
    private const int Ano = 2025;

    // Venda com o resultado pedido: custo = valor − resultado
    private static Venda V(int mes, decimal alienacao, decimal resultado, ClasseFiscal classe = ClasseFiscal.Acao, bool dayTrade = false, int dia = 10, int ano = Ano) =>
        new(1, "XXXX3", classe, new DateOnly(ano, mes, dia), 100, alienacao, alienacao, alienacao - resultado, dayTrade);

    private static Mes Bolsa(Resultado r, int mes, int ano = Ano) => r.Bolsa.Single(m => m.Ano == ano && m.MesDoAno == mes);

    [Fact]
    public void AcoesAte20Mil_GanhoIsentoEForaDaFicha()
    {
        var r = Apurar([V(3, 10_000, 1_000)], [], Ano);

        var m = Bolsa(r, 3);
        Assert.True(m.AcoesIsentas);
        Assert.Equal(1_000, m.GanhoIsentoAcoes);
        Assert.Equal(0, m.Comum.Resultado);
        Assert.Equal(0, m.Comum.ImpostoDevido);
    }

    [Fact]
    public void AcoesAcimaDe20Mil_Tributa15()
    {
        var r = Apurar([V(3, 25_000, 2_000)], [], Ano);

        var m = Bolsa(r, 3);
        Assert.False(m.AcoesIsentas);
        Assert.Equal(2_000, m.Comum.Resultado);
        Assert.Equal(300, m.Comum.ImpostoDevido);
    }

    [Fact]
    public void EtfEBdr_NuncaIsentosENaoContamNoLimite()
    {
        var r = Apurar([V(3, 10_000, 1_000), V(3, 15_000, 500, ClasseFiscal.EtfRendaVariavel), V(3, 5_000, 200, ClasseFiscal.Bdr)], [], Ano);

        var m = Bolsa(r, 3);
        Assert.True(m.AcoesIsentas); // as vendas de ETF e BDR não entram nos R$ 20 mil
        Assert.Equal(1_000, m.GanhoIsentoAcoes);
        Assert.Equal(700, m.Comum.Resultado);
        Assert.Equal(105, m.Comum.ImpostoDevido);
    }

    [Fact]
    public void Prejuizo_CompensaNosMesesSeguintes()
    {
        var r = Apurar([V(3, 30_000, -1_000), V(5, 30_000, 1_500)], [], Ano);

        Assert.Equal(1_000, Bolsa(r, 3).Comum.PrejuizoACompensar);
        Assert.Equal(1_000, Bolsa(r, 4).Comum.PrejuizoACompensar);
        var maio = Bolsa(r, 5);
        Assert.Equal(1_000, maio.Comum.PrejuizoAnterior);
        Assert.Equal(500, maio.Comum.BaseCalculo);
        Assert.Equal(75, maio.Comum.ImpostoDevido);
        Assert.Equal(0, maio.Comum.PrejuizoACompensar);
    }

    [Fact]
    public void PrejuizoDeAcoesEmMesIsento_ContinuaCompensavel()
    {
        var r = Apurar([V(3, 5_000, -300), V(4, 8_000, 1_000, ClasseFiscal.Bdr)], [], Ano);

        Assert.Equal(-300, Bolsa(r, 3).Comum.Resultado);
        Assert.Equal(0, Bolsa(r, 3).GanhoIsentoAcoes);
        Assert.Equal(700, Bolsa(r, 4).Comum.BaseCalculo);
    }

    [Fact]
    public void Prejuizo_PassaDeUmAnoParaOOutro()
    {
        var r = Apurar([V(11, 30_000, -2_000, ano: 2024), V(2, 30_000, 3_000)], [], Ano);

        var fevereiro = Bolsa(r, 2);
        Assert.Equal(2_000, fevereiro.Comum.PrejuizoAnterior);
        Assert.Equal(1_000, fevereiro.Comum.BaseCalculo);
    }

    [Fact]
    public void DayTrade_SeparadoDasComunsCom20PorCentoEIrrfDe1PorCento()
    {
        var r = Apurar([V(6, 30_000, -500), V(6, 10_000, 1_000, dayTrade: true, dia: 11)], [], Ano);

        var m = Bolsa(r, 6);
        Assert.Equal(-500, m.Comum.Resultado);
        Assert.Equal(500, m.Comum.PrejuizoACompensar);
        Assert.Equal(1_000, m.DayTrade!.Resultado);
        Assert.Equal(200, m.DayTrade.ImpostoDevido);
        Assert.Equal(10, m.IrrfDayTradeMes);
        Assert.Equal(1.50m, m.IrrfMes); // 0,005% de R$ 30 mil
        Assert.Equal(188.50m, m.ImpostoAPagar);
    }

    [Fact]
    public void IrrfAteUmReal_EhDispensado()
    {
        var r = Apurar([V(6, 19_000, 100)], [], Ano);

        Assert.Equal(0, Bolsa(r, 6).IrrfMes); // 0,005% de R$ 19 mil = R$ 0,95
    }

    [Fact]
    public void ImpostoAbaixoDe10Reais_AcumulaParaOMesSeguinte()
    {
        var r = Apurar(
            [V(3, 30_000, 40, ClasseFiscal.Bdr, dia: 1), V(4, 30_000, 40, ClasseFiscal.Bdr, dia: 1), V(5, 30_000, 40, ClasseFiscal.Bdr, dia: 1)],
            [], Ano);

        // R$ 6 de imposto em cada mês, menos R$ 1,50 de IRRF = R$ 4,50
        var marco = Bolsa(r, 3);
        Assert.Equal(4.50m, marco.ImpostoAPagar);
        Assert.Equal(0, marco.ARecolher);
        var abril = Bolsa(r, 4);
        Assert.Equal(4.50m, abril.AcumuladoAnterior);
        Assert.Equal(0, abril.ARecolher); // R$ 9,00: ainda abaixo de R$ 10
        var maio = Bolsa(r, 5);
        Assert.Equal(9.00m, maio.AcumuladoAnterior);
        Assert.Equal(13.50m, maio.ARecolher);
        Assert.Equal(13.50m, maio.DarfPendente);
    }

    [Fact]
    public void DarfRegistrado_QuitaOMes()
    {
        var r = Apurar([V(3, 30_000, 2_000)], [new Darf(Ano, 3, FichaRendaVariavel.OperacoesComuns, 298.50m)], Ano);

        var m = Bolsa(r, 3);
        Assert.Equal(298.50m, m.ARecolher);
        Assert.Equal(0, m.DarfPendente);
    }

    [Fact]
    public void Fii_FichaPropria20PorCentoSemCompensarComAcoes()
    {
        var r = Apurar([V(3, 30_000, -1_000), V(3, 5_000, 500, ClasseFiscal.Fii)], [], Ano);

        var fii = r.Fii.Single(m => m.Ano == Ano && m.MesDoAno == 3);
        Assert.Equal(500, fii.Comum.Resultado);
        Assert.Equal(100, fii.Comum.ImpostoDevido);
        Assert.Equal(1_000, Bolsa(r, 3).Comum.PrejuizoACompensar);
    }

    [Fact]
    public void TesouroEEtfDeRendaFixa_FicamForaDaFicha()
    {
        var r = Apurar([V(3, 30_000, 2_000, ClasseFiscal.Tesouro), V(3, 30_000, 2_000, ClasseFiscal.EtfRendaFixa)], [], Ano);

        Assert.Empty(r.Bolsa);
    }
}

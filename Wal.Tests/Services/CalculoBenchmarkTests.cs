using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Dtos;

namespace Wal.Tests.Services;

public class CalculoBenchmarkTests
{
    private static CotacaoIndice Valor(IndiceReferencia indice, DateOnly data, decimal valor) =>
        new() { Indice = indice, Data = data, Valor = valor };

    private static Dictionary<IndiceReferencia, List<CotacaoIndice>> Series(params CotacaoIndice[] valores) =>
        valores.GroupBy(v => v.Indice).ToDictionary(g => g.Key, g => g.ToList());

    [Fact]
    public void Cdi_TaxaDoDiaRendeAteOProximoDiaUtil()
    {
        // 02/01/2026 é sexta; a taxa da sexta rende o fim de semana inteiro
        var series = Series(
            Valor(IndiceReferencia.CDI, new DateOnly(2026, 1, 2), 0.01m),
            Valor(IndiceReferencia.CDI, new DateOnly(2026, 1, 5), 0.01m));

        var pontos = CalculoBenchmark.Acumular(Benchmark.CDI, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 6), series);

        Assert.Equal(5, pontos.Count);
        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada);       // 02/01
        Assert.Equal(0.01m, pontos[1].RentabilidadeAcumulada);    // 03/01
        Assert.Equal(0.01m, pontos[3].RentabilidadeAcumulada);    // 05/01
        Assert.Equal(1.01m * 1.01m - 1, pontos[4].RentabilidadeAcumulada); // 06/01
    }

    [Fact]
    public void Ipca_MesInteiroAcumulaAVariacaoDoMes()
    {
        var series = Series(Valor(IndiceReferencia.IPCA, new DateOnly(2026, 1, 1), 0.031m));

        var pontos = CalculoBenchmark.Acumular(Benchmark.IPCA, new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31), series);

        Assert.Equal(0.031, (double)pontos[^1].RentabilidadeAcumulada, 8);
        // Meio do mês: metade dos dias, juros compostos
        Assert.Equal(Math.Pow(1.031, 15d / 31) - 1, (double)pontos[15].RentabilidadeAcumulada, 8);
    }

    [Fact]
    public void IpcaMais_SomaATaxaRealAoLongoDoAno()
    {
        var series = Series(Enumerable.Range(1, 12)
            .Select(m => Valor(IndiceReferencia.IPCA, new DateOnly(2025, m, 1), 0.004m)).ToArray());

        var pontos = CalculoBenchmark.Acumular(
            Benchmark.IpcaMais, new DateOnly(2024, 12, 31), new DateOnly(2025, 12, 31), series, taxaRealAnual: 0.06m);

        var esperado = Math.Pow(1.004, 12) * 1.06 - 1;
        Assert.Equal(esperado, (double)pontos[^1].RentabilidadeAcumulada, 8);
    }

    [Fact]
    public void IndiceDePreco_DiaSemPregaoRepeteUltimoValor()
    {
        var series = Series(
            Valor(IndiceReferencia.IBOV, new DateOnly(2026, 1, 2), 100),
            Valor(IndiceReferencia.IBOV, new DateOnly(2026, 1, 5), 110));

        var pontos = CalculoBenchmark.Acumular(Benchmark.IBOV, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), series);

        Assert.Equal(0m, pontos[2].RentabilidadeAcumulada);    // domingo
        Assert.Equal(0.10m, pontos[3].RentabilidadeAcumulada); // segunda
    }

    [Fact]
    public void IndiceDePreco_SerieQueComecaDepoisDoInicio_UsaPrimeiroValorComoBase()
    {
        var series = Series(
            Valor(IndiceReferencia.IDIV, new DateOnly(2026, 1, 5), 50),
            Valor(IndiceReferencia.IDIV, new DateOnly(2026, 1, 6), 55));

        var pontos = CalculoBenchmark.Acumular(Benchmark.IDIV, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 6), series);

        Assert.Equal(0m, pontos[0].RentabilidadeAcumulada);
        Assert.Equal(0.10m, pontos[^1].RentabilidadeAcumulada);
    }

    [Fact]
    public void Sp500_ConvertidoPelaPtax()
    {
        var series = Series(
            Valor(IndiceReferencia.SP500, new DateOnly(2026, 1, 2), 100),
            Valor(IndiceReferencia.SP500, new DateOnly(2026, 1, 5), 110),
            Valor(IndiceReferencia.PTAX, new DateOnly(2026, 1, 2), 5),
            Valor(IndiceReferencia.PTAX, new DateOnly(2026, 1, 5), 5.5m));

        var pontos = CalculoBenchmark.Acumular(Benchmark.SP500, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), series);

        Assert.Equal(0.21m, pontos[^1].RentabilidadeAcumulada); // 1,1 × 1,1 − 1
    }

    [Fact]
    public void SemDadosDoIndice_RetornaVazio()
    {
        var series = Series(Valor(IndiceReferencia.SP500, new DateOnly(2026, 1, 2), 100)); // falta a PTAX

        Assert.Empty(CalculoBenchmark.Acumular(Benchmark.SP500, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), series));
        Assert.Empty(CalculoBenchmark.Acumular(Benchmark.CDI, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), series));
    }
}

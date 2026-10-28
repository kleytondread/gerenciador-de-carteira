using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Dtos;

namespace Wal.Services;

/// <summary>
/// Rentabilidade acumulada dos benchmarks dia a dia (dias corridos), a partir das séries guardadas.
/// O primeiro dia vale 0; dias sem dado repetem o último valor conhecido.
/// </summary>
internal static class CalculoBenchmark
{
    private const double DiasNoAno = 365;

    /// <summary>Indices de que cada benchmark depende.</summary>
    public static IReadOnlyList<IndiceReferencia> IndicesNecessarios(Benchmark benchmark) => benchmark switch
    {
        Benchmark.CDI => [IndiceReferencia.CDI],
        Benchmark.IBOV => [IndiceReferencia.IBOV],
        Benchmark.IDIV => [IndiceReferencia.IDIV],
        Benchmark.IPCA or Benchmark.IpcaMais => [IndiceReferencia.IPCA],
        Benchmark.SP500 => [IndiceReferencia.SP500, IndiceReferencia.PTAX],
        _ => throw new ArgumentOutOfRangeException(nameof(benchmark))
    };

    /// <param name="series">Valores de cada índice necessário, em qualquer ordem.</param>
    /// <param name="taxaRealAnual">Só para <see cref="Benchmark.IpcaMais"/>: 0,06 = IPCA + 6% ao ano.</param>
    public static List<PontoBenchmarkResponse> Acumular(
        Benchmark benchmark, DateOnly inicio, DateOnly fim,
        IReadOnlyDictionary<IndiceReferencia, List<CotacaoIndice>> series, decimal taxaRealAnual = 0)
    {
        if (fim < inicio) return [];
        foreach (var indice in IndicesNecessarios(benchmark))
            if (!series.TryGetValue(indice, out var lista) || lista.Count == 0) return [];

        return benchmark switch
        {
            Benchmark.CDI => AcumularTaxaDiaria(inicio, fim, series[IndiceReferencia.CDI]),
            Benchmark.IPCA => AcumularIpca(inicio, fim, series[IndiceReferencia.IPCA], 0),
            Benchmark.IpcaMais => AcumularIpca(inicio, fim, series[IndiceReferencia.IPCA], taxaRealAnual),
            Benchmark.IBOV => AcumularPreco(inicio, fim, series[IndiceReferencia.IBOV], null),
            Benchmark.IDIV => AcumularPreco(inicio, fim, series[IndiceReferencia.IDIV], null),
            Benchmark.SP500 => AcumularPreco(inicio, fim, series[IndiceReferencia.SP500], series[IndiceReferencia.PTAX]),
            _ => []
        };
    }

    // A taxa publicada para o dia t rende de t até o dia útil seguinte: o acumulado no dia d
    // multiplica as taxas dos dias em [inicio, d)
    private static List<PontoBenchmarkResponse> AcumularTaxaDiaria(DateOnly inicio, DateOnly fim, List<CotacaoIndice> taxas)
    {
        var porData = taxas.ToDictionary(t => t.Data, t => t.Valor);
        var pontos = new List<PontoBenchmarkResponse>();
        var fator = 1m;
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            if (d > inicio && porData.TryGetValue(d.AddDays(-1), out var taxa)) fator *= 1 + taxa;
            pontos.Add(new PontoBenchmarkResponse(d, fator - 1));
        }
        return pontos;
    }

    // Variação mensal distribuída igualmente pelos dias corridos do mês (juros compostos). Mês ainda não
    // divulgado não rende. A taxa real, quando há, rende todos os dias.
    private static List<PontoBenchmarkResponse> AcumularIpca(DateOnly inicio, DateOnly fim, List<CotacaoIndice> mensais, decimal taxaRealAnual)
    {
        var porMes = mensais.ToDictionary(m => (m.Data.Year, m.Data.Month), m => m.Valor);
        var fatorRealDia = Math.Pow(1 + (double)taxaRealAnual, 1 / DiasNoAno);
        var pontos = new List<PontoBenchmarkResponse>();
        var fator = 1d;
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            if (d > inicio)
            {
                if (porMes.TryGetValue((d.Year, d.Month), out var variacao))
                    fator *= Math.Pow(1 + (double)variacao, 1d / DateTime.DaysInMonth(d.Year, d.Month));
                fator *= fatorRealDia;
            }
            pontos.Add(new PontoBenchmarkResponse(d, (decimal)(fator - 1)));
        }
        return pontos;
    }

    // Índice de preço: valor do dia ÷ valor na data inicial (último conhecido até ela). Com câmbio,
    // o valor é preço × PTAX, cada um com seu último valor conhecido.
    private static List<PontoBenchmarkResponse> AcumularPreco(
        DateOnly inicio, DateOnly fim, List<CotacaoIndice> precos, List<CotacaoIndice>? cambio)
    {
        var seriePreco = new UltimoConhecido(precos);
        var serieCambio = cambio is null ? null : new UltimoConhecido(cambio);

        decimal? Valor(DateOnly d)
        {
            var preco = seriePreco.Ate(d);
            if (preco is null) return null;
            if (serieCambio is null) return preco;
            var taxa = serieCambio.Ate(d);
            return taxa is null ? null : preco * taxa;
        }

        var pontos = new List<PontoBenchmarkResponse>();
        decimal? baseValor = null;
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            var valor = Valor(d);
            // Sem dado ainda (série começa depois da data inicial): a base é o primeiro valor que aparecer
            baseValor ??= valor;
            pontos.Add(new PontoBenchmarkResponse(d, valor is decimal v && baseValor is decimal b && b != 0 ? v / b - 1 : 0));
        }
        return pontos;
    }

    /// <summary>Consulta "último valor até a data" com as datas pedidas em ordem crescente.</summary>
    private sealed class UltimoConhecido(List<CotacaoIndice> valores)
    {
        private readonly List<CotacaoIndice> ordenados = valores.OrderBy(v => v.Data).ToList();
        private int proximo;
        private decimal? atual;

        public decimal? Ate(DateOnly data)
        {
            while (proximo < ordenados.Count && ordenados[proximo].Data <= data)
                atual = ordenados[proximo++].Valor;
            return atual;
        }
    }
}

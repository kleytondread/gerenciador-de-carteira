using System.Globalization;
using Wal.Domain.Enums;
using Wal.Services.Dtos;

namespace GerenciadorDeCarteira.Components.Dashboard;

/// <summary>
/// Cor fixa de cada entidade nos gráficos: a cor segue a série, nunca a posição dela na lista
/// (desmarcar um benchmark não repinta os outros). Paleta categórica validada para daltonismo.
/// </summary>
public static class CoresSeries
{
    public const string Carteira = "#2a78d6";
    public const string ValorAplicado = "#52514e";
    public const string Queda = "#c5221f";

    /// <summary>Taxa real do IPCA + X% quando o usuário não escolheu outra (em Ativos rastreados › Índices).</summary>
    public const decimal TaxaIpcaMaisPadrao = 0.06m;

    /// <summary>Chave do localStorage com a taxa em pontos percentuais, formato invariante ("6", "5.5").</summary>
    public const string ChaveTaxaIpcaMais = "wal-ipca-mais";

    /// <summary>Taxa salva no navegador (0,06 = 6% a.a.); ausente ou fora de 0–20% volta ao padrão.</summary>
    public static decimal LerTaxaIpcaMais(string? salvo) =>
        decimal.TryParse(salvo, NumberStyles.Number, CultureInfo.InvariantCulture, out var pontos) && pontos is >= 0 and <= 20
            ? pontos / 100
            : TaxaIpcaMaisPadrao;

    public static string GravarTaxaIpcaMais(decimal taxa) => (taxa * 100).ToString("0.##", CultureInfo.InvariantCulture);

    public static readonly Benchmark[] Benchmarks =
        [Benchmark.CDI, Benchmark.IBOV, Benchmark.IPCA, Benchmark.IDIV, Benchmark.SP500, Benchmark.IpcaMais];

    public static string Cor(Benchmark benchmark) => benchmark switch
    {
        Benchmark.CDI => "#eb6834",
        Benchmark.IBOV => "#1baf7a",
        Benchmark.IPCA => "#eda100",
        Benchmark.IDIV => "#e87ba4",
        Benchmark.SP500 => "#008300",
        _ => "#6250d6"
    };

    public static string Nome(Benchmark benchmark, decimal taxaIpcaMais = TaxaIpcaMaisPadrao) => benchmark switch
    {
        Benchmark.SP500 => "S&P 500",
        Benchmark.IpcaMais => $"IPCA + {(taxaIpcaMais * 100).ToString("0.##", Formatacao.PtBR)}%",
        _ => benchmark.ToString()
    };

    public static readonly TipoAtivo[] OrdemClasses =
        [TipoAtivo.Acao, TipoAtivo.ETF, TipoAtivo.FII, TipoAtivo.BDR, TipoAtivo.Tesouro];

    public static string Cor(TipoAtivo tipo) => tipo switch
    {
        TipoAtivo.Acao => "#2a78d6",
        TipoAtivo.ETF => "#eb6834",
        TipoAtivo.FII => "#1baf7a",
        TipoAtivo.BDR => "#e87ba4",
        _ => "#eda100"
    };

    public static string NomeClasse(TipoAtivo tipo) => tipo switch
    {
        TipoAtivo.Acao => "Ações",
        TipoAtivo.ETF => "ETFs",
        TipoAtivo.FII => "FIIs",
        TipoAtivo.BDR => "BDRs",
        _ => "Tesouro Direto"
    };
}

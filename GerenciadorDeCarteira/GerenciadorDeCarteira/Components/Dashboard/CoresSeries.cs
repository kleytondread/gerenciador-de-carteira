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

    /// <summary>Taxa real do benchmark IPCA + X% até a tela de índices permitir escolher (Fase 6).</summary>
    public const decimal TaxaIpcaMais = 0.06m;

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

    public static string Nome(Benchmark benchmark) => benchmark switch
    {
        Benchmark.SP500 => "S&P 500",
        Benchmark.IpcaMais => $"IPCA + {(TaxaIpcaMais * 100).ToString("0.##", Formatacao.PtBR)}%",
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

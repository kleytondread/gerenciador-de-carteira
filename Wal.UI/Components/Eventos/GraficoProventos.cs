using MudBlazor;
using Wal.Services.Dtos;
using Wal.Domain.Enums;

namespace GerenciadorDeCarteira.Components.Eventos;

/// <summary>
/// Proventos por tipo: ordem, nomes e cores fixos, e as barras empilhadas do MudChart (tela de Eventos e detalhe
/// do ativo) com o mesmo eixo, tooltip e máscara de valores ocultos.
/// </summary>
public static class GraficoProventos
{
    public static readonly TipoProvento[] Tipos = [TipoProvento.Dividendo, TipoProvento.JCP, TipoProvento.Rendimento];

    public static string Cor(TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => "#eb6834",
        TipoProvento.Rendimento => "#1baf7a",
        _ => "#2a78d6"
    };

    /// <summary>Nome no plural, para legendas e totais ("Dividendos", "JCP", "Rendimentos").</summary>
    public static string Nome(TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => "JCP",
        TipoProvento.Rendimento => "Rendimentos",
        _ => "Dividendos"
    };

    /// <summary>Quanto do período veio de cada tipo.</summary>
    public static decimal Valor(ProventosPeriodoResponse periodo, TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => periodo.Jcp,
        TipoProvento.Rendimento => periodo.Rendimentos,
        _ => periodo.Dividendos
    };

    /// <summary>Uma série por tipo, com um valor por grupo (mês ou ano), arredondado em centavos (o tooltip mostra o número cru).</summary>
    public static List<ChartSeries<double>> Series<T>(IReadOnlyList<T> grupos, Func<T, TipoProvento, decimal> valor) =>
        Tipos.Select(tipo => new ChartSeries<double>
        {
            Name = Nome(tipo),
            Data = new ChartData<double>(grupos.Select(g => (double)Math.Round(valor(g, tipo), 2)).ToList())
        }).ToList();

    /// <summary>Altura da maior barra (soma dos tipos no grupo).</summary>
    public static double MaiorBarra(List<ChartSeries<double>> series)
    {
        var grupos = series.Count == 0 ? 0 : series[0].Data.Values.Count();
        return grupos == 0 ? 0 : Enumerable.Range(0, grupos).Max(i => series.Sum(s => s.Data.Values.ElementAt(i)));
    }

    /// <summary>
    /// Linhas do eixo Y a cada R$ 500; com barras acima de R$ 5.000 (anos inteiros) a cada R$ 1.000,
    /// para não amontoar o eixo.
    /// </summary>
    public static StackedBarChartOptions Opcoes(List<ChartSeries<double>> series, bool mascarar) =>
        Opcoes(mascarar, MaiorBarra(series) > 5000 ? 1000 : 500);

    /// <param name="passoEixo">Distância entre as linhas do eixo Y em R$; nulo deixa o MudChart escolher.</param>
    public static StackedBarChartOptions Opcoes(bool mascarar, int? passoEixo = null)
    {
        var opcoes = new StackedBarChartOptions
        {
            ChartPalette = Tipos.Select(Cor).ToArray(),
            // Quem usa o gráfico já mostra os tipos com as cores ao lado (resumo ou tabela)
            ShowLegend = false,
            // Com valores ocultos, nem o eixo nem o tooltip mostram R$
            ShowToolTips = !mascarar,
            // O MudChart não formata o número do tooltip; o valor já vem arredondado em centavos
            TooltipTitleFormat = "R$ {{Y_VALUE}}",
            TooltipSubtitleFormat = "{{SERIES_NAME}}",
            YAxisToStringFunc = v => mascarar ? "" : $"R$ {v.ToString("N0", Formatacao.PtBR)}"
        };
        if (passoEixo is int passo)
        {
            opcoes.YAxisTicks = passo;
            // Sem limite baixo: o MudChart "afinaria" as linhas e mudaria o intervalo escolhido
            opcoes.MaxNumYAxisTicks = 40;
        }
        return opcoes;
    }
}

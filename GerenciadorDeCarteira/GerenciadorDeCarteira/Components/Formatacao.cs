using System.Globalization;
using Wal.Domain.Enums;

namespace GerenciadorDeCarteira.Components;

/// <summary>Formatação de exibição compartilhada pelas telas (sempre pt-BR, independente da cultura do servidor).</summary>
public static class Formatacao
{
    public static readonly CultureInfo PtBR = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal valor) => valor.ToString("C2", PtBR);

    public static string Preco(decimal valor) => valor.ToString("C4", PtBR);

    public static string Quantidade(decimal valor) => valor.ToString("N4", PtBR);

    public static string MoedaComSinal(decimal valor) =>
        (valor > 0 ? "+" : string.Empty) + valor.ToString("C2", PtBR);

    public static string Percentual(decimal fracao) =>
        (fracao > 0 ? "+" : string.Empty) + fracao.ToString("P2", PtBR);

    public static string CorGanhoPerda(decimal? valor) => valor switch
    {
        > 0 => "text-success",
        < 0 => "text-danger",
        _ => string.Empty
    };

    public static string NomeTipo(TipoAtivo tipo) => tipo switch
    {
        TipoAtivo.Acao => "Ação",
        TipoAtivo.ETF => "ETF",
        TipoAtivo.FII => "FII",
        TipoAtivo.BDR => "BDR",
        TipoAtivo.Tesouro => "Tesouro",
        _ => tipo.ToString()
    };
}

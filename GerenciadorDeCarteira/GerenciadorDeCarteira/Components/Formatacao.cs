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

    /// <summary>Percentual sem sinal de +, para pesos e participações (0,153 → "15,3%").</summary>
    public static string PercentualSimples(decimal fracao, int casas = 1) =>
        fracao.ToString("P" + casas, PtBR);

    /// <summary>R$ com sinal, ou a máscara quando os valores estão ocultos.</summary>
    public static string MoedaComSinal(decimal valor, bool mascarar) => mascarar ? ValorOculto : MoedaComSinal(valor);

    public static string Moeda(decimal valor, bool mascarar) => mascarar ? ValorOculto : Moeda(valor);

    public const string ValorOculto = "R$ •••••";

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

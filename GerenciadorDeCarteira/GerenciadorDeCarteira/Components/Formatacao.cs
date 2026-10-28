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

    /// <summary>Quantidade sem zeros sobrando: 20 → "20", 9,39 → "9,39", 1760 → "1.760".</summary>
    public static string QuantidadeCurta(decimal valor) => valor.ToString("#,##0.####", PtBR);

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

    /// <summary>
    /// Tamanho de uma barra em CSS ("42.5%"), proporcional ao maior valor da lista. Ponto decimal invariante:
    /// em pt-BR a vírgula quebraria o CSS. <paramref name="minimo"/> deixa barras pequenas ainda visíveis.
    /// </summary>
    public static string LarguraCss(decimal valor, decimal maior, decimal minimo = 0) =>
        Math.Max(maior > 0 ? Math.Abs(valor) / maior * 100 : 0, minimo).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static readonly string[] NomesMes = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    /// <summary>"jan/26", ou só "jan" sem o ano. Sem o ponto que a cultura pt-BR coloca na abreviação.</summary>
    public static string MesCurto(DateOnly mes, bool comAno = true) =>
        comAno ? $"{NomesMes[mes.Month - 1]}/{mes.Year % 100:00}" : NomesMes[mes.Month - 1];

    /// <summary>
    /// Rótulo do eixo X de um gráfico mensal. Até 13 meses, todos ("jan", "fev"...); com mais, um por ano (janeiro)
    /// e o primeiro mês, se ele não ficar colado no janeiro seguinte.
    /// </summary>
    public static string RotuloEixoMensal(DateOnly mes, int indice, int total) =>
        total <= 13 ? MesCurto(mes, comAno: false)
        : mes.Month == 1 || (indice == 0 && mes.Month <= 10) ? MesCurto(mes)
        : string.Empty;

    /// <summary>Nome de um provento no singular ("Dividendo", "JCP", "Rendimento").</summary>
    public static string NomeProvento(TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => "JCP",
        TipoProvento.Rendimento => "Rendimento",
        _ => "Dividendo"
    };

    /// <summary>"Desdobramento 2:1", "Grupamento 1:10", "Bonificação de 10,0% (11:10)".</summary>
    public static string DescricaoEvento(TipoEventoCorporativo tipo, decimal numerador, decimal denominador)
    {
        var proporcao = $"{numerador.ToString("0.####", PtBR)}:{denominador.ToString("0.####", PtBR)}";
        return tipo switch
        {
            TipoEventoCorporativo.Desdobramento => $"Desdobramento {proporcao}",
            TipoEventoCorporativo.Grupamento => $"Grupamento {proporcao}",
            _ => $"Bonificação de {PercentualSimples(numerador / denominador - 1)} ({proporcao})"
        };
    }

    public static string CorGanhoPerda(decimal? valor) => valor switch
    {
        > 0 => "wal-ganho",
        < 0 => "wal-perda",
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

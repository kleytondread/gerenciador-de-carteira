namespace Wal.Domain.Enums;

/// <summary>
/// Porção da carteira para controle de alocação (como na planilha): renda variável no Brasil, renda variável
/// em dólar e renda fixa. Independe do tipo: um ETF pode ser de ações brasileiras, de S&amp;P 500 ou de títulos.
/// </summary>
public enum CategoriaAlocacao
{
    BrasilRendaVariavel = 0,
    DolarRendaVariavel = 1,
    RendaFixa = 2
}

public static class CategoriasAlocacao
{
    /// <summary>Sugestão pelo tipo: BDR em dólar, Tesouro em renda fixa, o resto Brasil (ETFs podem precisar de ajuste).</summary>
    public static CategoriaAlocacao Padrao(TipoAtivo tipo) => tipo switch
    {
        TipoAtivo.BDR => CategoriaAlocacao.DolarRendaVariavel,
        TipoAtivo.Tesouro => CategoriaAlocacao.RendaFixa,
        _ => CategoriaAlocacao.BrasilRendaVariavel
    };
}

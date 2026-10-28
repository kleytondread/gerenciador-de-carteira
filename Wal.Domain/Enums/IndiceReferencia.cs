namespace Wal.Domain.Enums;

/// <summary>Séries de mercado guardadas em <see cref="Entities.CotacaoIndice"/> para comparar com a carteira.</summary>
public enum IndiceReferencia
{
    /// <summary>Taxa DI diária (Banco Central, SGS 12).</summary>
    CDI,
    /// <summary>Variação mensal do IPCA (Banco Central, SGS 433).</summary>
    IPCA,
    /// <summary>Ibovespa em pontos (Yahoo, ^BVSP).</summary>
    IBOV,
    /// <summary>IDIV aproximado pelo ETF DIVO11 com proventos reinvestidos (Yahoo, preço ajustado).</summary>
    IDIV,
    /// <summary>S&amp;P 500 em pontos, em dólar (Yahoo, ^GSPC).</summary>
    SP500,
    /// <summary>Dólar PTAX de venda em R$ (Banco Central, SGS 1) — converte o S&amp;P 500 para reais.</summary>
    PTAX
}

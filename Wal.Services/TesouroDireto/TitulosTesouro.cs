namespace Wal.Services.TesouroDireto;

/// <summary>Categorias do filtro de títulos no cadastro e como cada tipo de título se apresenta.</summary>
public static class TitulosTesouro
{
    public const string Prefixado = "Prefixado";
    public const string Ipca = "IPCA+";
    public const string IpcaSemestral = "IPCA+ com Juros Semestrais";
    public const string Selic = "Selic";
    public const string Renda = "Renda+";

    public static readonly IReadOnlyList<string> Categorias = [Prefixado, Ipca, IpcaSemestral, Selic, Renda];

    // Tipos de título do arquivo do Tesouro -> (categoria do filtro, sigla do ticker).
    // Os mais específicos vêm antes ("IPCA+ com Juros" antes de "IPCA+"). IGP-M+ e Educa+ ficam de fora.
    private static readonly (string Prefixo, string Categoria, string Sigla)[] Tipos =
    [
        ("tesouro prefixado com juros", Prefixado, "PREFIXADO JS"),
        ("tesouro prefixado", Prefixado, "PREFIXADO"),
        ("tesouro ipca+ com juros", IpcaSemestral, "IPCA+ JS"),
        ("tesouro ipca+", Ipca, "IPCA+"),
        ("tesouro selic", Selic, "SELIC"),
        ("tesouro renda+", Renda, "RENDA+"),
    ];

    /// <summary>Categoria do filtro, ou null para tipos que o sistema não oferece.</summary>
    public static string? Categoria(string tipoTitulo) => Buscar(tipoTitulo)?.Categoria;

    /// <summary>Ticker gerado para o título (ex.: "IPCA+ 2045", "PREFIXADO JS 2035").</summary>
    public static string Ticker(string tipoTitulo, DateOnly vencimento) =>
        $"{Buscar(tipoTitulo)?.Sigla ?? tipoTitulo.ToUpperInvariant()} {vencimento.Year}";

    /// <summary>Nome como no site do Tesouro Direto (ex.: "Tesouro IPCA+ 2045").</summary>
    public static string Nome(string tipoTitulo, DateOnly vencimento) => $"{tipoTitulo.Trim()} {vencimento.Year}";

    public static bool MesmoTipo(string a, string b) =>
        TesouroDiretoCsv.Normalizar(a) == TesouroDiretoCsv.Normalizar(b);

    private static (string Prefixo, string Categoria, string Sigla)? Buscar(string tipoTitulo)
    {
        var normalizado = TesouroDiretoCsv.Normalizar(tipoTitulo);
        foreach (var tipo in Tipos)
            if (normalizado.StartsWith(tipo.Prefixo, StringComparison.Ordinal))
                return tipo;
        return null;
    }
}

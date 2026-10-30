namespace Wal.Services.Common;

/// <summary>CNPJ guardado só com os 14 dígitos; formatação e validação dos dígitos verificadores.</summary>
public static class Cnpj
{
    /// <summary>Só os dígitos; nulo quando não há nenhum.</summary>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        return digitos.Length == 0 ? null : digitos;
    }

    public static bool Valido(string? digitos)
    {
        if (digitos is not { Length: 14 } || !digitos.All(char.IsAsciiDigit) || digitos.Distinct().Count() == 1)
            return false;
        return digitos[12] - '0' == Digito(digitos, 12) && digitos[13] - '0' == Digito(digitos, 13);
    }

    /// <summary>Estabelecimento matriz (ordem 0001). Empresas e fundos declaram o CNPJ da matriz.</summary>
    public static bool EhMatriz(string digitos) => digitos.Length == 14 && digitos.Substring(8, 4) == "0001";

    /// <summary>00.000.000/0000-00; o texto como veio se não tiver 14 dígitos.</summary>
    public static string Formatar(string? digitos) =>
        digitos is { Length: 14 }
            ? $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}"
            : digitos ?? string.Empty;

    // Módulo 11 com pesos 2..9 da direita para a esquerda
    private static int Digito(string digitos, int tamanho)
    {
        var soma = 0;
        for (var i = 0; i < tamanho; i++)
        {
            var peso = (tamanho - 1 - i) % 8 + 2;
            soma += (digitos[i] - '0') * peso;
        }
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}

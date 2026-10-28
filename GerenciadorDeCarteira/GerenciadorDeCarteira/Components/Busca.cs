namespace GerenciadorDeCarteira.Components;

/// <summary>Filtro de texto das caixas "Buscar ativo" das telas.</summary>
public static class Busca
{
    /// <summary>Termo vazio encontra tudo; senão, procura o termo (sem diferenciar maiúsculas) em qualquer dos campos.</summary>
    public static bool Encontra(string? termo, params string?[] campos) =>
        string.IsNullOrWhiteSpace(termo)
        || campos.Any(c => c?.Contains(termo.Trim(), StringComparison.OrdinalIgnoreCase) == true);
}

namespace Wal.Domain.Enums;

public enum TipoEventoCorporativo
{
    /// <summary>Proporção inteira maior que 1 (ex.: 2:1).</summary>
    Desdobramento,
    /// <summary>Proporção menor que 1 (ex.: 1:10).</summary>
    Grupamento,
    /// <summary>Proporção fracionária maior que 1 (ex.: 11:10 = 10% em ações).</summary>
    Bonificacao
}

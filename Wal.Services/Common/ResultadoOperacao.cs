namespace Wal.Services.Common;

public enum TipoErroOperacao
{
    Validacao,
    Conflito,
    NaoEncontrado
}

public class ResultadoOperacao
{
    public bool Sucesso { get; init; }
    public string? Erro { get; init; }
    public TipoErroOperacao? TipoErro { get; init; }

    public static ResultadoOperacao Ok() => new() { Sucesso = true };

    public static ResultadoOperacao Falha(string erro, TipoErroOperacao tipo) =>
        new() { Sucesso = false, Erro = erro, TipoErro = tipo };
}

public class ResultadoOperacao<T>
{
    public bool Sucesso { get; init; }
    public T? Valor { get; init; }
    public string? Erro { get; init; }
    public TipoErroOperacao? TipoErro { get; init; }

    public static ResultadoOperacao<T> Ok(T valor) => new() { Sucesso = true, Valor = valor };

    public static ResultadoOperacao<T> Falha(string erro, TipoErroOperacao tipo) =>
        new() { Sucesso = false, Erro = erro, TipoErro = tipo };
}

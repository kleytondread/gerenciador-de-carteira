namespace Wal.ExternalApis;

public class BrapiIndisponivelException : Exception
{
    public BrapiIndisponivelException(string ticker, Exception inner)
        : base($"brapi.dev indisponível para o ticker '{ticker}'.", inner) { }

    public BrapiIndisponivelException(string ticker, string motivo)
        : base($"brapi.dev retornou erro para o ticker '{ticker}': {motivo}.") { }
}

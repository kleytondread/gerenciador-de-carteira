namespace Wal.ExternalApis;

public interface ITesouroDiretoClient
{
    /// <summary>
    /// Baixa o arquivo "Taxas dos Títulos Ofertados pelo Tesouro Direto" (Tesouro Transparente, dados abertos)
    /// para <paramref name="destino"/>. Lança <see cref="HttpRequestException"/> em resposta de erro — o portal
    /// responde 429 quando recebe requisições demais da mesma rede.
    /// </summary>
    Task BaixarPrecosETaxasAsync(Stream destino, CancellationToken cancellationToken = default);
}

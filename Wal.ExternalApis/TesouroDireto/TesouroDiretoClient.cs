using Microsoft.Extensions.Logging;

namespace Wal.ExternalApis;

public class TesouroDiretoClient(HttpClient http, ILogger<TesouroDiretoClient> logger) : ITesouroDiretoClient
{
    // Recurso CSV do conjunto "taxas-dos-titulos-ofertados-pelo-tesouro-direto" (atualizado diariamente)
    public const string CaminhoArquivo =
        "ckan/dataset/df56aa42-484a-4a59-8184-7676580c81e3/resource/796d2059-14e9-44e3-80c9-2d9e30b405c1/download/precotaxatesourodireto.csv";

    public async Task BaixarPrecosETaxasAsync(Stream destino, CancellationToken cancellationToken = default)
    {
        using var resposta = await http.GetAsync(CaminhoArquivo, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogWarning("Tesouro Transparente respondeu {Status} ao baixar o arquivo de preços e taxas.",
                (int)resposta.StatusCode);
            resposta.EnsureSuccessStatusCode();
        }

        await using var conteudo = await resposta.Content.ReadAsStreamAsync(cancellationToken);
        await conteudo.CopyToAsync(destino, cancellationToken);
    }
}

using System.Text;
using Microsoft.Extensions.Logging;
using Wal.ExternalApis;
using Wal.Services.Dtos;

namespace Wal.Services.TesouroDireto;

public class TesouroDiretoOptions
{
    /// <summary>Onde o arquivo baixado fica salvo (caminho absoluto).</summary>
    public string CaminhoArquivo { get; set; } = string.Empty;

    /// <summary>
    /// Só baixa de novo quando o arquivo tem pelo menos esta idade. O portal do Tesouro limita requisições
    /// (responde 429) e o arquivo é atualizado uma vez por dia útil.
    /// </summary>
    public TimeSpan IdadeMinimaParaBaixar { get; set; } = TimeSpan.FromHours(5);

    /// <summary>Títulos sem preço publicado neste período saíram de oferta e não aparecem no cadastro.</summary>
    public TimeSpan JanelaTituloEmOferta { get; set; } = TimeSpan.FromDays(30);
}

public interface ITesouroDiretoService
{
    /// <summary>
    /// Baixa o arquivo de preços e taxas se ele não existir ou tiver pelo menos a idade mínima (5h),
    /// sobrescrevendo o antigo. Se o download falhar, mantém o arquivo que já existe.
    /// Retorna se há um arquivo disponível para leitura.
    /// </summary>
    Task<bool> AtualizarArquivoAsync(CancellationToken cancellationToken = default);

    /// <summary>Títulos em oferta e ainda não vencidos, das categorias que o sistema aceita.</summary>
    Task<List<TituloTesouroResponse>> ListarTitulosAsync(CancellationToken cancellationToken = default);

    /// <summary>Histórico de preços do título (preço de avaliação por data), em ordem de data.</summary>
    Task<List<(DateOnly Data, decimal Preco)>> ObterPrecosAsync(string tipoTitulo, DateOnly vencimento, CancellationToken cancellationToken = default);
}

public class TesouroDiretoService(
    ITesouroDiretoClient client,
    TesouroDiretoOptions options,
    ILogger<TesouroDiretoService> logger) : ITesouroDiretoService
{
    // Um download por vez para o processo inteiro (o job e a tela de cadastro podem pedir juntos)
    private static readonly SemaphoreSlim TravaDownload = new(1, 1);

    // Leitura do arquivo em cache, invalidada quando o arquivo muda (data de gravação ou tamanho)
    private static readonly object TravaCache = new();
    private static (string Caminho, DateTime Gravacao, long Tamanho, List<PrecoTituloTesouro> Linhas)? cache;

    public async Task<bool> AtualizarArquivoAsync(CancellationToken cancellationToken = default)
    {
        var caminho = options.CaminhoArquivo;
        if (EstaRecente(caminho)) return true;

        await TravaDownload.WaitAsync(cancellationToken);
        try
        {
            // Outra chamada pode ter baixado enquanto esta esperava
            if (EstaRecente(caminho)) return true;

            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            var temporario = caminho + ".baixando";
            try
            {
                await using (var destino = File.Create(temporario))
                    await client.BaixarPrecosETaxasAsync(destino, cancellationToken);

                // O portal devolve uma página HTML em alguns erros; só troca o arquivo se for o CSV de verdade
                if (!TesouroDiretoCsv.CabecalhoValido(LerPrimeiraLinha(temporario)))
                {
                    logger.LogWarning("Tesouro Direto: conteúdo baixado não é o CSV esperado; mantendo o arquivo anterior.");
                    return File.Exists(caminho);
                }

                File.Move(temporario, caminho, overwrite: true);
                logger.LogInformation("Tesouro Direto: arquivo de preços e taxas atualizado ({Bytes:N0} bytes).",
                    new FileInfo(caminho).Length);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                logger.LogWarning(ex, "Tesouro Direto: falha ao baixar o arquivo; mantendo o anterior{Situacao}.",
                    File.Exists(caminho) ? string.Empty : " (não há arquivo anterior)");
                return File.Exists(caminho);
            }
            finally
            {
                if (File.Exists(temporario)) File.Delete(temporario);
            }
        }
        finally
        {
            TravaDownload.Release();
        }
    }

    public async Task<List<TituloTesouroResponse>> ListarTitulosAsync(CancellationToken cancellationToken = default)
    {
        var linhas = await LinhasAsync(cancellationToken);
        if (linhas.Count == 0) return [];

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var dataMaisRecente = linhas.Max(l => l.Data);
        var inicioJanela = dataMaisRecente.AddDays(-(int)options.JanelaTituloEmOferta.TotalDays);

        return linhas
            .Where(l => l.Vencimento > hoje && TitulosTesouro.Categoria(l.TipoTitulo) is not null)
            .GroupBy(l => (Tipo: TesouroDiretoCsv.Normalizar(l.TipoTitulo), l.Vencimento))
            .Select(g => g.MaxBy(l => l.Data)!)
            .Where(ultimo => ultimo.Data >= inicioJanela)
            .Select(u => new TituloTesouroResponse(
                u.TipoTitulo, u.Vencimento, TitulosTesouro.Categoria(u.TipoTitulo)!,
                TitulosTesouro.Nome(u.TipoTitulo, u.Vencimento), TitulosTesouro.Ticker(u.TipoTitulo, u.Vencimento),
                u.Data, u.PrecoAvaliacao, u.TaxaCompra))
            .OrderBy(t => t.Categoria).ThenBy(t => t.Vencimento).ThenBy(t => t.Nome)
            .ToList();
    }

    public async Task<List<(DateOnly Data, decimal Preco)>> ObterPrecosAsync(
        string tipoTitulo, DateOnly vencimento, CancellationToken cancellationToken = default)
    {
        var linhas = await LinhasAsync(cancellationToken);
        return linhas
            .Where(l => l.Vencimento == vencimento && TitulosTesouro.MesmoTipo(l.TipoTitulo, tipoTitulo))
            .GroupBy(l => l.Data)
            .Select(g => (g.Key, g.Last().PrecoAvaliacao))
            .Where(p => p.Item2 > 0)
            .OrderBy(p => p.Key)
            .ToList();
    }

    private async Task<List<PrecoTituloTesouro>> LinhasAsync(CancellationToken cancellationToken)
    {
        if (!await AtualizarArquivoAsync(cancellationToken)) return [];

        var info = new FileInfo(options.CaminhoArquivo);
        lock (TravaCache)
        {
            if (cache is { } c && c.Caminho == info.FullName && c.Gravacao == info.LastWriteTimeUtc && c.Tamanho == info.Length)
                return c.Linhas;

            using var leitor = new StreamReader(info.FullName, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
            var linhas = TesouroDiretoCsv.Ler(leitor);
            cache = (info.FullName, info.LastWriteTimeUtc, info.Length, linhas);
            return linhas;
        }
    }

    private bool EstaRecente(string caminho)
    {
        var info = new FileInfo(caminho);
        return info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < options.IdadeMinimaParaBaixar;
    }

    private static string? LerPrimeiraLinha(string caminho)
    {
        using var leitor = new StreamReader(caminho, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return leitor.ReadLine();
    }
}

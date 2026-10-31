using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services.Common;

namespace Wal.Services;

public class CotacaoAoVivoService(
    IYahooFinanceClient yahoo,
    IMemoryCache cache,
    TimeProvider relogio,
    ILogger<CotacaoAoVivoService> logger) : ICotacaoAoVivoService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);

    private const int ChamadasSimultaneas = 4;

    // Última busca de um ticker. Fica em memória até o fim do dia: entre o fim do pregão e o job das 20h
    // a tela continua com o preço de hoje em vez de voltar para o fechamento de ontem.
    // Em: última tentativa (controla o intervalo); VerificadaEm: última que o Yahoo respondeu.
    private sealed record Busca(YahooCotacaoAtual? Cotacao, DateTimeOffset Em, DateTimeOffset? VerificadaEm);

    public async Task<CotacoesAoVivo> ObterAsync(IEnumerable<string> tickers, bool buscarAgora = false)
    {
        var agora = relogio.GetUtcNow();
        var pregaoAberto = Pregao.Aberto(agora);
        var resultado = new ConcurrentDictionary<string, YahooCotacaoAtual>();
        var verificadas = new ConcurrentBag<DateTimeOffset>();

        await Parallel.ForEachAsync(tickers.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = ChamadasSimultaneas },
            async (ticker, _) =>
            {
                var busca = cache.Get<Busca>(Chave(ticker));
                if (buscarAgora || (pregaoAberto && (busca is null || agora - busca.Em >= Intervalo)))
                    busca = await BuscarAsync(ticker, busca, agora);

                if (busca?.VerificadaEm is { } verificada) verificadas.Add(verificada);

                // Fora do pregão (ou em feriado) o Yahoo devolve o último pregão; quem usa compara com o fechamento
                // gravado no banco e só fica com o preço se ele for mais novo (ex.: sábado, antes do job ter sexta)
                if (busca?.Cotacao is { } cotacao)
                    resultado[ticker] = cotacao;
            });

        return new CotacoesAoVivo(resultado, verificadas.IsEmpty ? null : verificadas.Max());
    }

    private async Task<Busca> BuscarAsync(string ticker, Busca? anterior, DateTimeOffset agora)
    {
        YahooCotacaoAtual? cotacao;
        try
        {
            cotacao = await yahoo.ObterCotacaoAtualAsync(ticker);
        }
        catch (Exception ex)
        {
            // Sem preço novo a tela segue com o anterior (ou com o banco); só tenta de novo no próximo intervalo
            logger.LogWarning(ex, "Cotação ao vivo indisponível para {Ticker}.", ticker);
            cotacao = null;
        }

        var busca = cotacao is null
            ? new Busca(anterior?.Cotacao, agora, anterior?.VerificadaEm)
            : new Busca(cotacao, agora, agora);
        cache.Set(Chave(ticker), busca, TimeSpan.FromDays(1));
        return busca;
    }

    private static string Chave(string ticker) => $"cotacao-ao-vivo:{ticker}";
}

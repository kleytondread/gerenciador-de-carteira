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
    private sealed record Busca(YahooCotacaoAtual? Cotacao, DateTimeOffset Em);

    public async Task<IReadOnlyDictionary<string, YahooCotacaoAtual>> ObterAsync(IEnumerable<string> tickers)
    {
        var agora = relogio.GetUtcNow();
        var hoje = Pregao.Dia(agora);
        var pregaoAberto = Pregao.Aberto(agora);
        var resultado = new ConcurrentDictionary<string, YahooCotacaoAtual>();

        await Parallel.ForEachAsync(tickers.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = ChamadasSimultaneas },
            async (ticker, _) =>
            {
                var busca = cache.Get<Busca>(Chave(ticker));
                if (pregaoAberto && (busca is null || agora - busca.Em >= Intervalo))
                    busca = await BuscarAsync(ticker, busca, agora);

                // Feriado: o Yahoo devolve o último pregão, que o banco já tem
                if (busca?.Cotacao is { } cotacao && Pregao.Dia(cotacao.Horario) == hoje)
                    resultado[ticker] = cotacao;
            });

        return resultado;
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

        var busca = new Busca(cotacao ?? anterior?.Cotacao, agora);
        cache.Set(Chave(ticker), busca, TimeSpan.FromDays(1));
        return busca;
    }

    private static string Chave(string ticker) => $"cotacao-ao-vivo:{ticker}";
}

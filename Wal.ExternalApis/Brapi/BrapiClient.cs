using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using Wal.ExternalApis.Dtos;

namespace Wal.ExternalApis;

public class BrapiClient(HttpClient http, ILogger<BrapiClient> logger) : IBrapiClient
{
    public async Task<BrapiQuoteResult?> ObterCotacoesEProventosAsync(string ticker)
    {
        try
        {
            var historicoTask = http.GetFromJsonAsync<BrapiHistoricalApiResponse>(
                $"v2/stocks/historical?symbols={ticker}&range=3mo&interval=1d");

            // Plano gratuito não possui acesso ao endpoint de dividendos.
            // var proventosTask = http.GetFromJsonAsync<BrapiDividendsApiResponse>(
            //     $"v2/stocks/dividends?symbols={ticker}");

            // await Task.WhenAll(historicoTask, proventosTask);
            await historicoTask;

            var historicoResult = historicoTask.Result?.Results.FirstOrDefault();
            if (historicoResult is null) return null;

            return new BrapiQuoteResult
            {
                Symbol = historicoResult.Symbol,
                HistoricalDataPrice = historicoResult.Data?.HistoricalDataPrice ?? [],
                // CashDividends = proventosTask.Result?.Results.FirstOrDefault()?.Data?.CashDividends ?? []
                CashDividends = []
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("brapi.dev retornou 401 para {Ticker}. Token não configurado ou inválido.", ticker);
            throw new BrapiIndisponivelException(ticker,
                "A API exige autenticação (401). Configure o token em \"Brapi:Token\" no appsettings.json.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Erro HTTP ao buscar dados da brapi.dev para {Ticker}", ticker);
            throw new BrapiIndisponivelException(ticker, ex);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout ao buscar dados da brapi.dev para {Ticker}", ticker);
            throw new BrapiIndisponivelException(ticker, ex);
        }
    }
}

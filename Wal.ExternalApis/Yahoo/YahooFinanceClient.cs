using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Wal.ExternalApis.Dtos;

namespace Wal.ExternalApis;

public class YahooFinanceClient(HttpClient http, ILogger<YahooFinanceClient> logger) : IYahooFinanceClient
{
    // Ações B3 usam sufixo .SA no Yahoo Finance (ex: PETR4.SA)
    public Task<YahooQuoteResult> ObterDadosAsync(string ticker, long period1) =>
        ObterDadosSimboloAsync($"{ticker}.SA", period1, ticker);

    public Task<YahooQuoteResult> ObterDadosSimboloAsync(string simbolo, long period1) =>
        ObterDadosSimboloAsync(simbolo, period1, simbolo);

    private async Task<YahooQuoteResult> ObterDadosSimboloAsync(string yahooTicker, long period1, string ticker)
    {
        try
        {
            var period2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var resposta = await http.GetFromJsonAsync<YahooChartResponse>(
                $"chart/{Uri.EscapeDataString(yahooTicker)}?period1={period1}&period2={period2}&interval=1d&events=div%7Csplit&includePrePost=false");

            if (resposta?.Chart?.Error is { } erro)
            {
                logger.LogWarning(
                    "Yahoo Finance retornou erro para {Ticker}: [{Code}] {Description}",
                    ticker, erro.Code, erro.Description);
                return new YahooQuoteResult { Symbol = ticker };
            }

            var result = resposta?.Chart?.Result?.FirstOrDefault();
            if (result?.Timestamp is null or { Count: 0 })
                return new YahooQuoteResult { Symbol = ticker };

            var quote = result.Indicators?.Quote?.FirstOrDefault();
            if (quote is null) return new YahooQuoteResult { Symbol = ticker };

            // Preço ajustado por proventos (retorno total), paralelo aos timestamps; nem sempre vem
            var ajustados = result.Indicators?.AdjClose?.FirstOrDefault()?.AdjClose;

            // ── Cotações OHLCV ────────────────────────────────────────────────
            var precos = new List<YahooHistoricalPrice>(result.Timestamp.Count);
            for (int i = 0; i < result.Timestamp.Count; i++)
            {
                // Pula entradas sem preço de fechamento (dias sem negociação)
                if (quote.Close?.ElementAtOrDefault(i) is not { } close) continue;

                precos.Add(new YahooHistoricalPrice
                {
                    Date   = result.Timestamp[i],
                    Open   = quote.Open?.ElementAtOrDefault(i)   ?? close,
                    High   = quote.High?.ElementAtOrDefault(i)   ?? close,
                    Low    = quote.Low?.ElementAtOrDefault(i)    ?? close,
                    Close  = close,
                    AdjClose = ajustados?.ElementAtOrDefault(i),
                    Volume = quote.Volume?.ElementAtOrDefault(i) ?? 0
                });
            }

            // ── Proventos (events=div) ────────────────────────────────────────
            // API retorna dicionário com timestamp string como chave; convertemos para lista ordenada.
            var dividendos = result.Events?.Dividends?.Values
                .Select(d => new YahooDividend { Date = d.Date, Amount = d.Amount })
                .OrderBy(d => d.Date)
                .ToList() ?? [];

            // ── Desdobramentos, grupamentos e bonificações (events=split) ─────
            // O Yahoo trata bonificação da B3 como split e ajusta preços e proventos anteriores por ela.
            var eventos = result.Events?.Splits?.Values
                .Where(s => s.Numerator > 0 && s.Denominator > 0)
                .Select(s => new YahooSplit { Date = s.Date, Numerator = s.Numerator, Denominator = s.Denominator })
                .OrderBy(s => s.Date)
                .ToList() ?? [];

            var symbol = result.Meta?.Symbol ?? yahooTicker;
            logger.LogInformation(
                "Yahoo Finance: {CountP} cotações, {CountD} proventos e {CountS} eventos obtidos para {Ticker}.",
                precos.Count, dividendos.Count, eventos.Count, ticker);

            return new YahooQuoteResult
            {
                Symbol             = symbol,
                HistoricalDataPrice = precos,
                Dividends          = dividendos,
                Splits             = eventos
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Erro HTTP ao buscar dados do Yahoo Finance para {Ticker}.", ticker);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout ao buscar dados do Yahoo Finance para {Ticker}.", ticker);
            throw;
        }
    }
}

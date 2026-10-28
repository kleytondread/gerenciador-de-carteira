using System.Text.Json.Serialization;

namespace Wal.ExternalApis.Dtos;

// ── DTOs públicos retornados ao consumidor ────────────────────────────────────

public class YahooQuoteResult
{
    public string Symbol { get; set; } = string.Empty;
    public List<YahooHistoricalPrice> HistoricalDataPrice { get; set; } = [];
    // Proventos em dinheiro (dividendos, JCP, rendimentos de FII).
    // Yahoo Finance não distingue o tipo — todos chegam aqui como Dividendo no mapeamento.
    public List<YahooDividend> Dividends { get; set; } = [];
    // Desdobramentos, grupamentos e bonificações — o Yahoo ajusta o histórico de preços por eles.
    public List<YahooSplit> Splits { get; set; } = [];
}

/// <summary>Preço do momento (com o atraso do Yahoo, ~15 min na B3) e o fechamento do pregão anterior.</summary>
public record YahooCotacaoAtual(decimal Preco, decimal FechamentoAnterior, DateTimeOffset Horario);

public class YahooSplit
{
    public long Date { get; set; }          // Data ex do evento (Unix timestamp em segundos)
    public decimal Numerator { get; set; }  // Proporção Numerator:Denominator (ex.: 2:1, 1:10, 3.5:2.5)
    public decimal Denominator { get; set; }
}

public class YahooHistoricalPrice
{
    public long Date { get; set; } // Unix timestamp em segundos
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    // Fechamento ajustado por proventos (retorno total). Nulo quando o Yahoo não manda.
    public decimal? AdjClose { get; set; }
    public long Volume { get; set; }
}

public class YahooDividend
{
    public long Date { get; set; }      // Data ex-dividendo (Unix timestamp em segundos)
    public decimal Amount { get; set; } // Valor por ação/cota, bruto
}

// ── DTOs internos de resposta da API do Yahoo Finance ────────────────────────
// Não são expostos fora de Wal.ExternalApis.

internal class YahooChartResponse
{
    [JsonPropertyName("chart")]
    public YahooChart? Chart { get; set; }
}

internal class YahooChart
{
    [JsonPropertyName("result")]
    public List<YahooChartResult>? Result { get; set; }

    [JsonPropertyName("error")]
    public YahooError? Error { get; set; }
}

internal class YahooError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

internal class YahooChartResult
{
    [JsonPropertyName("meta")]
    public YahooMeta? Meta { get; set; }

    // Timestamps paralelos aos arrays de OHLCV em indicators.quote
    [JsonPropertyName("timestamp")]
    public List<long>? Timestamp { get; set; }

    [JsonPropertyName("indicators")]
    public YahooIndicators? Indicators { get; set; }

    [JsonPropertyName("events")]
    public YahooEvents? Events { get; set; }
}

internal class YahooMeta
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("regularMarketPrice")]
    public decimal? RegularMarketPrice { get; set; }

    [JsonPropertyName("chartPreviousClose")]
    public decimal? ChartPreviousClose { get; set; }

    // Unix timestamp em segundos do último negócio
    [JsonPropertyName("regularMarketTime")]
    public long? RegularMarketTime { get; set; }
}

internal class YahooIndicators
{
    [JsonPropertyName("quote")]
    public List<YahooQuote>? Quote { get; set; }

    [JsonPropertyName("adjclose")]
    public List<YahooAdjClose>? AdjClose { get; set; }
}

internal class YahooAdjClose
{
    [JsonPropertyName("adjclose")]
    public List<decimal?>? AdjClose { get; set; }
}

// Arrays paralelos ao array timestamp — índice i de timestamp corresponde ao índice i aqui.
// Valores podem ser null em dias sem negociação.
internal class YahooQuote
{
    [JsonPropertyName("open")]
    public List<decimal?>? Open { get; set; }

    [JsonPropertyName("high")]
    public List<decimal?>? High { get; set; }

    [JsonPropertyName("low")]
    public List<decimal?>? Low { get; set; }

    [JsonPropertyName("close")]
    public List<decimal?>? Close { get; set; }

    [JsonPropertyName("volume")]
    public List<long?>? Volume { get; set; }
}

internal class YahooEvents
{
    // Dicionário com timestamp (string) como chave — formato retornado pela API com events=div
    [JsonPropertyName("dividends")]
    public Dictionary<string, YahooDividendEvent>? Dividends { get; set; }

    // Mesmo formato, com events=split
    [JsonPropertyName("splits")]
    public Dictionary<string, YahooSplitEvent>? Splits { get; set; }
}

internal class YahooSplitEvent
{
    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("numerator")]
    public decimal Numerator { get; set; }

    [JsonPropertyName("denominator")]
    public decimal Denominator { get; set; }
}

internal class YahooDividendEvent
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}

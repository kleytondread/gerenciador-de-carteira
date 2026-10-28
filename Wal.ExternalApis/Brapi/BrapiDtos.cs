using System.Text.Json.Serialization;

namespace Wal.ExternalApis.Dtos;

// Resultado combinado retornado ao consumidor — montado internamente a partir de 2 chamadas à API v2
public class BrapiQuoteResult
{
    public string Symbol { get; set; } = string.Empty;
    public List<BrapiHistoricalPrice> HistoricalDataPrice { get; set; } = [];
    public List<BrapiCashDividend> CashDividends { get; set; } = [];
    // stockDividends são recebidos da API mas nunca persistidos — spec seção 4.3
}

public class BrapiHistoricalPrice
{
    [JsonPropertyName("date")]
    public long Date { get; set; } // Unix timestamp em segundos

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    [JsonPropertyName("adjustedClose")]
    public decimal AdjustedClose { get; set; }
}

public class BrapiCashDividend
{
    [JsonPropertyName("paymentDate")]
    public string? PaymentDate { get; set; }

    // DataCom — pode ser nulo dependendo do tipo de provento
    [JsonPropertyName("lastDatePrior")]
    public string? LastDatePrior { get; set; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

// ── DTOs internos de resposta da API v2 ─────────────────────────────────────
// Não são expostos fora de Wal.ExternalApis; apenas BrapiClient os usa.

internal class BrapiHistoricalApiResponse
{
    [JsonPropertyName("results")]
    public List<BrapiHistoricalApiResult> Results { get; set; } = [];
}

internal class BrapiHistoricalApiResult
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public BrapiHistoricalApiData? Data { get; set; }
}

internal class BrapiHistoricalApiData
{
    [JsonPropertyName("historicalDataPrice")]
    public List<BrapiHistoricalPrice> HistoricalDataPrice { get; set; } = [];
}

internal class BrapiDividendsApiResponse
{
    [JsonPropertyName("results")]
    public List<BrapiDividendsApiResult> Results { get; set; } = [];
}

internal class BrapiDividendsApiResult
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public BrapiDividendsApiData? Data { get; set; }
}

internal class BrapiDividendsApiData
{
    [JsonPropertyName("cashDividends")]
    public List<BrapiCashDividend> CashDividends { get; set; } = [];

    // stockDividends recebidos e descartados — spec seção 4.3
    [JsonPropertyName("stockDividends")]
    public List<BrapiStockDividend> StockDividends { get; set; } = [];
}

internal class BrapiStockDividend
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

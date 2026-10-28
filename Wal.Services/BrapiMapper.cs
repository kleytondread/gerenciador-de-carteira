using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis.Dtos;

namespace Wal.Services;

// Mapeamento brapi.dev → entidades de domínio.
// stockDividends é recebido no DTO mas nunca é passado aqui — fora do escopo da spec (seção 4.3).
internal static class BrapiMapper
{
    internal static CotacaoHistorica? ToCotacao(int ativoId, BrapiHistoricalPrice src)
    {
        try
        {
            var data = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(src.Date).UtcDateTime);
            return new CotacaoHistorica
            {
                AtivoId = ativoId,
                Data = data,
                Abertura = src.Open,
                Maxima = src.High,
                Minima = src.Low,
                Fechamento = src.Close,
                Volume = src.Volume
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            // Timestamp Unix fora do intervalo válido — registro ignorado
            return null;
        }
    }

    internal static Provento? ToProvento(int ativoId, BrapiCashDividend src)
    {
        if (!TryParseDate(src.PaymentDate, out var dataPagamento))
            return null;

        var tipo = ParseTipoProvento(src.Label);
        if (tipo is null)
            return null;

        DateOnly? dataCom = TryParseDate(src.LastDatePrior, out var dc) ? dc : null;

        return new Provento
        {
            AtivoId = ativoId,
            TipoProvento = tipo.Value,
            DataCom = dataCom,
            DataPagamento = dataPagamento,
            ValorPorAcao = src.Rate,
            DataImportacao = DateTime.UtcNow
        };
    }

    // Tabela de mapeamento label → TipoProvento:
    // "DIVIDENDO"  → Dividendo
    // "JCP"        → JCP
    // "RENDIMENTO" → Rendimento
    // demais labels (ex: fundo isento) → null (registro ignorado)
    private static TipoProvento? ParseTipoProvento(string label) => label.ToUpperInvariant() switch
    {
        "DIVIDENDO" => TipoProvento.Dividendo,
        "JCP" => TipoProvento.JCP,
        "RENDIMENTO" => TipoProvento.Rendimento,
        _ => null
    };

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTimeOffset.TryParse(value, out var dto)) return false;
        date = DateOnly.FromDateTime(dto.UtcDateTime);
        return true;
    }
}

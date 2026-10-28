using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis.Dtos;

namespace Wal.Services;

internal static class YahooMapper
{
    internal static CotacaoHistorica? ToCotacao(int ativoId, YahooHistoricalPrice src)
    {
        // Barra incompleta do dia corrente: o Yahoo devolve OHLV zerados com o fechamento repetido
        // do pregão anterior enquanto o dia não consolida. Persistir isso poluiria o cabeçalho da
        // tela de detalhe (máx/mín R$ 0,00) e zeraria a variação do dia — o dado real chega na
        // sincronização diária seguinte e seria upsertado por (AtivoId, Data) de qualquer forma.
        if (src is { Open: 0, High: 0, Low: 0, Volume: 0 })
            return null;

        try
        {
            var data = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(src.Date).UtcDateTime);
            return new CotacaoHistorica
            {
                AtivoId    = ativoId,
                Data       = data,
                Abertura   = src.Open,
                Maxima     = src.High,
                Minima     = src.Low,
                Fechamento = src.Close,
                Volume     = src.Volume
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // timestamp Unix fora do intervalo válido de DateOnly
        }
    }

    internal static Provento? ToProvento(int ativoId, YahooDividend src)
    {
        try
        {
            // Yahoo Finance fornece apenas a data ex-dividendo e o valor bruto.
            // Tipo padrão = Dividendo — a API não distingue Dividendo / JCP / Rendimento.
            // DataPagamento = DataCom (data ex) pois Yahoo não fornece a data de pagamento.
            var dataEx = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(src.Date).UtcDateTime);
            return new Provento
            {
                AtivoId        = ativoId,
                TipoProvento   = TipoProvento.Dividendo,
                Origem         = OrigemDado.YahooFinance,
                DataCom        = dataEx,
                DataPagamento  = dataEx,
                ValorPorAcao   = src.Amount,
                DataImportacao = DateTime.UtcNow
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal static EventoCorporativo? ToEventoCorporativo(int ativoId, YahooSplit src)
    {
        if (src.Numerator <= 0 || src.Denominator <= 0)
            return null;

        try
        {
            return new EventoCorporativo
            {
                AtivoId        = ativoId,
                Data           = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(src.Date).UtcDateTime),
                Numerador      = src.Numerator,
                Denominador    = src.Denominator,
                Origem         = OrigemDado.YahooFinance,
                DataImportacao = DateTime.UtcNow
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

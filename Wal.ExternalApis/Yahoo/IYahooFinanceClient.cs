using Wal.ExternalApis.Dtos;

namespace Wal.ExternalApis;

public interface IYahooFinanceClient
{
    /// <summary>
    /// Retorna cotações OHLCV, proventos e eventos (desdobramento/grupamento/bonificação) de um ticker
    /// da B3 a partir de <paramref name="period1"/>. O sufixo ".SA" é adicionado automaticamente.
    /// </summary>
    Task<YahooQuoteResult> ObterDadosAsync(string ticker, long period1);

    /// <summary>
    /// Mesmo que <see cref="ObterDadosAsync"/>, com o símbolo do Yahoo como está, sem sufixo
    /// (índices como ^BVSP e ^GSPC, ou "DIVO11.SA").
    /// </summary>
    Task<YahooQuoteResult> ObterDadosSimboloAsync(string simbolo, long period1);
}

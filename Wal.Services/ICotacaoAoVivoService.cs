using Wal.ExternalApis.Dtos;

namespace Wal.Services;

/// <summary>Preço do momento dos ativos, guardado só em memória (nunca no banco).</summary>
public interface ICotacaoAoVivoService
{
    /// <summary>
    /// Cotação de hoje de cada ticker que tiver uma. Durante o pregão, busca no Yahoo quando a última busca
    /// tem mais de <see cref="CotacaoAoVivoService.Intervalo"/>; fora dele, só devolve o que já estava em memória.
    /// </summary>
    Task<IReadOnlyDictionary<string, YahooCotacaoAtual>> ObterAsync(IEnumerable<string> tickers);
}

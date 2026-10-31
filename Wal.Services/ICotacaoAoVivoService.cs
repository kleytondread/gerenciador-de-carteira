using Wal.ExternalApis.Dtos;

namespace Wal.Services;

/// <summary>Preço do momento dos ativos, guardado só em memória (nunca no banco).</summary>
public interface ICotacaoAoVivoService
{
    /// <summary>
    /// Última cotação buscada de cada ticker (pode ser de um pregão anterior). Durante o pregão, busca no Yahoo quando a última busca
    /// tem mais de <see cref="CotacaoAoVivoService.Intervalo"/>; fora dele, só devolve o que já estava em memória.
    /// <paramref name="buscarAgora"/> (botão "Atualizar cotações") busca na hora, em qualquer horário.
    /// </summary>
    Task<CotacoesAoVivo> ObterAsync(IEnumerable<string> tickers, bool buscarAgora = false);
}

/// <param name="Cotacoes">Última cotação buscada por ticker.</param>
/// <param name="VerificadasEm">Última busca no Yahoo que deu certo entre os tickers pedidos; nula se nenhuma ainda.</param>
public record CotacoesAoVivo(IReadOnlyDictionary<string, YahooCotacaoAtual> Cotacoes, DateTimeOffset? VerificadasEm);

using Wal.ExternalApis.Dtos;

namespace Wal.ExternalApis;

public interface IBrapiClient
{
    Task<BrapiQuoteResult?> ObterCotacoesEProventosAsync(string ticker);
}

using Hangfire;
using Wal.Services;

namespace Wal.Jobs;

/// <summary>Job recorrente diário: atualiza os índices de referência (CDI, IPCA, PTAX, IBOV, IDIV, S&amp;P 500).</summary>
public class IndiceSyncJob(IIndicesReferenciaService indices)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public Task SincronizarAsync() => indices.SincronizarAsync();
}

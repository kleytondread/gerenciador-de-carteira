using Hangfire;
using Wal.Services;

namespace Wal.Jobs;

// IAtivoJobScheduler vive em Wal.Services (não aqui) para evitar dependência circular:
// Wal.Services precisa da interface (AtivoService a injeta), e Wal.Jobs já depende de Wal.Services
// (usa IDadosMercadoService). A implementação concreta, que depende do Hangfire, permanece aqui.

public class AtivoJobScheduler(IBackgroundJobClient backgroundJobClient) : IAtivoJobScheduler
{
    public string EnfileirarCargaHistoricaInicial(int ativoId)
        => backgroundJobClient.Enqueue<IDadosMercadoService>(s => s.CargaHistoricaCompletaAsync(ativoId));

    public void EnfileirarSincronizacaoGeral()
    {
        backgroundJobClient.Enqueue<AtivoSyncJob>(j => j.SincronizarTodosAtivosAsync());
        backgroundJobClient.Enqueue<TesouroSyncJob>(j => j.AtualizarAsync());
        backgroundJobClient.Enqueue<IndiceSyncJob>(j => j.SincronizarAsync());
    }

    public void EnfileirarSincronizacaoIndices()
        => backgroundJobClient.Enqueue<IndiceSyncJob>(j => j.SincronizarAsync());
}

using Hangfire;
using Wal.Services;
using Wal.Services.Common;

namespace Wal.Jobs;

// IAtivoJobScheduler vive em Wal.Services (não aqui) para evitar dependência circular:
// Wal.Services precisa da interface (AtivoService a injeta), e Wal.Jobs já depende de Wal.Services
// (usa IDadosMercadoService). A implementação concreta, que depende do Hangfire, permanece aqui.

public class AtivoJobScheduler(IBackgroundJobClient backgroundJobClient, AtualizacoesDados atualizacoes) : IAtivoJobScheduler
{
    public string EnfileirarCargaHistoricaInicial(int ativoId)
        => backgroundJobClient.Enqueue<IDadosMercadoService>(s => s.CargaHistoricaCompletaAsync(ativoId));

    public bool EnfileirarSincronizacaoGeral()
    {
        if (!atualizacoes.IniciarAtualizacaoCompleta()) return false;
        try
        {
            backgroundJobClient.Enqueue<AtualizacaoCompletaJob>(j => j.AtualizarTudoAsync());
            return true;
        }
        catch
        {
            atualizacoes.ConcluirAtualizacaoCompleta();
            throw;
        }
    }

    public void EnfileirarSincronizacaoIndices()
        => backgroundJobClient.Enqueue<IndiceSyncJob>(j => j.SincronizarAsync());
}

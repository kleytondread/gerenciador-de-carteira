using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Services;

namespace Wal.Jobs;

/// <summary>
/// Job recorrente diário (pós-fechamento B3): sincroniza cotações e proventos de todos os ativos ativos.
/// </summary>
public class AtivoSyncJob(AppDbContext db, IDadosMercadoService dadosMercadoService, ILogger<AtivoSyncJob> logger)
{
    // Retry no nível do job inteiro (ex: falha de banco) — falha isolada por ativo é tratada dentro do loop
    // e não propaga, para não derrubar a sincronização dos demais ativos (spec, seção 4.3, "Tratamento de falha").
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task SincronizarTodosAtivosAsync()
    {
        var ativosIds = await db.Ativos
            .Where(a => a.EstaAtivo)
            .Select(a => a.Id)
            .ToListAsync();

        logger.LogInformation("Sincronização diária: iniciando para {Count} ativo(s) ativo(s).", ativosIds.Count);

        var falhas = 0;
        foreach (var ativoId in ativosIds)
        {
            try
            {
                await dadosMercadoService.SincronizarAtivoAsync(ativoId);
            }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex,
                    "Sincronização diária: falha ao sincronizar ativo {AtivoId}; prosseguindo com os demais.",
                    ativoId);
            }
        }

        logger.LogInformation(
            "Sincronização diária: concluída. {Total} ativo(s) processado(s), {Falhas} falha(s).",
            ativosIds.Count, falhas);
    }
}

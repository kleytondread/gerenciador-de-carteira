using Hangfire;
using Microsoft.Extensions.Logging;
using Wal.Services.Common;

namespace Wal.Jobs;

/// <summary>
/// Botão "Atualizar tudo": os três jobs recorrentes em sequência (ativos, Tesouro e índices), como um job só,
/// para a tela saber quando terminou. Falha numa etapa não impede as outras.
/// </summary>
public class AtualizacaoCompletaJob(
    AtivoSyncJob ativos,
    TesouroSyncJob tesouro,
    IndiceSyncJob indices,
    AtualizacoesDados atualizacoes,
    ILogger<AtualizacaoCompletaJob> logger)
{
    // Sem retry: quem quiser tenta de novo pelo botão; os jobs recorrentes seguem nos horários deles
    [AutomaticRetry(Attempts = 0)]
    public async Task AtualizarTudoAsync()
    {
        try
        {
            await EtapaAsync("ativos", ativos.SincronizarTodosAtivosAsync);
            await EtapaAsync("Tesouro Direto", tesouro.AtualizarAsync);
            await EtapaAsync("índices", indices.SincronizarAsync);
        }
        finally
        {
            atualizacoes.ConcluirAtualizacaoCompleta();
        }
    }

    private async Task EtapaAsync(string nome, Func<Task> etapa)
    {
        try
        {
            await etapa();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Atualizar tudo: falha ao atualizar {Etapa}; seguindo com as demais.", nome);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.TesouroDireto;

namespace Wal.Jobs;

/// <summary>
/// Job recorrente: mantém o arquivo de preços do Tesouro Direto atualizado e grava os preços dos títulos
/// cadastrados. O download só acontece quando o arquivo tem pelo menos 5h (ver TesouroDiretoOptions),
/// então rodar com mais frequência não sobrecarrega o portal do Tesouro.
/// </summary>
public class TesouroSyncJob(
    AppDbContext db,
    ITesouroDiretoService tesouroDireto,
    IDadosMercadoService dadosMercado,
    ILogger<TesouroSyncJob> logger)
{
    public async Task AtualizarAsync()
    {
        if (!await tesouroDireto.AtualizarArquivoAsync())
        {
            logger.LogWarning("Tesouro Direto: nenhum arquivo de preços disponível; títulos não atualizados.");
            return;
        }

        var titulos = await db.Ativos
            .Where(a => a.EstaAtivo && a.Tipo == TipoAtivo.Tesouro)
            .Select(a => new { a.Id, a.Ticker })
            .ToListAsync();

        foreach (var titulo in titulos)
        {
            try
            {
                await dadosMercado.SincronizarAtivoAsync(titulo.Id);
            }
            catch (Exception ex)
            {
                // Falha num título não impede os demais
                logger.LogError(ex, "Tesouro Direto: falha ao atualizar {Ticker}.", titulo.Ticker);
            }
        }
    }
}

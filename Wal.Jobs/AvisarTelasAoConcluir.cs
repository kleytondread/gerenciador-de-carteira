using Hangfire.Server;
using Wal.Services.Common;

namespace Wal.Jobs;

/// <summary>
/// Filtro do Hangfire: conta os jobs rodando e, quando um termina sem erro, avisa as telas abertas
/// (<see cref="AtualizacoesDados"/>).
/// </summary>
internal sealed class AvisarTelasAoConcluir(AtualizacoesDados atualizacoes) : IServerFilter
{
    public void OnPerforming(PerformingContext context) => atualizacoes.JobIniciado();

    public void OnPerformed(PerformedContext context) => atualizacoes.JobConcluido(sucesso: context.Exception is null);
}

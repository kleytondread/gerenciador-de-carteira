namespace Wal.Services.Common;

/// <summary>
/// Estado dos jobs para o app inteiro. Quando um job termina sem erro, avisa que pode haver dados novos (cotações,
/// proventos, índices, Tesouro): as telas abertas recarregam sem precisar trocar de tela.
/// Enquanto algum job roda (ou a atualização completa espera na fila), o botão "Atualizar tudo" fica girando e
/// bloqueado, para não disparar outra atualização no meio de uma.
/// </summary>
public sealed class AtualizacoesDados
{
    private int jobsRodando;
    private int atualizacaoCompletaPendente;

    /// <summary>Um job terminou sem erro: pode haver dados novos.</summary>
    public event Action? DadosAtualizados;

    /// <summary><see cref="Atualizando"/> mudou (job começou ou terminou).</summary>
    public event Action? EstadoMudou;

    public bool Atualizando => Volatile.Read(ref jobsRodando) > 0 || Volatile.Read(ref atualizacaoCompletaPendente) == 1;

    public void JobIniciado()
    {
        Interlocked.Increment(ref jobsRodando);
        EstadoMudou?.Invoke();
    }

    public void JobConcluido(bool sucesso)
    {
        Interlocked.Decrement(ref jobsRodando);
        EstadoMudou?.Invoke();
        if (sucesso) DadosAtualizados?.Invoke();
    }

    /// <summary>Marca a atualização completa como em andamento. Falso se já há uma atualização rodando (não enfileira).</summary>
    public bool IniciarAtualizacaoCompleta()
    {
        if (Atualizando || Interlocked.Exchange(ref atualizacaoCompletaPendente, 1) == 1) return false;
        EstadoMudou?.Invoke();
        return true;
    }

    public void ConcluirAtualizacaoCompleta() => Volatile.Write(ref atualizacaoCompletaPendente, 0);
}

namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Aviso, dentro do circuito, de que transações mudaram — a barra superior recalcula patrimônio e resultado.
/// </summary>
public sealed class NotificacoesCarteira
{
    public event Action? CarteiraAlterada;

    public void NotificarAlteracao() => CarteiraAlterada?.Invoke();
}

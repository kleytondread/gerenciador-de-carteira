using Wal.Services.Common;

namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Aviso, dentro do circuito, de que a carteira mudou: transações salvas nas telas ou dados novos gravados por um
/// job (<see cref="AtualizacoesDados"/>). A barra superior e a tela aberta recarregam.
/// O aviso dos jobs chega de outra thread; quem escuta redesenha via InvokeAsync.
/// </summary>
public sealed class NotificacoesCarteira : IDisposable
{
    private readonly AtualizacoesDados atualizacoes;

    public NotificacoesCarteira(AtualizacoesDados atualizacoes)
    {
        this.atualizacoes = atualizacoes;
        atualizacoes.DadosAtualizados += NotificarAlteracao;
    }

    public event Action? CarteiraAlterada;

    public void NotificarAlteracao() => CarteiraAlterada?.Invoke();

    public void Dispose() => atualizacoes.DadosAtualizados -= NotificarAlteracao;
}

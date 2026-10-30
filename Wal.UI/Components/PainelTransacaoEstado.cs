namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Pede ao painel lateral de transação (no MainLayout) que abra — de qualquer tela, sem sair dela.
/// </summary>
public sealed class PainelTransacaoEstado
{
    /// <param name="AtivoId">Ativo já escolhido (ex.: aberto pelo detalhe do ativo).</param>
    /// <param name="MovimentacaoId">Transação existente a editar; nulo para uma nova.</param>
    public sealed record Pedido(int? AtivoId, int? MovimentacaoId);

    public event Action<Pedido>? AbrirSolicitado;

    public void Nova(int? ativoId = null) => AbrirSolicitado?.Invoke(new Pedido(ativoId, null));

    public void Editar(int movimentacaoId) => AbrirSolicitado?.Invoke(new Pedido(null, movimentacaoId));
}

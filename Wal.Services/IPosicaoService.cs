namespace Wal.Services;

public record PosicaoCalculada(decimal Quantidade, decimal PrecoMedio);

public interface IPosicaoService
{
    /// <summary>
    /// Calcula quantidade acumulada e preço médio ponderado de um ativo a partir das Movimentacoes.
    /// </summary>
    /// <param name="ateData">Se informado, considera apenas movimentações com Data &lt;= ateData.</param>
    /// <param name="excluirMovimentacaoId">Se informado, ignora essa movimentação no cálculo — usado ao
    /// revalidar a posição durante a edição de uma movimentação existente (ver Tarefa 3.3).</param>
    Task<PosicaoCalculada> CalcularPosicaoAsync(int ativoId, DateOnly? ateData = null, int? excluirMovimentacaoId = null);
}

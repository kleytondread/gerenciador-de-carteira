using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

public interface IMovimentacaoService
{
    Task<ResultadoOperacao<MovimentacaoResponse>> CriarAsync(CriarMovimentacaoRequest request);

    Task<List<MovimentacaoResponse>> ListarAsync(int? ativoId = null);

    Task<ResultadoOperacao<MovimentacaoResponse>> ObterAsync(int id);

    Task<ResultadoOperacao<MovimentacaoResponse>> EditarAsync(int id, EditarMovimentacaoRequest request);

    /// <summary>
    /// Remoção definitiva (spec não pede soft delete para Movimentacao, diferente de Ativo).
    /// Bloqueada se deixar alguma venda posterior sem ações (ex.: excluir a compra que a cobria). Criar e
    /// editar também conferem o saldo dia a dia, não só na data da própria movimentação.
    /// </summary>
    Task<ResultadoOperacao> ExcluirAsync(int id);
}

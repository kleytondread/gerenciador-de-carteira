using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

public interface IMovimentacaoService
{
    Task<ResultadoOperacao<MovimentacaoResponse>> CriarAsync(CriarMovimentacaoRequest request);

    Task<List<MovimentacaoResponse>> ListarAsync(int? ativoId = null);

    Task<ResultadoOperacao<MovimentacaoResponse>> EditarAsync(int id, EditarMovimentacaoRequest request);

    /// <summary>
    /// Remoção definitiva (spec não pede soft delete para Movimentacao, diferente de Ativo).
    /// Limitação conhecida: excluir uma compra que deixaria uma venda posterior "no negativo" não é
    /// validado retroativamente neste MVP (validação só roda na criação/edição da própria movimentação).
    /// </summary>
    Task<ResultadoOperacao> ExcluirAsync(int id);
}

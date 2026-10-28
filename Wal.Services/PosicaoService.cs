using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Enums;

namespace Wal.Services;

public class PosicaoService(AppDbContext db) : IPosicaoService
{
    public async Task<PosicaoCalculada> CalcularPosicaoAsync(
        int ativoId, DateOnly? ateData = null, int? excluirMovimentacaoId = null)
    {
        var query = db.Movimentacoes.AsNoTracking().Where(m => m.AtivoId == ativoId);

        if (ateData.HasValue)
            query = query.Where(m => m.Data <= ateData.Value);

        if (excluirMovimentacaoId.HasValue)
            query = query.Where(m => m.Id != excluirMovimentacaoId.Value);

        var movimentacoes = await query.OrderBy(m => m.Data).ThenBy(m => m.Id).ToListAsync();

        decimal quantidade = 0;
        decimal precoMedio = 0;

        foreach (var movimentacao in movimentacoes)
        {
            if (movimentacao.Tipo == TipoMovimentacao.Compra)
            {
                var novaQuantidade = quantidade + movimentacao.Quantidade;
                precoMedio = novaQuantidade == 0
                    ? 0
                    : ((quantidade * precoMedio) + (movimentacao.Quantidade * movimentacao.PrecoUnitario)) / novaQuantidade;
                quantidade = novaQuantidade;
            }
            else // Venda: preço médio não muda
            {
                quantidade -= movimentacao.Quantidade;
            }
        }

        return new PosicaoCalculada(quantidade, precoMedio);
    }
}

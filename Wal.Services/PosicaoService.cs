using Microsoft.EntityFrameworkCore;
using Wal.Data;

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
        var posicao = CustoMedio.Calcular(movimentacoes, fiscal: false);
        return new PosicaoCalculada(posicao.Quantidade, posicao.PrecoMedio);
    }
}

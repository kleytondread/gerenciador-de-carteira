using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

public interface IAtivoService
{
    Task<ResultadoOperacao<AtivoResponse>> CriarAsync(CriarAtivoRequest request);

    Task<ResultadoOperacao<AtivoResponse>> ObterPorIdAsync(int id);

    Task<List<AtivoResponse>> ListarAsync(string? busca = null, bool incluirInativos = true);

    Task<ResultadoOperacao<AtivoResponse>> EditarAsync(int id, EditarAtivoRequest request);

    /// <summary>
    /// Soft delete (EstaAtivo = false). Bloqueado se houver qualquer Movimentacao associada ao ativo
    /// (spec, seção 4.1) — nesse caso o ativo não pode nem ser desativado por esta via.
    /// </summary>
    Task<ResultadoOperacao> ExcluirAsync(int id);

    /// <summary>Desfaz o soft delete (EstaAtivo = true). Idempotente.</summary>
    Task<ResultadoOperacao> ReativarAsync(int id);

    Task<ResultadoOperacao<List<CotacaoResponse>>> ObterCotacoesAsync(int ativoId, string? periodo);

    /// <summary>Todos os ativos com a situação na carteira e o estado das cotações, por ticker.</summary>
    Task<List<AtivoRastreadoResponse>> ObterRastreadosAsync();

    /// <summary>
    /// Liga ou desliga a sincronização diária do ativo (EstaAtivo). Diferente de <see cref="ExcluirAsync"/>, vale
    /// também para ativos com transações: pausar só para de buscar dados novos, nada é apagado.
    /// </summary>
    Task<ResultadoOperacao> DefinirRastreamentoAsync(int id, bool rastrear);

    /// <summary>
    /// Pregão mais recente até a data (inclusive), para sugerir o preço de uma transação. Nulo sem cotação.
    /// Atenção: o histórico vem ajustado pelos eventos corporativos posteriores (base de ações atual).
    /// </summary>
    Task<CotacaoResponse?> ObterCotacaoAteAsync(int ativoId, DateOnly data);
}

using Wal.Services.Dtos;

namespace Wal.Services;

public interface ICarteiraService
{
    /// <summary>Ativos com posição atual (Quantidade > 0), com cotação, valor e rentabilidade calculados.</summary>
    Task<List<PosicaoAtivoResponse>> ObterPosicoesAsync();

    /// <summary>
    /// Valor investido e atual das posições abertas, e o resultado desde o início (em R$ e em %),
    /// incluindo vendas, proventos e ativos já zerados.
    /// </summary>
    Task<ResumoCarteiraResponse> ObterResumoAsync();

    /// <summary>
    /// Valor da carteira dia a dia, desde a primeira movimentação registrada (período fixo "tudo" no MVP).
    /// </summary>
    Task<List<PontoRentabilidadeResponse>> ObterRentabilidadeHistoricaAsync();

    /// <summary>Patrimônio, resultado, variação do dia e datas de referência para a barra superior.</summary>
    Task<CabecalhoCarteiraResponse> ObterCabecalhoAsync();
}

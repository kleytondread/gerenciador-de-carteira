using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

/// <summary>
/// Lançamento e correção de proventos. Importados (Yahoo, Brapi) nunca são apagados nem têm a chave alterada,
/// porque a sincronização os recriaria: a correção fica em colunas próprias e a exclusão vira "oculto".
/// </summary>
public interface IProventoService
{
    Task<ResultadoOperacao<ProventoResponse>> ObterAsync(int id);

    Task<ResultadoOperacao<ProventoResponse>> LancarAsync(LancarProventoRequest request);

    Task<ResultadoOperacao<ProventoResponse>> EditarAsync(int id, EditarProventoRequest request);

    /// <summary>Manual: apaga. Importado: oculta (deixa de contar em tudo, pode ser restaurado).</summary>
    Task<ResultadoOperacao> ExcluirAsync(int id);

    /// <summary>Volta a considerar um provento importado que estava oculto.</summary>
    Task<ResultadoOperacao> RestaurarAsync(int id);

    /// <summary>Proventos importados ocultos, do pagamento mais recente para o mais antigo.</summary>
    Task<List<ProventoResponse>> ListarOcultosAsync();
}

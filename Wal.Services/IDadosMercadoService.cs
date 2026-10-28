namespace Wal.Services;

public interface IDadosMercadoService
{
    /// <summary>
    /// Sincronização incremental via brapi.dev — últimos 3 meses de cotações + proventos.
    /// Usada pelo job diário recorrente.
    /// </summary>
    Task SincronizarAtivoAsync(int ativoId);

    /// <summary>
    /// Carga histórica completa via Yahoo Finance (range=max).
    /// Deve ser chamada na primeira vez que um ativo é adicionado à carteira.
    /// Persiste apenas cotações; proventos vêm da brapi.dev via SincronizarAtivoAsync.
    /// </summary>
    Task CargaHistoricaCompletaAsync(int ativoId);
}

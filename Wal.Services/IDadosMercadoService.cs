namespace Wal.Services;

public interface IDadosMercadoService
{
    /// <summary>
    /// Sincronização incremental via Yahoo Finance (títulos do Tesouro: arquivo do Tesouro Transparente) —
    /// últimos 90 dias de cotações, proventos e eventos corporativos.
    /// Usada pelo job diário recorrente.
    /// </summary>
    Task SincronizarAtivoAsync(int ativoId);

    /// <summary>
    /// Carga histórica completa via Yahoo Finance (range=max).
    /// Deve ser chamada na primeira vez que um ativo é adicionado à carteira.
    /// Traz cotações, proventos e eventos corporativos desde 2020.
    /// </summary>
    Task CargaHistoricaCompletaAsync(int ativoId);
}

namespace Wal.Services;

public interface IAtivoJobScheduler
{
    /// <summary>
    /// Enfileira a carga histórica completa (cotações + proventos desde 2020-01-01) de um Ativo recém-cadastrado.
    /// Fire-and-forget: quem chama não aguarda o job terminar.
    /// </summary>
    string EnfileirarCargaHistoricaInicial(int ativoId);
}

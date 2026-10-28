namespace Wal.ExternalApis;

/// <summary>Valor de uma série do SGS como publicado (CDI e IPCA vêm em %, PTAX em R$).</summary>
public record ValorSerieSgs(DateOnly Data, decimal Valor);

public interface IBancoCentralClient
{
    /// <summary>
    /// Série temporal do Sistema Gerenciador de Séries do Banco Central (api.bcb.gov.br), entre as datas.
    /// Sem valores no intervalo devolve lista vazia.
    /// </summary>
    Task<List<ValorSerieSgs>> ObterSerieAsync(int codigoSerie, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default);
}

using Wal.Services.Dtos;

namespace Wal.Services;

public interface IIndicesReferenciaService
{
    /// <summary>
    /// Baixa CDI, IPCA e PTAX (Banco Central) e IBOV, IDIV e S&amp;P 500 (Yahoo) desde 2020 e grava o que mudou.
    /// Falha num índice não impede os demais.
    /// </summary>
    Task SincronizarAsync(CancellationToken cancellationToken = default);

    /// <summary>Rentabilidade acumulada do benchmark para cada dia corrido entre as datas (a inicial vale 0).</summary>
    /// <param name="taxaRealAnual">Só para <see cref="Benchmark.IpcaMais"/>: 0,06 = IPCA + 6% ao ano.</param>
    Task<List<PontoBenchmarkResponse>> ObterRentabilidadeAcumuladaAsync(
        Benchmark benchmark, DateOnly inicio, DateOnly fim, decimal taxaRealAnual = 0);

    /// <summary>Datas e último valor guardados de cada índice.</summary>
    Task<List<SituacaoIndiceResponse>> ObterSituacaoAsync();
}

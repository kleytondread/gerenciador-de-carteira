using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <summary>Referências que podem ser comparadas com a carteira no gráfico de rentabilidade.</summary>
public enum Benchmark
{
    CDI,
    IBOV,
    IPCA,
    IDIV,
    /// <summary>S&amp;P 500 convertido para reais pela PTAX.</summary>
    SP500,
    /// <summary>IPCA mais uma taxa real anual informada.</summary>
    IpcaMais
}

/// <summary>Rentabilidade acumulada do benchmark desde a data inicial (que vale 0), como fração.</summary>
public record PontoBenchmarkResponse(DateOnly Data, decimal RentabilidadeAcumulada);

/// <summary>Quanto de cada índice está no banco — para a aba Índices de referência.</summary>
public record SituacaoIndiceResponse(
    IndiceReferencia Indice,
    DateOnly? PrimeiraData,
    DateOnly? UltimaData,
    decimal? UltimoValor,
    int Quantidade);

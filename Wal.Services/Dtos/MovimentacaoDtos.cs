using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

public record CriarMovimentacaoRequest(
    int AtivoId,
    TipoMovimentacao Tipo,
    DateOnly Data,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas = 0);

public record EditarMovimentacaoRequest(
    TipoMovimentacao Tipo,
    DateOnly Data,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas = 0);

public record MovimentacaoResponse(
    int Id,
    int AtivoId,
    string AtivoTicker,
    TipoMovimentacao Tipo,
    DateOnly Data,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas);

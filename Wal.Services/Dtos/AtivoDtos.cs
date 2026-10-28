using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <param name="TituloTesouro">Só para Tipo = Tesouro: tipo do título como no arquivo do Tesouro (ex.: "Tesouro IPCA+").</param>
/// <param name="VencimentoTesouro">Só para Tipo = Tesouro. Ticker e nome são gerados a partir do título.</param>
public record CriarAtivoRequest(
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    string? TituloTesouro = null,
    DateOnly? VencimentoTesouro = null);

public record EditarAtivoRequest(string Nome, TipoAtivo Tipo);

public record AtivoResponse(
    int Id,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    bool EstaAtivo,
    DateTime DataCadastro);

public record CotacaoResponse(
    DateOnly Data,
    decimal Abertura,
    decimal Maxima,
    decimal Minima,
    decimal Fechamento,
    long Volume);

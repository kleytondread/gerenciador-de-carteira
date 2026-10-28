namespace Wal.Services.Dtos;

/// <summary>Título disponível para cadastro, com o preço e a taxa mais recentes do arquivo do Tesouro.</summary>
public record TituloTesouroResponse(
    string TipoTitulo,
    DateOnly Vencimento,
    string Categoria,
    string Nome,
    string Ticker,
    DateOnly DataUltimoPreco,
    decimal UltimoPreco,
    decimal? TaxaCompra);

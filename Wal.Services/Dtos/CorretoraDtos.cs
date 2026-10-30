namespace Wal.Services.Dtos;

/// <param name="Cnpj">Formatado (00.000.000/0000-00).</param>
/// <param name="Titulos">Quantos títulos do Tesouro estão nela (com algum, não pode ser excluída).</param>
public record CorretoraResponse(int Id, string Nome, string Cnpj, bool Principal, int Titulos);

/// <param name="Cnpj">Com ou sem máscara.</param>
/// <param name="Principal">Marca como principal (desmarca a anterior). A primeira cadastrada é sempre principal.</param>
public record SalvarCorretoraRequest(string Nome, string Cnpj, bool Principal = false);

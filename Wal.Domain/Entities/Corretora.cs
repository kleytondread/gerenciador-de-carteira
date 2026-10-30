namespace Wal.Domain.Entities;

/// <summary>
/// Instituição onde ficam os ativos. No IR é a fonte pagadora dos títulos do Tesouro Direto.
/// Uma delas é a principal: títulos novos vêm com ela.
/// </summary>
public class Corretora
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    /// <summary>Só os 14 dígitos.</summary>
    public string Cnpj { get; set; } = string.Empty;
    public bool Principal { get; set; }
}

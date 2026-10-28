using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

public class Movimentacao
{
    public int Id { get; set; }
    public int AtivoId { get; set; }
    public TipoMovimentacao Tipo { get; set; }
    public DateOnly Data { get; set; }
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Taxas { get; set; }

    public Ativo Ativo { get; set; } = null!;
}

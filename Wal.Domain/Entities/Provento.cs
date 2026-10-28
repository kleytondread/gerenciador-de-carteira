using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

public class Provento
{
    public int Id { get; set; }
    public int AtivoId { get; set; }
    public TipoProvento TipoProvento { get; set; }
    public OrigemDado Origem { get; set; }
    public DateOnly? DataCom { get; set; }
    public DateOnly DataPagamento { get; set; }
    public decimal ValorPorAcao { get; set; }
    public DateTime DataImportacao { get; set; } = DateTime.UtcNow;

    public Ativo Ativo { get; set; } = null!;
}

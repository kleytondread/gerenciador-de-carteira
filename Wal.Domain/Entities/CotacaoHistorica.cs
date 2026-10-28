namespace Wal.Domain.Entities;

public class CotacaoHistorica
{
    public int Id { get; set; }
    public int AtivoId { get; set; }
    public DateOnly Data { get; set; }
    public decimal Abertura { get; set; }
    public decimal Maxima { get; set; }
    public decimal Minima { get; set; }
    public decimal Fechamento { get; set; }
    public long Volume { get; set; }

    public Ativo Ativo { get; set; } = null!;
}

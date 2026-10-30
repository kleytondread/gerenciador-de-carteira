using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

/// <summary>DARF (código 6015) pago sobre o ganho em renda variável de um mês de apuração.</summary>
public class DarfPago
{
    public int Id { get; set; }

    /// <summary>Mês de apuração (competência) — não o mês do pagamento.</summary>
    public int Ano { get; set; }
    public int Mes { get; set; }

    public FichaRendaVariavel Ficha { get; set; }

    /// <summary>Só o principal: multa e juros de atraso não entram no "imposto pago" da ficha.</summary>
    public decimal ValorPrincipal { get; set; }

    public DateOnly DataPagamento { get; set; }
}

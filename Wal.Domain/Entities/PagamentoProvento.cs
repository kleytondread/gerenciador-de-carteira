namespace Wal.Domain.Entities;

/// <summary>
/// Parcela paga de um provento, como aparece no extrato da B3. Um provento declarado (tipo + data com + valor
/// por ação) pode ser pago em várias parcelas e datas — e é a data da parcela que decide o ano do dividendo no IR.
/// </summary>
public class PagamentoProvento
{
    public int Id { get; set; }
    public int ProventoId { get; set; }
    public DateOnly Data { get; set; }

    /// <summary>Quantidade com direito informada pela B3 (posição na data com).</summary>
    public decimal Quantidade { get; set; }

    /// <summary>Antes do IR retido (no JCP, líquido ÷ (1 − alíquota)).</summary>
    public decimal ValorBruto { get; set; }

    /// <summary>O que foi creditado na conta ("Valor da Operação" do extrato).</summary>
    public decimal ValorLiquido { get; set; }

    public Provento Provento { get; set; } = null!;
}

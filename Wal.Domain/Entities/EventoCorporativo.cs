using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

/// <summary>
/// Desdobramento, grupamento ou bonificação: muda a quantidade de ações sem entrada de dinheiro.
/// A fonte de cotações (Yahoo) ajusta todo o histórico de preços e proventos por esses eventos;
/// o cálculo de rentabilidade usa esta tabela para colocar as quantidades na mesma base.
/// </summary>
public class EventoCorporativo
{
    public int Id { get; set; }
    public int AtivoId { get; set; }

    /// <summary>Data ex: primeiro pregão já com a nova quantidade.</summary>
    public DateOnly Data { get; set; }

    /// <summary>
    /// Proporção "Numerador:Denominador" — quem tinha Denominador ações passa a ter Numerador.
    /// Ex.: desdobramento 2:1, grupamento 1:10, bonificação de 40% = 3,5:2,5.
    /// </summary>
    public decimal Numerador { get; set; }
    public decimal Denominador { get; set; }

    public OrigemDado Origem { get; set; }
    public DateTime DataImportacao { get; set; } = DateTime.UtcNow;

    public Ativo Ativo { get; set; } = null!;

    /// <summary>Multiplicador da quantidade (2 no desdobramento 2:1, 0,1 no grupamento 1:10).</summary>
    public decimal Fator => Numerador / Denominador;

    /// <summary>
    /// Tipo deduzido da proporção — o Yahoo não informa: fator inteiro maior que 1 é desdobramento,
    /// menor que 1 é grupamento, e o resto (11:10, 3,5:2,5, 4:3...) é bonificação.
    /// </summary>
    public TipoEventoCorporativo Tipo => Fator switch
    {
        < 1 => TipoEventoCorporativo.Grupamento,
        _ when Fator == decimal.Truncate(Fator) => TipoEventoCorporativo.Desdobramento,
        _ => TipoEventoCorporativo.Bonificacao
    };
}

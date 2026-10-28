using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

/// <summary>Um valor de um índice de referência numa data.</summary>
public class CotacaoIndice
{
    public int Id { get; set; }
    public IndiceReferencia Indice { get; set; }
    public DateOnly Data { get; set; }

    /// <summary>
    /// Depende do índice:
    ///  - CDI: taxa do dia como fração (0,00050788 = 0,050788% ao dia), vale do dia até o próximo dia útil;
    ///  - IPCA: variação do mês como fração, na data do 1º dia do mês;
    ///  - IBOV, SP500: pontos de fechamento; IDIV: preço ajustado do DIVO11;
    ///  - PTAX: reais por dólar.
    /// </summary>
    public decimal Valor { get; set; }

    public DateTime DataImportacao { get; set; } = DateTime.UtcNow;
}

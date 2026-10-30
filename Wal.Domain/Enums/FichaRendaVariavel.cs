namespace Wal.Domain.Enums;

/// <summary>
/// Ficha do Demonstrativo de Renda Variável a que um DARF se refere: operações comuns/day trade (uma única
/// linha de "imposto pago" por mês) ou fundos imobiliários/Fiagro (ficha própria).
/// </summary>
public enum FichaRendaVariavel
{
    OperacoesComuns = 0,
    FundosImobiliarios = 1
}

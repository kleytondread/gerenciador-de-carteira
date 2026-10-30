using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <summary>O que a importação do extrato da B3 faz com um provento (ver ConciliacaoProventosB3).</summary>
public enum AcaoImportacaoB3
{
    /// <summary>Provento do app explicado por inteiro: é ocultado e dá lugar aos da B3, um por tipo.</summary>
    Substituir,
    /// <summary>Como Substituir, mas ainda faltam parcelas (pagamento parcelado em andamento).</summary>
    Parcial,
    /// <summary>Nova parcela de um provento que já veio da B3.</summary>
    AdicionarParcela,
    /// <summary>Crédito sem provento correspondente no app: vira um provento da B3 sem data com.</summary>
    Criar,
    /// <summary>Já importado antes.</summary>
    Confirmado,
    /// <summary>Provento do Yahoo que repete um já trazido da B3.</summary>
    OcultarDuplicado,
    /// <summary>Não dá para decidir com segurança: não é aplicado; corrigir à mão.</summary>
    Ambiguo,
    /// <summary>Provento do app no período do extrato sem crédito na B3 (em geral, pagamento futuro). Só informa.</summary>
    SemCreditoNaB3
}

/// <param name="ValorBruto">No JCP, líquido ÷ (1 − alíquota).</param>
public record CreditoB3Response(int Linha, DateOnly Data, TipoProvento Tipo, decimal Quantidade, decimal ValorLiquido, decimal Aliquota, decimal ValorBruto);

/// <param name="ProventoId">Provento do app envolvido (o que é substituído, completado ou ocultado).</param>
/// <param name="ValorEsperado">Valor bruto que o app esperava receber.</param>
/// <param name="NovosProventos">Descrição dos proventos que serão criados.</param>
public record ItemImportacaoB3(
    AcaoImportacaoB3 Acao,
    string Ticker,
    int? ProventoId,
    DateOnly? DataEx,
    DateOnly? DataCom,
    decimal? ValorEsperado,
    decimal ValorRecebidoBruto,
    IReadOnlyList<CreditoB3Response> Creditos,
    IReadOnlyList<string> NovosProventos,
    string Detalhe,
    bool Aplicavel);

/// <summary>Linha do extrato que não é provento (bonificação, fração, leilão, subscrição...): só aparece na prévia.</summary>
public record LinhaInformativaB3(DateOnly Data, string Movimentacao, string Ticker, decimal Quantidade, decimal? Valor);

public sealed class PreviaImportacaoB3
{
    public required DateOnly Inicio { get; init; }
    public required DateOnly Fim { get; init; }
    public required IReadOnlyList<string> Arquivos { get; init; }
    public required IReadOnlyList<ItemImportacaoB3> Itens { get; init; }
    public required IReadOnlyList<LinhaInformativaB3> Informativos { get; init; }

    /// <summary>Compras e vendas liquidadas no período (não mexem em nada; servem de referência).</summary>
    public required int Liquidacoes { get; init; }

    /// <summary>Tickers com proventos no extrato que não estão cadastrados no app.</summary>
    public required IReadOnlyList<string> TickersNaoCadastrados { get; init; }

    public int Alteracoes => Itens.Count(i => i.Aplicavel);

    // Linhas lidas, para refazer a conciliação na hora de aplicar (o banco pode ter mudado desde a prévia)
    internal IReadOnlyList<Wal.Services.ImportacaoB3.ExtratoMovimentacaoB3.Linha> Linhas { get; init; } = [];
}

/// <param name="Backup">Arquivo de backup do banco feito antes de aplicar (nulo em banco em memória).</param>
public record ResumoImportacaoB3(int Alteracoes, int ProventosCriados, int ProventosOcultados, int ParcelasGravadas, string? Backup);

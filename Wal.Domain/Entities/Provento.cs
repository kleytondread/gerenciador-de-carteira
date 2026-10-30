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

    // Correções do usuário sobre um provento importado. Ficam em colunas próprias porque a sincronização
    // encontra o registro pela chave original (data de pagamento, tipo e origem) e só atualiza valor e data com.

    /// <summary>Tipo corrigido (o Yahoo grava tudo como dividendo, inclusive JCP).</summary>
    public TipoProvento? TipoCorrigido { get; set; }

    /// <summary>Data de pagamento real (o Yahoo só informa a data ex).</summary>
    public DateOnly? DataPagamentoCorrigida { get; set; }

    /// <summary>Provento importado que o usuário descartou: não entra em nenhum cálculo. Apagar não adiantaria, a sincronização recriaria.</summary>
    public bool Oculto { get; set; }

    public TipoProvento TipoEfetivo => TipoCorrigido ?? TipoProvento;

    public DateOnly DataPagamentoEfetiva => DataPagamentoCorrigida ?? DataPagamento;

    /// <summary>Parcelas pagas, vindas do extrato da B3 (só em proventos com Origem = ExtratoB3).</summary>
    public ICollection<PagamentoProvento> Pagamentos { get; set; } = [];

    public Ativo Ativo { get; set; } = null!;
}

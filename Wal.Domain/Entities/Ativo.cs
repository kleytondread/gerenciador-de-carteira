using Wal.Domain.Enums;

namespace Wal.Domain.Entities;

public class Ativo
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public TipoAtivo Tipo { get; set; }

    /// <summary>Porção da carteira na alocação (Brasil RV, Dólar RV, Renda Fixa). Tesouro é sempre Renda Fixa.</summary>
    public CategoriaAlocacao Categoria { get; set; }
    public bool EstaAtivo { get; set; } = true;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    // Só para Tipo = Tesouro: identificam o título no arquivo de preços do Tesouro Transparente
    // (ex.: "Tesouro IPCA+" com vencimento 15/05/2045).
    public string? TituloTesouro { get; set; }
    public DateOnly? VencimentoTesouro { get; set; }

    /// <summary>CNPJ do emissor (empresa ou fundo), só os 14 dígitos. Exigido na declaração do IR.</summary>
    public string? Cnpj { get; set; }
    /// <summary>Só para Tipo = Tesouro: corretora onde o título fica (fonte pagadora no IR).</summary>
    public int? CorretoraId { get; set; }
    public Corretora? Corretora { get; set; }

    public ICollection<Movimentacao> Movimentacoes { get; set; } = [];
    public ICollection<CotacaoHistorica> CotacoesHistoricas { get; set; } = [];
    public ICollection<Provento> Proventos { get; set; } = [];
    public ICollection<EventoCorporativo> EventosCorporativos { get; set; } = [];
}

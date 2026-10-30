using Wal.Domain.Enums;
using Wal.Services.Irpf;

namespace Wal.Services.Dtos;

/// <summary>Corretora cadastrada (tela Configurações), com o CNPJ formatado. É a fonte pagadora do Tesouro.</summary>
public record CorretoraIrpf(string Nome, string Cnpj);

public enum AbaIrpf { Conferencia, BensDireitos, RendimentosIsentos, TributacaoExclusiva, RendaVariavel }

/// <summary>Bloqueia: o valor de alguma ficha pode estar errado. Aviso: conferir. Informação: lembrete.</summary>
public enum GravidadePendencia { Bloqueia, Aviso, Informacao }

public enum TipoPendenciaIrpf
{
    ProventoNaoConferido,
    ProventoSemDataCom,
    CnpjAusente,
    CnpjInvalido,
    CnpjFilial,
    CorretoraAusente,
    DarfPendente,
    InformeDaCorretora,
    ForaDoEscopo,
    Alerta,
    Lembrete
}

/// <param name="Aba">Aba cujos valores a pendência afeta.</param>
public record PendenciaIrpf(
    TipoPendenciaIrpf Tipo,
    GravidadePendencia Gravidade,
    AbaIrpf Aba,
    string Titulo,
    string Detalhe,
    int? AtivoId = null,
    int? ProventoId = null);

/// <param name="Cnpj">Formatado (00.000.000/0000-00), ou nulo se o ativo ainda não tem.</param>
/// <param name="CodigoConferido">O código já apareceu numa declaração entregue; senão, conferir no programa.</param>
/// <param name="Opcional">Ações/BDR com custo abaixo de R$ 1.000,00 nos dois anos: a declaração não é obrigatória.</param>
public record BemDireitoIrpf(
    CodigoIrpf Codigo,
    bool CodigoConferido,
    int? AtivoId,
    string Ticker,
    string Nome,
    string? Cnpj,
    bool NegociadoEmBolsa,
    string Discriminacao,
    decimal QuantidadeAnterior,
    decimal SituacaoAnterior,
    decimal QuantidadeAtual,
    decimal SituacaoAtual,
    bool Opcional);

/// <param name="Calculado">Valor estimado pelo app (sem o crédito da B3 ou da nota): conferir.</param>
public record ComposicaoRendimento(DateOnly Data, string Descricao, decimal ValorBruto, decimal ValorLiquido, bool Calculado);

/// <param name="Valor">O valor a declarar (no JCP, o líquido).</param>
public record RendimentoIrpf(
    CodigoIrpf Codigo,
    int? AtivoId,
    string Ticker,
    string NomeFonte,
    string? Cnpj,
    decimal Valor,
    bool Calculado,
    IReadOnlyList<ComposicaoRendimento> Composicao,
    string? Observacao = null);

public record ColunaRendaVariavelResponse(
    decimal Resultado, decimal PrejuizoAnterior, decimal BaseCalculo, decimal PrejuizoACompensar, decimal Aliquota, decimal ImpostoDevido);

public record VendaIrpfResponse(
    string Ticker, ClasseFiscal Classe, DateOnly Data, decimal Quantidade, decimal ValorAlienacao, decimal ValorVenda,
    decimal Custo, decimal Resultado, bool DayTrade);

/// <param name="ARecolher">Quanto o DARF do mês deveria ter (já com o acumulado de meses abaixo de R$ 10).</param>
public record MesRendaVariavelResponse(
    int Mes,
    ColunaRendaVariavelResponse Comum,
    ColunaRendaVariavelResponse? DayTrade,
    decimal AlienacoesAcoes,
    bool AcoesIsentas,
    decimal GanhoIsentoAcoes,
    decimal IrrfMes,
    decimal IrrfCompensado,
    decimal IrrfACompensar,
    decimal ImpostoAPagar,
    decimal AcumuladoAnterior,
    decimal ARecolher,
    decimal ImpostoPago,
    decimal DarfPendente,
    bool TemMovimento,
    IReadOnlyList<VendaIrpfResponse> Vendas);

public record DarfResponse(int Id, int Ano, int Mes, FichaRendaVariavel Ficha, decimal ValorPrincipal, DateOnly DataPagamento);

/// <param name="Ano">Ano e mês de apuração (competência), não do pagamento.</param>
public record RegistrarDarfRequest(int Ano, int Mes, FichaRendaVariavel Ficha, decimal ValorPrincipal, DateOnly DataPagamento);

/// <param name="CodigosConferidos">Falso para anos cujo programa ainda não foi conferido (usa a tabela do último).</param>
public record RelatorioIrpfResponse(
    int Ano,
    IReadOnlyList<int> AnosDisponiveis,
    bool AnoEmAndamento,
    bool CodigosConferidos,
    IReadOnlyList<CorretoraIrpf> Corretoras,
    IReadOnlyList<PendenciaIrpf> Pendencias,
    IReadOnlyList<BemDireitoIrpf> BensDireitos,
    IReadOnlyList<RendimentoIrpf> Isentos,
    IReadOnlyList<RendimentoIrpf> Exclusivos,
    IReadOnlyList<MesRendaVariavelResponse> RendaVariavel,
    IReadOnlyList<MesRendaVariavelResponse> RendaVariavelFii,
    IReadOnlyList<DarfResponse> Darfs);

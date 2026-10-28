using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <param name="TituloTesouro">Só para Tipo = Tesouro: tipo do título como no arquivo do Tesouro (ex.: "Tesouro IPCA+").</param>
/// <param name="VencimentoTesouro">Só para Tipo = Tesouro. Ticker e nome são gerados a partir do título.</param>
public record CriarAtivoRequest(
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    string? TituloTesouro = null,
    DateOnly? VencimentoTesouro = null);

public record EditarAtivoRequest(string Nome, TipoAtivo Tipo);

public record AtivoResponse(
    int Id,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    bool EstaAtivo,
    DateTime DataCadastro);

public enum SituacaoNaCarteira { Aberta, Encerrada, SemTransacoes }

public enum SaudeCotacoes { EmDia, Desatualizada, SemCotacao }

/// <summary>Um ativo na tela de Ativos rastreados: o que o app busca dele e em que estado estão os dados.</summary>
/// <param name="Rastreando">A sincronização diária busca cotações e proventos dele (= EstaAtivo).</param>
/// <param name="Quantidade">Quantidade registrada hoje (compras − vendas).</param>
/// <param name="Saude">Desatualizada: a última cotação está mais de 5 dias atrás do pregão mais recente da carteira.</param>
public record AtivoRastreadoResponse(
    int Id,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    bool Rastreando,
    SituacaoNaCarteira Situacao,
    decimal Quantidade,
    DateOnly? PrimeiraCotacao,
    DateOnly? UltimaCotacao,
    int Proventos,
    SaudeCotacoes Saude);

public record CotacaoResponse(
    DateOnly Data,
    decimal Abertura,
    decimal Maxima,
    decimal Minima,
    decimal Fechamento,
    long Volume);

using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <param name="TituloTesouro">Só para Tipo = Tesouro: tipo do título como no arquivo do Tesouro (ex.: "Tesouro IPCA+").</param>
/// <param name="VencimentoTesouro">Só para Tipo = Tesouro. Ticker e nome são gerados a partir do título.</param>
/// <param name="Categoria">Porção da carteira; nula usa a sugestão pelo tipo (CategoriasAlocacao.Padrao).</param>
/// <param name="Cnpj">CNPJ do emissor, com ou sem máscara; opcional.</param>
/// <param name="CorretoraId">Só para Tesouro: onde o título fica. Nula usa a corretora principal.</param>
public record CriarAtivoRequest(
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    string? TituloTesouro = null,
    DateOnly? VencimentoTesouro = null,
    CategoriaAlocacao? Categoria = null,
    string? Cnpj = null,
    int? CorretoraId = null);

/// <param name="Categoria">Nula mantém a atual. Ignorada para Tesouro (sempre renda fixa).</param>
/// <param name="Cnpj">Nulo mantém o atual; vazio apaga.</param>
/// <param name="CorretoraId">Só para Tesouro. Nula mantém a atual.</param>
public record EditarAtivoRequest(string Nome, TipoAtivo Tipo, CategoriaAlocacao? Categoria = null, string? Cnpj = null, int? CorretoraId = null);

public record AtivoResponse(
    int Id,
    string Ticker,
    string Nome,
    TipoAtivo Tipo,
    bool EstaAtivo,
    DateTime DataCadastro,
    CategoriaAlocacao Categoria,
    string? Cnpj = null,
    int? CorretoraId = null);

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
    SaudeCotacoes Saude,
    CategoriaAlocacao Categoria,
    string? Cnpj = null,
    int? CorretoraId = null);

public record CotacaoResponse(
    DateOnly Data,
    decimal Abertura,
    decimal Maxima,
    decimal Minima,
    decimal Fechamento,
    long Volume);

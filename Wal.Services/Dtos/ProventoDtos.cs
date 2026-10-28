using Wal.Domain.Enums;

namespace Wal.Services.Dtos;

/// <summary>Provento lançado à mão (Origem = Manual), com o valor por ação da época.</summary>
/// <param name="DataCom">Último dia com direito: quem tinha o ativo no fim desse dia recebe.</param>
public record LancarProventoRequest(
    int AtivoId,
    TipoProvento Tipo,
    DateOnly DataCom,
    DateOnly DataPagamento,
    decimal ValorPorAcao);

/// <summary>
/// Correção de um provento. Num importado só tipo e data de pagamento mudam (data com e valor vêm da fonte e
/// ficam como estão); num manual, tudo.
/// </summary>
public record EditarProventoRequest(
    TipoProvento Tipo,
    DateOnly DataPagamento,
    DateOnly? DataCom = null,
    decimal? ValorPorAcao = null);

/// <summary>Como o provento está gravado, para preencher o formulário de edição.</summary>
/// <param name="Importado">Veio de uma fonte automática: data com e valor não são editáveis.</param>
public record ProventoResponse(
    int Id,
    int AtivoId,
    string Ticker,
    TipoProvento Tipo,
    OrigemDado Origem,
    DateOnly? DataCom,
    DateOnly DataPagamento,
    decimal ValorPorAcao,
    bool Importado,
    bool Oculto);

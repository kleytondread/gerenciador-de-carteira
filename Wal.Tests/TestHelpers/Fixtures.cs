using Wal.Domain.Entities;
using Wal.Domain.Enums;

namespace Wal.Tests.TestHelpers;

/// <summary>Fábricas curtas para entidades de domínio usadas repetidamente nos testes.</summary>
internal static class Fixtures
{
    public static Ativo NovoAtivo(
        string ticker = "PETR4", string nome = "Petrobras PN", TipoAtivo tipo = TipoAtivo.Acao, bool estaAtivo = true) =>
        new() { Ticker = ticker, Nome = nome, Tipo = tipo, EstaAtivo = estaAtivo, DataCadastro = DateTime.UtcNow };

    public static Movimentacao NovaMovimentacao(
        int ativoId, TipoMovimentacao tipo, DateOnly data, decimal quantidade, decimal precoUnitario, decimal taxas = 0) =>
        new() { AtivoId = ativoId, Tipo = tipo, Data = data, Quantidade = quantidade, PrecoUnitario = precoUnitario, Taxas = taxas };

    public static CotacaoHistorica NovaCotacao(
        int ativoId, DateOnly data, decimal fechamento,
        decimal? abertura = null, decimal? maxima = null, decimal? minima = null, long volume = 1000) =>
        new()
        {
            AtivoId = ativoId,
            Data = data,
            Abertura = abertura ?? fechamento,
            Maxima = maxima ?? fechamento,
            Minima = minima ?? fechamento,
            Fechamento = fechamento,
            Volume = volume
        };

    public static EventoCorporativo NovoEvento(int ativoId, DateOnly data, decimal numerador, decimal denominador) =>
        new()
        {
            AtivoId = ativoId,
            Data = data,
            Numerador = numerador,
            Denominador = denominador,
            Origem = OrigemDado.YahooFinance,
            DataImportacao = DateTime.UtcNow
        };

    public static Provento NovoProvento(
        int ativoId, DateOnly dataPagamento, decimal valorPorAcao,
        TipoProvento tipo = TipoProvento.Dividendo, OrigemDado origem = OrigemDado.YahooFinance) =>
        new()
        {
            AtivoId = ativoId,
            TipoProvento = tipo,
            Origem = origem,
            DataCom = dataPagamento,
            DataPagamento = dataPagamento,
            ValorPorAcao = valorPorAcao,
            DataImportacao = DateTime.UtcNow
        };
}

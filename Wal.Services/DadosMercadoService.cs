using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services.TesouroDireto;

namespace Wal.Services;

public class DadosMercadoService(
    AppDbContext db,
    IYahooFinanceClient yahooClient,
    ITesouroDiretoService tesouroDireto,
    ILogger<DadosMercadoService> logger) : IDadosMercadoService
{
    // 2020-01-01 UTC — ponto de partida da carga histórica completa
    private const long Period1CargaCompleta = 1_577_836_800L;

    public async Task SincronizarAtivoAsync(int ativoId)
    {
        var ativo = await db.Ativos.FindAsync(ativoId);
        if (ativo is null)
        {
            logger.LogWarning("SincronizarAtivo: ativo {AtivoId} não encontrado.", ativoId);
            return;
        }

        if (ativo.Tipo == TipoAtivo.Tesouro)
        {
            await SincronizarTesouroAsync(ativo);
            return;
        }

        // Atualização incremental: últimos 90 dias de cotações e proventos
        var period1 = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeSeconds();

        YahooQuoteResult resultado;
        try
        {
            resultado = await yahooClient.ObterDadosAsync(ativo.Ticker, period1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao buscar dados do Yahoo Finance para {Ticker}.", ativo.Ticker);
            throw;
        }

        var (cotacoesInseridas, cotacoesAtualizadas) = await UpsertCotacoesAsync(
            ativoId, resultado.HistoricalDataPrice.Select(i => YahooMapper.ToCotacao(ativoId, i)));

        var (proventosInseridos, proventosAtualizados) = await UpsertProventosAsync(
            ativoId, resultado.Dividends.Select(i => YahooMapper.ToProvento(ativoId, i)));

        var eventosInseridos = await InserirEventosCorporativosAsync(
            ativoId, resultado.Splits.Select(i => YahooMapper.ToEventoCorporativo(ativoId, i)));

        logger.LogInformation(
            "Sincronização {Ticker}: cotações +{Ci}/~{Ca} | proventos +{Pi}/~{Pa} | eventos +{Ei}",
            ativo.Ticker, cotacoesInseridas, cotacoesAtualizadas, proventosInseridos, proventosAtualizados, eventosInseridos);
    }

    public async Task CargaHistoricaCompletaAsync(int ativoId)
    {
        var ativo = await db.Ativos.FindAsync(ativoId);
        if (ativo is null)
        {
            logger.LogWarning("CargaHistoricaCompleta: ativo {AtivoId} não encontrado.", ativoId);
            return;
        }

        if (ativo.Tipo == TipoAtivo.Tesouro)
        {
            await SincronizarTesouroAsync(ativo);
            return;
        }

        YahooQuoteResult resultado;
        try
        {
            resultado = await yahooClient.ObterDadosAsync(ativo.Ticker, Period1CargaCompleta);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao buscar histórico completo no Yahoo Finance para {Ticker}.", ativo.Ticker);
            throw;
        }

        var (cotacoesInseridas, cotacoesAtualizadas) = await UpsertCotacoesAsync(
            ativoId, resultado.HistoricalDataPrice.Select(i => YahooMapper.ToCotacao(ativoId, i)));

        var (proventosInseridos, proventosAtualizados) = await UpsertProventosAsync(
            ativoId, resultado.Dividends.Select(i => YahooMapper.ToProvento(ativoId, i)));

        var eventosInseridos = await InserirEventosCorporativosAsync(
            ativoId, resultado.Splits.Select(i => YahooMapper.ToEventoCorporativo(ativoId, i)));

        logger.LogInformation(
            "Carga histórica completa {Ticker}: cotações +{Ci}/~{Ca} | proventos +{Pi}/~{Pa} | eventos +{Ei}",
            ativo.Ticker, cotacoesInseridas, cotacoesAtualizadas, proventosInseridos, proventosAtualizados, eventosInseridos);
    }

    // Título do Tesouro: o arquivo traz o histórico inteiro, então carga completa e sincronização são a mesma
    // coisa — grava todos os preços (o upsert por data não duplica). Um preço por dia, sem OHLC nem volume.
    private async Task SincronizarTesouroAsync(Ativo ativo)
    {
        if (ativo.TituloTesouro is null || ativo.VencimentoTesouro is null)
        {
            logger.LogWarning("Tesouro {Ticker}: título ou vencimento não informado no cadastro.", ativo.Ticker);
            return;
        }

        var precos = await tesouroDireto.ObterPrecosAsync(ativo.TituloTesouro, ativo.VencimentoTesouro.Value);
        if (precos.Count == 0)
        {
            // Sem arquivo (download falhou) ou título fora do arquivo: lança para o job registrar a falha
            throw new InvalidOperationException(
                $"Sem preços do Tesouro Direto para {ativo.Nome} — o arquivo pode estar indisponível.");
        }

        var (inseridas, atualizadas) = await UpsertCotacoesAsync(ativo.Id, precos.Select(p => (CotacaoHistorica?)new CotacaoHistorica
        {
            AtivoId = ativo.Id,
            Data = p.Data,
            Abertura = p.Preco,
            Maxima = p.Preco,
            Minima = p.Preco,
            Fechamento = p.Preco,
            Volume = 0
        }));

        logger.LogInformation("Tesouro {Ticker}: cotações +{Ci}/~{Ca}", ativo.Ticker, inseridas, atualizadas);
    }

    // Eventos corporativos não mudam depois de anunciados: só insere os que ainda não existem
    // para a mesma data e origem.
    private async Task<int> InserirEventosCorporativosAsync(int ativoId, IEnumerable<EventoCorporativo?> eventos)
    {
        var existentes = await db.EventosCorporativos
            .Where(e => e.AtivoId == ativoId)
            .Select(e => new { e.Data, e.Origem })
            .ToListAsync();
        var chaves = existentes.Select(e => (e.Data, e.Origem)).ToHashSet();

        var inseridos = 0;
        foreach (var evento in eventos)
        {
            if (evento is null || !chaves.Add((evento.Data, evento.Origem))) continue;
            db.EventosCorporativos.Add(evento);
            inseridos++;
        }

        await db.SaveChangesAsync();
        return inseridos;
    }

    // Upsert fonte-agnóstico: recebe cotações já mapeadas de qualquer origem
    private async Task<(int inseridos, int atualizados)> UpsertCotacoesAsync(
        int ativoId, IEnumerable<CotacaoHistorica?> cotacoes)
    {
        int inseridos = 0, atualizados = 0;

        var existentes = await db.CotacoesHistoricas
            .Where(c => c.AtivoId == ativoId)
            .ToDictionaryAsync(c => c.Data);

        foreach (var cotacao in cotacoes)
        {
            if (cotacao is null) continue;

            if (existentes.TryGetValue(cotacao.Data, out var existente))
            {
                existente.Abertura   = cotacao.Abertura;
                existente.Maxima     = cotacao.Maxima;
                existente.Minima     = cotacao.Minima;
                existente.Fechamento = cotacao.Fechamento;
                existente.Volume     = cotacao.Volume;
                atualizados++;
            }
            else
            {
                db.CotacoesHistoricas.Add(cotacao);
                existentes[cotacao.Data] = cotacao;
                inseridos++;
            }
        }

        await db.SaveChangesAsync();
        return (inseridos, atualizados);
    }

    // Upsert fonte-agnóstico: recebe proventos já mapeados de qualquer origem
    private async Task<(int inseridos, int atualizados)> UpsertProventosAsync(
        int ativoId, IEnumerable<Provento?> proventos)
    {
        int inseridos = 0, atualizados = 0;

        var existentes = await db.Proventos
            .Where(p => p.AtivoId == ativoId)
            .ToListAsync();

        foreach (var provento in proventos)
        {
            if (provento is null) continue;

            // A origem entra na chave: sem ela, um provento cadastrado manualmente com a mesma data
            // e tipo seria sobrescrito pela sincronização.
            var existente = existentes.FirstOrDefault(p =>
                p.DataPagamento == provento.DataPagamento &&
                p.TipoProvento  == provento.TipoProvento &&
                p.Origem        == provento.Origem);

            if (existente is not null)
            {
                existente.ValorPorAcao   = provento.ValorPorAcao;
                existente.DataCom        = provento.DataCom;
                existente.DataImportacao = DateTime.UtcNow;
                atualizados++;
            }
            else
            {
                db.Proventos.Add(provento);
                existentes.Add(provento);
                inseridos++;
            }
        }

        await db.SaveChangesAsync();
        return (inseridos, atualizados);
    }
}

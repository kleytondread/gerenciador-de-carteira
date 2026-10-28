using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

public class ProventoService(AppDbContext db) : IProventoService
{
    public async Task<ResultadoOperacao<ProventoResponse>> ObterAsync(int id)
    {
        var provento = await db.Proventos.Include(p => p.Ativo).FirstOrDefaultAsync(p => p.Id == id);
        return provento is null ? NaoEncontrado() : ResultadoOperacao<ProventoResponse>.Ok(ParaResponse(provento));
    }

    public async Task<ResultadoOperacao<ProventoResponse>> LancarAsync(LancarProventoRequest request)
    {
        var ativo = await db.Ativos.FindAsync(request.AtivoId);
        if (ativo is null)
            return ResultadoOperacao<ProventoResponse>.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        if (Validar(request.ValorPorAcao, request.DataCom, request.DataPagamento) is string erro)
            return ResultadoOperacao<ProventoResponse>.Falha(erro, TipoErroOperacao.Validacao);

        if (await ExisteManualAsync(request.AtivoId, request.DataPagamento, request.Tipo, ignorarId: null))
            return ResultadoOperacao<ProventoResponse>.Falha(
                "Já existe um provento manual deste tipo com essa data de pagamento.", TipoErroOperacao.Conflito);

        var provento = new Provento
        {
            AtivoId = request.AtivoId,
            Ativo = ativo,
            TipoProvento = request.Tipo,
            Origem = OrigemDado.Manual,
            DataCom = request.DataCom,
            DataPagamento = request.DataPagamento,
            ValorPorAcao = request.ValorPorAcao,
            DataImportacao = DateTime.UtcNow
        };
        db.Proventos.Add(provento);
        await db.SaveChangesAsync();
        return ResultadoOperacao<ProventoResponse>.Ok(ParaResponse(provento));
    }

    public async Task<ResultadoOperacao<ProventoResponse>> EditarAsync(int id, EditarProventoRequest request)
    {
        var provento = await db.Proventos.Include(p => p.Ativo).FirstOrDefaultAsync(p => p.Id == id);
        if (provento is null) return NaoEncontrado();

        if (provento.Origem == OrigemDado.Manual)
        {
            var dataCom = request.DataCom ?? provento.DataCom;
            var valor = request.ValorPorAcao ?? provento.ValorPorAcao;
            if (Validar(valor, dataCom, request.DataPagamento) is string erro)
                return ResultadoOperacao<ProventoResponse>.Falha(erro, TipoErroOperacao.Validacao);
            if (await ExisteManualAsync(provento.AtivoId, request.DataPagamento, request.Tipo, ignorarId: id))
                return ResultadoOperacao<ProventoResponse>.Falha(
                    "Já existe um provento manual deste tipo com essa data de pagamento.", TipoErroOperacao.Conflito);

            provento.TipoProvento = request.Tipo;
            provento.DataPagamento = request.DataPagamento;
            provento.DataCom = dataCom;
            provento.ValorPorAcao = valor;
        }
        else
        {
            // Importado: a chave original fica intacta para a sincronização continuar encontrando o registro
            if (provento.DataCom is DateOnly dataEx && request.DataPagamento < dataEx)
                return ResultadoOperacao<ProventoResponse>.Falha(
                    "O pagamento não pode ser antes da data ex.", TipoErroOperacao.Validacao);

            provento.TipoCorrigido = request.Tipo == provento.TipoProvento ? null : request.Tipo;
            provento.DataPagamentoCorrigida = request.DataPagamento == provento.DataPagamento ? null : request.DataPagamento;
        }

        await db.SaveChangesAsync();
        return ResultadoOperacao<ProventoResponse>.Ok(ParaResponse(provento));
    }

    public async Task<ResultadoOperacao> ExcluirAsync(int id)
    {
        var provento = await db.Proventos.FindAsync(id);
        if (provento is null) return ResultadoOperacao.Falha("Provento não encontrado.", TipoErroOperacao.NaoEncontrado);

        if (provento.Origem == OrigemDado.Manual)
            db.Proventos.Remove(provento);
        else
            provento.Oculto = true;

        await db.SaveChangesAsync();
        return ResultadoOperacao.Ok();
    }

    public async Task<ResultadoOperacao> RestaurarAsync(int id)
    {
        var provento = await db.Proventos.FindAsync(id);
        if (provento is null) return ResultadoOperacao.Falha("Provento não encontrado.", TipoErroOperacao.NaoEncontrado);

        provento.Oculto = false;
        await db.SaveChangesAsync();
        return ResultadoOperacao.Ok();
    }

    public async Task<List<ProventoResponse>> ListarOcultosAsync() =>
        (await db.Proventos.AsNoTracking().Include(p => p.Ativo).Where(p => p.Oculto).ToListAsync())
        .OrderByDescending(p => p.DataPagamentoEfetiva)
        .Select(ParaResponse)
        .ToList();

    private static string? Validar(decimal valorPorAcao, DateOnly? dataCom, DateOnly dataPagamento) =>
        valorPorAcao <= 0 ? "O valor por ação deve ser maior que zero."
        : dataCom is DateOnly c && dataPagamento < c ? "O pagamento não pode ser antes da data com."
        : null;

    // O índice único (ativo, pagamento, tipo, origem) recusaria o segundo; melhor avisar antes
    private Task<bool> ExisteManualAsync(int ativoId, DateOnly dataPagamento, TipoProvento tipo, int? ignorarId) =>
        db.Proventos.AnyAsync(p =>
            p.AtivoId == ativoId && p.Origem == OrigemDado.Manual &&
            p.DataPagamento == dataPagamento && p.TipoProvento == tipo && p.Id != ignorarId);

    private static ProventoResponse ParaResponse(Provento p) => new(
        p.Id, p.AtivoId, p.Ativo?.Ticker ?? "?", p.TipoEfetivo, p.Origem, p.DataCom, p.DataPagamentoEfetiva,
        p.ValorPorAcao, p.Origem != OrigemDado.Manual, p.Oculto);

    private static ResultadoOperacao<ProventoResponse> NaoEncontrado() =>
        ResultadoOperacao<ProventoResponse>.Falha("Provento não encontrado.", TipoErroOperacao.NaoEncontrado);
}

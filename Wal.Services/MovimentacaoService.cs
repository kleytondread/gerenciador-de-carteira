using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

public class MovimentacaoService(
    AppDbContext db,
    IPosicaoService posicaoService,
    ILogger<MovimentacaoService> logger) : IMovimentacaoService
{
    public async Task<ResultadoOperacao<MovimentacaoResponse>> CriarAsync(CriarMovimentacaoRequest request)
    {
        var erroCampos = ValidarCampos(request.Tipo, request.Data, request.Quantidade, request.PrecoUnitario, request.Taxas);
        if (erroCampos is not null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha(erroCampos, TipoErroOperacao.Validacao);

        var ativo = await db.Ativos.FindAsync(request.AtivoId);
        if (ativo is null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        if (request.Tipo == TipoMovimentacao.Venda)
        {
            var erroVenda = await ValidarVendaAsync(request.AtivoId, request.Data, request.Quantidade);
            if (erroVenda is not null)
                return ResultadoOperacao<MovimentacaoResponse>.Falha(erroVenda, TipoErroOperacao.Validacao);
        }

        var movimentacao = new Movimentacao
        {
            AtivoId = request.AtivoId,
            Tipo = request.Tipo,
            Data = request.Data,
            Quantidade = request.Quantidade,
            PrecoUnitario = request.PrecoUnitario,
            Taxas = request.Taxas
        };

        db.Movimentacoes.Add(movimentacao);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Movimentação registrada: {Tipo} de {Quantidade} {Ticker} @ {PrecoUnitario} em {Data}.",
            movimentacao.Tipo, movimentacao.Quantidade, ativo.Ticker, movimentacao.PrecoUnitario, movimentacao.Data);

        return ResultadoOperacao<MovimentacaoResponse>.Ok(ParaResponse(movimentacao, ativo.Ticker));
    }

    public async Task<List<MovimentacaoResponse>> ListarAsync(int? ativoId = null)
    {
        var query = db.Movimentacoes.AsNoTracking().Include(m => m.Ativo).AsQueryable();

        if (ativoId.HasValue)
            query = query.Where(m => m.AtivoId == ativoId.Value);

        return await query
            .OrderByDescending(m => m.Data).ThenByDescending(m => m.Id)
            .Select(m => new MovimentacaoResponse(
                m.Id, m.AtivoId, m.Ativo.Ticker, m.Tipo, m.Data, m.Quantidade, m.PrecoUnitario, m.Taxas))
            .ToListAsync();
    }

    public async Task<ResultadoOperacao<MovimentacaoResponse>> EditarAsync(int id, EditarMovimentacaoRequest request)
    {
        var movimentacao = await db.Movimentacoes.Include(m => m.Ativo).FirstOrDefaultAsync(m => m.Id == id);
        if (movimentacao is null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha("Movimentação não encontrada.", TipoErroOperacao.NaoEncontrado);

        var erroCampos = ValidarCampos(request.Tipo, request.Data, request.Quantidade, request.PrecoUnitario, request.Taxas);
        if (erroCampos is not null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha(erroCampos, TipoErroOperacao.Validacao);

        if (request.Tipo == TipoMovimentacao.Venda)
        {
            var erroVenda = await ValidarVendaAsync(movimentacao.AtivoId, request.Data, request.Quantidade, excluirMovimentacaoId: id);
            if (erroVenda is not null)
                return ResultadoOperacao<MovimentacaoResponse>.Falha(erroVenda, TipoErroOperacao.Validacao);
        }

        movimentacao.Tipo = request.Tipo;
        movimentacao.Data = request.Data;
        movimentacao.Quantidade = request.Quantidade;
        movimentacao.PrecoUnitario = request.PrecoUnitario;
        movimentacao.Taxas = request.Taxas;
        await db.SaveChangesAsync();

        return ResultadoOperacao<MovimentacaoResponse>.Ok(ParaResponse(movimentacao, movimentacao.Ativo.Ticker));
    }

    public async Task<ResultadoOperacao> ExcluirAsync(int id)
    {
        var movimentacao = await db.Movimentacoes.FindAsync(id);
        if (movimentacao is null)
            return ResultadoOperacao.Falha("Movimentação não encontrada.", TipoErroOperacao.NaoEncontrado);

        db.Movimentacoes.Remove(movimentacao);
        await db.SaveChangesAsync();

        logger.LogInformation("Movimentação excluída (hard delete): Id {MovimentacaoId}, AtivoId {AtivoId}.",
            id, movimentacao.AtivoId);

        return ResultadoOperacao.Ok();
    }

    private async Task<string?> ValidarVendaAsync(
        int ativoId, DateOnly data, decimal quantidadeVenda, int? excluirMovimentacaoId = null)
    {
        var posicao = await posicaoService.CalcularPosicaoAsync(ativoId, data, excluirMovimentacaoId);
        if (quantidadeVenda > posicao.Quantidade)
            return $"Quantidade indisponível para venda: disponível {posicao.Quantidade}, solicitado {quantidadeVenda}.";

        return null;
    }

    private static string? ValidarCampos(
        TipoMovimentacao tipo, DateOnly data, decimal quantidade, decimal precoUnitario, decimal taxas)
    {
        if (!Enum.IsDefined(tipo)) return "Tipo de movimentação inválido (use Compra ou Venda).";
        if (quantidade <= 0) return "Quantidade deve ser maior que zero.";
        if (precoUnitario <= 0) return "Preço unitário deve ser maior que zero.";
        if (taxas < 0) return "Taxas não pode ser negativo.";
        if (data > DateOnly.FromDateTime(DateTime.Today)) return "A data da movimentação não pode estar no futuro.";
        return null;
    }

    private static MovimentacaoResponse ParaResponse(Movimentacao m, string ticker) =>
        new(m.Id, m.AtivoId, ticker, m.Tipo, m.Data, m.Quantidade, m.PrecoUnitario, m.Taxas);
}

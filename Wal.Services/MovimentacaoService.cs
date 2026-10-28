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

        var erroSaldo = await ValidarSaldoAsync(request.AtivoId, semMovimentacaoId: null,
            nova: (request.Data, request.Tipo, request.Quantidade));
        if (erroSaldo is not null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha(erroSaldo, TipoErroOperacao.Validacao);

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

    public async Task<ResultadoOperacao<MovimentacaoResponse>> ObterAsync(int id)
    {
        var m = await db.Movimentacoes.AsNoTracking().Include(x => x.Ativo).FirstOrDefaultAsync(x => x.Id == id);
        return m is null
            ? ResultadoOperacao<MovimentacaoResponse>.Falha("Movimentação não encontrada.", TipoErroOperacao.NaoEncontrado)
            : ResultadoOperacao<MovimentacaoResponse>.Ok(ParaResponse(m, m.Ativo.Ticker));
    }

    public async Task<ResultadoOperacao<MovimentacaoResponse>> EditarAsync(int id, EditarMovimentacaoRequest request)
    {
        var movimentacao = await db.Movimentacoes.Include(m => m.Ativo).FirstOrDefaultAsync(m => m.Id == id);
        if (movimentacao is null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha("Movimentação não encontrada.", TipoErroOperacao.NaoEncontrado);

        var erroCampos = ValidarCampos(request.Tipo, request.Data, request.Quantidade, request.PrecoUnitario, request.Taxas);
        if (erroCampos is not null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha(erroCampos, TipoErroOperacao.Validacao);

        var erroSaldo = await ValidarSaldoAsync(movimentacao.AtivoId, semMovimentacaoId: id,
            nova: (request.Data, request.Tipo, request.Quantidade));
        if (erroSaldo is not null)
            return ResultadoOperacao<MovimentacaoResponse>.Falha(erroSaldo, TipoErroOperacao.Validacao);

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

        // Excluir uma compra pode deixar uma venda posterior sem ações
        var erroSaldo = await ValidarSaldoAsync(movimentacao.AtivoId, semMovimentacaoId: id, nova: null);
        if (erroSaldo is not null)
            return ResultadoOperacao.Falha(erroSaldo, TipoErroOperacao.Validacao);

        db.Movimentacoes.Remove(movimentacao);
        await db.SaveChangesAsync();

        logger.LogInformation("Movimentação excluída (hard delete): Id {MovimentacaoId}, AtivoId {AtivoId}.",
            id, movimentacao.AtivoId);

        return ResultadoOperacao.Ok();
    }

    /// <summary>
    /// Confere a quantidade do ativo dia a dia como ficaria depois da alteração: nenhuma venda pode ficar sem ações.
    /// Olhar só a data da venda lançada não basta: excluir ou reduzir uma compra, ou lançar uma venda antiga,
    /// também pode descobrir uma venda posterior.
    /// </summary>
    /// <param name="semMovimentacaoId">Movimentação que sai (exclusão) ou é substituída (edição).</param>
    /// <param name="nova">Movimentação que entra (criação) ou a versão editada.</param>
    private async Task<string?> ValidarSaldoAsync(
        int ativoId, int? semMovimentacaoId, (DateOnly Data, TipoMovimentacao Tipo, decimal Quantidade)? nova)
    {
        // Decimal no SQLite é texto: as somas são feitas em memória
        var lista = (await db.Movimentacoes.AsNoTracking()
                .Where(m => m.AtivoId == ativoId && (semMovimentacaoId == null || m.Id != semMovimentacaoId))
                .Select(m => new { m.Data, m.Tipo, m.Quantidade })
                .ToListAsync())
            .Select(m => (m.Data, m.Tipo, m.Quantidade))
            .ToList();
        if (nova is { } n) lista.Add(n);

        decimal saldo = 0;
        foreach (var dia in lista.GroupBy(m => m.Data).OrderBy(g => g.Key))
        {
            // Compras do dia contam antes das vendas do mesmo dia (day trade)
            var disponivel = saldo + dia.Where(m => m.Tipo == TipoMovimentacao.Compra).Sum(m => m.Quantidade);
            var vendido = dia.Where(m => m.Tipo == TipoMovimentacao.Venda).Sum(m => m.Quantidade);
            if (vendido > disponivel)
                return $"Quantidade indisponível para venda em {dia.Key:dd/MM/yyyy}: disponível {disponivel}, vendido {vendido}.";
            saldo = disponivel - vendido;
        }
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

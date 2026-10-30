using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services;

/// <summary>Corretoras (tela Configurações). Sempre há exatamente uma principal enquanto houver alguma.</summary>
public interface ICorretoraService
{
    /// <summary>A principal primeiro, depois por nome.</summary>
    Task<List<CorretoraResponse>> ListarAsync();

    Task<ResultadoOperacao<CorretoraResponse>> CriarAsync(SalvarCorretoraRequest request);

    Task<ResultadoOperacao<CorretoraResponse>> EditarAsync(int id, SalvarCorretoraRequest request);

    /// <summary>Torna principal. Títulos do Tesouro ainda sem corretora passam a usar esta.</summary>
    Task<ResultadoOperacao> DefinirPrincipalAsync(int id);

    /// <summary>Recusa se algum título estiver nela. Se era a principal, a primeira restante (por nome) assume.</summary>
    Task<ResultadoOperacao> ExcluirAsync(int id);
}

public class CorretoraService(AppDbContext db) : ICorretoraService
{
    public async Task<List<CorretoraResponse>> ListarAsync()
    {
        var titulos = await db.Ativos.AsNoTracking()
            .Where(a => a.CorretoraId != null)
            .GroupBy(a => a.CorretoraId!.Value)
            .Select(g => new { Id = g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Quantidade);

        return (await db.Corretoras.AsNoTracking().ToListAsync())
            .OrderByDescending(c => c.Principal).ThenBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => ParaResponse(c, titulos.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<ResultadoOperacao<CorretoraResponse>> CriarAsync(SalvarCorretoraRequest request)
    {
        var (nome, cnpj, erro) = Validar(request);
        if (erro is not null) return ResultadoOperacao<CorretoraResponse>.Falha(erro, TipoErroOperacao.Validacao);
        if (await db.Corretoras.AnyAsync(c => c.Cnpj == cnpj))
            return ResultadoOperacao<CorretoraResponse>.Falha($"Já existe uma corretora com o CNPJ {Cnpj.Formatar(cnpj!)}.", TipoErroOperacao.Conflito);

        var corretora = new Corretora { Nome = nome!, Cnpj = cnpj! };
        db.Corretoras.Add(corretora);
        await db.SaveChangesAsync();

        // A primeira é sempre a principal
        if (request.Principal || !await db.Corretoras.AnyAsync(c => c.Principal))
            await TornarPrincipalAsync(corretora);

        return ResultadoOperacao<CorretoraResponse>.Ok(ParaResponse(corretora, await TitulosAsync(corretora.Id)));
    }

    public async Task<ResultadoOperacao<CorretoraResponse>> EditarAsync(int id, SalvarCorretoraRequest request)
    {
        var corretora = await db.Corretoras.FindAsync(id);
        if (corretora is null) return ResultadoOperacao<CorretoraResponse>.Falha("Corretora não encontrada.", TipoErroOperacao.NaoEncontrado);

        var (nome, cnpj, erro) = Validar(request);
        if (erro is not null) return ResultadoOperacao<CorretoraResponse>.Falha(erro, TipoErroOperacao.Validacao);
        if (await db.Corretoras.AnyAsync(c => c.Cnpj == cnpj && c.Id != id))
            return ResultadoOperacao<CorretoraResponse>.Falha($"Já existe uma corretora com o CNPJ {Cnpj.Formatar(cnpj!)}.", TipoErroOperacao.Conflito);

        corretora.Nome = nome!;
        corretora.Cnpj = cnpj!;
        await db.SaveChangesAsync();
        // Desmarcar não existe: a principal só muda marcando outra
        if (request.Principal && !corretora.Principal) await TornarPrincipalAsync(corretora);

        return ResultadoOperacao<CorretoraResponse>.Ok(ParaResponse(corretora, await TitulosAsync(id)));
    }

    public async Task<ResultadoOperacao> DefinirPrincipalAsync(int id)
    {
        var corretora = await db.Corretoras.FindAsync(id);
        if (corretora is null) return ResultadoOperacao.Falha("Corretora não encontrada.", TipoErroOperacao.NaoEncontrado);
        await TornarPrincipalAsync(corretora);
        return ResultadoOperacao.Ok();
    }

    public async Task<ResultadoOperacao> ExcluirAsync(int id)
    {
        var corretora = await db.Corretoras.FindAsync(id);
        if (corretora is null) return ResultadoOperacao.Falha("Corretora não encontrada.", TipoErroOperacao.NaoEncontrado);

        var titulos = await TitulosAsync(id);
        if (titulos > 0)
            return ResultadoOperacao.Falha(
                $"{corretora.Nome} tem {titulos} título(s) do Tesouro. Troque a corretora deles no cadastro do ativo antes de excluir.",
                TipoErroOperacao.Conflito);

        db.Corretoras.Remove(corretora);
        await db.SaveChangesAsync();

        if (corretora.Principal)
        {
            var proxima = (await db.Corretoras.ToListAsync()).OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
            if (proxima is not null) await TornarPrincipalAsync(proxima);
        }
        return ResultadoOperacao.Ok();
    }

    private async Task TornarPrincipalAsync(Corretora corretora)
    {
        foreach (var outra in await db.Corretoras.Where(c => c.Principal && c.Id != corretora.Id).ToListAsync())
            outra.Principal = false;
        corretora.Principal = true;

        // Títulos cadastrados antes de existir corretora (ou de outra já excluída) ficam nesta
        foreach (var titulo in await db.Ativos.Where(a => a.Tipo == TipoAtivo.Tesouro && a.CorretoraId == null).ToListAsync())
            titulo.CorretoraId = corretora.Id;

        await db.SaveChangesAsync();
    }

    private Task<int> TitulosAsync(int id) => db.Ativos.CountAsync(a => a.CorretoraId == id);

    private static (string? Nome, string? Cnpj, string? Erro) Validar(SalvarCorretoraRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome)) return (null, null, "Informe o nome da corretora.");
        var cnpj = Cnpj.Normalizar(request.Cnpj);
        if (cnpj is null) return (null, null, "Informe o CNPJ da corretora.");
        if (!Cnpj.Valido(cnpj)) return (null, null, $"CNPJ {request.Cnpj.Trim()} inválido: confira os 14 dígitos (os verificadores não conferem).");
        return (request.Nome.Trim(), cnpj, null);
    }

    private static CorretoraResponse ParaResponse(Corretora c, int titulos) => new(c.Id, c.Nome, Cnpj.Formatar(c.Cnpj), c.Principal, titulos);
}

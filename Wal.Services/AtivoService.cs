using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.TesouroDireto;

namespace Wal.Services;

public partial class AtivoService(
    AppDbContext db,
    IAtivoJobScheduler jobScheduler,
    ITesouroDiretoService tesouroDireto,
    ILogger<AtivoService> logger) : IAtivoService
{
    // Formato B3: raiz de 4 caracteres (começa com letra; os demais podem ser letra ou dígito)
    // + 1 ou 2 dígitos. Ex: PETR4, HGLG11 e ETFs com dígito na raiz, como B5P211 (raiz B5P2 + 11).
    // A tela de Ativos rastreados mostra a mensagem de erro daqui, sem validar o formato por conta própria.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{3}\d{1,2}$")]
    private static partial Regex TickerRegex();

    public async Task<ResultadoOperacao<AtivoResponse>> CriarAsync(CriarAtivoRequest request)
    {
        if (request.Tipo == TipoAtivo.Tesouro)
            return await CriarTesouroAsync(request);

        var ticker = (request.Ticker ?? string.Empty).Trim().ToUpperInvariant();

        if (!TickerRegex().IsMatch(ticker))
            return ResultadoOperacao<AtivoResponse>.Falha(
                $"Ticker '{request.Ticker}' não segue o formato B3 esperado (4 caracteres iniciando por letra + 1 ou 2 dígitos, ex: PETR4, HGLG11, B5P211).",
                TipoErroOperacao.Validacao);

        var erroCampos = ValidarNomeETipo(request.Nome, request.Tipo) ?? ValidarCnpj(request.Cnpj);
        if (erroCampos is not null)
            return ResultadoOperacao<AtivoResponse>.Falha(erroCampos, TipoErroOperacao.Validacao);

        var jaExiste = await db.Ativos.AnyAsync(a => a.Ticker == ticker);
        if (jaExiste)
            return ResultadoOperacao<AtivoResponse>.Falha(
                $"Já existe um ativo cadastrado com o ticker '{ticker}'.", TipoErroOperacao.Conflito);

        var ativo = new Ativo
        {
            Ticker = ticker,
            Nome = request.Nome.Trim(),
            Tipo = request.Tipo,
            Categoria = request.Categoria ?? CategoriasAlocacao.Padrao(request.Tipo),
            Cnpj = Cnpj.Normalizar(request.Cnpj),
            EstaAtivo = true,
            DataCadastro = DateTime.UtcNow
        };

        return await SalvarNovoAsync(ativo);
    }

    // Título do Tesouro: não tem ticker da B3. Ticker e nome vêm do catálogo do arquivo do Tesouro
    // (ex.: "IPCA+ 2045" / "Tesouro IPCA+ 2045"), o que também garante que existe preço para ele.
    private async Task<ResultadoOperacao<AtivoResponse>> CriarTesouroAsync(CriarAtivoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TituloTesouro) || request.VencimentoTesouro is null)
            return ResultadoOperacao<AtivoResponse>.Falha("Selecione o título do Tesouro.", TipoErroOperacao.Validacao);

        var titulos = await tesouroDireto.ListarTitulosAsync();
        if (titulos.Count == 0)
            return ResultadoOperacao<AtivoResponse>.Falha(
                "A lista de títulos do Tesouro está indisponível agora (o arquivo de preços não pôde ser baixado). Tente mais tarde.",
                TipoErroOperacao.Validacao);

        var titulo = titulos.FirstOrDefault(t =>
            t.Vencimento == request.VencimentoTesouro && TitulosTesouro.MesmoTipo(t.TipoTitulo, request.TituloTesouro));
        if (titulo is null)
            return ResultadoOperacao<AtivoResponse>.Falha(
                $"Título '{request.TituloTesouro}' com vencimento {request.VencimentoTesouro:dd/MM/yyyy} não está entre os títulos em oferta no Tesouro Direto.",
                TipoErroOperacao.Validacao);

        if (await db.Ativos.AnyAsync(a => a.Ticker == titulo.Ticker))
            return ResultadoOperacao<AtivoResponse>.Falha(
                $"Já existe um ativo cadastrado para o {titulo.Nome}.", TipoErroOperacao.Conflito);

        if (request.CorretoraId is int escolhida && !await db.Corretoras.AnyAsync(c => c.Id == escolhida))
            return ResultadoOperacao<AtivoResponse>.Falha("Corretora não encontrada.", TipoErroOperacao.Validacao);
        // Sem escolha, vai na principal (nula se ainda não há corretora: o IR aponta a pendência)
        var corretoraId = request.CorretoraId
            ?? await db.Corretoras.Where(c => c.Principal).Select(c => (int?)c.Id).FirstOrDefaultAsync();

        return await SalvarNovoAsync(new Ativo
        {
            Ticker = titulo.Ticker,
            Nome = titulo.Nome,
            Tipo = TipoAtivo.Tesouro,
            Categoria = CategoriaAlocacao.RendaFixa,
            TituloTesouro = titulo.TipoTitulo,
            VencimentoTesouro = titulo.Vencimento,
            CorretoraId = corretoraId,
            EstaAtivo = true,
            DataCadastro = DateTime.UtcNow
        });
    }

    private async Task<ResultadoOperacao<AtivoResponse>> SalvarNovoAsync(Ativo ativo)
    {
        db.Ativos.Add(ativo);
        await db.SaveChangesAsync();

        // Fire-and-forget: a criação do Ativo não aguarda a carga histórica terminar (spec, seção 4.1/4.3).
        jobScheduler.EnfileirarCargaHistoricaInicial(ativo.Id);

        logger.LogInformation("Ativo cadastrado: {Ticker} (Id {AtivoId}); carga histórica inicial enfileirada.",
            ativo.Ticker, ativo.Id);

        return ResultadoOperacao<AtivoResponse>.Ok(ParaResponse(ativo));
    }

    public async Task<ResultadoOperacao<AtivoResponse>> ObterPorIdAsync(int id)
    {
        var ativo = await db.Ativos.FindAsync(id);
        return ativo is null
            ? ResultadoOperacao<AtivoResponse>.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado)
            : ResultadoOperacao<AtivoResponse>.Ok(ParaResponse(ativo));
    }

    public async Task<List<AtivoResponse>> ListarAsync(string? busca = null, bool incluirInativos = true)
    {
        var query = db.Ativos.AsNoTracking().AsQueryable();

        if (!incluirInativos)
            query = query.Where(a => a.EstaAtivo);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            // No provider SQLite do EF Core, string.Contains() é traduzido para instr() (case-sensitive).
            // EF.Functions.Like usa o LIKE nativo do SQLite, que é case-insensitive para ASCII por padrão.
            var termo = $"%{busca.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Ticker, termo) || EF.Functions.Like(a.Nome, termo));
        }

        return await query
            .OrderBy(a => a.Ticker)
            .Select(a => new AtivoResponse(a.Id, a.Ticker, a.Nome, a.Tipo, a.EstaAtivo, a.DataCadastro, a.Categoria, a.Cnpj, a.CorretoraId))
            .ToListAsync();
    }

    public async Task<ResultadoOperacao<AtivoResponse>> EditarAsync(int id, EditarAtivoRequest request)
    {
        var ativo = await db.Ativos.FindAsync(id);
        if (ativo is null)
            return ResultadoOperacao<AtivoResponse>.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        var erroCampos = ValidarNomeETipo(request.Nome, request.Tipo) ?? ValidarCnpj(request.Cnpj);
        if (erroCampos is not null)
            return ResultadoOperacao<AtivoResponse>.Falha(erroCampos, TipoErroOperacao.Validacao);

        // Título do Tesouro e ativo da B3 têm origem de cotação diferente: trocar entre eles quebraria a sincronização
        if ((ativo.Tipo == TipoAtivo.Tesouro) != (request.Tipo == TipoAtivo.Tesouro))
            return ResultadoOperacao<AtivoResponse>.Falha(
                "Não é possível trocar um título do Tesouro para outro tipo (nem o contrário). Desative e cadastre de novo.",
                TipoErroOperacao.Validacao);

        if (ativo.Tipo == TipoAtivo.Tesouro && request.CorretoraId is int corretoraId)
        {
            if (!await db.Corretoras.AnyAsync(c => c.Id == corretoraId))
                return ResultadoOperacao<AtivoResponse>.Falha("Corretora não encontrada.", TipoErroOperacao.Validacao);
            ativo.CorretoraId = corretoraId;
        }

        // Ticker não é editável — o DTO de edição nem o expõe (decisão da Tarefa 2.3).
        ativo.Nome = request.Nome.Trim();
        ativo.Tipo = request.Tipo;
        // Nula = mantém a atual. Tesouro é sempre renda fixa.
        if (ativo.Tipo == TipoAtivo.Tesouro) ativo.Categoria = CategoriaAlocacao.RendaFixa;
        else if (request.Categoria is CategoriaAlocacao categoria) ativo.Categoria = categoria;
        if (request.Cnpj is not null) ativo.Cnpj = Cnpj.Normalizar(request.Cnpj);
        await db.SaveChangesAsync();

        return ResultadoOperacao<AtivoResponse>.Ok(ParaResponse(ativo));
    }

    public async Task<ResultadoOperacao> ExcluirAsync(int id)
    {
        var ativo = await db.Ativos.FindAsync(id);
        if (ativo is null)
            return ResultadoOperacao.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        var possuiMovimentacoes = await db.Movimentacoes.AnyAsync(m => m.AtivoId == id);
        if (possuiMovimentacoes)
            return ResultadoOperacao.Falha(
                "Ativo possui movimentações associadas; não pode ser excluído. Desative-o em vez disso.",
                TipoErroOperacao.Conflito);

        ativo.EstaAtivo = false;
        await db.SaveChangesAsync();

        logger.LogInformation("Ativo desativado (soft delete): {Ticker} (Id {AtivoId}).", ativo.Ticker, ativo.Id);

        return ResultadoOperacao.Ok();
    }

    public async Task<ResultadoOperacao> ReativarAsync(int id)
    {
        var ativo = await db.Ativos.FindAsync(id);
        if (ativo is null)
            return ResultadoOperacao.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        if (!ativo.EstaAtivo)
        {
            ativo.EstaAtivo = true;
            await db.SaveChangesAsync();
            logger.LogInformation("Ativo reativado: {Ticker} (Id {AtivoId}).", ativo.Ticker, ativo.Id);
        }

        return ResultadoOperacao.Ok();
    }

    // Folga para fim de semana e feriados prolongados antes de considerar as cotações de um ativo paradas
    private const int DiasParaDesatualizar = 5;

    public async Task<List<AtivoRastreadoResponse>> ObterRastreadosAsync()
    {
        var ativos = await db.Ativos.AsNoTracking().OrderBy(a => a.Ticker).ToListAsync();

        var cotacoes = await db.CotacoesHistoricas.AsNoTracking()
            .GroupBy(c => c.AtivoId)
            .Select(g => new { AtivoId = g.Key, Primeira = g.Min(c => c.Data), Ultima = g.Max(c => c.Data) })
            .ToDictionaryAsync(x => x.AtivoId);

        var proventos = await db.Proventos.AsNoTracking()
            .Where(p => !p.Oculto)
            .GroupBy(p => p.AtivoId)
            .Select(g => new { AtivoId = g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.AtivoId, x => x.Quantidade);

        // Decimal no SQLite é texto: a soma por ativo é feita em memória
        var quantidades = (await db.Movimentacoes.AsNoTracking()
                .Select(m => new { m.AtivoId, m.Tipo, m.Quantidade })
                .ToListAsync())
            .GroupBy(m => m.AtivoId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Tipo == TipoMovimentacao.Compra ? m.Quantidade : -m.Quantidade));

        // Referência: o pregão mais recente entre todos os ativos (não "hoje", para um dia sem sincronizar
        // não marcar tudo como desatualizado)
        var referencia = cotacoes.Count > 0 ? cotacoes.Values.Max(c => c.Ultima) : (DateOnly?)null;

        return ativos.Select(a =>
        {
            var c = cotacoes.GetValueOrDefault(a.Id);
            var temTransacao = quantidades.TryGetValue(a.Id, out var quantidade);
            var situacao = !temTransacao ? SituacaoNaCarteira.SemTransacoes
                : quantidade > 0.000001m ? SituacaoNaCarteira.Aberta
                : SituacaoNaCarteira.Encerrada;
            var saude = c is null ? SaudeCotacoes.SemCotacao
                : referencia is DateOnly r && c.Ultima < r.AddDays(-DiasParaDesatualizar) ? SaudeCotacoes.Desatualizada
                : SaudeCotacoes.EmDia;
            return new AtivoRastreadoResponse(
                a.Id, a.Ticker, a.Nome, a.Tipo, a.EstaAtivo, situacao, temTransacao ? Math.Max(quantidade, 0) : 0,
                c?.Primeira, c?.Ultima, proventos.GetValueOrDefault(a.Id), saude, a.Categoria, a.Cnpj, a.CorretoraId);
        }).ToList();
    }

    public async Task<ResultadoOperacao> DefinirRastreamentoAsync(int id, bool rastrear)
    {
        var ativo = await db.Ativos.FindAsync(id);
        if (ativo is null) return ResultadoOperacao.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        ativo.EstaAtivo = rastrear;
        await db.SaveChangesAsync();
        logger.LogInformation("Rastreamento de {Ticker} {Estado}.", ativo.Ticker, rastrear ? "retomado" : "pausado");
        return ResultadoOperacao.Ok();
    }

    public async Task<CotacaoResponse?> ObterCotacaoAteAsync(int ativoId, DateOnly data) =>
        await db.CotacoesHistoricas.AsNoTracking()
            .Where(c => c.AtivoId == ativoId && c.Data <= data)
            .OrderByDescending(c => c.Data)
            .Select(ParaCotacao)
            .FirstOrDefaultAsync();

    public async Task<ResultadoOperacao<List<CotacaoResponse>>> ObterCotacoesAsync(int ativoId, string? periodo)
    {
        var ativoExiste = await db.Ativos.AnyAsync(a => a.Id == ativoId);
        if (!ativoExiste)
            return ResultadoOperacao<List<CotacaoResponse>>.Falha("Ativo não encontrado.", TipoErroOperacao.NaoEncontrado);

        var query = db.CotacoesHistoricas.AsNoTracking().Where(c => c.AtivoId == ativoId);

        var ultimaData = await query.OrderByDescending(c => c.Data).Select(c => (DateOnly?)c.Data).FirstOrDefaultAsync();
        if (ultimaData is null)
            return ResultadoOperacao<List<CotacaoResponse>>.Ok([]);

        // "Início" e período inválido/ausente trazem o histórico inteiro (sem filtro de data) — Tarefa 6.4
        DateOnly? dataCorte = periodo?.Trim().ToUpperInvariant() switch
        {
            "1M" => ultimaData.Value.AddDays(-30),
            "6M" => ultimaData.Value.AddMonths(-6),
            "1A" => ultimaData.Value.AddYears(-1),
            "YTD" => new DateOnly(ultimaData.Value.Year, 1, 1),
            "24M" => ultimaData.Value.AddMonths(-24),
            _ => null
        };

        if (dataCorte.HasValue)
            query = query.Where(c => c.Data >= dataCorte.Value);

        var cotacoes = await query
            .OrderBy(c => c.Data)
            .Select(ParaCotacao)
            .ToListAsync();

        return ResultadoOperacao<List<CotacaoResponse>>.Ok(cotacoes);
    }

    private static string? ValidarNomeETipo(string? nome, Domain.Enums.TipoAtivo tipo)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return "Informe o nome do ativo.";
        if (!Enum.IsDefined(tipo))
            return "Tipo de ativo inválido (use Ação, ETF, FII, BDR ou Tesouro).";
        return null;
    }

    // Vazio é aceito (apaga); preenchido tem de ter 14 dígitos e dígitos verificadores corretos
    private static string? ValidarCnpj(string? cnpj) =>
        Cnpj.Normalizar(cnpj) is string digitos && !Cnpj.Valido(digitos)
            ? $"CNPJ {cnpj!.Trim()} inválido: confira os 14 dígitos (os verificadores não conferem)."
            : null;

    // Expressão (não método): o EF traduz para SQL e busca só as colunas usadas
    private static readonly Expression<Func<CotacaoHistorica, CotacaoResponse>> ParaCotacao =
        c => new CotacaoResponse(c.Data, c.Abertura, c.Maxima, c.Minima, c.Fechamento, c.Volume);

    private static AtivoResponse ParaResponse(Ativo ativo) =>
        new(ativo.Id, ativo.Ticker, ativo.Nome, ativo.Tipo, ativo.EstaAtivo, ativo.DataCadastro, ativo.Categoria, ativo.Cnpj, ativo.CorretoraId);
}

using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Dtos;

namespace Wal.Services;

public class CarteiraService(AppDbContext db, IPosicaoService posicaoService) : ICarteiraService
{
    public async Task<List<PosicaoAtivoResponse>> ObterPosicoesAsync()
    {
        var ativos = await db.Ativos.AsNoTracking().ToListAsync();
        var resultado = new List<PosicaoAtivoResponse>();

        foreach (var ativo in ativos)
        {
            var posicao = await posicaoService.CalcularPosicaoAsync(ativo.Id);
            if (posicao.Quantidade <= 0) continue;

            // Ativo recém-cadastrado: job de carga inicial pode não ter rodado ainda — cotação fica nula
            // em vez de omitir o ativo da lista (decisão da Tarefa 6.1).
            var ultimasCotacoes = await db.CotacoesHistoricas.AsNoTracking()
                .Where(c => c.AtivoId == ativo.Id)
                .OrderByDescending(c => c.Data)
                .Take(2)
                .ToListAsync();

            decimal? cotacaoAtual = ultimasCotacoes.Count > 0 ? ultimasCotacoes[0].Fechamento : null;

            decimal? variacaoDia = ultimasCotacoes.Count == 2 && ultimasCotacoes[1].Fechamento != 0
                ? (ultimasCotacoes[0].Fechamento - ultimasCotacoes[1].Fechamento) / ultimasCotacoes[1].Fechamento
                : null;

            decimal? valorPosicao = cotacaoAtual.HasValue ? posicao.Quantidade * cotacaoAtual.Value : null;

            decimal? rentabilidade = cotacaoAtual.HasValue && posicao.PrecoMedio != 0
                ? (cotacaoAtual.Value - posicao.PrecoMedio) / posicao.PrecoMedio
                : null;

            resultado.Add(new PosicaoAtivoResponse(
                ativo.Id, ativo.Ticker, ativo.Nome, posicao.Quantidade, posicao.PrecoMedio,
                cotacaoAtual, variacaoDia, valorPosicao, rentabilidade, ativo.Tipo, ativo.Categoria));
        }

        return resultado;
    }

    public async Task<ResumoCarteiraResponse> ObterResumoAsync() => await MontarResumoAsync(await ObterPosicoesAsync());

    public async Task<CabecalhoCarteiraResponse> ObterCabecalhoAsync()
    {
        var posicoes = await ObterPosicoesAsync();
        var resumo = await MontarResumoAsync(posicoes);

        // Valor de cada posição no pregão anterior, deduzido da variação do dia (só ativos com duas cotações)
        var comVariacao = posicoes
            .Where(p => p.ValorPosicao.HasValue && p.VariacaoDiaPercentual is > -1)
            .Select(p => (Hoje: p.ValorPosicao!.Value, Ontem: p.ValorPosicao!.Value / (1 + p.VariacaoDiaPercentual!.Value)))
            .ToList();
        decimal? variacaoReais = comVariacao.Count > 0 ? comVariacao.Sum(v => v.Hoje - v.Ontem) : null;
        var baseOntem = comVariacao.Sum(v => v.Ontem);
        decimal? variacaoPercentual = variacaoReais.HasValue && baseOntem != 0 ? variacaoReais / baseOntem : null;

        var primeiraMovimentacao = await db.Movimentacoes.AsNoTracking().MinAsync(m => (DateOnly?)m.Data);
        var ultimaCotacao = await db.CotacoesHistoricas.AsNoTracking().MaxAsync(c => (DateOnly?)c.Data);

        return new CabecalhoCarteiraResponse(
            resumo.ValorAtual, resumo.RentabilidadeReais, resumo.RentabilidadePercentual,
            variacaoReais, variacaoPercentual, primeiraMovimentacao, ultimaCotacao);
    }

    private async Task<ResumoCarteiraResponse> MontarResumoAsync(List<PosicaoAtivoResponse> posicoes)
    {
        var valorInvestido = posicoes.Sum(p => p.Quantidade * p.PrecoMedio);
        var valorAtual = posicoes.Sum(p => p.ValorPosicao ?? 0);

        // Resultado desde o início, em dinheiro: o que a carteira vale hoje mais o que já saiu dela
        // (vendas e proventos), menos tudo o que entrou (compras). Inclui ganhos já realizados e ativos
        // zerados, que a diferença "valor atual - custo das posições abertas" deixaria de fora.
        var movimentacoes = await db.Movimentacoes.AsNoTracking().ToListAsync();
        var bonificacoes = await BonificacoesAsync(movimentacoes);
        var compras = movimentacoes
            .Where(m => m.Tipo == TipoMovimentacao.Compra && !bonificacoes.Contains(m.Id))
            .Sum(m => m.Quantidade * m.PrecoUnitario + m.Taxas);
        var vendas = movimentacoes
            .Where(m => m.Tipo == TipoMovimentacao.Venda)
            .Sum(m => m.Quantidade * m.PrecoUnitario - m.Taxas);

        var (pontos, proventosRecebidos) = await CalcularSerieAsync();
        var resultadoReais = valorAtual + vendas + proventosRecebidos - compras;
        // Percentual = a mesma rentabilidade do gráfico (método de cotas), que não é distorcida pelo
        // tamanho e pelas datas dos aportes
        decimal? rentabilidadePercentual = pontos.Count > 0 ? pontos[^1].RentabilidadeAcumulada : null;

        return new ResumoCarteiraResponse(valorInvestido, valorAtual, resultadoReais, rentabilidadePercentual);
    }

    public async Task<List<PontoRentabilidadeResponse>> ObterRentabilidadeHistoricaAsync() =>
        (await CalcularSerieAsync()).Pontos;

    // Bonificação lançada como compra não é dinheiro novo: o custo dela serve ao preço médio do IR
    private async Task<HashSet<int>> BonificacoesAsync(List<Movimentacao> movimentacoes)
    {
        var ativoIds = movimentacoes.Select(m => m.AtivoId).Distinct().ToList();
        var eventos = await db.EventosCorporativos.AsNoTracking().Where(e => ativoIds.Contains(e.AtivoId)).ToListAsync();
        return ConversaoBaseYahoo.Criar(movimentacoes, eventos).Bonificacoes;
    }

    // Série diária (patrimônio e rentabilidade acumulada) e o total de proventos com data ex até o fim dela
    private async Task<(List<PontoRentabilidadeResponse> Pontos, decimal ProventosRecebidos)> CalcularSerieAsync()
    {
        var motor = await MotorCarteira.CalcularAsync(db);
        var pontos = motor.Pontos
            .Select(p => new PontoRentabilidadeResponse(p.Data, p.Valor, p.RentabilidadeAcumulada))
            .ToList();
        return (pontos, motor.TotalProventos);
    }
}

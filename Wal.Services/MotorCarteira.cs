using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;

namespace Wal.Services;

/// <summary>
/// Reconstrói a carteira dia a dia, desde a primeira movimentação até a cotação mais recente, e guarda o
/// que as telas precisam: patrimônio, rentabilidade (método de cotas) e fluxos de cada dia, totais por
/// ativo e cada provento recebido. Tudo o que é derivado da série (períodos, drawdown, resultado mensal,
/// contribuição) sai daqui, para as telas nunca discordarem entre si.
/// </summary>
internal sealed class MotorCarteira
{
    /// <summary>Um dia da carteira. Compras e vendas são o dinheiro que entrou e saiu; proventos, o que foi pago na data ex.</summary>
    public record Ponto(DateOnly Data, decimal Valor, decimal RentabilidadeAcumulada, decimal Compras, decimal Vendas, decimal Proventos);

    /// <summary>
    /// Provento que a carteira recebeu: quantidade com direito na data ex × valor por ação. Tipo e data de
    /// pagamento já vêm com a correção do usuário, quando houver.
    /// </summary>
    public record ProventoRecebido(
        int ProventoId, int AtivoId, TipoProvento Tipo, OrigemDado Origem, DateOnly? DataCom, DateOnly DataEx, DateOnly DataPagamento,
        decimal Quantidade, decimal ValorPorAcao, decimal Valor, bool Corrigido, bool DataPagamentoEstimada);

    /// <summary>Totais de um ativo desde o início (compras sem as bonificações).</summary>
    public sealed class TotaisAtivo
    {
        public decimal Compras { get; internal set; }
        public decimal Vendas { get; internal set; }
        public decimal Proventos { get; internal set; }
        /// <summary>Quantidade na base de preço do Yahoo (ver <see cref="ConversaoBaseYahoo"/>).</summary>
        public decimal Quantidade { get; internal set; }
        public decimal Valor { get; internal set; }
        public DateOnly? PrimeiraCompra { get; internal set; }
        public DateOnly? UltimaVenda { get; internal set; }

        /// <summary>Valor atual + vendas + proventos − compras.</summary>
        public decimal Resultado => Valor + Vendas + Proventos - Compras;
    }

    /// <summary>Valor do ativo num dia e os fluxos acumulados até ele — a diferença entre duas fotos dá o resultado no intervalo.</summary>
    public record FotoAtivo(decimal Valor, decimal Compras, decimal Vendas, decimal Proventos)
    {
        public static readonly FotoAtivo Vazia = new(0, 0, 0, 0);
    }

    public List<Ponto> Pontos { get; } = [];
    public Dictionary<int, TotaisAtivo> PorAtivo { get; } = [];
    public List<ProventoRecebido> ProventosRecebidos { get; } = [];

    /// <summary>Fotos por ativo nas datas pedidas em <see cref="CalcularAsync"/> (só as que caem dentro da série).</summary>
    public Dictionary<DateOnly, Dictionary<int, FotoAtivo>> Fotos { get; } = [];

    public decimal TotalProventos => ProventosRecebidos.Sum(p => p.Valor);

    /// <summary>Um dia de um ativo: valor da posição e quanto foi aplicado nele (compras − vendas, acumulado).</summary>
    public record PontoAtivo(DateOnly Data, decimal Valor, decimal Aplicado);

    /// <summary>Série diária do ativo pedido em <see cref="CalcularAsync"/>; vazia se nenhum foi pedido.</summary>
    public List<PontoAtivo> SerieAtivo { get; } = [];

    /// <summary>Movimentações de compra que são a bonificação em si (não são aporte; ver <see cref="ConversaoBaseYahoo"/>).</summary>
    public IReadOnlySet<int> Bonificacoes => Conversao.Bonificacoes;

    /// <summary>Como as quantidades registradas se relacionam com os eventos corporativos (ver <see cref="ConversaoBaseYahoo"/>).</summary>
    internal ConversaoBaseYahoo Conversao { get; private set; } = ConversaoBaseYahoo.Criar([], []);

    /// <param name="ativoSerie">Ativo cuja série diária (valor da posição × aplicado) também é guardada.</param>
    public static async Task<MotorCarteira> CalcularAsync(
        AppDbContext db, IReadOnlyCollection<DateOnly>? datasFoto = null, int? ativoSerie = null)
    {
        var motor = new MotorCarteira();
        var fotosPedidas = datasFoto is null ? [] : new HashSet<DateOnly>(datasFoto);

        var movimentacoes = await db.Movimentacoes.AsNoTracking()
            .OrderBy(m => m.Data).ThenBy(m => m.Id)
            .ToListAsync();

        if (movimentacoes.Count == 0)
            return motor;

        var ativoIds = movimentacoes.Select(m => m.AtivoId).Distinct().ToList();

        var cotacoesPorAtivo = (await db.CotacoesHistoricas.AsNoTracking()
                .Where(c => ativoIds.Contains(c.AtivoId))
                .OrderBy(c => c.Data)
                .ToListAsync())
            .GroupBy(c => c.AtivoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Proventos agrupados pela data ex (primeiro dia sem direito), quando entram na rentabilidade: os do
        // Yahoo e os lançados à mão (que servem justamente para o que o Yahoo não traz).
        // Ocultos foram descartados pelo usuário e não entram em nada
        var proventosPorDataEx = (await db.Proventos.AsNoTracking()
                .Where(p => ativoIds.Contains(p.AtivoId) && !p.Oculto)
                .ToListAsync())
            .ToLookup(DataEx);

        // Quantidades na base de preço do Yahoo (ver ConversaoBaseYahoo)
        var eventos = await db.EventosCorporativos.AsNoTracking()
            .Where(e => ativoIds.Contains(e.AtivoId))
            .ToListAsync();
        var conversao = ConversaoBaseYahoo.Criar(movimentacoes, eventos);
        motor.Conversao = conversao;
        var ajustesFracaoPorData = conversao.AjustesFracao.ToLookup(a => a.Data);

        var dataInicio = movimentacoes[0].Data;
        var dataFim = cotacoesPorAtivo.Count > 0
            ? cotacoesPorAtivo.Values.SelectMany(l => l).Max(c => c.Data)
            : dataInicio;
        if (dataFim < dataInicio) dataFim = dataInicio;

        TotaisAtivo Totais(int ativoId)
        {
            if (!motor.PorAtivo.TryGetValue(ativoId, out var totais))
                motor.PorAtivo[ativoId] = totais = new TotaisAtivo();
            return totais;
        }

        // Loop diário incremental: mantém quantidade acumulada por ativo e avança um ponteiro por ativo
        // na lista de cotações (já ordenada), em vez de recalcular tudo do zero a cada dia (Tarefa 6.3).
        // Quantidade na base do Yahoo: avalia a posição e recebe os proventos do Yahoo (mesma base)
        var quantidadePorAtivo = new Dictionary<int, decimal>();
        // Quantidade como foi registrada: recebe os proventos manuais e de outras fontes, que vêm com o
        // valor por ação da época
        var quantidadeRegistradaPorAtivo = new Dictionary<int, decimal>();
        var indiceCotacaoPorAtivo = new Dictionary<int, int>();
        var ultimaCotacaoPorAtivo = new Dictionary<int, decimal>();
        // Preço da última movimentação: vale como cotação enquanto o ativo não tem nenhuma cotação
        // persistida até a data (ativo deslistado ou carga histórica incompleta), em vez de valer zero.
        var ultimoPrecoMovimentacaoPorAtivo = new Dictionary<int, decimal>();

        // Rentabilidade total ponderada no tempo (método de cotas): o retorno de cada dia desconta o
        // dinheiro que entrou ou saiu, então aporte não aparece como ganho.
        //   retorno do dia = (valor no fim do dia + vendas + proventos do dia) / (valor do dia anterior + compras do dia) - 1
        // Compras contam no início do dia e vendas no fim, o que mantém o denominador positivo mesmo
        // quando a carteira é zerada. Provento entra na data ex — o dia em que a cotação cai pelo valor
        // pago —, pelo valor bruto (o JCP tem 15% de IR retido que não é descontado aqui).
        decimal valorAnterior = 0;
        decimal indiceCota = 1;
        var indiceMovimentacao = 0;

        for (var data = dataInicio; data <= dataFim; data = data.AddDays(1))
        {
            decimal comprasDia = 0, vendasDia = 0, proventosDia = 0;

            // Tem direito quem tinha o ativo no fim do dia anterior à data ex, por isso a quantidade é
            // lida antes das movimentações do dia: compra na data ex não recebe, venda na data ex recebe.
            foreach (var provento in proventosPorDataEx[data])
            {
                var quantidades = provento.Origem == OrigemDado.YahooFinance ? quantidadePorAtivo : quantidadeRegistradaPorAtivo;
                if (!quantidades.TryGetValue(provento.AtivoId, out var quantidadeComDireito) || quantidadeComDireito <= 0) continue;

                var valor = quantidadeComDireito * provento.ValorPorAcao;
                proventosDia += valor;
                Totais(provento.AtivoId).Proventos += valor;
                var corrigido = provento.TipoCorrigido.HasValue || provento.DataPagamentoCorrigida.HasValue;
                motor.ProventosRecebidos.Add(new ProventoRecebido(
                    provento.Id, provento.AtivoId, provento.TipoEfetivo, provento.Origem, provento.DataCom, data,
                    provento.DataPagamentoEfetiva, quantidadeComDireito, provento.ValorPorAcao, valor, corrigido,
                    // O Yahoo só informa a data ex; a de pagamento é aproximada até o usuário corrigir
                    provento.Origem == OrigemDado.YahooFinance && provento.DataPagamentoCorrigida is null));
            }

            while (indiceMovimentacao < movimentacoes.Count && movimentacoes[indiceMovimentacao].Data == data)
            {
                var m = movimentacoes[indiceMovimentacao++];
                var sinal = m.Tipo == TipoMovimentacao.Compra ? 1 : -1;
                quantidadeRegistradaPorAtivo[m.AtivoId] =
                    quantidadeRegistradaPorAtivo.GetValueOrDefault(m.AtivoId) + sinal * m.Quantidade;

                // A bonificação já está nas quantidades convertidas e não é aporte: o custo lançado
                // nela serve ao preço médio do IR, não à rentabilidade
                if (conversao.Bonificacoes.Contains(m.Id)) continue;

                var fator = conversao.Fator(m.AtivoId, m.Data);
                quantidadePorAtivo[m.AtivoId] = quantidadePorAtivo.GetValueOrDefault(m.AtivoId) + sinal * m.Quantidade * fator;
                var financeiro = m.Quantidade * m.PrecoUnitario;
                var totais = Totais(m.AtivoId);
                if (m.Tipo == TipoMovimentacao.Compra)
                {
                    comprasDia += financeiro + m.Taxas;
                    totais.Compras += financeiro + m.Taxas;
                    totais.PrimeiraCompra ??= m.Data;
                }
                else
                {
                    vendasDia += financeiro - m.Taxas;
                    totais.Vendas += financeiro - m.Taxas;
                    totais.UltimaVenda = m.Data;
                }
                ultimoPrecoMovimentacaoPorAtivo[m.AtivoId] = m.PrecoUnitario / fator;
            }

            foreach (var ativoId in quantidadePorAtivo.Keys)
            {
                if (!cotacoesPorAtivo.TryGetValue(ativoId, out var lista)) continue;

                var indice = indiceCotacaoPorAtivo.GetValueOrDefault(ativoId, 0);
                while (indice < lista.Count && lista[indice].Data <= data)
                {
                    ultimaCotacaoPorAtivo[ativoId] = lista[indice].Fechamento;
                    indice++;
                }
                indiceCotacaoPorAtivo[ativoId] = indice;
            }

            decimal PrecoDoDia(int ativoId) => ultimaCotacaoPorAtivo.TryGetValue(ativoId, out var cotacao)
                ? cotacao
                : ultimoPrecoMovimentacaoPorAtivo[ativoId];

            // Fração de bonificação vendida em leilão: sai da posição e volta como dinheiro
            foreach (var ajuste in ajustesFracaoPorData[data])
            {
                if (!quantidadePorAtivo.ContainsKey(ajuste.AtivoId)) continue;
                var quantidade = ajuste.Quantidade * conversao.Fator(ajuste.AtivoId, data);
                quantidadePorAtivo[ajuste.AtivoId] += quantidade;
                var financeiro = Math.Abs(quantidade) * PrecoDoDia(ajuste.AtivoId);
                if (quantidade < 0)
                {
                    vendasDia += financeiro;
                    Totais(ajuste.AtivoId).Vendas += financeiro;
                }
                else
                {
                    comprasDia += financeiro;
                    Totais(ajuste.AtivoId).Compras += financeiro;
                }
            }

            // Tolerância: frações residuais de arredondamento não contam como posição
            decimal ValorAtivo(int ativoId) => Math.Abs(quantidadePorAtivo[ativoId]) > 0.000001m
                ? quantidadePorAtivo[ativoId] * PrecoDoDia(ativoId)
                : 0;

            var valorDia = quantidadePorAtivo.Keys.Sum(ValorAtivo);

            var capitalInicial = valorAnterior + comprasDia;
            if (capitalInicial > 0)
                indiceCota *= (valorDia + vendasDia + proventosDia) / capitalInicial;
            valorAnterior = valorDia;

            motor.Pontos.Add(new Ponto(data, valorDia, indiceCota - 1, comprasDia, vendasDia, proventosDia));

            if (ativoSerie is int idSerie && motor.PorAtivo.TryGetValue(idSerie, out var totaisSerie))
                motor.SerieAtivo.Add(new PontoAtivo(
                    data,
                    quantidadePorAtivo.ContainsKey(idSerie) ? ValorAtivo(idSerie) : 0,
                    totaisSerie.Compras - totaisSerie.Vendas));

            // O último dia sempre ganha foto: vale como "hoje" para intervalos que terminam depois da série
            if (fotosPedidas.Contains(data) || data == dataFim)
                motor.Fotos[data] = motor.PorAtivo.ToDictionary(
                    kv => kv.Key,
                    kv => new FotoAtivo(
                        quantidadePorAtivo.ContainsKey(kv.Key) ? ValorAtivo(kv.Key) : 0,
                        kv.Value.Compras, kv.Value.Vendas, kv.Value.Proventos));

            if (data == dataFim)
            {
                foreach (var (ativoId, quantidade) in quantidadePorAtivo)
                {
                    var totais = Totais(ativoId);
                    totais.Quantidade = Math.Abs(quantidade) > 0.000001m ? quantidade : 0;
                    totais.Valor = ValorAtivo(ativoId);
                }
            }
        }

        return motor;
    }

    /// <summary>Foto do ativo numa data pedida; fora da série (antes da carteira existir) ou sem o ativo, vazia.</summary>
    public FotoAtivo FotoEm(DateOnly data, int ativoId) =>
        Fotos.TryGetValue(data, out var fotos) && fotos.TryGetValue(ativoId, out var foto) ? foto : FotoAtivo.Vazia;

    // O Yahoo grava a própria data ex em DataCom; o lançamento manual grava a data com (último dia com
    // direito), e a data ex é o dia seguinte. Sem data com, a data de pagamento é a melhor aproximação.
    private static DateOnly DataEx(Provento provento) => provento.Origem == OrigemDado.YahooFinance
        ? provento.DataCom ?? provento.DataPagamento
        : provento.DataCom?.AddDays(1) ?? provento.DataPagamento;
}

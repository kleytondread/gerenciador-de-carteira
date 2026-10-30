using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Dtos;
using Wal.Services.Irpf;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class IrpfServiceTests : SqliteInMemoryTestBase
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private readonly IrpfService sut;
    private readonly Ativo itsa;
    private readonly Ativo semCnpj;

    private static DateOnly D(int ano, int mes, int dia) => new(ano, mes, dia);

    public IrpfServiceTests()
    {
        sut = new IrpfService(Db, new RelogioFixo(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero)));

        itsa = Fixtures.NovoAtivo("ITSA4", "Itaúsa PN");
        itsa.Cnpj = "61532644000115";
        semCnpj = Fixtures.NovoAtivo("WEGE3", "WEG ON");
        Db.Ativos.AddRange(itsa, semCnpj);
        Db.SaveChanges();

        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(itsa.Id, TipoMovimentacao.Compra, D(2024, 5, 2), 100, 10, taxas: 1),
            // Bonificação de 10% (compra pelo custo atribuído, como o usuário lança)
            Fixtures.NovaMovimentacao(itsa.Id, TipoMovimentacao.Compra, D(2025, 5, 9), 10, 2.85m),
            Fixtures.NovaMovimentacao(itsa.Id, TipoMovimentacao.Venda, D(2025, 8, 1), 50, 12, taxas: 1),
            Fixtures.NovaMovimentacao(semCnpj.Id, TipoMovimentacao.Compra, D(2025, 2, 3), 10, 40));
        Db.EventosCorporativos.Add(Fixtures.NovoEvento(itsa.Id, D(2025, 5, 7), 11, 10));
        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(itsa.Id, D(2024, 5, 2), 10),
            Fixtures.NovaCotacao(itsa.Id, D(2026, 6, 30), 12),
            Fixtures.NovaCotacao(semCnpj.Id, D(2026, 6, 30), 40));

        // JCP creditado em 2025 e pago em 2026 (já importado da B3)
        Db.Proventos.Add(new Provento
        {
            AtivoId = itsa.Id, TipoProvento = TipoProvento.JCP, Origem = OrigemDado.ExtratoB3, DataCom = D(2025, 12, 9),
            DataPagamento = D(2026, 3, 6), ValorPorAcao = 0.10m,
            Pagamentos = [new PagamentoProvento { Data = D(2026, 3, 6), Quantidade = 60, ValorBruto = 6.00m, ValorLiquido = 5.10m }]
        });
        // Dividendo pago em 2025
        Db.Proventos.Add(new Provento
        {
            AtivoId = itsa.Id, TipoProvento = TipoProvento.Dividendo, Origem = OrigemDado.ExtratoB3, DataCom = D(2025, 6, 2),
            DataPagamento = D(2025, 7, 1), ValorPorAcao = 0.05m,
            Pagamentos = [new PagamentoProvento { Data = D(2025, 7, 1), Quantidade = 110, ValorBruto = 5.50m, ValorLiquido = 5.50m }]
        });
        // Dividendo declarado em dezembro e pago em janeiro: é de 2026
        Db.Proventos.Add(new Provento
        {
            AtivoId = itsa.Id, TipoProvento = TipoProvento.Dividendo, Origem = OrigemDado.ExtratoB3, DataCom = D(2025, 12, 10),
            DataPagamento = D(2026, 1, 7), ValorPorAcao = 0.20m,
            Pagamentos = [new PagamentoProvento { Data = D(2026, 1, 7), Quantidade = 60, ValorBruto = 12.00m, ValorLiquido = 12.00m }]
        });
        // Ainda do Yahoo: pendência
        Db.Proventos.Add(Fixtures.NovoProvento(semCnpj.Id, D(2025, 11, 10), 0.5m));
        Db.SaveChanges();
    }

    private async Task<RelatorioIrpfResponse> Relatorio(int ano) => (await sut.ObterAsync(ano))!;

    [Fact]
    public async Task SemAnoPedido_UsaOUltimoAnoFechado()
    {
        var r = (await sut.ObterAsync())!;

        Assert.Equal(2025, r.Ano);
        Assert.False(r.AnoEmAndamento);
        Assert.True(r.CodigosConferidos);
        Assert.Equal([2026, 2025, 2024], r.AnosDisponiveis);
    }

    [Fact]
    public async Task BensDireitos_CustoComTaxasEBonificacaoNaDiscriminacao()
    {
        var r = await Relatorio(2025);

        var bem = Assert.Single(r.BensDireitos, b => b.Ticker == "ITSA4" && b.Codigo.Grupo == "03");
        Assert.Equal("01", bem.Codigo.Codigo);
        Assert.Equal("61.532.644/0001-15", bem.Cnpj);
        Assert.Equal(1001.00m, bem.SituacaoAnterior); // 100 × 10 + R$ 1 de taxa
        // (1.001 + 28,50) / 110 = 9,3591 por ação; 60 ações
        Assert.Equal(561.55m, bem.SituacaoAtual);
        Assert.Equal(60, bem.QuantidadeAtual);
        Assert.StartsWith("60 ACOES DA EMPRESA ITAUSA PN. CODIGO DE NEGOCIACAO: ITSA4.", bem.Discriminacao);
        Assert.Contains("BONIFICACAO EM 09/05/2025: 10 ACOES", bem.Discriminacao);
    }

    [Fact]
    public async Task Jcp_VaiNoAnoDoCreditoPeloLiquidoEO_NaoPagoEm99_07()
    {
        var r2025 = await Relatorio(2025);
        var jcp = Assert.Single(r2025.Exclusivos, x => x.Codigo == TabelaIrpf.Jcp);
        Assert.Equal(5.10m, jcp.Valor);
        Assert.False(jcp.Calculado);
        var aReceber = Assert.Single(r2025.BensDireitos, b => b.Codigo == TabelaIrpf.JcpAReceber);
        Assert.Equal(5.10m, aReceber.SituacaoAtual);

        var r2026 = await Relatorio(2026);
        Assert.DoesNotContain(r2026.Exclusivos, x => x.Codigo == TabelaIrpf.Jcp);
        var quitado = Assert.Single(r2026.BensDireitos, b => b.Codigo == TabelaIrpf.JcpAReceber);
        Assert.Equal(5.10m, quitado.SituacaoAnterior);
        Assert.Equal(0, quitado.SituacaoAtual);
    }

    [Fact]
    public async Task Dividendos_PeloAnoDoPagamento()
    {
        var r2025 = await Relatorio(2025);
        Assert.Equal(5.50m, Assert.Single(r2025.Isentos, x => x.Codigo == TabelaIrpf.Dividendos && x.Ticker == "ITSA4").Valor);
        // O do Yahoo (WEGE3) entra, mas marcado como calculado
        Assert.True(Assert.Single(r2025.Isentos, x => x.Codigo == TabelaIrpf.Dividendos && x.Ticker == "WEGE3").Calculado);

        var r2026 = await Relatorio(2026);
        Assert.Equal(12.00m, Assert.Single(r2026.Isentos, x => x.Codigo == TabelaIrpf.Dividendos && x.Ticker == "ITSA4").Valor);
    }

    [Fact]
    public async Task BonificacaoNoCodigo18_EGanhoIsentoNoCodigo20()
    {
        var r = await Relatorio(2025);

        Assert.Equal(28.50m, Assert.Single(r.Isentos, x => x.Codigo == TabelaIrpf.Bonificacoes).Valor);
        // Venda de 50 a R$ 12 (− R$ 1) com custo de 9,3591: alienação de R$ 600, isenta
        Assert.Equal(131.05m, Assert.Single(r.Isentos, x => x.Codigo == TabelaIrpf.GanhoAcoesIsento).Valor);
        var agosto = r.RendaVariavel.Single(m => m.Mes == 8);
        Assert.True(agosto.AcoesIsentas);
        Assert.Equal(0, agosto.Comum.ImpostoDevido);
    }

    [Fact]
    public async Task Pendencias_ProventoDoYahooECnpjFaltando()
    {
        var r = await Relatorio(2025);

        Assert.Contains(r.Pendencias, p => p.Tipo == TipoPendenciaIrpf.ProventoNaoConferido && p.AtivoId == semCnpj.Id);
        Assert.Contains(r.Pendencias, p => p.Tipo == TipoPendenciaIrpf.CnpjAusente && p.AtivoId == semCnpj.Id);
        Assert.DoesNotContain(r.Pendencias, p => p.Tipo == TipoPendenciaIrpf.CnpjAusente && p.AtivoId == itsa.Id);
    }

    [Fact]
    public async Task Tesouro_UsaACorretoraDoTituloESemCorretoraViraPendencia()
    {
        var itau = new Corretora { Nome = "Itaú Corretora", Cnpj = "61532644000115" };
        Db.Corretoras.AddRange(new Corretora { Nome = "XP Investimentos", Cnpj = "02332886000104", Principal = true }, itau);
        var naItau = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", TipoAtivo.Tesouro);
        var semCorretora = Fixtures.NovoAtivo("SELIC 2029", "Tesouro Selic 2029", TipoAtivo.Tesouro);
        Db.Ativos.AddRange(naItau, semCorretora);
        Db.SaveChanges();
        naItau.CorretoraId = itau.Id;
        Db.Movimentacoes.AddRange(
            Fixtures.NovaMovimentacao(naItau.Id, TipoMovimentacao.Compra, D(2025, 3, 10), 1, 1500),
            Fixtures.NovaMovimentacao(semCorretora.Id, TipoMovimentacao.Compra, D(2025, 3, 10), 1, 15000));
        Db.SaveChanges();

        var r = await Relatorio(2025);

        var bem = Assert.Single(r.BensDireitos, b => b.Ticker == "IPCA+ 2045");
        Assert.Equal("61.532.644/0001-15", bem.Cnpj);
        Assert.Contains("CUSTODIADOS NA ITAU CORRETORA", bem.Discriminacao);
        var semFonte = Assert.Single(r.BensDireitos, b => b.Ticker == "SELIC 2029");
        Assert.Null(semFonte.Cnpj);
        Assert.DoesNotContain("CUSTODIADOS", semFonte.Discriminacao);
        var pendencia = Assert.Single(r.Pendencias, p => p.Tipo == TipoPendenciaIrpf.CorretoraAusente);
        Assert.Equal(semCorretora.Id, pendencia.AtivoId);
        Assert.Equal(GravidadePendencia.Bloqueia, pendencia.Gravidade);
        // Principal primeiro, para o texto da Tributação Exclusiva
        Assert.Equal(["XP Investimentos", "Itaú Corretora"], r.Corretoras.Select(c => c.Nome));
        Assert.Equal("02.332.886/0001-04", r.Corretoras[0].Cnpj);
    }

    [Fact]
    public async Task Darf_RegistraEExclui()
    {
        var registrado = await sut.RegistrarDarfAsync(new RegistrarDarfRequest(2025, 8, FichaRendaVariavel.OperacoesComuns, 12.345m, D(2025, 9, 30)));
        Assert.True(registrado.Sucesso);
        Assert.Equal(12.35m, registrado.Valor!.ValorPrincipal);
        Assert.Single((await Relatorio(2025)).Darfs);

        Assert.True((await sut.ExcluirDarfAsync(registrado.Valor.Id)).Sucesso);
        Assert.Empty((await Relatorio(2025)).Darfs);
    }

    [Fact]
    public async Task Darf_PagamentoAntesDaApuracao_Recusa()
    {
        var r = await sut.RegistrarDarfAsync(new RegistrarDarfRequest(2025, 8, FichaRendaVariavel.OperacoesComuns, 10, D(2025, 7, 30)));

        Assert.False(r.Sucesso);
    }
}

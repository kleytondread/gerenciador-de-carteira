using Microsoft.EntityFrameworkCore;
using Wal.Domain.Enums;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Data;

/// <summary>
/// Cobre os índices únicos configurados na Tarefa 1.2 — são regras de integridade de dado,
/// não apenas detalhe de schema: garantem que o upsert idempotente da Fase 4 (chave AtivoId+Data
/// para cotação, AtivoId+DataPagamento+Origem para provento) e o cadastro de Ativo (Ticker único)
/// realmente não permitem duplicidade no nível do banco.
/// </summary>
public class AppDbContextIndexTests : SqliteInMemoryTestBase
{
    [Fact]
    public async Task Ativo_TickerDuplicado_ViolaIndiceUnico()
    {
        Db.Ativos.Add(Fixtures.NovoAtivo("PETR4"));
        await Db.SaveChangesAsync();

        Db.Ativos.Add(Fixtures.NovoAtivo("PETR4"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task CotacaoHistorica_MesmoAtivoEMesmaData_ViolaIndiceUnico()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 10), 10));
        await Db.SaveChangesAsync();

        Db.CotacoesHistoricas.Add(Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 10), 99));

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task CotacaoHistorica_MesmoAtivoDataDiferente_NaoViolaIndice()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.CotacoesHistoricas.AddRange(
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 10), 10),
            Fixtures.NovaCotacao(ativo.Id, new DateOnly(2026, 1, 11), 11));

        await Db.SaveChangesAsync(); // não deve lançar

        Assert.Equal(2, await Db.CotacoesHistoricas.CountAsync());
    }

    [Fact]
    public async Task Provento_MesmoAtivoDataPagamentoEOrigem_ViolaIndiceUnico()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 10), 1.5m, origem: OrigemDado.YahooFinance));
        await Db.SaveChangesAsync();

        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 10), 9.9m, origem: OrigemDado.YahooFinance));

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Provento_MesmoAtivoEDataPagamentoComOrigensDiferentes_Coexistem()
    {
        // Regra explícita da spec (seção 5): o índice único inclui Origem justamente para permitir
        // que duas fontes diferentes tenham, cada uma, seu próprio registro para a mesma data.
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Proventos.AddRange(
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 10), 1.5m, origem: OrigemDado.YahooFinance),
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2026, 1, 10), 1.6m, origem: OrigemDado.Manual));

        await Db.SaveChangesAsync(); // não deve lançar

        Assert.Equal(2, await Db.Proventos.CountAsync());
    }

    [Fact]
    public async Task Provento_DividendoEJcpPagosNoMesmoDia_Coexistem()
    {
        var ativo = Fixtures.NovoAtivo();
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();

        Db.Proventos.AddRange(
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2024, 6, 28), 0.05m, tipo: TipoProvento.Dividendo, origem: OrigemDado.Manual),
            Fixtures.NovoProvento(ativo.Id, new DateOnly(2024, 6, 28), 0.17m, tipo: TipoProvento.JCP, origem: OrigemDado.Manual));

        await Db.SaveChangesAsync(); // não deve lançar

        Assert.Equal(2, await Db.Proventos.CountAsync());
    }
}

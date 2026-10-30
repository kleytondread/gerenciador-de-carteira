using Microsoft.EntityFrameworkCore;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class CorretoraServiceTests : SqliteInMemoryTestBase
{
    private const string CnpjXp = "02.332.886/0001-04";
    private const string CnpjItau = "61.532.644/0001-15";
    private const string CnpjBtg = "60.746.948/0001-12";

    private readonly CorretoraService sut;

    public CorretoraServiceTests() => sut = new CorretoraService(Db);

    private async Task<int> Criar(string nome, string cnpj, bool principal = false) =>
        (await sut.CriarAsync(new SalvarCorretoraRequest(nome, cnpj, principal))).Valor!.Id;

    private async Task<string> Principal() => (await Db.Corretoras.AsNoTracking().SingleAsync(c => c.Principal)).Nome;

    [Fact]
    public async Task Criar_APrimeiraEhPrincipalEAsOutrasNao()
    {
        await Criar("XP", CnpjXp);
        await Criar("Itaú", CnpjItau);

        Assert.Equal("XP", await Principal());
        var lista = await sut.ListarAsync();
        Assert.Equal(["XP", "Itaú"], lista.Select(c => c.Nome));
        Assert.Equal(CnpjXp, lista[0].Cnpj);
    }

    [Fact]
    public async Task Criar_MarcadaComoPrincipal_DesmarcaAAnterior()
    {
        await Criar("XP", CnpjXp);
        await Criar("Itaú", CnpjItau, principal: true);

        Assert.Equal("Itaú", await Principal());
    }

    [Fact]
    public async Task Editar_DesmarcarAPrincipalNaoTiraAMarca()
    {
        var xp = await Criar("XP", CnpjXp);

        var resultado = await sut.EditarAsync(xp, new SalvarCorretoraRequest("XP Investimentos", CnpjXp, Principal: false));

        Assert.True(resultado.Sucesso);
        Assert.Equal("XP Investimentos", await Principal());
    }

    [Fact]
    public async Task DefinirPrincipal_LevaOsTitulosSemCorretoraENaoMexeNosOutros()
    {
        var xp = await Criar("XP", CnpjXp);
        var itau = await Criar("Itaú", CnpjItau);
        var semCorretora = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", TipoAtivo.Tesouro);
        var naXp = Fixtures.NovoAtivo("SELIC 2029", "Tesouro Selic 2029", TipoAtivo.Tesouro);
        naXp.CorretoraId = xp;
        var acao = Fixtures.NovoAtivo("ITSA4", "Itaúsa PN");
        Db.Ativos.AddRange(semCorretora, naXp, acao);
        await Db.SaveChangesAsync();

        Assert.True((await sut.DefinirPrincipalAsync(itau)).Sucesso);

        Db.ChangeTracker.Clear();
        Assert.Equal("Itaú", await Principal());
        Assert.Equal(itau, (await Db.Ativos.FindAsync(semCorretora.Id))!.CorretoraId);
        Assert.Equal(xp, (await Db.Ativos.FindAsync(naXp.Id))!.CorretoraId);
        Assert.Null((await Db.Ativos.FindAsync(acao.Id))!.CorretoraId);
    }

    [Fact]
    public async Task Criar_APrimeira_LevaOsTitulosJaCadastrados()
    {
        var titulo = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", TipoAtivo.Tesouro);
        Db.Ativos.Add(titulo);
        await Db.SaveChangesAsync();

        var xp = await Criar("XP", CnpjXp);

        Db.ChangeTracker.Clear();
        Assert.Equal(xp, (await Db.Ativos.FindAsync(titulo.Id))!.CorretoraId);
        Assert.Equal(1, (await sut.ListarAsync()).Single().Titulos);
    }

    [Fact]
    public async Task Excluir_ComTitulo_Recusa()
    {
        var xp = await Criar("XP", CnpjXp);
        var titulo = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", TipoAtivo.Tesouro);
        titulo.CorretoraId = xp;
        Db.Ativos.Add(titulo);
        await Db.SaveChangesAsync();

        var resultado = await sut.ExcluirAsync(xp);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
        Assert.Single(Db.Corretoras);
    }

    [Fact]
    public async Task Excluir_APrincipal_APrimeiraRestantePorNomeAssume()
    {
        var xp = await Criar("XP", CnpjXp);
        await Criar("Itaú", CnpjItau);
        await Criar("BTG", CnpjBtg);

        Assert.True((await sut.ExcluirAsync(xp)).Sucesso);

        Db.ChangeTracker.Clear();
        Assert.Equal("BTG", await Principal());
        Assert.Equal(2, await Db.Corretoras.CountAsync());
    }

    [Theory]
    [InlineData("", CnpjXp)]
    [InlineData("XP", "")]
    [InlineData("XP", "02.332.886/0001-05")]
    public async Task Criar_NomeOuCnpjInvalido_Recusa(string nome, string cnpj)
    {
        var resultado = await sut.CriarAsync(new SalvarCorretoraRequest(nome, cnpj));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Validacao, resultado.TipoErro);
        Assert.Empty(Db.Corretoras);
    }

    [Fact]
    public async Task Criar_CnpjRepetido_Recusa()
    {
        await Criar("XP", CnpjXp);

        var resultado = await sut.CriarAsync(new SalvarCorretoraRequest("XP de novo", "02332886000104"));

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoErroOperacao.Conflito, resultado.TipoErro);
    }
}

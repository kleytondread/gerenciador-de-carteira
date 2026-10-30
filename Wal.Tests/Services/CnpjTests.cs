using Wal.Services.Common;

namespace Wal.Tests.Services;

public class CnpjTests
{
    // Os CNPJs gravados pela migration AdicionarDadosIrpf (vindos da declaração de 2025) e o da corretora
    [Theory]
    [InlineData("00000000000191")] // BBAS3
    [InlineData("89637490000145")] // KLBN11/KLBN4
    [InlineData("17344597000194")] // BBSE3
    [InlineData("42278473000103")] // WIZC3
    [InlineData("08312229000173")] // EZTC3
    [InlineData("09346601000125")] // B3SA3 (matriz)
    [InlineData("33592510000154")] // VALE3
    [InlineData("61532644000115")] // ITSA4
    [InlineData("02474103000119")] // EGIE3
    [InlineData("23373000000132")] // VAMO3
    [InlineData("47960950000121")] // MGLU3
    [InlineData("35578672000163")] // NASD11
    [InlineData("13416245000146")] // DIVO11
    [InlineData("19909560000191")] // IVVB11
    [InlineData("38314708000190")] // HASH11
    [InlineData("02332886000104")] // XP (corretora)
    public void Valido_CnpjsConhecidos(string cnpj) => Assert.True(Cnpj.Valido(cnpj));

    [Theory]
    [InlineData("00000000000192")] // dígito errado
    [InlineData("11111111111111")] // todos iguais
    [InlineData("0000000000019")] // 13 dígitos
    [InlineData(null)]
    public void Valido_RecusaInvalidos(string? cnpj) => Assert.False(Cnpj.Valido(cnpj));

    [Fact]
    public void Normalizar_TiraMascaraEVazioViraNulo()
    {
        Assert.Equal("09346601000125", Cnpj.Normalizar("09.346.601/0001-25"));
        Assert.Null(Cnpj.Normalizar("  "));
        Assert.Null(Cnpj.Normalizar("--/"));
    }

    [Fact]
    public void Formatar_PoeMascara() => Assert.Equal("09.346.601/0001-25", Cnpj.Formatar("09346601000125"));

    [Fact]
    public void EhMatriz_DistingueFilial()
    {
        Assert.True(Cnpj.EhMatriz("09346601000125"));
        Assert.False(Cnpj.EhMatriz("09346601000397")); // filial usada por engano na declaração de 2025
    }
}

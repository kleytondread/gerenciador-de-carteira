using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.ExternalApis;
using Wal.Services.TesouroDireto;

namespace Wal.Tests.Services;

public class TesouroDiretoServiceTests : IDisposable
{
    // Mesmo formato do arquivo do Tesouro Transparente: ';', vírgula decimal, datas dd/MM/aaaa
    private const string Csv = """
        Tipo Titulo;Data Vencimento;Data Base;Taxa Compra Manha;Taxa Venda Manha;PU Compra Manha;PU Venda Manha;PU Base Manha
        Tesouro IPCA+;15/05/2045;23/10/2026;7,10;7,22;1470,10;1457,55;1457,55
        Tesouro IPCA+;15/05/2045;02/12/2022;6,20;6,32;1067,15;1050,00;1050,00
        Tesouro IPCA+ com Juros Semestrais;15/05/2045;23/10/2026;7,05;7,17;4300,00;4250,00;4250,00
        Tesouro Selic;01/03/2029;23/10/2026;0,10;0,12;17000,00;16990,00;16990,00
        Tesouro Prefixado;01/01/2020;02/01/2019;6,00;6,10;900,00;899,00;899,00
        Tesouro IPCA+;15/08/2035;01/01/2025;6,50;6,62;2000,00;1990,00;1990,00
        Tesouro Educa+;15/12/2030;23/10/2026;6,00;6,10;3000,00;2990,00;2990,00
        Tesouro Renda+ Aposentadoria Extra;15/12/2065;23/10/2026;6,90;7,02;1000,00;990,00;990,00
        """;

    private readonly string pasta = Path.Combine(Path.GetTempPath(), "wal-tesouro-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ITesouroDiretoClient> clientMock = new();
    private readonly TesouroDiretoOptions options;
    private readonly TesouroDiretoService sut;

    public TesouroDiretoServiceTests()
    {
        options = new TesouroDiretoOptions { CaminhoArquivo = Path.Combine(pasta, "precotaxatesourodireto.csv") };
        sut = new TesouroDiretoService(clientMock.Object, options, NullLogger<TesouroDiretoService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void SalvarArquivo(string conteudo, TimeSpan idade)
    {
        Directory.CreateDirectory(pasta);
        File.WriteAllText(options.CaminhoArquivo, conteudo, Encoding.UTF8);
        File.SetLastWriteTimeUtc(options.CaminhoArquivo, DateTime.UtcNow - idade);
    }

    private void DownloadDevolve(string conteudo) =>
        clientMock.Setup(c => c.BaixarPrecosETaxasAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((destino, _) => destino.WriteAsync(Encoding.UTF8.GetBytes(conteudo)).AsTask());

    private void DownloadFalhaCom429() =>
        clientMock.Setup(c => c.BaixarPrecosETaxasAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429", null, HttpStatusCode.TooManyRequests));

    [Fact]
    public async Task AtualizarArquivoAsync_ArquivoComMenosDe5h_NaoBaixa()
    {
        SalvarArquivo(Csv, idade: TimeSpan.FromHours(4.9));

        var disponivel = await sut.AtualizarArquivoAsync();

        Assert.True(disponivel);
        clientMock.Verify(c => c.BaixarPrecosETaxasAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarArquivoAsync_ArquivoCom5hOuMais_BaixaESobrescreve()
    {
        SalvarArquivo(Csv, idade: TimeSpan.FromHours(5));
        var novo = Csv.Replace("1457,55", "1460,00");
        DownloadDevolve(novo);

        var disponivel = await sut.AtualizarArquivoAsync();

        Assert.True(disponivel);
        Assert.Equal(novo, File.ReadAllText(options.CaminhoArquivo));
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(options.CaminhoArquivo) < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AtualizarArquivoAsync_SemArquivo_Baixa()
    {
        DownloadDevolve(Csv);

        Assert.True(await sut.AtualizarArquivoAsync());
        Assert.True(File.Exists(options.CaminhoArquivo));
    }

    [Fact]
    public async Task AtualizarArquivoAsync_DownloadFalha_MantemArquivoAntigo()
    {
        SalvarArquivo(Csv, idade: TimeSpan.FromHours(30));
        DownloadFalhaCom429();

        var disponivel = await sut.AtualizarArquivoAsync();

        Assert.True(disponivel);
        Assert.Equal(Csv, File.ReadAllText(options.CaminhoArquivo));
    }

    [Fact]
    public async Task AtualizarArquivoAsync_DownloadFalhaSemArquivoAnterior_IndicaIndisponivel()
    {
        DownloadFalhaCom429();

        Assert.False(await sut.AtualizarArquivoAsync());
        Assert.Empty(await sut.ListarTitulosAsync());
    }

    [Fact]
    public async Task AtualizarArquivoAsync_PaginaHtmlNoLugarDoCsv_MantemArquivoAntigo()
    {
        SalvarArquivo(Csv, idade: TimeSpan.FromHours(30));
        DownloadDevolve("<!DOCTYPE html><html><title>429 - Muitas Requisições</title></html>");

        var disponivel = await sut.AtualizarArquivoAsync();

        Assert.True(disponivel);
        Assert.Equal(Csv, File.ReadAllText(options.CaminhoArquivo));
        Assert.Single(Directory.GetFiles(pasta)); // o temporário foi apagado
    }

    [Fact]
    public async Task ListarTitulosAsync_SoTitulosEmOfertaNaoVencidosDasCategoriasAceitas()
    {
        SalvarArquivo(Csv, idade: TimeSpan.Zero);

        var titulos = await sut.ListarTitulosAsync();

        // Fora: Prefixado vencido, IPCA+ 2035 sem preço há mais de 30 dias, Educa+ (categoria não oferecida)
        Assert.Equal(["IPCA+ 2045", "IPCA+ JS 2045", "RENDA+ 2065", "SELIC 2029"], titulos.Select(t => t.Ticker).Order());

        var ipca = titulos.Single(t => t.Ticker == "IPCA+ 2045");
        Assert.Equal("Tesouro IPCA+ 2045", ipca.Nome);
        Assert.Equal(TitulosTesouro.Ipca, ipca.Categoria);
        Assert.Equal(new DateOnly(2026, 10, 23), ipca.DataUltimoPreco);
        Assert.Equal(1457.55m, ipca.UltimoPreco); // preço de venda (resgate)
        Assert.Equal(7.10m, ipca.TaxaCompra);
    }

    [Fact]
    public async Task ObterPrecosAsync_HistoricoDoTituloEmOrdemDeData()
    {
        SalvarArquivo(Csv, idade: TimeSpan.Zero);

        var precos = await sut.ObterPrecosAsync("Tesouro IPCA+", new DateOnly(2045, 5, 15));

        Assert.Equal(
            [(new DateOnly(2022, 12, 2), 1050.00m), (new DateOnly(2026, 10, 23), 1457.55m)],
            precos);
    }

    [Fact]
    public void Ler_CabecalhoComAcentosEBom_LocalizaColunasPeloNome()
    {
        var csv = "﻿Tipo Título;Data Vencimento;Data Base;PU Base Manhã;PU Venda Manhã;PU Compra Manhã\n" +
                  "Tesouro Selic;01/03/2029;23/10/2026;16990,50;0;17000,00\n";

        var linha = Assert.Single(TesouroDiretoCsv.Ler(new StringReader(csv)));

        Assert.Equal(16990.50m, linha.PuBase);
        Assert.Equal(16990.50m, linha.PrecoAvaliacao); // sem preço de venda, usa o PU base
    }
}

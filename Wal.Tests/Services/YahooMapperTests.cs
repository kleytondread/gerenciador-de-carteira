using Wal.Domain.Enums;
using Wal.ExternalApis.Dtos;
using Wal.Services;

namespace Wal.Tests.Services;

public class YahooMapperTests
{
    [Fact]
    public void ToCotacao_MapeiaCamposEConverteTimestampUnixParaDateOnly()
    {
        var dataUnix = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var origem = new YahooHistoricalPrice { Date = dataUnix, Open = 10, High = 12, Low = 9, Close = 11, Volume = 5000 };

        var cotacao = YahooMapper.ToCotacao(ativoId: 7, origem);

        Assert.NotNull(cotacao);
        Assert.Equal(7, cotacao!.AtivoId);
        Assert.Equal(new DateOnly(2026, 3, 15), cotacao.Data);
        Assert.Equal(10, cotacao.Abertura);
        Assert.Equal(12, cotacao.Maxima);
        Assert.Equal(9, cotacao.Minima);
        Assert.Equal(11, cotacao.Fechamento);
        Assert.Equal(5000, cotacao.Volume);
    }

    [Fact]
    public void ToCotacao_TimestampForaDoIntervaloValido_RetornaNull()
    {
        var origem = new YahooHistoricalPrice { Date = long.MaxValue, Close = 10 };

        var cotacao = YahooMapper.ToCotacao(ativoId: 7, origem);

        Assert.Null(cotacao);
    }

    [Fact]
    public void ToCotacao_BarraIncompletaDoDiaCorrente_RetornaNull()
    {
        // O Yahoo devolve OHLV zerados (com Close repetido do pregão anterior) enquanto o dia
        // corrente não consolida — essa barra não deve ser persistida.
        var origem = new YahooHistoricalPrice { Date = 0, Open = 0, High = 0, Low = 0, Close = 40.66m, Volume = 0 };

        var cotacao = YahooMapper.ToCotacao(ativoId: 7, origem);

        Assert.Null(cotacao);
    }

    [Fact]
    public void ToCotacao_PregaoRealComVolumeZero_NaoEDescartado()
    {
        // Volume 0 sozinho não caracteriza barra incompleta — só o conjunto OHLV todo zerado.
        var origem = new YahooHistoricalPrice { Date = 0, Open = 10, High = 12, Low = 9, Close = 11, Volume = 0 };

        var cotacao = YahooMapper.ToCotacao(ativoId: 7, origem);

        Assert.NotNull(cotacao);
    }

    [Fact]
    public void ToProvento_DataComEDataPagamentoIguaisADataExRecebidaDoYahoo()
    {
        // Limitação conhecida (spec, seção 4.3): Yahoo só fornece a data ex-dividendo,
        // não a data de pagamento real — o mapper usa a mesma data para os dois campos.
        var dataUnix = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var origem = new YahooDividend { Date = dataUnix, Amount = 1.75m };

        var provento = YahooMapper.ToProvento(ativoId: 3, origem);

        Assert.NotNull(provento);
        Assert.Equal(new DateOnly(2026, 5, 1), provento!.DataCom);
        Assert.Equal(new DateOnly(2026, 5, 1), provento.DataPagamento);
        Assert.Equal(1.75m, provento.ValorPorAcao);
    }

    [Fact]
    public void ToProvento_TipoESempreDividendoEOrigemYahooFinance()
    {
        // Limitação conhecida (spec, seção 4.3): Yahoo não distingue Dividendo/JCP/Rendimento.
        var origem = new YahooDividend { Date = 0, Amount = 1 };

        var provento = YahooMapper.ToProvento(ativoId: 3, origem);

        Assert.Equal(TipoProvento.Dividendo, provento!.TipoProvento);
        Assert.Equal(OrigemDado.YahooFinance, provento.Origem);
    }

    [Fact]
    public void ToProvento_TimestampForaDoIntervaloValido_RetornaNull()
    {
        var origem = new YahooDividend { Date = long.MinValue, Amount = 1 };

        var provento = YahooMapper.ToProvento(ativoId: 3, origem);

        Assert.Null(provento);
    }
}

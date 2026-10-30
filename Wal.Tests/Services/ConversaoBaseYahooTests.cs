using Wal.Domain.Enums;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class ConversaoBaseYahooTests
{
    private static DateOnly D(int ano, int mes, int dia) => new(ano, mes, dia);

    [Fact]
    public void DesdobramentoJaLancadoNaBaseNova_FatorRegistroLevaAQuantidadeDaEpocaParaAdoApp()
    {
        // Compra de 2022 lançada já dobrada; desdobramento 1:2 em 16/04/2024
        var movs = new[] { Fixtures.NovaMovimentacao(1, TipoMovimentacao.Compra, D(2022, 3, 2), 480, 17.5m) };
        var eventos = new[] { Fixtures.NovoEvento(1, D(2024, 4, 16), 2, 1) };

        var conversao = ConversaoBaseYahoo.Criar(movs, eventos);

        Assert.Equal(2, conversao.FatorRegistro(1, D(2024, 2, 21)));
        Assert.Equal(1, conversao.FatorRegistro(1, D(2024, 4, 16)));
        Assert.Equal(1, conversao.Fator(1, D(2024, 2, 21)));
    }

    [Fact]
    public void EventoConvertido_NaoMudaOFatorRegistro()
    {
        // Sem posição antes do evento: as quantidades estão em ações da época
        var movs = new[] { Fixtures.NovaMovimentacao(1, TipoMovimentacao.Compra, D(2024, 5, 2), 100, 10) };
        var eventos = new[] { Fixtures.NovoEvento(1, D(2024, 4, 16), 2, 1) };

        var conversao = ConversaoBaseYahoo.Criar(movs, eventos);

        Assert.Equal(1, conversao.FatorRegistro(1, D(2024, 2, 21)));
        Assert.Equal(2, conversao.Fator(1, D(2024, 2, 21)));
    }
}

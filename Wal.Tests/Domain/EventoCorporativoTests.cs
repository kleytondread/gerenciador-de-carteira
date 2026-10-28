using Wal.Domain.Entities;
using Wal.Domain.Enums;

namespace Wal.Tests.Domain;

public class EventoCorporativoTests
{
    [Theory]
    [InlineData(2, 1, TipoEventoCorporativo.Desdobramento)]       // BBAS3 2024
    [InlineData(8, 1, TipoEventoCorporativo.Desdobramento)]       // TSLA34 2020
    [InlineData(1, 10, TipoEventoCorporativo.Grupamento)]         // MGLU3 2024
    [InlineData(11, 10, TipoEventoCorporativo.Bonificacao)]       // ITSA4 2022
    [InlineData(3.5, 2.5, TipoEventoCorporativo.Bonificacao)]     // EGIE3 2025
    [InlineData(1.3333, 1, TipoEventoCorporativo.Bonificacao)]    // GOAU4 2025
    [InlineData(12.715559, 10, TipoEventoCorporativo.Bonificacao)] // EZTC3 2025
    public void Tipo_DeduzidoDaProporcao(double numerador, double denominador, TipoEventoCorporativo esperado)
    {
        var evento = new EventoCorporativo { Numerador = (decimal)numerador, Denominador = (decimal)denominador };

        Assert.Equal(esperado, evento.Tipo);
    }
}

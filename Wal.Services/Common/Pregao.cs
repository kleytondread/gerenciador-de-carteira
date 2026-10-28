namespace Wal.Services.Common;

/// <summary>Horário de São Paulo e a janela em que a B3 tem negociação.</summary>
public static class Pregao
{
    public static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    // Sem calendário de feriados: em feriado o Yahoo devolve o último pregão, e quem consome descarta pela data
    private static readonly TimeOnly Abertura = new(10, 0);
    private static readonly TimeOnly Encerramento = new(18, 0);

    public static bool Aberto(DateTimeOffset momento)
    {
        var local = TimeZoneInfo.ConvertTime(momento, Fuso);
        var hora = TimeOnly.FromDateTime(local.DateTime);
        return local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && hora >= Abertura && hora < Encerramento;
    }

    /// <summary>Data do momento no horário de São Paulo.</summary>
    public static DateOnly Dia(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, Fuso).DateTime);
}

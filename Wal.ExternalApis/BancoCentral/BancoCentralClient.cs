using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Wal.ExternalApis;

public class BancoCentralClient(HttpClient http, ILogger<BancoCentralClient> logger) : IBancoCentralClient
{
    // O SGS recusa séries diárias com mais de 10 anos, e consultas longas demoram e às vezes voltam 502.
    // Um ano por consulta responde em fração de segundo.
    private const int AnosPorConsulta = 1;
    private const int Tentativas = 3;
    private const int DiasMinimosParaDividir = 31;

    /// <summary>Espera entre tentativas após erro 5xx; zerada nos testes.</summary>
    internal TimeSpan EsperaEntreTentativas { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<List<ValorSerieSgs>> ObterSerieAsync(
        int codigoSerie, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default)
    {
        var resultado = new List<ValorSerieSgs>();
        for (var janelaInicio = inicio; janelaInicio <= fim; janelaInicio = janelaInicio.AddYears(AnosPorConsulta))
        {
            var janelaFim = janelaInicio.AddYears(AnosPorConsulta).AddDays(-1);
            if (janelaFim > fim) janelaFim = fim;
            resultado.AddRange(await ObterJanelaAsync(codigoSerie, janelaInicio, janelaFim, cancellationToken));
        }
        return resultado;
    }

    private async Task<List<ValorSerieSgs>> ObterJanelaAsync(
        int codigoSerie, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken)
    {
        var url = $"dados/serie/bcdata.sgs.{codigoSerie}/dados?formato=json" +
                  $"&dataInicial={inicio:dd/MM/yyyy}&dataFinal={fim:dd/MM/yyyy}";

        var resposta = await http.GetAsync(url, cancellationToken);
        // Instabilidade passageira do gateway do BC (502/503/504): tenta de novo antes de desistir
        for (var tentativa = 2; tentativa <= Tentativas && (int)resposta.StatusCode >= 500; tentativa++)
        {
            resposta.Dispose();
            await Task.Delay(EsperaEntreTentativas * (tentativa - 1), cancellationToken);
            resposta = await http.GetAsync(url, cancellationToken);
        }

        using var _ = resposta;
        // O SGS responde 404 quando o intervalo não tem valores (ex.: IPCA do mês ainda não divulgado)
        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];

        // Algumas janelas falham sempre (ex.: CDI de 2024 inteiro volta 200 com {"erro":{}}), mas as
        // metades funcionam: divide o intervalo até um mês antes de desistir
        // (Erro 5xx que persistiu não divide: com o BC fora do ar, viraria uma enxurrada de requisições.)
        var conteudo = resposta.IsSuccessStatusCode ? await resposta.Content.ReadAsStringAsync(cancellationToken) : null;
        if (conteudo is null || !conteudo.TrimStart().StartsWith('['))
        {
            if (conteudo is not null && fim.DayNumber - inicio.DayNumber > DiasMinimosParaDividir)
            {
                var meio = inicio.AddDays((fim.DayNumber - inicio.DayNumber) / 2);
                var primeira = await ObterJanelaAsync(codigoSerie, inicio, meio, cancellationToken);
                var segunda = await ObterJanelaAsync(codigoSerie, meio.AddDays(1), fim, cancellationToken);
                return [.. primeira, .. segunda];
            }

            logger.LogWarning("Banco Central não devolveu a série {Serie} de {Inicio} a {Fim} (status {Status}).",
                codigoSerie, inicio, fim, (int)resposta.StatusCode);
            throw new HttpRequestException(
                $"Banco Central não devolveu a série {codigoSerie} de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}.",
                null, resposta.StatusCode);
        }

        var itens = JsonSerializer.Deserialize<List<ItemSgs>>(conteudo) ?? [];
        var valores = new List<ValorSerieSgs>(itens.Count);
        foreach (var item in itens)
        {
            if (DateOnly.TryParseExact(item.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) &&
                decimal.TryParse(item.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
                valores.Add(new ValorSerieSgs(data, valor));
        }
        return valores;
    }

    private sealed class ItemSgs
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;

        [JsonPropertyName("valor")]
        public string Valor { get; set; } = string.Empty;
    }
}

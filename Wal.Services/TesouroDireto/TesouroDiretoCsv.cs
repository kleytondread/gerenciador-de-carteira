using System.Globalization;
using System.Text;

namespace Wal.Services.TesouroDireto;

/// <summary>Uma linha do arquivo: preço e taxa de um título numa data.</summary>
public record PrecoTituloTesouro(
    string TipoTitulo,
    DateOnly Vencimento,
    DateOnly Data,
    decimal? TaxaCompra,
    decimal PuCompra,
    decimal PuVenda,
    decimal PuBase)
{
    /// <summary>
    /// Preço usado para avaliar a posição: o de venda (o que o Tesouro paga no resgate antecipado);
    /// sem ele, o PU base; por último, o de compra.
    /// </summary>
    public decimal PrecoAvaliacao => PuVenda > 0 ? PuVenda : PuBase > 0 ? PuBase : PuCompra;
}

/// <summary>
/// Lê o CSV "Taxas dos Títulos Ofertados pelo Tesouro Direto": separador ';', vírgula decimal e datas
/// dd/MM/aaaa. As colunas são localizadas pelo nome (sem acento e sem diferenciar maiúsculas), para não
/// quebrar se o Tesouro mudar a ordem.
/// </summary>
public static class TesouroDiretoCsv
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Confere se a primeira linha é o cabeçalho esperado (e não, por exemplo, uma página de erro HTML).</summary>
    public static bool CabecalhoValido(string? primeiraLinha)
    {
        if (string.IsNullOrWhiteSpace(primeiraLinha)) return false;
        var colunas = primeiraLinha.Split(';').Select(Normalizar).ToList();
        return colunas.Contains("tipo titulo") && colunas.Contains("data vencimento") && colunas.Contains("data base");
    }

    public static List<PrecoTituloTesouro> Ler(TextReader leitor)
    {
        var cabecalho = leitor.ReadLine();
        if (!CabecalhoValido(cabecalho))
            throw new FormatException("Arquivo do Tesouro Direto sem o cabeçalho esperado (Tipo Titulo; Data Vencimento; Data Base...).");

        var colunas = cabecalho!.Split(';').Select(Normalizar).ToList();
        int Coluna(string nome) => colunas.IndexOf(nome);
        var iTipo = Coluna("tipo titulo");
        var iVencimento = Coluna("data vencimento");
        var iData = Coluna("data base");
        var iTaxaCompra = Coluna("taxa compra manha");
        var iPuCompra = Coluna("pu compra manha");
        var iPuVenda = Coluna("pu venda manha");
        var iPuBase = Coluna("pu base manha");

        var linhas = new List<PrecoTituloTesouro>();
        string? linha;
        while ((linha = leitor.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            var c = linha.Split(';');
            if (!TryData(Campo(c, iVencimento), out var vencimento) || !TryData(Campo(c, iData), out var data))
                continue; // linha incompleta: ignora em vez de derrubar a leitura do arquivo inteiro

            linhas.Add(new PrecoTituloTesouro(
                TipoTitulo: Campo(c, iTipo).Trim(),
                Vencimento: vencimento,
                Data: data,
                TaxaCompra: TryDecimal(Campo(c, iTaxaCompra), out var taxa) ? taxa : null,
                PuCompra: TryDecimal(Campo(c, iPuCompra), out var puCompra) ? puCompra : 0,
                PuVenda: TryDecimal(Campo(c, iPuVenda), out var puVenda) ? puVenda : 0,
                PuBase: TryDecimal(Campo(c, iPuBase), out var puBase) ? puBase : 0));
        }
        return linhas;
    }

    private static string Campo(string[] campos, int indice) =>
        indice >= 0 && indice < campos.Length ? campos[indice] : string.Empty;

    private static bool TryData(string texto, out DateOnly data) =>
        DateOnly.TryParseExact(texto.Trim(), "dd/MM/yyyy", PtBr, DateTimeStyles.None, out data);

    private static bool TryDecimal(string texto, out decimal valor) =>
        decimal.TryParse(texto.Trim(), NumberStyles.Number, PtBr, out valor);

    internal static string Normalizar(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var ch in texto.Trim().Trim('﻿').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(ch);
        return string.Join(' ', semAcento.ToString().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

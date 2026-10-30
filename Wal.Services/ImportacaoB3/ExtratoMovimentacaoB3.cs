using System.Globalization;
using ClosedXML.Excel;

namespace Wal.Services.ImportacaoB3;

/// <summary>
/// Lê o extrato de Movimentação da Área do Investidor da B3 (Extratos → Movimentação → Excel).
/// Cabeçalho: Entrada/Saída | Data | Movimentação | Produto | Instituição | Quantidade | Preço unitário |
/// Valor da Operação. A data vem como texto dd/mm/aaaa, os números como células numéricas e o vazio como "-".
/// O produto é "TICKER - NOME".
/// </summary>
internal static class ExtratoMovimentacaoB3
{
    public record Linha(
        int NumeroLinha, bool Credito, DateOnly Data, string Movimentacao, string Ticker, string Produto,
        decimal Quantidade, decimal? PrecoUnitario, decimal? Valor);

    public sealed class FormatoInvalidoException(string mensagem) : Exception(mensagem);

    private static readonly string[] Colunas =
        ["Entrada/Saída", "Data", "Movimentação", "Produto", "Instituição", "Quantidade", "Preço unitário", "Valor da Operação"];

    public static List<Linha> Ler(Stream arquivo)
    {
        XLWorkbook livro;
        try { livro = new XLWorkbook(arquivo); }
        catch (Exception) { throw new FormatoInvalidoException("O arquivo não é uma planilha Excel (.xlsx) válida."); }

        using (livro)
        {
            var planilha = livro.Worksheets.FirstOrDefault(p => p.Name.Trim().Equals("Movimentação", StringComparison.OrdinalIgnoreCase))
                           ?? livro.Worksheets.First();

            // Mapeia as colunas pelo nome do cabeçalho, sem depender da ordem
            var cabecalho = planilha.Row(1);
            var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var celula in cabecalho.CellsUsed())
                indice[celula.GetString().Trim()] = celula.Address.ColumnNumber;
            var faltando = Colunas.Where(c => !indice.ContainsKey(c)).ToList();
            if (faltando.Count > 0)
                throw new FormatoInvalidoException(
                    $"Não parece o extrato de Movimentação da B3: faltam as colunas {string.Join(", ", faltando)}.");

            var linhas = new List<Linha>();
            var ultima = planilha.LastRowUsed()?.RowNumber() ?? 1;
            for (var n = 2; n <= ultima; n++)
            {
                var row = planilha.Row(n);
                string Texto(string coluna) => row.Cell(indice[coluna]).GetString().Trim();
                if (string.IsNullOrEmpty(Texto("Data")) && string.IsNullOrEmpty(Texto("Produto"))) continue;

                var data = LerData(row.Cell(indice["Data"]))
                           ?? throw new FormatoInvalidoException($"Linha {n}: data '{Texto("Data")}' inválida.");
                var produto = Texto("Produto");
                linhas.Add(new Linha(
                    n,
                    Texto("Entrada/Saída").StartsWith("Cred", StringComparison.OrdinalIgnoreCase),
                    data,
                    Texto("Movimentação"),
                    TickerDoProduto(produto),
                    produto,
                    LerNumero(row.Cell(indice["Quantidade"])) ?? 0,
                    LerNumero(row.Cell(indice["Preço unitário"])),
                    LerNumero(row.Cell(indice["Valor da Operação"]))));
            }
            return linhas;
        }
    }

    /// <summary>"BBAS3 - BANCO DO BRASIL S/A" → "BBAS3".</summary>
    internal static string TickerDoProduto(string produto)
    {
        var separador = produto.IndexOf(" - ", StringComparison.Ordinal);
        return (separador >= 0 ? produto[..separador] : produto).Trim().ToUpperInvariant();
    }

    private static DateOnly? LerData(IXLCell celula)
    {
        if (celula.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(celula.GetDateTime());
        if (celula.DataType == XLDataType.Number) return DateOnly.FromDateTime(DateTime.FromOADate(celula.GetDouble()));
        return DateOnly.TryParseExact(celula.GetString().Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    // Numérico de verdade; "-" ou vazio = sem valor; texto "1.234,56" por garantia
    private static decimal? LerNumero(IXLCell celula)
    {
        if (celula.DataType == XLDataType.Number) return (decimal)celula.GetDouble();
        var texto = celula.GetString().Trim().Replace("R$", "").Trim();
        if (texto is "" or "-") return null;
        return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var valor) ? valor : null;
    }
}

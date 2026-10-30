using ClosedXML.Excel;

namespace Wal.Tests.Services;

/// <summary>Monta um extrato de Movimentação da B3 em memória, no formato real (data em texto, "-" no vazio).</summary>
internal static class ExtratoB3TestHelper
{
    public record Linha(string EntradaSaida, string Data, string Movimentacao, string Produto, decimal Quantidade, decimal? Preco, decimal? Valor);

    public static MemoryStream Planilha(IEnumerable<Linha> linhas, bool semColunaValor = false)
    {
        using var livro = new XLWorkbook();
        var planilha = livro.AddWorksheet("Movimentação");
        string[] cabecalho = ["Entrada/Saída", "Data", "Movimentação", "Produto", "Instituição", "Quantidade", "Preço unitário", "Valor da Operação"];
        var colunas = semColunaValor ? cabecalho[..^1] : cabecalho;
        for (var i = 0; i < colunas.Length; i++) planilha.Cell(1, i + 1).Value = colunas[i];

        var n = 2;
        foreach (var l in linhas)
        {
            planilha.Cell(n, 1).Value = l.EntradaSaida;
            planilha.Cell(n, 2).Value = l.Data; // texto, como no arquivo da B3
            planilha.Cell(n, 3).Value = l.Movimentacao;
            planilha.Cell(n, 4).Value = l.Produto;
            planilha.Cell(n, 5).Value = "XP INVESTIMENTOS CCTVM S/A";
            planilha.Cell(n, 6).Value = l.Quantidade;
            if (l.Preco is decimal p) planilha.Cell(n, 7).Value = p; else planilha.Cell(n, 7).Value = "-";
            if (!semColunaValor)
            {
                if (l.Valor is decimal v) planilha.Cell(n, 8).Value = v; else planilha.Cell(n, 8).Value = "-";
            }
            n++;
        }

        var memoria = new MemoryStream();
        livro.SaveAs(memoria);
        memoria.Position = 0;
        return memoria;
    }
}

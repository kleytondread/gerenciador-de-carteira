namespace Wal.Services.Irpf;

/// <summary>Regras fiscais usadas tanto na importação do extrato da B3 quanto na aba de Imposto de Renda.</summary>
public static class RegrasIrpf
{
    /// <summary>
    /// IRRF do JCP, que é retido "na data do pagamento ou crédito" (Perguntas e Respostas IRPF 2026, pergunta
    /// 320): 15% até 2025 e 17,5% a partir de 01/01/2026 (Lei Complementar 224/2025, art. 8º). O crédito é a
    /// data com — o extrato da B3 confirma: JCP declarado em 2025 e pago em 2026 saiu com 15%.
    /// </summary>
    public static decimal AliquotaJcp(DateOnly dataCredito) => dataCredito.Year >= 2026 ? 0.175m : 0.15m;

    /// <summary>Centavos com arredondamento comercial (0,005 sobe), não o bancário do Math.Round.</summary>
    public static decimal Centavos(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Data com (último dia com direito) a partir da data ex: o último pregão antes dela, pelas datas de cotação
    /// do ativo; sem cotação, o dia útil anterior (só fins de semana). Ex.: data ex 02/01/2026 → 30/12/2025.
    /// </summary>
    /// <param name="pregoes">Datas com cotação do ativo, em ordem crescente.</param>
    public static DateOnly DataComPelaDataEx(DateOnly dataEx, IReadOnlyList<DateOnly> pregoes)
    {
        var indice = BuscarAnterior(pregoes, dataEx);
        // Cotação muito antiga não serve (ativo sem histórico recente): cai no dia útil anterior
        if (indice >= 0 && pregoes[indice] >= dataEx.AddDays(-7)) return pregoes[indice];

        var data = dataEx.AddDays(-1);
        while (data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) data = data.AddDays(-1);
        return data;
    }

    // Índice da maior data < alvo (ou -1)
    private static int BuscarAnterior(IReadOnlyList<DateOnly> datas, DateOnly alvo)
    {
        int inicio = 0, fim = datas.Count - 1, resposta = -1;
        while (inicio <= fim)
        {
            var meio = (inicio + fim) / 2;
            if (datas[meio] < alvo) { resposta = meio; inicio = meio + 1; }
            else fim = meio - 1;
        }
        return resposta;
    }
}

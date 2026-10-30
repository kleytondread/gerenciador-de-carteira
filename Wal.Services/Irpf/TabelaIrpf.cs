using Wal.Domain.Enums;

namespace Wal.Services.Irpf;

/// <summary>Como a Receita trata cada ativo: define a ficha, o código e a apuração do ganho.</summary>
public enum ClasseFiscal
{
    /// <summary>Ações e units: isenção de vendas até R$ 20 mil/mês.</summary>
    Acao,
    /// <summary>ETF de renda variável (fundo de índice de ações): 15%, sem isenção.</summary>
    EtfRendaVariavel,
    /// <summary>BDR: 15%, sem isenção.</summary>
    Bdr,
    /// <summary>FII/Fiagro: 20%, ficha própria, prejuízo só compensa com FII.</summary>
    Fii,
    /// <summary>ETF de renda fixa: IR retido na fonte pela corretora; fora da ficha Renda Variável.</summary>
    EtfRendaFixa,
    /// <summary>Tesouro Direto: IR retido na fonte; fora da ficha Renda Variável.</summary>
    Tesouro
}

/// <summary>Código de uma ficha do programa do IRPF (Bens e Direitos: grupo-código; rendimentos: só código).</summary>
public record CodigoIrpf(string Grupo, string Codigo, string Descricao)
{
    public string Rotulo => Grupo.Length > 0 ? $"{Grupo}-{Codigo}" : Codigo;
}

/// <summary>
/// Códigos do programa do IRPF por ano-calendário. Os de 2025 são os que o programa IRPF 2026 usou na declaração
/// do usuário (ações 03-01, Tesouro 04-02, BDR 04-04, ETF 07-06, JCP a receber 99-07; rendimentos 09, 10, 06,
/// 20). Os marcados como <see cref="Conferido"/> = false vêm de guias, não de uma declaração entregue.
/// O programa do ano seguinte só sai em março: até lá, anos posteriores usam esta tabela com aviso.
/// </summary>
public static class TabelaIrpf
{
    public const int UltimoAnoConferido = 2025;

    public static bool Conferido(int anoCalendario) => anoCalendario <= UltimoAnoConferido;

    public static ClasseFiscal Classe(TipoAtivo tipo, CategoriaAlocacao categoria) => tipo switch
    {
        TipoAtivo.Acao => ClasseFiscal.Acao,
        TipoAtivo.BDR => ClasseFiscal.Bdr,
        TipoAtivo.FII => ClasseFiscal.Fii,
        TipoAtivo.Tesouro => ClasseFiscal.Tesouro,
        // ETF de renda fixa (ex.: B5P211) é reconhecido pela categoria de alocação
        TipoAtivo.ETF when categoria == CategoriaAlocacao.RendaFixa => ClasseFiscal.EtfRendaFixa,
        _ => ClasseFiscal.EtfRendaVariavel
    };

    public static CodigoIrpf BemDireito(ClasseFiscal classe) => classe switch
    {
        ClasseFiscal.Acao => new("03", "01", "Ações (inclusive as listadas em bolsa)"),
        ClasseFiscal.Bdr => new("04", "04", "Ativos negociados em bolsa no Brasil (BDRs, opções e outros)"),
        ClasseFiscal.Tesouro => new("04", "02", "Títulos públicos e privados sujeitos à tributação (Tesouro Direto, CDB, RDB e outros)"),
        ClasseFiscal.EtfRendaVariavel => new("07", "06", "ETF – entidade de investimento (Lei 14.754/2023)"),
        ClasseFiscal.EtfRendaFixa => new("07", "08", "Fundos de índice de renda fixa (ETFs) – Lei 13.043/2014"),
        _ => new("07", "03", "Fundo de Investimento Imobiliário (FII)")
    };

    /// <summary>Códigos que ainda não apareceram numa declaração entregue: conferir no programa.</summary>
    public static bool BemDireitoConferido(ClasseFiscal classe) =>
        classe is ClasseFiscal.Acao or ClasseFiscal.Bdr or ClasseFiscal.Tesouro or ClasseFiscal.EtfRendaVariavel;

    public static readonly CodigoIrpf JcpAReceber = new("99", "07", "Juros sobre capital próprio creditado, mas não pago");

    public static readonly CodigoIrpf Dividendos = new("", "09", "Lucros e dividendos recebidos");
    public static readonly CodigoIrpf Bonificacoes = new("", "18", "Incorporação de reservas ao capital / bonificações em ações");
    public static readonly CodigoIrpf GanhoAcoesIsento = new("", "20", "Ganhos líquidos em operações no mercado à vista de ações (vendas até R$ 20 mil no mês)");
    public static readonly CodigoIrpf RendimentoFii = new("", "26", "Outros (rendimentos de FII/Fiagro)");

    public static readonly CodigoIrpf Jcp = new("", "10", "Juros sobre capital próprio");
    public static readonly CodigoIrpf AplicacoesFinanceiras = new("", "06", "Rendimentos de aplicações financeiras");

    public const decimal LimiteIsencaoAcoes = 20_000m;
    public const decimal DarfMinimo = 10m;
    public const decimal AliquotaComum = 0.15m;
    public const decimal AliquotaDayTrade = 0.20m;
    public const decimal AliquotaFii = 0.20m;
    public const decimal IrrfVendas = 0.00005m; // 0,005%
    public const decimal IrrfDayTrade = 0.01m;
}

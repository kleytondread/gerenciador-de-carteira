namespace Wal.Domain.Enums;

public enum TipoAtivo
{
    Acao,
    ETF,
    FII,
    // Título público do Tesouro Direto: cotação vem do arquivo do Tesouro Transparente, não do Yahoo
    Tesouro,
    // Recibo de ação estrangeira negociado na B3 (ex.: TSLA34). Cotação e proventos vêm do Yahoo, como ações.
    BDR
}

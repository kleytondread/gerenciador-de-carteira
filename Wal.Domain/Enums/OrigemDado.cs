namespace Wal.Domain.Enums;

// Gravado no banco como número: os valores ficam fixos. O 1 era da brapi.dev, fonte que deixou de existir.
public enum OrigemDado
{
    YahooFinance = 0,
    Manual = 2,
    // Provento criado a partir do extrato de Movimentação da B3: tipo, datas e valores pagos são os reais.
    // Editável como um lançamento manual.
    ExtratoB3 = 3
}

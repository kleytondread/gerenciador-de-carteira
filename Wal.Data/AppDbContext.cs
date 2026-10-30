using Microsoft.EntityFrameworkCore;
using Wal.Domain.Entities;

namespace Wal.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ativo> Ativos => Set<Ativo>();
    public DbSet<Movimentacao> Movimentacoes => Set<Movimentacao>();
    public DbSet<CotacaoHistorica> CotacoesHistoricas => Set<CotacaoHistorica>();
    public DbSet<Provento> Proventos => Set<Provento>();
    public DbSet<EventoCorporativo> EventosCorporativos => Set<EventoCorporativo>();
    public DbSet<CotacaoIndice> CotacoesIndices => Set<CotacaoIndice>();
    public DbSet<PagamentoProvento> PagamentosProventos => Set<PagamentoProvento>();
    public DbSet<DarfPago> DarfsPagos => Set<DarfPago>();
    public DbSet<Corretora> Corretoras => Set<Corretora>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ativo>(e =>
        {
            e.HasIndex(a => a.Ticker).IsUnique();
            // Corretora em uso não pode ser apagada (o serviço avisa antes)
            e.HasOne(a => a.Corretora).WithMany().HasForeignKey(a => a.CorretoraId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Corretora>(e =>
        {
            e.HasIndex(c => c.Cnpj).IsUnique();
        });

        modelBuilder.Entity<Movimentacao>(e =>
        {
            e.Property(m => m.Quantidade).HasPrecision(18, 4);
            e.Property(m => m.PrecoUnitario).HasPrecision(18, 4);
            e.Property(m => m.Taxas).HasPrecision(18, 4);
        });

        modelBuilder.Entity<CotacaoHistorica>(e =>
        {
            e.HasIndex(c => new { c.AtivoId, c.Data }).IsUnique();
            e.Property(c => c.Abertura).HasPrecision(18, 4);
            e.Property(c => c.Maxima).HasPrecision(18, 4);
            e.Property(c => c.Minima).HasPrecision(18, 4);
            e.Property(c => c.Fechamento).HasPrecision(18, 4);
        });

        modelBuilder.Entity<Provento>(e =>
        {
            // O tipo entra na chave: a empresa pode pagar dividendo e JCP no mesmo dia (ex.: CPLE6 em 28/06/2024).
            // A data com também: no mesmo dia podem ser pagos JCPs de créditos diferentes (ex.: ITSA4 em 07/03/2025)
            e.HasIndex(p => new { p.AtivoId, p.DataPagamento, p.TipoProvento, p.Origem, p.DataCom }).IsUnique();
            e.Property(p => p.ValorPorAcao).HasPrecision(18, 4);
            e.Ignore(p => p.TipoEfetivo);
            e.Ignore(p => p.DataPagamentoEfetiva);
        });

        modelBuilder.Entity<PagamentoProvento>(e =>
        {
            e.HasOne(p => p.Provento).WithMany(p => p.Pagamentos).HasForeignKey(p => p.ProventoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.ProventoId, p.Data });
            e.Property(p => p.Quantidade).HasPrecision(18, 4);
            e.Property(p => p.ValorBruto).HasPrecision(18, 4);
            e.Property(p => p.ValorLiquido).HasPrecision(18, 4);
        });

        modelBuilder.Entity<DarfPago>(e =>
        {
            e.HasIndex(d => new { d.Ano, d.Mes });
            e.Property(d => d.ValorPrincipal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<EventoCorporativo>(e =>
        {
            e.HasIndex(ev => new { ev.AtivoId, ev.Data, ev.Origem }).IsUnique();
            // Bonificações vêm com proporções quebradas (ex.: 12,715559:10)
            e.Property(ev => ev.Numerador).HasPrecision(18, 6);
            e.Property(ev => ev.Denominador).HasPrecision(18, 6);
            e.Ignore(ev => ev.Fator);
            e.Ignore(ev => ev.Tipo);
        });

        modelBuilder.Entity<CotacaoIndice>(e =>
        {
            e.HasIndex(c => new { c.Indice, c.Data }).IsUnique();
            // Taxa diária do CDI tem 8 casas como fração (0,00050788)
            e.Property(c => c.Valor).HasPrecision(20, 10);
        });
    }
}

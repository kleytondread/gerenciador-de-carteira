using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wal.Data;
using Wal.Services.ImportacaoB3;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

/// <summary>Backup antes da importação da B3: precisa de um banco em arquivo (em memória não há backup).</summary>
public class ImportacaoB3BackupTests : IDisposable
{
    // Apóstrofo no caminho: o caso que, montado dentro do SQL, quebraria o comando
    private readonly string pasta = Path.Combine(Path.GetTempPath(), $"wal-backup d'teste {Guid.NewGuid():N}");

    [Fact]
    public async Task FazerBackup_CopiaOBancoParaOArquivoAoLado()
    {
        Directory.CreateDirectory(pasta);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(pasta, "carteira.db")};Pooling=False")
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Ativos.Add(Fixtures.NovoAtivo("BBAS3", "Banco do Brasil ON"));
            await db.SaveChangesAsync();

            var backup = await new ImportacaoB3Service(db, NullLogger<ImportacaoB3Service>.Instance).FazerBackupAsync();

            Assert.NotNull(backup);
            Assert.Equal(pasta, Path.GetDirectoryName(backup));
            Assert.StartsWith("carteira.backup-antes-importacao-b3-", Path.GetFileName(backup));

            await using var copia = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={backup};Pooling=False").Options);
            Assert.Equal("BBAS3", (await copia.Ativos.SingleAsync()).Ticker);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        GC.SuppressFinalize(this);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wal.Data;

namespace Wal.Tests.TestHelpers;

/// <summary>
/// Base para testes que precisam de um AppDbContext real (não o provider InMemory do EF Core),
/// para que índices únicos, tipos e comportamento de query fiquem fiéis ao SQLite usado em produção.
/// Cada teste recebe uma conexão SQLite ":memory:" própria — a conexão precisa ficar aberta durante
/// todo o teste (fechá-la apagaria o banco), e é descartada junto com o DbContext ao final.
/// </summary>
public abstract class SqliteInMemoryTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly AppDbContext Db;

    protected SqliteInMemoryTestBase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

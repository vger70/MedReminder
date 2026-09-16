using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Tests.Support;

// Fixture per test integrazione: SQLite in-memory con connessione
// tenuta aperta per la vita del test. Ogni test istanzia il proprio
// fixture -> DB completamente isolato (nessuna interferenza tra test).
// Da usare in `using` per garantire dispose deterministico.
internal sealed class SqliteInMemoryFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MedReminderDbContext> _options;

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public MedReminderDbContext CreateContext() => new(_options);

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

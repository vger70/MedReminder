using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// ProfileDatabaseBuilder: payload -> fresh SQLite file, read back through
// the real repositories.
public sealed class ProfileDatabaseBuilderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "medreminder-builder-" + Guid.NewGuid().ToString("N"));

    public ProfileDatabaseBuilderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Builds_a_database_the_repositories_can_read()
    {
        var payload = TestArchiveWriter.SamplePayload();
        var path = Path.Combine(_directory, "medreminder.db");

        await ProfileDatabaseBuilder.BuildAsync(path, payload, default);

        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(SqliteConnectionStrings.ForFile(path))
            .Options;
        await using var db = new MedReminderDbContext(options);
        var medicines = await new MedicineRepository(db).ListAllAsync(default);
        var movements = await new StockMovementRepository(db).ListForMedicineAsync(medicines.Single().Id, default);
        var schedule = await new MedicationScheduleHistoryRepository(db).ListForMedicineAsync(medicines.Single().Id, default);

        medicines.Should().ContainSingle(m => m.Name == "Sample" && m.StockEpoch == 2);
        MedicineStock.Current(movements).Should().Be(28m);
        schedule.Should().ContainSingle();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }
}

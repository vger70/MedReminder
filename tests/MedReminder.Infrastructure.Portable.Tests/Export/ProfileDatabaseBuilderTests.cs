using FluentAssertions;
using MedReminder.Application.Migrations;
using MedReminder.Application.Export;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;
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
        // The archive may predate the as-needed flag.
        (await new PendingDataMigrations(db).IsPendingAsync(AsNeededSlotBackfill.MigrationName, default))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Version_1_archive_imports_as_legacy_with_a_cutoff()
    {
        var payload = ExportPayloadUpgraderTests.VersionOnePayloadWithSlots(out var medicineId);
        var path = Path.Combine(_directory, "medreminder.db");
        var importedAt = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

        await ProfileDatabaseBuilder.BuildAsync(
            path, payload, default, new ExportPayloadUpgraderTests.TestTime(importedAt));

        await using var db = Open(path);
        (await db.StockMovements.ToListAsync()).Should().HaveCount(2)
            .And.OnlyContain(m => m.Origin == StockMovementOrigin.Legacy);
        (await db.LedgerCutoffs.SingleAsync()).CutoffDay.Should().Be(new DateOnly(2026, 9, 24));
        (await new MedicationAdministrationSlotRepository(db).ListForMedicineAsync(medicineId, default))
            .Should().HaveCount(2);
    }

    // Every import is frozen at the import instant (B.1 Phase 2c-2):
    // the archive's rows become Legacy and its cutoff is replaced; slot
    // sets and stock counts are kept as they are.
    [Fact]
    public async Task Version_2_archive_is_frozen_at_the_import()
    {
        var payload = TestArchiveWriter.SamplePayload();
        var medicineId = payload.Medicines.Single().Id;
        payload.StockMovements[0].Origin = "User";
        payload.StockMovements[1].Origin = "Derived";
        var setId = Guid.NewGuid();
        payload.MedicationAdministrationSlotSets.Add(new ExportedAdministrationSlotSet
        {
            Id = setId,
            MedicineId = medicineId,
            EffectiveFrom = new DateOnly(2026, 9, 10),
            RecordedAt = new DateTimeOffset(2026, 9, 10, 7, 0, 0, TimeSpan.Zero),
        });
        payload.MedicationAdministrationSlots.Add(new ExportedAdministrationSlot
        {
            Id = Guid.NewGuid(), MedicineId = medicineId, SetId = setId, Dose = 2m, Order = 0,
        });
        payload.StockCounts.Add(new ExportedStockCount
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            CountDay = new DateOnly(2026, 9, 12),
            CountedQuantity = 20m,
            TakenToday = 1m,
            ThresholdAtCount = 7,
            RecordedAt = new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero),
        });
        payload.LedgerCutoff = new ExportedLedgerCutoff
        {
            CutoffDay = new DateOnly(2026, 9, 5),
            FrozenAt = new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero),
        };
        var path = Path.Combine(_directory, "medreminder.db");
        var importedAt = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

        await ProfileDatabaseBuilder.BuildAsync(
            path, payload, default, new ExportPayloadUpgraderTests.TestTime(importedAt));

        await using var db = Open(path);
        (await db.StockMovements.ToListAsync())
            .Should().HaveCount(2).And.OnlyContain(m => m.Origin == StockMovementOrigin.Legacy);
        (await new MedicationAdministrationSlotRepository(db).ListForMedicineAsync(medicineId, default))
            .Should().ContainSingle(s => s.SetId == setId && s.Dose == 2m);
        (await db.StockCounts.SingleAsync()).TakenToday.Should().Be(1m);
        var cutoff = await db.LedgerCutoffs.SingleAsync();
        cutoff.CutoffDay.Should().Be(new DateOnly(2026, 9, 24));
        cutoff.FrozenAt.Should().Be(importedAt);
        (await db.Medicines.SingleAsync()).LedgerBaselineEpoch.Should().Be(2);
    }

    private static MedReminderDbContext Open(string path)
        => new(new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(SqliteConnectionStrings.ForFile(path))
            .Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }
}

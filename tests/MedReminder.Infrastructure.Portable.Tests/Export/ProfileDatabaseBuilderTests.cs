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

    // Repeatable prescriptions (docs/EXPORT-FORMAT.md §3.14): the
    // additive fields travel through payload.json and the database; a
    // prescription written without them imports as a single one.
    [Fact]
    public async Task Repeatable_prescriptions_round_trip_and_older_entries_import_as_single()
    {
        var payload = TestArchiveWriter.SamplePayload();
        var medicineId = payload.Medicines.Single().Id;
        var at = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var repeatable = new ExportedPrescription
        {
            Id = Guid.NewGuid(), MedicineId = medicineId, IssuedOn = new DateOnly(2026, 10, 1),
            ValidUntil = new DateOnly(2027, 9, 30), RecordedAt = at, UpdatedAt = at, Dispensations = 12,
            DispensationRecords =
            [
                new ExportedPrescriptionDispensation
                {
                    Id = Guid.NewGuid(), CollectedOn = new DateOnly(2026, 10, 1), Packages = 1, RecordedAt = at, UpdatedAt = at,
                },
            ],
        };
        payload.Prescriptions = [repeatable];
        var json = System.Text.Json.JsonSerializer.Serialize(payload, ExportJson.Options);
        json.Should().Contain("\"dispensations\": 12").And.Contain("\"dispensationRecords\"");
        var singleId = Guid.NewGuid();
        // An entry as an older app wrote it: neither field.
        var legacy = $$"""
            {"id":"{{singleId}}","medicineId":"{{medicineId}}","issuedOn":"2026-09-01","validUntil":"2026-09-30",
             "recordedAt":"2026-09-01T09:00:00+00:00","updatedAt":"2026-09-01T09:00:00+00:00"}
            """;
        var back = System.Text.Json.JsonSerializer.Deserialize<ExportPayload>(json, ExportJson.Options)!;
        back.Prescriptions.Add(System.Text.Json.JsonSerializer.Deserialize<ExportedPrescription>(legacy, ExportJson.Options)!);
        var path = Path.Combine(_directory, "medreminder.db");

        await ProfileDatabaseBuilder.BuildAsync(path, back, default);

        await using var db = Open(path);
        (await db.Prescriptions.SingleAsync(p => p.Id == repeatable.Id)).Dispensations.Should().Be(12);
        var single = await db.Prescriptions.SingleAsync(p => p.Id == singleId);
        single.Dispensations.Should().BeNull();
        single.IsRepeatable.Should().BeFalse();
        var dispensation = await db.PrescriptionDispensations.SingleAsync();
        dispensation.PrescriptionId.Should().Be(repeatable.Id);
        dispensation.MedicineId.Should().Be(medicineId);
        dispensation.Packages.Should().Be(1);
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

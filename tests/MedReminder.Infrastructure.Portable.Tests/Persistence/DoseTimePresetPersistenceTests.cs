using FluentAssertions;
using MedReminder.Application.Migrations;
using MedReminder.Domain.Medicines;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Time-of-day presets on real SQLite (docs/analysis/
// ANALYSIS-INTRADAY-CONSUMPTION.md §6): the rows persist and are
// replaced as a whole, the boot patch creates the tables and the slot
// column on an older database and marks the preset link pending, and
// the archive carries them.
public class DoseTimePresetPersistenceTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public async Task Presets_and_defaults_are_replaced_as_a_whole()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseTimePresetRepository(ctx);
            await repo.ReplaceAllAsync(
                [new DoseTimePreset { Label = "Old", Time = new TimeOnly(6, 0) }],
                [new DoseTimeDefault { AdministrationsPerDay = 1, Times = "06:00" }], default);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseTimePresetRepository(ctx);
            await repo.ReplaceAllAsync(
                [
                    new DoseTimePreset
                    {
                        Id = BuiltInDoseTimePresets.IdOf("Morning"), BuiltInKey = "Morning", Time = new TimeOnly(7, 15),
                    },
                ],
                [new DoseTimeDefault { AdministrationsPerDay = 2, Times = "07:00;19:00" }], default);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseTimePresetRepository(ctx);
            (await repo.ListPresetsAsync(default)).Should().ContainSingle()
                .Which.Time.Should().Be(new TimeOnly(7, 15));
            (await repo.ListDefaultsAsync(default)).Should().ContainSingle()
                .Which.Times.Should().Be("07:00;19:00");
        }
    }

    [Fact]
    public async Task The_patch_creates_the_tables_and_marks_the_preset_link_pending()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DoseTimePresets"";");
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DoseTimeDefaults"";");
            await ctx.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""MedicationAdministrationSlots"" DROP COLUMN ""PresetId"";");
        }

        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await new DoseTimePresetRepository(ctx).ListPresetsAsync(default)).Should().BeEmpty();
            (await ctx.MedicationAdministrationSlots.ToListAsync()).Should().BeEmpty();
            (await new PendingDataMigrations(ctx).IsPendingAsync(SlotPresetBackfill.MigrationName, default))
                .Should().BeTrue();
        }
    }

    [Fact]
    public void Presets_defaults_and_slot_presets_round_trip_through_the_archive()
    {
        var preset = new DoseTimePreset { Label = "Dopo la palestra", Time = new TimeOnly(18, 0), IsHidden = true };
        var slot = new MedicationAdministrationSlot
        {
            MedicineId = Guid.NewGuid(), SetId = Guid.NewGuid(), Dose = 1m, PresetId = preset.Id,
        };

        ExportMapper.ToEntity(ExportMapper.ToDto(preset)).Should().BeEquivalentTo(preset);
        ExportMapper.ToEntity(ExportMapper.ToDto(new DoseTimeDefault { AdministrationsPerDay = 3, Times = "07:00;12:00;19:00" }))
            .Times.Should().Be("07:00;12:00;19:00");
        ExportMapper.ToEntity(ExportMapper.ToDto(slot)).PresetId.Should().Be(preset.Id);
    }
}

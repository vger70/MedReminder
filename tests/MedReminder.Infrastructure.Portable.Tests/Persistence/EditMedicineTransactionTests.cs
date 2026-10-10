using FluentAssertions;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// EditMedicine on the real EF Core stack: the general fields and the
// schedule change are saved together or not at all.
public sealed class EditMedicineTransactionTests
{
    private static readonly TimeProvider Clock = new FixedTime(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Saves_the_fields_and_the_schedule_together()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await AddAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            await Edit(ctx).ExecuteAsync(new EditMedicineCommand(
                Update(id, "Enalapril 20"),
                new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 13))), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await new MedicineRepository(ctx).GetAsync(id, CancellationToken.None))!.Name.Should().Be("Enalapril 20");
            (await new MedicationScheduleHistoryRepository(ctx).ListForMedicineAsync(id, CancellationToken.None))
                .Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task A_failed_schedule_change_rolls_back_the_fields()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await AddAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            // The edit moves the start date past the schedule change: the
            // change fails inside the transaction, after UpdateMedicine has
            // saved the new name and start date.
            var act = () => Edit(ctx).ExecuteAsync(new EditMedicineCommand(
                Update(id, "Enalapril 20") with { StartDate = new DateOnly(2026, 9, 20) },
                new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 13))), CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*therapy start date*");
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await new MedicineRepository(ctx).GetAsync(id, CancellationToken.None))!.Name.Should().Be("Enalapril");
            (await new MedicationScheduleHistoryRepository(ctx).ListForMedicineAsync(id, CancellationToken.None))
                .Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task A_schedule_change_for_another_medicine_is_refused_before_any_write()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await AddAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            var act = () => Edit(ctx).ExecuteAsync(new EditMedicineCommand(
                Update(id, "Enalapril 20"),
                new ChangeMedicationScheduleCommand(Guid.NewGuid(), 2m, 1, new DateOnly(2026, 9, 13))), CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*another medicine*");
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await new MedicineRepository(ctx).GetAsync(id, CancellationToken.None))!.Name.Should().Be("Enalapril");
        }
    }

    [Fact]
    public async Task The_transaction_runs_inside_the_write_gate()
    {
        // The gate must be held for the whole transaction: otherwise a
        // catch-up, the monitor or a database swap could take it between
        // the two steps, or the SQLite write lock would precede the gate.
        using var fixture = new SqliteInMemoryFixture();
        var id = await AddAsync(fixture);
        bool? gateWasFree = null;

        await using (var ctx = fixture.CreateContext())
        {
            var probe = new GateProbe(new TransactionalScope(ctx), async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                try
                {
                    await new DatabaseExclusiveAccess().RunExclusiveAsync(_ => Task.CompletedTask, timeout.Token);
                    gateWasFree = true;
                }
                catch (OperationCanceledException)
                {
                    gateWasFree = false;
                }
            });
            await Edit(ctx, probe).ExecuteAsync(new EditMedicineCommand(Update(id, "Enalapril 20")), CancellationToken.None);
        }

        gateWasFree.Should().BeFalse();
    }

    private sealed class GateProbe(ITransactionalScope inner, Func<Task> probe) : ITransactionalScope
    {
        public Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
            => inner.RunAsync(async ct =>
            {
                await probe();
                await work(ct);
            }, cancellationToken);
    }

    private static EditMedicine Edit(MedReminderDbContext ctx, ITransactionalScope? transaction = null)
        => new(
            new UpdateMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new MedicineActivityRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock),
            new ChangeMedicationSchedule(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock),
            transaction ?? new TransactionalScope(ctx));

    private static UpdateMedicineCommand Update(Guid id, string name)
        => new(id, name, ActiveIngredient: null, Package: null, Unit: "compresse", ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows, EndDate: null, DoctorName: null, Notes: null,
            IsActive: true);

    private static async Task<Guid> AddAsync(SqliteInMemoryFixture fixture)
    {
        await using var ctx = fixture.CreateContext();
        return await new AddMedicine(
            new MedicineRepository(ctx),
            new MedicationScheduleHistoryRepository(ctx),
            new MedicationAdministrationSlotRepository(ctx),
            new StockMovementRepository(ctx),
            TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock)
            .ExecuteAsync(new AddMedicineCommand(
                Name: "Enalapril",
                Unit: "compresse",
                DosePerAdministration: 1m,
                AdministrationsPerDay: 2,
                StartDate: new DateOnly(2026, 9, 1),
                ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows,
                InitialQuantity: 30m), CancellationToken.None);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

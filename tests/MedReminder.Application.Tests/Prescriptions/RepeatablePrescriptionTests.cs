using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// Repeatable prescriptions (docs/prompt/PROMPT-REPEATABLE-PRESCRIPTION.md):
// the use cases and their operations, the sync of dispensations between
// two devices, the low-stock warning and the reminder before "valid
// until".
public class RepeatablePrescriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly DateOnly Until = PrescriptionRules.DefaultRepeatableValidUntil(Today);

    private static Task<Guid> AddMedicineAsync(ApplicationTestScope scope, decimal stock = 100m,
        NotificationChannels channels = NotificationChannels.Windows | NotificationChannels.Email)
        => scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 2,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: channels, EndDate: null,
            InitialQuantity: stock), default);

    private static SavePrescriptionCommand Repeatable(Guid medicine, int allowed = 12, DateOnly? until = null,
        Guid? id = null, IReadOnlyList<DispensationEntry>? records = null)
        => new(id, medicine, null, Today, "NRE-R", 1, until ?? Until, null, allowed, records);

    [Fact]
    public async Task Saving_writes_one_register_per_changed_dispensation_only()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);

        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine), default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        await scope.RecordDispensation.ExecuteAsync(id, Today.AddDays(30), 1, default);

        var ops = scope.SyncOperations.All;
        ops.Where(o => o.Type == nameof(PrescriptionChanged)).Should().ContainSingle()
            .Which.SchemaVersion.Should().Be(12, "a repeatable prescription stops an older app");
        ops.Where(o => o.Type == nameof(DispensationChanged)).Should().HaveCount(2)
            .And.OnlyContain(o => o.SchemaVersion == 12);
        scope.Dispensations.All.Should().HaveCount(2).And.OnlyContain(d => d.PrescriptionId == id && d.MedicineId == medicine);
        (await scope.PrescriptionList.LoadAsync(default)).Should().ContainSingle()
            .Which.Should().Match<PrescriptionListItem>(i => i.DispensationsLeft == 10 && i.Status == PrescriptionStatus.ToCollect);
    }

    [Fact]
    public async Task A_single_prescription_is_still_written_with_version_7()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);

        await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 1, until: Today.AddDays(29)), default);

        scope.SyncOperations.All.Where(o => o.Type == nameof(PrescriptionChanged)).Should().ContainSingle()
            .Which.SchemaVersion.Should().Be(7);
    }

    [Fact]
    public async Task Editing_the_list_changes_and_removes_dispensations()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine,
            records: [new DispensationEntry(null, Today, 1), new DispensationEntry(null, Today.AddDays(30), 1)]), default);
        var first = scope.Dispensations.All.Single(d => d.CollectedOn == Today);

        await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, id: id,
            records: [new DispensationEntry(first.Id, Today.AddDays(1), 2)]), default);

        var left = scope.Dispensations.All.Should().ContainSingle().Subject;
        left.Id.Should().Be(first.Id);
        left.CollectedOn.Should().Be(Today.AddDays(1));
        left.Packages.Should().Be(2);
        var dispensationOps = scope.SyncOperations.All.Where(o => o.Type == nameof(DispensationChanged)).ToList();
        dispensationOps.Should().HaveCount(4, "two added, one changed, one removed");
        scope.SyncOperations.All.Count(o => o.Type == nameof(PrescriptionChanged)).Should().Be(1,
            "an unchanged prescription is not written again");
    }

    [Fact]
    public async Task An_invalid_list_is_refused_and_changes_nothing()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 2), default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, null, default);
        await scope.RecordDispensation.ExecuteAsync(id, Today.AddDays(30), null, default);

        var act = () => scope.RecordDispensation.ExecuteAsync(id, Today.AddDays(60), null, default);

        (await act.Should().ThrowAsync<InvalidPrescriptionException>())
            .Which.Error.Should().Be(PrescriptionError.TooManyDispensations);
        scope.Dispensations.All.Should().HaveCount(2);
        scope.Prescriptions.All.Single().Dispensations.Should().Be(2);
    }

    [Fact]
    public async Task Deleting_a_prescription_deletes_its_dispensations_with_their_tombstones()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine,
            records: [new DispensationEntry(null, Today, 1)]), default);

        await scope.DeletePrescription.ExecuteAsync(id, default);

        scope.Dispensations.All.Should().BeEmpty();
        var tombstone = scope.SyncOperations.All.Last(o => o.Type == nameof(DispensationChanged));
        ((DispensationChanged)OperationCodec.Deserialize(tombstone.Type, tombstone.SchemaVersion, tombstone.Payload))
            .Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_repeatable_prescription_is_offered_only_while_it_can_take_a_dispensation()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 2), default);

        (await scope.PrescriptionList.OpenForMedicineAsync(medicine, default)).Should().ContainSingle()
            .Which.DispensationsLeft.Should().Be(2);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        (await scope.PrescriptionList.OpenForMedicineAsync(medicine, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_low_stock_warning_points_to_the_dispensation_left()
    {
        var scope = new ApplicationTestScope(Now);
        // Stock 6, rate 2: 3 days, inside the threshold.
        var medicine = await AddMedicineAsync(scope, stock: 6m);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 3), default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);

        await scope.Monitor.RunAsync(default);

        scope.Windows.Targets.Should().ContainSingle()
            .Which.Should().Be(NotificationTarget.LowStock(medicine, hasRepeatablePrescription: true));
        var (_, body) = scope.Windows.Sent.Should().ContainSingle().Subject;
        body.Should().Contain("repeatable prescription: 2").And.NotContain("new prescription");
        var email = scope.Email.Sent.Should().ContainSingle().Subject;
        email.Body.Should().Contain("repeatable prescription: 2").And.NotContain("new prescription");
    }

    [Fact]
    public async Task Without_a_dispensation_left_the_warning_suggests_a_new_prescription()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope, stock: 6m);
        var id = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 2), default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        await scope.RecordDispensation.ExecuteAsync(id, Today, 1, default);

        await scope.Monitor.RunAsync(default);

        scope.Windows.Targets.Should().ContainSingle().Which.Should().Be(NotificationTarget.LowStock(medicine));
        scope.Windows.Sent.Single().Body.Should().Contain("new prescription");
        scope.Email.Sent.Single().Body.Should().Contain("new prescription");
    }

    [Fact]
    public async Task The_reminder_before_valid_until_needs_dispensations_left_and_counts_them()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope, channels: NotificationChannels.Windows);
        var open = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 3, until: Today), default);
        await scope.RecordDispensation.ExecuteAsync(open, Today, 1, default);
        var done = await scope.SavePrescription.ExecuteAsync(Repeatable(medicine, allowed: 2, until: Today), default);
        await scope.RecordDispensation.ExecuteAsync(done, Today, 1, default);
        await scope.RecordDispensation.ExecuteAsync(done, Today, 1, default);

        (await scope.PrescriptionReminders.RunAsync(Today, sendsEmail: false, default)).Should().Be(1);

        scope.PrescriptionReminderEvents.All.Should().ContainSingle().Which.PrescriptionId.Should().Be(open);
        scope.Windows.Sent.Should().ContainSingle().Which.Body.Should().Contain("lost: 2");
    }

    [Fact]
    public void The_toast_offers_to_open_the_prescription_instead_of_a_request()
    {
        var medicine = Guid.NewGuid();
        NotificationActionArguments.ButtonsFor(NotificationTarget.LowStock(medicine, hasRepeatablePrescription: true), "p")
            .Should().Equal(new NotificationAction(NotificationActionKind.OpenPrescriptions, "p", medicine));
        NotificationActionArguments.ButtonsFor(NotificationTarget.LowStock(medicine), "p")
            .Should().Equal(new NotificationAction(NotificationActionKind.RequestPrescription, "p", medicine));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_language_has_the_repeatable_texts(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        var medicine = new Medicine
        {
            Name = "Enalapril", Unit = "tablets", StartDate = Today, CreatedAt = Now, UpdatedAt = Now,
        };
        var notice = new RepeatablePrescriptionNotice(4, Until);

        var (_, toast) = NotificationTexts.BuildToast(medicine, 3, localization: loc, repeatable: notice);
        var (_, second) = NotificationTexts.BuildToast(medicine, 2, localization: loc,
            stage: NotificationCycle.SecondStage, repeatable: notice);
        var email = NotificationTexts.BuildEmail(medicine, 6m, 3, null, localization: loc, repeatable: notice);
        loc.Get("Notifications.Prescription.BodyRepeatable", "Enalapril", "x", 2);
        loc.Get("Notifications.Action.OpenPrescription");

        loc.FallbackHits.Should().BeEmpty();
        var date = Until.ToString("d", loc.CurrentCulture);
        toast.Should().Contain("4").And.Contain(date);
        second.Should().Contain("4");
        email.Body.Should().Contain("4").And.Contain(date);
    }
}

// Dispensations between two devices of a sync group.
public class DispensationSyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, 500, TimeSpan.Zero));
    private readonly Guid _medicine;

    public DispensationSyncTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        _medicine = _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 30m), default).GetAwaiter().GetResult();
    }

    private async Task ExchangeAsync()
    {
        await _b.ApplyRemote.ExecuteAsync(
            (await _a.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_b)).ToList(), default);
        await _a.ApplyRemote.ExecuteAsync(
            (await _b.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_a)).ToList(), default);
    }

    private static Guid Device(ApplicationTestScope scope) => scope.SyncSettingsStore.Load()!.DeviceId;

    private SavePrescriptionCommand Command(Guid? id = null)
        => new(id, _medicine, null, Today, "NRE-R", 1, PrescriptionRules.DefaultRepeatableValidUntil(Today), null, 12);

    [Fact]
    public async Task Concurrent_dispensations_both_survive_and_a_deletion_travels()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(), default);
        await ExchangeAsync();
        _b.Prescriptions.All.Should().ContainSingle().Which.Dispensations.Should().Be(12);

        await _a.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        await _b.RecordDispensation.ExecuteAsync(id, Today.AddDays(1), 1, default);
        await ExchangeAsync();

        _a.Dispensations.All.Select(d => d.Id).Should().BeEquivalentTo(_b.Dispensations.All.Select(d => d.Id))
            .And.HaveCount(2);

        var mine = _b.Dispensations.All.Single(d => d.CollectedOn == Today.AddDays(1));
        _b.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _b.SavePrescription.ExecuteAsync(Command(id) with
        {
            DispensationRecords = [new DispensationEntry(mine.Id, mine.CollectedOn, mine.Packages)],
        }, default);
        await ExchangeAsync();

        _a.Dispensations.All.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task A_dispensation_recorded_while_the_prescription_is_deleted_elsewhere_is_kept_unused()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(), default);
        await ExchangeAsync();

        await _a.DeletePrescription.ExecuteAsync(id, default);
        _b.Clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await _b.RecordDispensation.ExecuteAsync(id, Today, 1, default);
        await ExchangeAsync();

        foreach (var scope in new[] { _a, _b })
        {
            scope.Prescriptions.All.Should().BeEmpty();
            scope.Dispensations.All.Should().ContainSingle("both devices hold the same rows");
            (await scope.PrescriptionList.LoadAsync(default)).Should().BeEmpty();
        }
    }
}

using FluentAssertions;
using MedReminder.Application.Calendar;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Calendar;

// The dates of the calendar export (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.7) and the event of the low-stock email.
public class CalendarExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static CalendarExportQuery Query(ApplicationTestScope scope, JsonDictionaryLocalizationService? loc = null)
        => new(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions, scope.Slots, scope.Prescriptions,
            scope.Deadlines, scope.Clock, loc, scope.Dispensations);

    private static Task<Guid> AddMedicineAsync(ApplicationTestScope scope, string name, decimal stock,
        NotificationChannels channels = NotificationChannels.Windows)
        => scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: channels, EndDate: null,
            InitialQuantity: stock), default);

    private static async Task<ApplicationTestScope> SeedAsync()
    {
        var scope = new ApplicationTestScope(Now);
        var enalapril = await AddMedicineAsync(scope, "Enalapril", 30m);
        var inactive = await AddMedicineAsync(scope, "Old", 30m);
        (await scope.Medicines.GetAsync(inactive, default))!.IsActive = false;

        await scope.SavePrescription.ExecuteAsync(
            new SavePrescriptionCommand(null, enalapril, null, Today, "NRE", 1, Today.AddDays(10), null), default);
        await scope.SavePrescription.ExecuteAsync(
            new SavePrescriptionCommand(null, enalapril, null, Today, null, 1, Today.AddDays(10), Today), default);

        await scope.SaveDeadline.ExecuteAsync(new SaveDeadlineCommand(
            null, enalapril, DeadlineKind.TherapeuticPlan, "Plan AIFA", Today.AddDays(60), 14, 12,
            NotificationChannels.Windows, null), default);
        await scope.SaveDeadline.ExecuteAsync(new SaveDeadlineCommand(
            null, null, DeadlineKind.CheckUp, null, Today.AddDays(-1), 14, null, NotificationChannels.Windows, null), default);
        var done = await scope.SaveDeadline.ExecuteAsync(new SaveDeadlineCommand(
            null, null, DeadlineKind.CheckUp, null, Today.AddDays(40), 14, null, NotificationChannels.Windows, null), default);
        await scope.CompleteDeadline.ExecuteAsync(done, Today, default);
        return scope;
    }

    [Fact]
    public async Task The_export_holds_the_coming_dates_of_active_medicines_prescriptions_and_deadlines()
    {
        var scope = await SeedAsync();

        var events = await Query(scope).LoadAsync(includeNames: false, default);

        events.Select(e => e.Uid.Split('-')[0]).Should().Equal("prescription", "reorder", "runout", "deadline");
        var runOut = events.Single(e => e.Uid.StartsWith("runout-", StringComparison.Ordinal)).Date;
        events.Single(e => e.Uid.StartsWith("reorder-", StringComparison.Ordinal)).Date.Should().Be(runOut.AddDays(-7));
        events.Single(e => e.Uid.StartsWith("prescription-", StringComparison.Ordinal)).Date.Should().Be(Today.AddDays(10));
        events.Single(e => e.Uid.StartsWith("deadline-", StringComparison.Ordinal)).Date.Should().Be(Today.AddDays(60));
        events.Should().BeInAscendingOrder(e => e.Date);
    }

    [Fact]
    public async Task Titles_are_generic_unless_the_names_are_asked_for()
    {
        var scope = await SeedAsync();

        var generic = await Query(scope).WriteAsync(includeNames: false, default);
        var named = await Query(scope).WriteAsync(includeNames: true, default);

        generic.Should().NotContain("Enalapril").And.NotContain("Plan AIFA").And.NotContain("NRE");
        named.Should().Contain("Enalapril").And.Contain("Plan AIFA").And.NotContain("NRE");
    }

    [Fact]
    public async Task The_same_subject_keeps_its_uid_between_exports()
    {
        var scope = await SeedAsync();

        var first = await Query(scope).LoadAsync(includeNames: false, default);
        scope.Clock.AdvanceBy(TimeSpan.FromDays(1));
        var second = await Query(scope).LoadAsync(includeNames: true, default);

        second.Select(e => e.Uid).Should().BeEquivalentTo(first.Select(e => e.Uid));
    }

    [Fact]
    public async Task A_reorder_day_already_past_is_left_out()
    {
        var scope = new ApplicationTestScope(Now);
        await AddMedicineAsync(scope, "Enalapril", 3m);

        var events = await Query(scope).LoadAsync(includeNames: false, default);

        events.Should().ContainSingle().Which.Uid.Should().StartWith("runout-");
    }

    [Fact]
    public async Task The_low_stock_email_carries_the_run_out_date_without_the_name()
    {
        var scope = new ApplicationTestScope(Now);
        await AddMedicineAsync(scope, "Enalapril", 3m, NotificationChannels.Email);

        await scope.Monitor.RunAsync(default);

        var calendarEvent = scope.Email.Sent.Should().ContainSingle().Which.CalendarEvent;
        calendarEvent.Should().NotBeNull();
        calendarEvent!.Summary.Should().NotContain("Enalapril");
        calendarEvent.Date.Should().BeOnOrAfter(Today);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public async Task Every_language_has_the_calendar_texts(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        var scope = await SeedAsync();

        await Query(scope, loc).WriteAsync(includeNames: false, default);
        await Query(scope, loc).WriteAsync(includeNames: true, default);

        loc.FallbackHits.Should().BeEmpty();
    }
}

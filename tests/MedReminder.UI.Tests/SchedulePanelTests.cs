using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using MedReminder.UI.Controls;
using Forms = MedReminder.UI.Forms;
using Xunit;

namespace MedReminder.UI.Tests;

// Exercises SchedulePanel with real WinForms handles: the panel is
// hosted inside a realized Form, seeded with ApplySchedule, then read
// back with TryBuildSchedule. This is the round-trip the Change /
// Edit dialogs rely on to re-open an existing advanced regime.
[Collection("STA")]
public sealed class SchedulePanelTests
{
    private sealed class KeyEchoLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";
        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
        public string Get(string key, params object?[] args)
            => args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, key, args) : key;
        public string GetIn(string languageCode, string key, params object?[] args)
            => Get(key, args);
    }

    // Runs the body on a dedicated STA thread with a realized host form,
    // mirroring how the dialog shows the panel.
    private static void OnRealizedPanel(Action<SchedulePanel> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form();
                var panel = new SchedulePanel(new KeyEchoLocalization());
                form.Controls.Add(panel.Root);
                // Force handle creation for the whole control tree.
                _ = form.Handle;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                body(panel);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Round_trips_a_stepped_tapering_schedule()
    {
        var seed = new SteppedTaperingSchedule(
            new[]
            {
                new TaperStage(4m, 7),
                new TaperStage(2m, 7),
                new TaperStage(1m, 14),
            },
            maintainLastDose: true);

        OnRealizedPanel(panel =>
        {
            panel.ApplySchedule(seed);
            var rebuilt = panel.TryBuildSchedule(out var error);
            error.Should().BeNull();
            rebuilt.Should().Be(seed);
        });
    }

    [Fact]
    public void Round_trips_a_linear_tapering_schedule()
    {
        var seed = new TaperingSchedule(4m, 0.5m, 0.5m, 7);
        OnRealizedPanel(panel =>
        {
            panel.ApplySchedule(seed);
            var rebuilt = panel.TryBuildSchedule(out var error);
            error.Should().BeNull();
            rebuilt.Should().Be(seed);
        });
    }

    [Fact]
    public void Round_trips_a_cyclic_schedule()
    {
        var seed = new CyclicSchedule(21, 7, 2m);
        OnRealizedPanel(panel =>
        {
            panel.ApplySchedule(seed);
            var rebuilt = panel.TryBuildSchedule(out var error);
            error.Should().BeNull();
            rebuilt.Should().Be(seed);
        });
    }

    [Fact]
    public void Round_trips_a_weekly_schedule()
    {
        var seed = new WeeklySchedule(new[] { 1m, 0.5m, 1m, 0.5m, 1m, 0m, 0m });
        OnRealizedPanel(panel =>
        {
            panel.ApplySchedule(seed);
            var rebuilt = panel.TryBuildSchedule(out var error);
            error.Should().BeNull();
            rebuilt.Should().BeOfType<WeeklySchedule>()
                .Which.QuantitiesByDayOfWeek.Should().Equal(seed.QuantitiesByDayOfWeek);
        });
    }

    // End-to-end through the real dialog: construct it with a current
    // stepped schedule, drive it through OnLoad the way ShowDialog
    // would, then read the dialog's own SchedulePanel back.
    [Fact]
    public void Change_schedule_dialog_reopens_on_the_saved_stepped_schedule()
    {
        var seed = new SteppedTaperingSchedule(
            new[]
            {
                new TaperStage(4m, 7),
                new TaperStage(2m, 7),
                new TaperStage(1m, 14),
            },
            maintainLastDose: true);

        Schedule? rebuilt = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new Forms.ChangeScheduleDialog(
                    "Prednisone", 4m, 1, new DateOnly(2026, 1, 1),
                    new KeyEchoLocalization(), seed);
                _ = dialog.Handle;
                dialog.Show();
                System.Windows.Forms.Application.DoEvents();

                var panelField = typeof(Forms.ChangeScheduleDialog)
                    .GetField("_schedulePanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var panel = (SchedulePanel)panelField!.GetValue(dialog)!;
                rebuilt = panel.TryBuildSchedule(out _);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;

        rebuilt.Should().Be(seed);
    }
}

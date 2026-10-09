using MedReminder.Application.Export;
using MedReminder.Infrastructure;
using MedReminder.MobileSpikes.Spikes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace MedReminder.MobileSpikes;

// One screen: run the spikes, pick a desktop archive for S1b, share the
// JSON report. Every spike runs off the UI thread.
public sealed class MainPage : ContentPage
{
    private readonly SpikeReport _report = new();
    private readonly IArchiveCipher _cipher;
    private readonly IArchiveReader _reader;
    private readonly Label _log = new() { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Entry _passphrase = new() { IsPassword = true, Placeholder = "Passphrase of the desktop archive" };
    private readonly List<Button> _buttons = [];
    private readonly Entry _smtpHost = new() { Placeholder = "SMTP host, e.g. smtp.gmail.com" };
    private readonly Entry _smtpPort = new() { Placeholder = "Port", Text = "587", Keyboard = Keyboard.Numeric };
    private readonly Switch _smtpStartTls = new() { IsToggled = true };
    private readonly Entry _smtpUser = new() { Placeholder = "SMTP user name (usually the address)", Keyboard = Keyboard.Email };
    private readonly Entry _smtpPassword = new() { Placeholder = "SMTP password or app password", IsPassword = true };
    private readonly Entry _smtpRecipient = new() { Placeholder = "Recipient of the test message", Keyboard = Keyboard.Email };
    private readonly Picker _scenario = new() { Title = "S5 scenario", ItemsSource = S5Alarms.Scenarios, SelectedIndex = 0 };

    public MainPage()
    {
        Title = "MedReminder B.1 Android spikes";

        // The production registrations; only the stateless cipher and
        // reader are resolved here, the database is never opened.
        var services = new ServiceCollection();
        services.AddMedReminderPortableInfrastructure(Path.Combine(FileSystem.CacheDirectory, "unused", "medreminder.db"));
        var provider = services.BuildServiceProvider();
        _cipher = provider.GetRequiredService<IArchiveCipher>();
        _reader = provider.GetRequiredService<IArchiveReader>();

        _report.Added += c => MainThread.BeginInvokeOnMainThread(() => _log.Text += SpikeReport.Format(c) + Environment.NewLine);

        var build = string.Join(", ", EnvironmentInfo.Collect()
            .Where(kv => kv.Key.StartsWith("build.", StringComparison.Ordinal))
            .Select(kv => $"{kv.Key["build.".Length..]}={kv.Value}"));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children =
                {
                    new Label { Text = build, FontSize = 12 },
                    NewButton("Run S1, S2, S3", RunAllAsync),
                    NewButton("S1 AES-GCM", () => RunAsync(() => S1AesGcm.Run(_report, _cipher))),
                    NewButton("S2 Argon2id cost", () => RunAsync(() => S2Argon2Cost.Run(_report, _cipher))),
                    NewButton("S3 EF Core SQLite", () => RunAsync(() => S3EfCoreSqlite.RunAsync(_report, S3Directory, CancellationToken.None))),
                    _passphrase,
                    NewButton("S1b Decrypt a desktop archive…", DecryptDesktopArchiveAsync),
                    new Label { Text = "S5 and S8 run over hours: see the README for the scenarios.", FontSize = 12 },
                    _scenario,
                    NewButton("S5 request notification permission", RequestNotificationPermissionAsync),
                    NewButton("S5 open exact alarm settings", OpenExactAlarmSettingsAsync),
                    NewButton("S5 schedule short battery (2-20 min)", () => ScheduleS5Async(longBattery: false)),
                    NewButton("S5 schedule long battery (30 min-8 h)", () => ScheduleS5Async(longBattery: true)),
                    NewButton("S8 start periodic work (15 min)", () => RunOnUiAsync("S8", "Start", S8Background.Start)),
                    NewButton("S8 stop periodic work", () => RunOnUiAsync("S8", "Stop", () =>
                    {
                        S8Background.Stop();
                        return "stopped";
                    })),
                    NewButton("Collect S5 and S8 results", () => RunAsync(() =>
                    {
                        S5Alarms.Collect(_report);
                        S8Background.Collect(_report);
                    })),
                    NewButton("Clear S5 and S8 data", ClearS5S8Async),
                    new Label { Text = "S10 MailKit: use a test mailbox; nothing typed here is saved or reported.", FontSize = 12 },
                    _smtpHost,
                    _smtpPort,
                    new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "STARTTLS", VerticalOptions = LayoutOptions.Center }, _smtpStartTls } },
                    _smtpUser,
                    _smtpPassword,
                    _smtpRecipient,
                    NewButton("S10 build message (offline)", () => RunAsync(() => S10MailKit.BuildMessage(_report))),
                    NewButton("S10 test connection", () => RunS10Async(send: false)),
                    NewButton("S10 send test email", () => RunS10Async(send: true)),
                    NewButton("Share report", ShareReportAsync),
                    NewButton("Clear", () =>
                    {
                        _report.Clear();
                        _log.Text = string.Empty;
                        return Task.CompletedTask;
                    }),
                    _log,
                },
            },
        };
    }

    private static string S3Directory => Path.Combine(FileSystem.AppDataDirectory, "s3");

    private Button NewButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) => await action();
        _buttons.Add(button);
        return button;
    }

    private Task RunAllAsync() => RunAsync(async () =>
    {
        S1AesGcm.Run(_report, _cipher);
        S2Argon2Cost.Run(_report, _cipher);
        await S3EfCoreSqlite.RunAsync(_report, S3Directory, CancellationToken.None);
    });

    private Task RunAsync(Action body) => RunAsync(() =>
    {
        body();
        return Task.CompletedTask;
    });

    private async Task RunAsync(Func<Task> body)
    {
        SetBusy(true);
        try
        {
            await Task.Run(body);
        }
        catch (Exception ex)
        {
            _report.Add("app", "Unhandled", Outcome.Fail, SpikeRunner.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DecryptDesktopArchiveAsync()
    {
        if (string.IsNullOrEmpty(_passphrase.Text))
        {
            await DisplayAlertAsync("S1b", "Enter the passphrase of the archive first.", "OK");
            return;
        }

        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Select a .mrz archive" });
        if (file is null)
        {
            return;
        }

        byte[] archive;
        await using (var source = await file.OpenReadAsync())
        using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer);
            archive = buffer.ToArray();
        }

        var passphrase = _passphrase.Text.ToCharArray();
        _passphrase.Text = string.Empty;
        try
        {
            await RunAsync(() => S1DesktopArchive.Run(_report, _reader, archive, passphrase));
        }
        finally
        {
            Array.Clear(passphrase);
        }
    }

    private async Task RunS10Async(bool send)
    {
        if (string.IsNullOrWhiteSpace(_smtpHost.Text)
            || !int.TryParse(_smtpPort.Text, out var port)
            || (send && string.IsNullOrWhiteSpace(_smtpRecipient.Text)))
        {
            await DisplayAlertAsync("S10", "Enter host, port and, to send, the recipient.", "OK");
            return;
        }
        var input = new SmtpInput(_smtpHost.Text, port, _smtpStartTls.IsToggled,
            _smtpUser.Text ?? string.Empty, _smtpPassword.Text ?? string.Empty, _smtpRecipient.Text ?? string.Empty);
        await RunAsync(() => S10MailKit.RunAsync(_report, input, send, CancellationToken.None));
    }

    private Task ScheduleS5Async(bool longBattery)
    {
        var scenario = _scenario.SelectedItem as string ?? S5Alarms.Scenarios[0];
        return RunOnUiAsync("S5", $"Schedule {(longBattery ? "long" : "short")} battery, {scenario}",
            () => S5Alarms.ScheduleBattery(scenario, longBattery));
    }

    // Alarm and WorkManager calls are quick; run them on the UI thread and
    // record the outcome.
    private Task RunOnUiAsync(string spike, string check, Func<string> action)
    {
        try
        {
            _report.Add(spike, check, Outcome.Measured, action());
        }
        catch (Exception ex)
        {
            _report.Add(spike, check, Outcome.Fail, SpikeRunner.Describe(ex));
        }
        return Task.CompletedTask;
    }

    private async Task RequestNotificationPermissionAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        _report.Add("S5", "POST_NOTIFICATIONS", status == PermissionStatus.Granted ? Outcome.Pass : Outcome.Fail, status.ToString());
    }

    private Task OpenExactAlarmSettingsAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            _report.Add("S5", "Exact alarm settings", Outcome.Skipped, "not needed before Android 12");
            return Task.CompletedTask;
        }
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionRequestScheduleExactAlarm,
            global::Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
        return Task.CompletedTask;
    }

    private async Task ClearS5S8Async()
    {
        if (!await DisplayAlertAsync("Clear", "Cancel the pending S5 alarms and the S8 work, and delete their logs?", "Clear", "Keep"))
        {
            return;
        }
        S5Alarms.Clear();
        S8Background.Clear();
        _report.Add("app", "Clear S5 and S8 data", Outcome.Measured, "done");
    }

    private async Task ShareReportAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"spike-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllBytesAsync(path, _report.ToJson(EnvironmentInfo.Collect()));
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "MedReminder B.1 spike report",
            File = new ShareFile(path),
        });
    }

    private void SetBusy(bool busy)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var b in _buttons)
            {
                b.IsEnabled = !busy;
            }
        });
}

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

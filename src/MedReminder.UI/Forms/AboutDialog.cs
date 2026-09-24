using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UpdateChecking;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// About dialog: application identity, author, license, links to the
// public repository, disclaimer, embedded reference-catalogue
// attributions, and a "Check for updates now" button that hits the
// same GitHub Releases endpoint as the passive startup check.
//
// Replaces the plain MessageBox that previously stood in for the
// About window in MainForm — the new dialog is fully localizable
// and clickable (email, repository, license).
internal sealed class AboutDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly IUpdateChecker _updateChecker;
    private readonly ILogger<AboutDialog> _log;

    private Button _checkUpdatesButton = null!;
    private Label _updateStatusLabel = null!;

    private const string AuthorHandle = "@vger70";
    private const string AuthorEmail = "info@medreminder26.org";
    private const string SiteUrl = "https://www.medreminder26.org";

    private const string RepositoryUrl = "https://github.com/vger70/MedReminder";
    private const string LicenseUrl = "https://github.com/vger70/MedReminder/blob/main/LICENSE";
    private const string IssuesUrl = "https://github.com/vger70/MedReminder/issues";

    public AboutDialog(
        ILocalizationService localization,
        IUpdateChecker updateChecker,
        ILogger<AboutDialog> log)
    {
        _loc = localization;
        _updateChecker = updateChecker;
        _log = log;

        Text = _loc.Get("Ui.AboutDialog.Title");
        // Sized to fit the longest label the dialog carries (the
        // GitHub source-code URL) without clipping in any of the
        // shipped languages.
        Width = 680;
        Height = 560;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.75F);

        BuildLayout();
    }

    private void BuildLayout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";

        var appIcon = new PictureBox
        {
            Image = (AppIcon.Default ?? SystemIcons.Application).ToBitmap(),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Width = 64,
            Height = 64,
            Dock = DockStyle.Left,
        };

        var appName = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.AppName"),
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
        };

        var versionLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.Version", version),
            ForeColor = Color.DarkGray,
        };

        var authorLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.Author", AuthorHandle),
        };

        var emailLink = new LinkLabel
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.Email", AuthorEmail),
            LinkArea = ComputeLinkArea(_loc.Get("Ui.AboutDialog.Email", AuthorEmail), AuthorEmail),
        };
        emailLink.LinkClicked += (_, _) => OpenExternal("mailto:" + AuthorEmail);

        var repoLink = new LinkLabel
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.Repository", RepositoryUrl),
            LinkArea = ComputeLinkArea(_loc.Get("Ui.AboutDialog.Repository", RepositoryUrl), RepositoryUrl),
        };
        repoLink.LinkClicked += (_, _) => OpenExternal(RepositoryUrl);

        var siteLink = new LinkLabel
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.Site", SiteUrl),
            LinkArea = ComputeLinkArea(_loc.Get("Ui.AboutDialog.Site", SiteUrl), SiteUrl),
        };
        siteLink.LinkClicked += (_, _) => OpenExternal(SiteUrl);

        var issuesLink = new LinkLabel
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.ReportIssue"),
        };
        issuesLink.LinkClicked += (_, _) => OpenExternal(IssuesUrl);

        var licenseLink = new LinkLabel
        {
            AutoSize = true,
            Text = _loc.Get("Ui.AboutDialog.License"),
        };
        licenseLink.LinkClicked += (_, _) => OpenExternal(LicenseUrl);

        var disclaimer = new Label
        {
            AutoSize = true,
            // Matches the usable width inside the body panel
            // (dialog width - 2 * body padding). Kept slightly under
            // that value to leave room for the vertical scrollbar
            // WinForms reserves on high-DPI displays.
            MaximumSize = new Size(620, 0),
            ForeColor = Color.DimGray,
            Text = _loc.Get("Ui.AboutDialog.Disclaimer"),
        };

        // Data-source attributions match those previously shown in
        // the legacy MessageBox About: one line per reference-
        // catalogue snapshot embedded in the build. Each label
        // wraps within the body's usable width — without the
        // MaximumSize they render on a single very long line and
        // are clipped by the dialog's right edge.
        var sourcesHeader = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = _loc.Get("Ui.AboutDialog.DataSources"),
        };
        var sourceLabelMaxSize = new Size(620, 0);
        var aifa = new Label
        {
            AutoSize = true,
            MaximumSize = sourceLabelMaxSize,
            Text = _loc.Get("about.dataSources.aifa"),
        };
        var ema = new Label
        {
            AutoSize = true,
            MaximumSize = sourceLabelMaxSize,
            Text = _loc.Get("about.dataSources.emaArticle57"),
        };
        var aemps = new Label
        {
            AutoSize = true,
            MaximumSize = sourceLabelMaxSize,
            Text = _loc.Get("about.dataSources.aemps"),
        };
        var bdpm = new Label
        {
            AutoSize = true,
            MaximumSize = sourceLabelMaxSize,
            Text = _loc.Get("about.dataSources.bdpm"),
        };

        _updateStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(480, 0),
            Text = string.Empty,
        };

        _checkUpdatesButton = new Button
        {
            Text = _loc.Get("Ui.AboutDialog.CheckForUpdates"),
            AutoSize = true,
            Height = 28,
        };
        _checkUpdatesButton.Click += async (_, _) => await RunUpdateCheckAsync();

        var okButton = new Button
        {
            Text = _loc.Get("Common.Close"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Height = 28,
        };

        var textStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0),
        };
        textStack.Controls.Add(appName);
        textStack.Controls.Add(versionLabel);
        textStack.Controls.Add(BuildSpacer(6));
        textStack.Controls.Add(authorLabel);
        textStack.Controls.Add(emailLink);
        textStack.Controls.Add(siteLink);
        textStack.Controls.Add(repoLink);
        textStack.Controls.Add(issuesLink);
        textStack.Controls.Add(licenseLink);

        var header = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(16, 16, 16, 8),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(appIcon, 0, 0);
        header.Controls.Add(textStack, 1, 0);

        // Body panel: Dock=Fill inside a TableLayoutPanel row is
        // reliable only when AutoSize is OFF — with AutoSize=true
        // the FlowLayoutPanel grew past its cell and pushed the
        // button row off the visible area of the dialog.
        // AutoScroll is on as a safety net for very tall
        // localisations, but the default localisations fit without
        // scrollbars in the sized dialog.
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 8, 16, 8),
        };
        body.Controls.Add(disclaimer);
        body.Controls.Add(BuildSpacer(8));
        body.Controls.Add(sourcesHeader);
        body.Controls.Add(aifa);
        body.Controls.Add(ema);
        body.Controls.Add(aemps);
        body.Controls.Add(bdpm);

        var updatePanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 4, 16, 4),
        };
        updatePanel.Controls.Add(_checkUpdatesButton);
        updatePanel.Controls.Add(_updateStatusLabel);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(okButton);

        // Root: 4 rows — header (auto), body (fill), update row
        // (auto, ~44 px), close button row (auto, ~44 px). Every
        // AutoSize row is sized against a MinimumSize so the button
        // rows never collapse to zero — a lesson from the previous
        // layout where the buttons vanished from view.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(updatePanel, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = okButton;
        CancelButton = okButton;
    }

    private static Control BuildSpacer(int height) => new Panel
    {
        Height = height,
        Width = 1,
    };

    // Compute a LinkLabel LinkArea that covers the token substring.
    // Falls back to the whole label when the token is not found —
    // this can happen when a localized version drops the placeholder.
    private static LinkArea ComputeLinkArea(string full, string token)
    {
        var index = full.IndexOf(token, StringComparison.Ordinal);
        return index >= 0
            ? new LinkArea(index, token.Length)
            : new LinkArea(0, full.Length);
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort: nothing sensible to show if the OS refuses
            // to open the URL. The clickable label handles it silently.
        }
    }

    private async Task RunUpdateCheckAsync()
    {
        _checkUpdatesButton.Enabled = false;
        _updateStatusLabel.ForeColor = Color.DimGray;
        _updateStatusLabel.Text = _loc.Get("Ui.AboutDialog.CheckingForUpdates");

        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _updateChecker.CheckAsync(current, cts.Token);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    _updateStatusLabel.ForeColor = Color.DarkGreen;
                    _updateStatusLabel.Text = _loc.Get("Ui.UpdateCheck.UpToDate");
                    break;

                case UpdateCheckStatus.NewVersionAvailable:
                    _updateStatusLabel.ForeColor = Color.DarkOrange;
                    _updateStatusLabel.Text = _loc.Get(
                        "Ui.UpdateCheck.NewVersion", result.LatestTag ?? "?");
                    UpdateCheckPrompt.Show(this, _loc, result);
                    break;

                case UpdateCheckStatus.Error:
                default:
                    _updateStatusLabel.ForeColor = Color.Firebrick;
                    _updateStatusLabel.Text = _loc.Get(
                        "Ui.UpdateCheck.Error", result.ErrorMessage ?? string.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check from the About dialog failed unexpectedly.");
            _updateStatusLabel.ForeColor = Color.Firebrick;
            _updateStatusLabel.Text = _loc.Get("Ui.UpdateCheck.Error", ex.Message);
        }
        finally
        {
            _checkUpdatesButton.Enabled = true;
        }
    }
}

// Shared prompt that surfaces a new-version result — reused by the
// About dialog, the manual "Check for updates now" menu entry, and
// the passive startup check.
internal static class UpdateCheckPrompt
{
    public static void Show(IWin32Window owner, ILocalizationService loc, UpdateCheckResult result)
    {
        if (result.Status != UpdateCheckStatus.NewVersionAvailable) return;

        var body = loc.Get("Ui.UpdateCheck.NewVersionPrompt",
            result.LatestTag ?? "?",
            result.ReleaseUrl ?? string.Empty);
        var response = MessageBox.Show(
            owner, body,
            loc.Get("Ui.UpdateCheck.NewVersionTitle"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);

        if (response == DialogResult.Yes && !string.IsNullOrEmpty(result.ReleaseUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = result.ReleaseUrl,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Best-effort; the URL is also visible in the message body.
            }
        }
    }
}

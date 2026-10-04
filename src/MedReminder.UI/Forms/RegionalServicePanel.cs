using MedReminder.Application.Abstractions;
using MedReminder.Application.Prescriptions;
using MedReminder.Domain.Prescriptions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// "Regional prescription service" (docs/prompt/
// PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.3), shown in Therapy →
// Prescriptions… and in the prescription request draft: a button with
// "Open in browser", "Open on phone" and "Change region…", and a line
// naming the service and how to sign in. The first click without a region
// asks for it. Hidden when the reference country is not Italy. MedReminder
// opens the service and shows its app link; it never signs in and never
// reads it. The data and the writes come from the caller.
internal sealed class RegionalServicePanel : FlowLayoutPanel
{
    private readonly ILocalizationService _loc;
    private readonly RegionalServiceActions _actions;
    private readonly Button _button;
    private readonly Label _info;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _openBrowser;
    private readonly ToolStripMenuItem _openPhone;
    private RegionalServiceForProfile _state = new(RegionalServiceAvailability.NotItaly, string.Empty, null);

    public RegionalServicePanel(RegionalServiceActions actions, ILocalizationService localization)
    {
        _loc = localization;
        _actions = actions;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Bottom;
        Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0);

        _button = DialogLayout.Button(_loc.Get("Ui.RegionalService.Button") + " ▾");
        _button.Margin = new Padding(0, 0, 0, UiTheme.Space.XS);
        _info = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            // Wraps to the panel width (FitInfo), whatever the dialog width.
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 0, 0, UiTheme.Space.XS),
        };

        _menu = new ContextMenuStrip();
        _openBrowser = new ToolStripMenuItem(_loc.Get("Ui.RegionalService.OpenBrowser"));
        _openPhone = new ToolStripMenuItem(_loc.Get("Ui.RegionalService.OpenPhone"));
        var changeRegion = new ToolStripMenuItem(_loc.Get("Ui.RegionalService.ChangeRegion"));
        _openBrowser.Click += (_, _) => OpenInBrowser();
        _openPhone.Click += (_, _) => OpenOnPhone();
        changeRegion.Click += async (_, _) => await ChooseRegionAsync();
        _menu.Items.AddRange([_openBrowser, _openPhone, new ToolStripSeparator(), changeRegion]);
        _button.Click += async (_, _) => await ClickAsync();

        Controls.Add(_button);
        Controls.Add(_info);
        SizeChanged += (_, _) => FitInfo();
        Disposed += (_, _) => _menu.Dispose();
        Reload();
    }

    // The info line wraps to the width the dock gives the panel, so a
    // narrow window or a larger text size does not cut it.
    private void FitInfo()
    {
        var width = Math.Max(200, ClientSize.Width - Padding.Horizontal - _info.Margin.Horizontal);
        if (_info.MaximumSize.Width != width) _info.MaximumSize = new Size(width, 0);
    }

    // Reads the profile's region and the list again.
    public void Reload()
    {
        try
        {
            _state = _actions.Load();
        }
        catch (Exception)
        {
            // The prescription windows stay usable without the button.
            _state = new(RegionalServiceAvailability.NotItaly, string.Empty, null);
        }
        Visible = _state.Availability != RegionalServiceAvailability.NotItaly;
        _info.Text = InfoText();
        _openBrowser.Enabled = _state.Service is not null;
        _openPhone.Enabled = _state.Service is not null;
    }

    private string InfoText()
    {
        switch (_state.Availability)
        {
            case RegionalServiceAvailability.NoRegion:
                return _loc.Get("Ui.RegionalService.Info.NoRegion");
            case RegionalServiceAvailability.NoEntry:
                return _loc.Get("Ui.RegionalService.Info.NoEntry", RegionNames.Of(_loc, _state.Region));
            case RegionalServiceAvailability.Found when _state.Service is { } service:
                var parts = new List<string>
                {
                    _loc.Get("Ui.RegionalService.Info.Service", service.Service, SignInText(service.SignIn)),
                    _loc.Get("Ui.RegionalService.Info.Private"),
                };
                if (service.FamilyDelegation) parts.Add(_loc.Get("Ui.RegionalService.Info.Delegation"));
                if (!service.ShowsPrescriptions) parts.Add(_loc.Get("Ui.RegionalService.Info.NotVerified"));
                return string.Join(" ", parts);
            default:
                return string.Empty;
        }
    }

    // "SPID, CIE or TS-CNS".
    private string SignInText(IReadOnlyList<SignInMethod> methods)
    {
        var names = methods.Select(m => m switch
        {
            SignInMethod.Spid => "SPID",
            SignInMethod.Cie => "CIE",
            _ => "TS-CNS",
        }).ToList();
        return names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + _loc.Get("Ui.RegionalService.Info.Or") + names[^1];
    }

    private async Task ClickAsync()
    {
        Reload();
        if (_state.Availability == RegionalServiceAvailability.NoRegion && !await ChooseRegionAsync()) return;
        if (_state.Availability == RegionalServiceAvailability.NoEntry)
        {
            UiMessageBox.Show(FindForm(), _loc.Get("Ui.RegionalService.NoEntry", RegionNames.Of(_loc, _state.Region)),
                _loc.Get("Ui.RegionalService.Button"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        _menu.Show(_button, new Point(0, _button.Height));
    }

    // True when a region was saved.
    private async Task<bool> ChooseRegionAsync()
    {
        using var dialog = new RegionPickerDialog(_loc, _state.Region);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.SelectedRegion is not { } region) return false;
        try
        {
            await _actions.SaveRegion(region);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(FindForm(), ex.Message, _loc.Get("Common.Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (IsDisposed) return false;
        Reload();
        return true;
    }

    private void OpenInBrowser()
    {
        if (_state.Service is not { } service) return;
        if (!_actions.Open(service.RegionCode, service.WebUrl.AbsoluteUri))
        {
            UiMessageBox.Show(FindForm(), _loc.Get("Ui.RegionalService.OpenFailed"),
                _loc.Get("Common.Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenOnPhone()
    {
        if (_state.Service is not { } service) return;
        using var dialog = new RegionalServiceQrDialog(_loc, service);
        dialog.ShowDialog(FindForm());
    }
}

// What the panel asks of its caller (MainForm), each in its own scope.
// Open: region code and URL, through RegionalServiceLinkLauncher.
internal sealed record RegionalServiceActions(
    Func<RegionalServiceForProfile> Load,
    Func<string, Task> SaveRegion,
    Func<string, string, bool> Open);

// The localized names of the Italian regions ("Regions.IT.<code>").
internal static class RegionNames
{
    public static string Of(ILocalizationService loc, string code) => loc.Get("Regions.IT." + code);

    // Every region and autonomous province, sorted by its name.
    public static IReadOnlyList<(string Code, string Name)> All(ILocalizationService loc)
        => ItalianRegions.Codes
            .Select(code => (code, Of(loc, code)))
            .OrderBy(r => r.Item2, StringComparer.Create(loc.CurrentCulture, ignoreCase: true))
            .ToList();
}

// Chooses the profile's region among the 21 regions and autonomous
// provinces. Saved by the caller.
internal sealed class RegionPickerDialog : MedReminderFormBase
{
    private readonly ListBox _regions;

    public RegionPickerDialog(ILocalizationService localization, string? current)
    {
        Text = localization.Get("Ui.RegionalService.Picker.Title");
        Width = 420;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.S),
            Text = localization.Get("Ui.RegionalService.Picker.Hint"),
        };
        DialogLayout.GrowWithText(hint);

        _regions = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var (code, name) in RegionNames.All(localization))
        {
            _regions.Items.Add(new RegionOption(code, name));
            if (code == current) _regions.SelectedIndex = _regions.Items.Count - 1;
        }
        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UiTheme.Space.L, 0, UiTheme.Space.L, 0) };
        list.Controls.Add(_regions);

        var ok = DialogLayout.Button(localization.Get("Common.Save"), DialogResult.OK);
        var cancel = DialogLayout.Button(localization.Get("Common.Cancel"), DialogResult.Cancel);
        ok.Enabled = _regions.SelectedIndex >= 0;
        _regions.SelectedIndexChanged += (_, _) => ok.Enabled = _regions.SelectedIndex >= 0;
        _regions.DoubleClick += (_, _) =>
        {
            if (_regions.SelectedIndex < 0) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        var buttons = DialogLayout.ButtonBar(this, ok, cancel);

        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(buttons);
    }

    public string? SelectedRegion => (_regions.SelectedItem as RegionOption)?.Code;

    private sealed record RegionOption(string Code, string Name)
    {
        public override string ToString() => Name;
    }
}

// "Open on phone": the QR code of the regional app (iPhone or Android)
// or of the web page, for the phone camera. Only links of the list.
internal sealed class RegionalServiceQrDialog : MedReminderFormBase
{
    private readonly PictureBox _qr;

    public RegionalServiceQrDialog(ILocalizationService localization, RegionalHealthService service)
    {
        Text = localization.Get("Ui.RegionalService.Qr.Title", service.Service);
        Width = 460;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        var targets = new List<(string Key, Uri Url)>();
        if (service.IosAppUrl is { } ios) targets.Add(("Ui.RegionalService.Qr.Iphone", ios));
        if (service.AndroidAppUrl is { } android) targets.Add(("Ui.RegionalService.Qr.Android", android));
        targets.Add(("Ui.RegionalService.Qr.Web", service.WebUrl));

        _qr = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 300,
            Height = 300,
            BackColor = Color.White,
        };
        // A choice only when there is more than the web page: no control is
        // built that the form would not own and dispose.
        Control choices = new Panel { Height = 0, Width = 0 };
        RadioButton? first = null;
        if (targets.Count > 1)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            foreach (var (key, url) in targets)
            {
                var choice = new RadioButton { AutoSize = true, Text = localization.Get(key) };
                choice.CheckedChanged += (_, _) =>
                {
                    if (choice.Checked) ShowQr(url);
                };
                row.Controls.Add(choice);
                first ??= choice;
            }
            choices = row;
        }
        var scan = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Text = localization.Get("Ui.RegionalService.Qr.Scan"),
        };
        var privacy = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = UiColors.Hint,
            Text = localization.Get("Ui.RegionalService.Info.Private"),
        };

        var close = DialogLayout.Button(localization.Get("Common.Close"), DialogResult.OK);
        var buttons = DialogLayout.ButtonBar(this, close, close);
        Controls.Add(DialogLayout.Stack(choices, _qr, scan, privacy));
        Controls.Add(buttons);
        FormClosed += (_, _) => _qr.Image?.Dispose();

        if (first is not null) first.Checked = true;
        else ShowQr(service.WebUrl);
    }

    private void ShowQr(Uri url)
    {
        var previous = _qr.Image;
        _qr.Image = QrImage.Render(url.AbsoluteUri);
        previous?.Dispose();
    }
}

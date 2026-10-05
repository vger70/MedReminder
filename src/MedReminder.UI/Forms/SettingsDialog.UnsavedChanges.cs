using System.Globalization;

namespace MedReminder.UI.Forms;

// Unsaved changes: every section with a Save button keeps the state of
// its fields as last loaded or saved. Closing the dialog with a section
// whose fields differ asks whether to save first, so an edit is no
// longer lost silently when the user forgets the section's Save button.
// Startup and the PIN apply at once and are not tracked.
internal sealed partial class SettingsDialog
{
    private const string GeneralSection = "Ui.SettingsDialog.Tab.General";
    private const string EmailSection = "Ui.SettingsDialog.Tab.Email";
    private const string NotificationsSection = "Ui.SettingsDialog.Tab.Notifications";
    private const string BackupSection = "Ui.SettingsDialog.Tab.Backup";

    private sealed class TrackedSection(string key, string title, int index, Func<string> state, Func<bool, Task<bool>> save)
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public int Index { get; } = index;
        public Func<bool, Task<bool>> Save { get; } = save;
        public string Baseline { get; set; } = state();
        public string Current() => state();
        public bool IsDirty => !string.Equals(Baseline, Current(), StringComparison.Ordinal);
    }

    private readonly List<TrackedSection> _tracked = [];

    // Set once the user chose to discard, or every save succeeded.
    private bool _closeConfirmed;
    private bool _savingOnClose;

    // Registers the section added last; its current fields become the
    // baseline. The save delegate receives true when called on closing,
    // where the "saved" confirmation is not shown.
    private void TrackSection(string key, Func<string> state, Func<bool, Task<bool>> save)
    {
        var index = _sections.Count - 1;
        _tracked.Add(new TrackedSection(key, _sections[index].Title, index, state, save));
    }

    private void MarkSaved(string key)
    {
        if (_tracked.FirstOrDefault(s => s.Key == key) is { } section)
        {
            section.Baseline = section.Current();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        // Only a close the user asked for: the Close button, Esc or the
        // title bar. A shutdown or a restart after a save goes through.
        if (e.Cancel || _closeConfirmed
            || e.CloseReason is not (CloseReason.UserClosing or CloseReason.None))
        {
            return;
        }
        if (_savingOnClose)
        {
            e.Cancel = true;
            return;
        }

        var dirty = _tracked.Where(s => s.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var answer = UiMessageBox.Show(this,
            _loc.Get("Ui.SettingsDialog.Unsaved.Prompt", string.Join(", ", dirty.Select(s => s.Title))),
            _loc.Get("Ui.SettingsDialog.Unsaved.Title"),
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.No) return;

        e.Cancel = true;
        if (answer == DialogResult.Yes)
        {
            // The saves are asynchronous: the close is cancelled here and
            // repeated once they all succeed.
            BeginInvoke(new Action(async () => await SaveAndCloseAsync(dirty)));
        }
    }

    private async Task SaveAndCloseAsync(IReadOnlyList<TrackedSection> dirty)
    {
        _savingOnClose = true;
        UseWaitCursor = true;
        try
        {
            foreach (var section in dirty)
            {
                if (await section.Save(true)) continue;
                // The save showed why it failed; the dialog stays open on
                // that section so the user can correct it.
                if (!IsDisposed) SelectSection(section.Index);
                return;
            }
        }
        finally
        {
            _savingOnClose = false;
            if (!IsDisposed) UseWaitCursor = false;
        }

        if (IsDisposed) return;
        _closeConfirmed = true;
        // Shown with ShowDialog: setting the result ends the modal loop.
        if (Modal) DialogResult = DialogResult.OK;
        else Close();
    }

    // ------------------ Section state ------------------
    // One string per section, built from the values its Save writes.
    // The password field counts only as typed or empty, so the secret is
    // never copied into the baseline.

    private static string Join(params object?[] values)
        => string.Join('\u001f', values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));

    private string GeneralState() => Join(
        (_languageCombo.SelectedItem as LanguageChoice)?.Code,
        _referenceCountryCombo.SelectedItem as string,
        _checkUpdatesBox.Checked,
        _logQueriesBox.Checked,
        (_textSizeCombo.SelectedItem as TextSizeChoice)?.Size,
        (_appearanceCombo.SelectedItem as AppearanceChoice)?.Mode);

    private string EmailState() => Join(
        _hostBox.Text,
        _portBox.Value,
        _useTlsBox.Checked,
        _usernameBox.Text,
        _passwordBox.TextLength > 0,
        _clearPasswordBox.Checked,
        _fromBox.Text,
        _fromNameBox.Text,
        _timeoutBox.Value);

    private string NotificationsState() => Join(
        _toBox.Text,
        _caregiverBox.Text,
        _doctorBox.Text,
        (_regionBox?.SelectedItem as RegionChoice)?.Code,
        string.Join(',', _caregiverKinds.Where(k => k.Value.Checked).Select(k => k.Key)),
        _caregiverDigest.Checked,
        _expiryLeadDays.Value,
        _inUseLeadDays.Value);

    // The cloud controls exist for an administrator only.
    private string BackupState() => Join(
        _backupEnabledBox.Checked,
        _backupDirectoryBox.Text,
        _backupTimePicker.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
        _backupRetentionBox.Value,
        _cloudEnabledBox?.Checked,
        _cloudDirectoryBox?.Text,
        _cloudRetentionBox?.Value,
        _cloudProviderBox?.SelectedIndex,
        _cloudProviderBox is null ? null : CloudTargetAccountId);
}

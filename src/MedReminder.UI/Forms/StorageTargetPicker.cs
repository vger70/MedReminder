using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Where a sync group or a household lives: a provider account (sign-in in
// the system browser) or a folder (B.1 Phases 4a, 4b). Shared by the sync
// window and the installation window (household step H3d). Errors are
// shown on the owner window; every method returns null when the user
// cancels or the sign-in fails.
internal sealed class StorageTargetPicker
{
    private readonly IWin32Window _owner;
    private readonly ICloudAccountService _accounts;
    private readonly ILocalizationService _loc;
    private readonly string _caption;

    public StorageTargetPicker(IWin32Window owner, ICloudAccountService accounts, ILocalizationService localization,
        string caption)
    {
        _owner = owner;
        _accounts = accounts;
        _loc = localization;
        _caption = caption;
    }

    public async Task<SyncTarget?> ChooseAsync()
    {
        var providers = new[] { CloudProvider.OneDrive, CloudProvider.GoogleDrive }.Where(_accounts.IsAvailable).ToList();
        if (providers.Count == 0)
        {
            return PickFolder() is { } only ? SyncTarget.ForFolder(only) : null;
        }

        var buttons = providers.ToDictionary(p => p, p => new TaskDialogCommandLinkButton(
            _loc.Get(ProviderKey("Ui.SyncDialog.Target", p, suffixOnly: true)),
            _loc.Get(ProviderKey("Ui.SyncDialog.Target", p, suffixOnly: true) + "Note")));
        var folder = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.SyncDialog.Target.Folder"), _loc.Get("Ui.SyncDialog.Target.FolderNote"));
        var page = new TaskDialogPage
        {
            Caption = _caption,
            Heading = _loc.Get("Ui.SyncDialog.Target.Heading"),
            AllowCancel = true,
        };
        foreach (var button in buttons.Values) page.Buttons.Add(button);
        page.Buttons.Add(folder);
        page.Buttons.Add(TaskDialogButton.Cancel);

        var choice = TaskDialog.ShowDialog(_owner, page);
        if (choice == folder) return PickFolder() is { } picked ? SyncTarget.ForFolder(picked) : null;
        var chosen = buttons.FirstOrDefault(b => b.Value == choice);
        if (chosen.Value is null) return null;

        var account = await SignInAsync(chosen.Key, null);
        return account is null ? null : SyncTarget.ForCloud(account.Provider, account.Id);
    }

    // The storage a pairing code names: its provider (sign-in now) or a
    // folder the user picks.
    public async Task<SyncTarget?> ForCodeAsync(CloudProvider? provider)
    {
        if (provider is not { } cloud)
        {
            return PickFolder() is { } folder ? SyncTarget.ForFolder(folder) : null;
        }
        if (!_accounts.IsAvailable(cloud))
        {
            Error(_loc.Get(ProviderKey("Ui.SyncDialog.PairingCode.ProviderUnavailable", cloud)));
            return null;
        }
        var account = await SignInAsync(cloud, null);
        return account is null ? null : SyncTarget.ForCloud(account.Provider, account.Id);
    }

    public async Task<CloudAccount?> SignInAsync(CloudProvider provider, string? accountId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            return await _accounts.SignInAsync(provider, accountId, timeout.Token);
        }
        catch (Exception ex)
        {
            Error(_loc.Get(ProviderKey("Ui.SyncDialog.SignIn.Failed", provider), ex.Message));
            return null;
        }
    }

    public string? PickFolder()
    {
        using var browser = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.SyncDialog.Folder"),
            UseDescriptionForTitle = true,
        };
        return browser.ShowDialog(_owner) == DialogResult.OK ? browser.SelectedPath : null;
    }

    // The OneDrive keys of Phase 4a are the unsuffixed ones; Google Drive
    // adds ".GoogleDrive". Target keys are named after the provider.
    public static string ProviderKey(string key, CloudProvider provider, bool suffixOnly = false)
        => suffixOnly
            ? $"{key}.{provider}"
            : provider == CloudProvider.GoogleDrive ? $"{key}.GoogleDrive" : key;

    private void Error(string message)
    {
        if (_owner is Control { IsDisposed: true }) return;
        MessageBox.Show(_owner, message, _caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

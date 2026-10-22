using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Services;

// Dictionary keys that differ by cloud provider, shared by the windows and
// the background services (the backup notification). The OneDrive keys of
// Phase 4a are the unsuffixed ones; Google Drive adds ".GoogleDrive".
// Target keys are named after the provider (suffixOnly).
internal static class ProviderKeys
{
    public static string For(string key, CloudProvider provider, bool suffixOnly = false)
        => suffixOnly
            ? $"{key}.{provider}"
            : provider == CloudProvider.GoogleDrive ? $"{key}.GoogleDrive" : key;
}

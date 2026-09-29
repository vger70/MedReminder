using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Shared by ImportDialog and RestoreFromCloudDialog. An import always
// overwrites the ACTIVE profile, whatever profile produced the archive.
// Since the cloud folder holds snapshots of every profile, picking
// another profile's archive by mistake would replace the active
// profile's data with someone else's, so that case asks for an explicit
// confirmation (default button: No).
internal static class OtherProfileArchivePrompt
{
    // Display name for a profile id, falling back to the raw id when the
    // profile is not registered on this machine (another device, or a
    // deleted profile).
    public static string DisplayName(IProfileRegistry registry, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return "—";
        return registry.GetById(profileId)?.DisplayName ?? profileId;
    }

    public static bool IsOtherProfile(ICurrentProfile currentProfile, string? archiveProfileId)
        => !string.IsNullOrWhiteSpace(archiveProfileId)
           && !string.Equals(archiveProfileId, currentProfile.Id, StringComparison.OrdinalIgnoreCase);

    // Returns true when the import may proceed.
    public static bool Confirm(
        IWin32Window owner,
        ILocalizationService loc,
        ICurrentProfile currentProfile,
        IProfileRegistry registry,
        string? archiveProfileId)
    {
        if (!IsOtherProfile(currentProfile, archiveProfileId)) return true;

        var answer = MessageBox.Show(
            owner,
            loc.Get(
                "Ui.Import.OtherProfile.Body",
                DisplayName(registry, archiveProfileId),
                currentProfile.DisplayName),
            loc.Get("Ui.Import.OtherProfile.Title"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return answer == DialogResult.Yes;
    }
}

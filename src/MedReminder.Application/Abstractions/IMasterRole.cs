namespace MedReminder.Application.Abstractions;

// Household step H4a (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
// §7.6): whether this device sends email and runs the cloud backup. Only
// the active master does, within its lease; every device does while the
// household has no master elected.
public interface IMasterRole
{
    Task<bool> SendsEmailAsync(CancellationToken cancellationToken);
}

using System.Diagnostics;
using MedReminder.Application.Donations;

namespace MedReminder.Infrastructure.Donations;

// The only place Process.Start is called for a donation launch (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.2, §7). UseShellExecute hands
// the URL off to the default Windows browser. No legacy WebBrowser
// control, no embedded payment page. Exceptions are caught and their
// message returned via the out parameter — the DonationService logs it
// and shows the user only a non-technical message.
public sealed class ShellUrlLauncher : IUrlLauncher
{
    public bool TryLaunch(Uri httpsUrl, out string? error)
    {
        ArgumentNullException.ThrowIfNull(httpsUrl);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = httpsUrl.ToString(),
                UseShellExecute = true, // hand off to the default browser
            });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message; // logged, never shown raw
            return false;
        }
    }
}

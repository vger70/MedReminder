using System.Diagnostics;
using MedReminder.Application.Prescriptions;

namespace MedReminder.UI.Services;

// IUrlOpener of the desktop: the default browser through the shell, as
// AboutDialog opens the project page. Called only by
// RegionalServiceLinkLauncher, which has checked the URL.
internal sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

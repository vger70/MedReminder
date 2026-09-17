using System.Diagnostics;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.Logging;
using WinFormsApp = System.Windows.Forms.Application;

namespace MedReminder.UI.Services;

// Self-orchestrated restart of the app after a backup restore.
//
// Sequence:
//   1. Retrieve the current executable path
//      (Environment.ProcessPath).
//   2. Launch a new instance in the background (Process.Start): the
//      new process tries to acquire the
//      Local\MedReminder.SingleInstance mutex and briefly waits for
//      the current one to release it.
//   3. Close the WinForms message loop; the finally in Program.Main
//      releases the mutex and shuts Serilog down. The new process
//      enters at that point.
//
// Note: does not use --minimized (the user just performed a restore
// and expects to see the app open with the new data).
internal sealed class ApplicationRestarter : IApplicationRestarter
{
    private readonly ILogger<ApplicationRestarter> _log;

    public ApplicationRestarter(ILogger<ApplicationRestarter> log)
    {
        _log = log;
    }

    public void RestartAndExit()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            _log.LogWarning(
                "Environment.ProcessPath is empty: cannot restart the app automatically.");
            WinFormsApp.Exit();
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            };
            Process.Start(psi);
            _log.LogInformation("Restarting MedReminder ({Exe}) — closing the current instance.", exe);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unable to start the new instance; the app will only close.");
        }
        finally
        {
            WinFormsApp.Exit();
        }
    }
}

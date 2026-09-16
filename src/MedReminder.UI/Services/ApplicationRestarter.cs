using System.Diagnostics;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.Logging;
using WinFormsApp = System.Windows.Forms.Application;

namespace MedReminder.UI.Services;

// Riavvio auto-orchestrato dell'app dopo un restore backup.
//
// Sequenza:
//   1. Recupera il path dell'eseguibile corrente (Environment.ProcessPath).
//   2. Lancia una nuova istanza in background (Process.Start): il nuovo
//      processo tenterà di acquisire il mutex Local\MedReminder.SingleInstance
//      e attenderà brevemente che l'attuale lo rilasci.
//   3. Chiude il message loop WinForms; il finally in Program.Main
//      rilascia il mutex e chiude Serilog. Il nuovo processo entra a
//      quel punto.
//
// Nota: non usa --minimized (l'utente ha appena fatto un restore e si
// aspetta di vedere l'app aperta con i dati nuovi).
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
                "Environment.ProcessPath vuoto: impossibile riavviare l'app in automatico.");
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
            _log.LogInformation("Riavvio MedReminder ({Exe}) — chiudo l'istanza corrente.", exe);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Impossibile avviare la nuova istanza; l'app verrà solo chiusa.");
        }
        finally
        {
            WinFormsApp.Exit();
        }
    }
}

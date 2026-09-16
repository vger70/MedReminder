using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using Microsoft.Win32;

namespace MedReminder.Infrastructure.AutoStart;

// Auto-start via HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// (spec §11, ANALYSIS §1.1 punto 12). Per-utente: nessun UAC elevato
// richiesto. Argomento --minimized (spec §12) fa partire in tray.
[SupportedOSPlatform("windows")]
internal sealed class RegistryAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "MedReminder";
    private const string StartupArgument = "--minimized";

    private readonly string _valueName;
    private readonly string _executablePath;

    // Il valueName è parametrizzabile per i test — così un test può
    // usare un valore univoco (es. "MedReminder.Tests") senza impattare
    // la configurazione reale dell'utente.
    public RegistryAutoStartService(string? valueName = null, string? executablePath = null)
    {
        _valueName = valueName ?? DefaultValueName;
        _executablePath = executablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossibile determinare il percorso dell'eseguibile.");
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null) return false;
            var value = key.GetValue(_valueName) as string;
            return !string.IsNullOrEmpty(value);
        }
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Impossibile aprire la chiave di Registry per la scrittura.");
        var command = $"\"{_executablePath}\" {StartupArgument}";
        key.SetValue(_valueName, command, RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        if (key.GetValue(_valueName) is not null)
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}

using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using Microsoft.Win32;

namespace MedReminder.Infrastructure.AutoStart;

// Auto-start via HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// (spec §11, ANALYSIS §1.1 item 12). Per-user: no elevated UAC
// required. The --minimized argument (spec §12) starts the app in the
// tray.
[SupportedOSPlatform("windows")]
internal sealed class RegistryAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "MedReminder";
    private const string StartupArgument = "--minimized";

    private readonly string _valueName;
    private readonly string _executablePath;

    // valueName is parameterizable for tests — a test can use a
    // unique value (e.g. "MedReminder.Tests") without impacting the
    // user's real configuration.
    public RegistryAutoStartService(string? valueName = null, string? executablePath = null)
    {
        _valueName = valueName ?? DefaultValueName;
        _executablePath = executablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the executable path.");
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
            ?? throw new InvalidOperationException("Unable to open the Registry key for writing.");
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

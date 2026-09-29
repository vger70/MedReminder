using FluentAssertions;
using MedReminder.Infrastructure.AutoStart;
using Microsoft.Win32;
using Xunit;

namespace MedReminder.Infrastructure.Tests.AutoStart;

// Il servizio scrive in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
// To avoid impacting the user's real configuration, the tests use
// un nome-valore univoco (Guid) e lo puliscono a fine test.
public class RegistryAutoStartServiceTests : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _testValueName;

    public RegistryAutoStartServiceTests()
    {
        _testValueName = $"MedReminder.Tests.{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        // Belt & braces: pulizia forzata anche se un test ha fallito.
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(_testValueName, throwOnMissingValue: false);
    }

    [Fact]
    public void Enable_writes_command_with_minimized_flag()
    {
        var sut = new RegistryAutoStartService(
            valueName: _testValueName,
            executablePath: @"C:\Program Files\MedReminder\MedReminder.exe");

        sut.IsEnabled.Should().BeFalse();
        sut.Enable();
        sut.IsEnabled.Should().BeTrue();

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var stored = key!.GetValue(_testValueName) as string;
        stored.Should().Be("\"C:\\Program Files\\MedReminder\\MedReminder.exe\" --minimized");
    }

    [Fact]
    public void Disable_removes_the_registry_value()
    {
        var sut = new RegistryAutoStartService(
            valueName: _testValueName,
            executablePath: @"C:\App\MedReminder.exe");
        sut.Enable();
        sut.Disable();
        sut.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Disable_is_noop_when_value_absent()
    {
        var sut = new RegistryAutoStartService(
            valueName: _testValueName,
            executablePath: @"C:\App\MedReminder.exe");

        var act = () => sut.Disable();
        act.Should().NotThrow();
    }
}

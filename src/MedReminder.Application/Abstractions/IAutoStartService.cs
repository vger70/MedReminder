namespace MedReminder.Application.Abstractions;

public interface IAutoStartService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}

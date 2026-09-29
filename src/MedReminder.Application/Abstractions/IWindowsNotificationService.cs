namespace MedReminder.Application.Abstractions;

public interface IWindowsNotificationService
{
    Task ShowAsync(string title, string body, CancellationToken cancellationToken);
}

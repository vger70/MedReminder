namespace MedReminder.Domain.Notifications;

[Flags]
public enum NotificationChannels
{
    None = 0,
    Email = 1,
    Windows = 2,
    Both = Email | Windows,
}

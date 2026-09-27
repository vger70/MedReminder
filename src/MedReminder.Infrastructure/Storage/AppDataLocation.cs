using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Storage;

// Windows implementation of IAppDataLocation: %LOCALAPPDATA%\MedReminder\.
public sealed class AppDataLocation : IAppDataLocation
{
    public string DataDirectory => AppDataPaths.GetAppDataDirectory();
}

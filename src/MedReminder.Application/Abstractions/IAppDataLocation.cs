namespace MedReminder.Application.Abstractions;

// Root directory of the per-installation application data
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.1). On Windows it is
// %LOCALAPPDATA%\MedReminder\ (AppDataPaths); a mobile host returns its
// sandboxed app-data directory. Lets platform-neutral infrastructure
// resolve paths without referencing a platform.
public interface IAppDataLocation
{
    string DataDirectory { get; }
}

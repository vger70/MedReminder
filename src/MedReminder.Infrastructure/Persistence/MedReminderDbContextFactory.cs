using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedReminder.Infrastructure.Persistence;

// Design-time factory used by `dotnet ef migrations add /
// database update`. At runtime the DbContext is built by the DI
// container in Program.cs (Increment 5); here we build a standalone
// instance pointing to the legacy single-user SQLite path under
// %LOCALAPPDATA%\MedReminder\. Design-time tooling does not need a
// real profile — this path is only used for schema generation.
public sealed class MedReminderDbContextFactory
    : IDesignTimeDbContextFactory<MedReminderDbContext>
{
    public MedReminderDbContext CreateDbContext(string[] args)
    {
        var designTimeDatabasePath = System.IO.Path.Combine(
            AppDataPaths.GetAppDataDirectory(),
            AppDataPaths.DatabaseFileName);
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(designTimeDatabasePath))
            .Options;
        return new MedReminderDbContext(options);
    }
}

using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedReminder.Infrastructure.Persistence;

// Design-time factory used by `dotnet ef migrations add /
// database update`. At runtime the DbContext is built by the DI
// container in Program.cs (Increment 5); here we build a standalone
// instance pointing to the default SQLite file under
// %LOCALAPPDATA%\MedReminder\.
public sealed class MedReminderDbContextFactory
    : IDesignTimeDbContextFactory<MedReminderDbContext>
{
    public MedReminderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString())
            .Options;
        return new MedReminderDbContext(options);
    }
}

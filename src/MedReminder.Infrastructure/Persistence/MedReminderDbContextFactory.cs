using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedReminder.Infrastructure.Persistence;

// Factory design-time usata da `dotnet ef migrations add / database update`.
// A runtime il DbContext è costruito dal DI container in Program.cs
// (Incremento 5); qui costruiamo un'istanza standalone che punta al file
// SQLite di default sotto %LOCALAPPDATA%\MedReminder\.
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

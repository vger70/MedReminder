using FluentAssertions;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

public class SchemaCreationTests
{
    // Se EnsureCreated ha materializzato lo schema, ogni DbSet deve poter
    // essere interrogato senza sollevare "no such table". Non usiamo
    // sqlite_master direttamente: la sanità del mapping EF Core → SQLite
    // è quello che vogliamo davvero verificare.
    [Fact]
    public async Task Every_entity_set_is_queryable_after_ensure_created()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CreateContext();

        (await context.Medicines.CountAsync()).Should().Be(0);
        (await context.StockMovements.CountAsync()).Should().Be(0);
        (await context.MedicationScheduleHistories.CountAsync()).Should().Be(0);
        (await context.MedicationSuspensions.CountAsync()).Should().Be(0);
        (await context.MedicationIntakes.CountAsync()).Should().Be(0);
        (await context.NotificationEvents.CountAsync()).Should().Be(0);
    }
}

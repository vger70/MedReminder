using FluentAssertions;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

public class SchemaCreationTests
{
    // If EnsureCreated has materialized the schema, every DbSet must
    // be queryable without raising "no such table". We do not use
    // sqlite_master directly: the sanity of the EF Core → SQLite
    // mapping is what we actually want to verify.
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

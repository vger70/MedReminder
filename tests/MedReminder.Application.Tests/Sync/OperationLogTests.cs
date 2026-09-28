using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

public class OperationLogTests
{
    private static readonly Guid Device = Guid.Parse("0d0d0d0d-0000-0000-0000-000000000001");
    private static readonly Guid M = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemorySyncSettingsStore _settings = new();
    private readonly InMemorySyncOperationRepository _repo = new();

    private OperationLog Log() => new(_settings, _repo,
        new SyncRegisters(new InMemorySyncFieldVersionRepository(), new InMemorySyncConflictRepository(), _clock), _clock);

    private static SyncOperationBody Op(string value) => new MedicineFieldChanged(M, "Notes", value);

    [Fact]
    public async Task Nothing_is_recorded_while_sync_is_disabled()
    {
        await Log().AppendAsync([Op("a"), Op("b")], CancellationToken.None);

        _repo.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Operations_get_increasing_timestamps_of_this_device()
    {
        _settings.Save(new SyncSettings(Guid.NewGuid(), Device, 3));

        await Log().AppendAsync([Op("a"), Op("b")], CancellationToken.None);

        var ms = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        _repo.All.Select(o => o.Timestamp).Should().Equal(
            new HybridTimestamp(ms, 0, Device), new HybridTimestamp(ms, 1, Device));
        _repo.All.Should().AllSatisfy(o =>
        {
            o.Generation.Should().Be(3);
            o.MedicineId.Should().Be(M);
            o.Type.Should().Be("MedicineFieldChanged");
            o.SchemaVersion.Should().Be(1);
            o.SegmentSeq.Should().BeNull();
        });
        // The second write of the register records the first as its base.
        OperationCodec.Deserialize(_repo.All[1].Type, 1, _repo.All[1].Payload)
            .Should().Be(new MedicineFieldChanged(M, "Notes", "b", new HybridTimestamp(ms, 0, Device)));
    }

    [Fact]
    public async Task A_new_scope_continues_after_the_stored_clock_even_if_the_wall_clock_went_back()
    {
        _settings.Save(new SyncSettings(Guid.NewGuid(), Device, 1));
        await Log().AppendAsync([Op("a")], CancellationToken.None);
        var first = _repo.All[0].Timestamp;

        _clock.SetUtcNow(_clock.GetUtcNow().AddMinutes(-5));
        await Log().AppendAsync([Op("b")], CancellationToken.None);

        _repo.All[1].Timestamp.Should().Be(new HybridTimestamp(first.PhysicalMs, 1, Device));
    }
}

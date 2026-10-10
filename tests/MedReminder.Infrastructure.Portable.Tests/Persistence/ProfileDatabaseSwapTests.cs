using FluentAssertions;
using MedReminder.Application;
using MedReminder.Infrastructure.Storage;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// The swap behind import, restore and sync: the current database is kept
// aside as .bak, and a failed move puts it back.
public sealed class ProfileDatabaseSwapTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-swap-" + Guid.NewGuid().ToString("N"));
    private readonly TimeProvider _clock = new FixedTime(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

    public ProfileDatabaseSwapTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Moves_the_new_file_in_and_keeps_the_current_one_aside()
    {
        var target = Write("medreminder.db", "current");
        var incoming = Write("incoming.db", "new");

        await ProfileDatabaseSwap.ReplaceAsync(new DatabaseExclusiveAccess(), db: null, target, incoming, _clock, CancellationToken.None);

        File.ReadAllText(target).Should().Be("new");
        File.ReadAllText(target + ".bak-20261010090000").Should().Be("current");
        File.Exists(incoming).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_move_puts_the_current_database_back()
    {
        var target = Write("medreminder.db", "current");
        var missing = Path.Combine(_directory, "missing.db");

        var act = () => ProfileDatabaseSwap.ReplaceAsync(new DatabaseExclusiveAccess(), db: null, target, missing, _clock, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
        File.ReadAllText(target).Should().Be("current");
        File.Exists(target + ".bak-20261010090000").Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_move_undoes_the_step_taken_before_it()
    {
        var target = Write("medreminder.db", "current");
        var missing = Path.Combine(_directory, "missing.db");
        var undone = false;

        var act = () => ProfileDatabaseSwap.ReplaceAsync(new DatabaseExclusiveAccess(), db: null, target, missing, _clock,
            CancellationToken.None, beforeSwap: () => () => undone = true);

        await act.Should().ThrowAsync<FileNotFoundException>();
        undone.Should().BeTrue("the swap did not happen, so its marker must not stay");
    }

    [Fact]
    public async Task A_successful_swap_keeps_the_step_taken_before_it()
    {
        var target = Write("medreminder.db", "current");
        var incoming = Write("incoming.db", "new");
        var undone = false;

        await ProfileDatabaseSwap.ReplaceAsync(new DatabaseExclusiveAccess(), db: null, target, incoming, _clock,
            CancellationToken.None, beforeSwap: () => () => undone = true);

        undone.Should().BeFalse();
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

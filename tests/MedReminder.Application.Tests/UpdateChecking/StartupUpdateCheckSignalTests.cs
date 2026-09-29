using FluentAssertions;
using MedReminder.Application.UpdateChecking;
using Xunit;

namespace MedReminder.Application.Tests.UpdateChecking;

public class StartupUpdateCheckSignalTests
{
    [Fact]
    public async Task WaitAsync_completes_when_the_signal_is_marked()
    {
        var signal = new StartupUpdateCheckSignal();

        var wait = signal.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
        wait.IsCompleted.Should().BeFalse();

        signal.MarkCompleted();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        signal.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task WaitAsync_completes_without_throwing_on_timeout()
    {
        var signal = new StartupUpdateCheckSignal();

        await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);

        signal.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task WaitAsync_throws_when_cancelled()
    {
        var signal = new StartupUpdateCheckSignal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => signal.WaitAsync(TimeSpan.FromMinutes(1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MarkCompleted_is_idempotent()
    {
        var signal = new StartupUpdateCheckSignal();

        signal.MarkCompleted();
        signal.MarkCompleted();

        await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        signal.IsCompleted.Should().BeTrue();
    }
}

using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Sync.Remote;

// Reads the pairing file a code names (mrpair1 and mrpair2). A file just
// written by the device that shows the code may not yet be visible to
// another session of the cloud storage: a join right after the code was
// shown read nothing and reported the code as expired, while a second
// attempt seconds later worked (medreminder-mobile #115). A missing file is
// read again after RetryDelays before SyncPairingExpiredException.
internal static class PairingFileRead
{
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    public static async Task<byte[]> ReadAsync(ISyncTransport transport, string path, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(clock);
        if (await transport.ReadAsync(path, cancellationToken) is { } file) return file;
        foreach (var delay in RetryDelays)
        {
            await Task.Delay(delay, clock, cancellationToken);
            if (await transport.ReadAsync(path, cancellationToken) is { } later) return later;
        }
        throw new SyncPairingExpiredException();
    }
}

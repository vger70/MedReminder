namespace MedReminder.Domain.Household;

// Rules of the master role (household step H4a; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §7.4). Pure functions of the master
// registers, this device's id and times, so every device decides the same
// way from the same state.
public static class MasterRules
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultMargin = TimeSpan.FromHours(1);

    // Whether this device sends email and runs the cloud backup.
    //
    //   - No election in the household (an installation that never
    //     published, or one published before step H4): every device, as
    //     before; the installation window asks an administrator to elect.
    //   - Otherwise only the active master, and on a published household
    //     only while its last successful household sync is younger than the
    //     lease: an isolated master stops on its own (§7.4).
    public static bool SendsEmail(Guid me, HouseholdMaster master, bool published, DateTimeOffset? lastSynced,
        DateTimeOffset now, TimeSpan lease)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (master.Election is null) return true;
        if (master.ActiveDevice != me) return false;
        if (!published) return true;
        return lastSynced is { } at && now - at < lease;
    }

    // Whether the elected device records the activation now: when nothing
    // was active, when it was itself, when the outgoing master released
    // this election, or when the outgoing master has not been seen for the
    // lease plus the margin (clock skew between devices).
    public static bool ShouldActivate(Guid me, HouseholdMaster master, DateTimeOffset? outgoingLastSeen,
        DateTimeOffset now, TimeSpan lease, TimeSpan margin)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (master.Election is not { } election || election.DeviceId != me || !master.Pending) return false;
        if (master.OutgoingDevice is not { } outgoing || outgoing == me) return true;
        if (master.Released == election.ElectionId) return true;
        return outgoingLastSeen is not { } seen || now - seen > lease + margin;
    }

    // Whether this device, still active under an older election, records
    // that it stopped for the current one.
    public static bool ShouldRelease(Guid me, HouseholdMaster master)
    {
        ArgumentNullException.ThrowIfNull(master);
        return master.OutgoingDevice == me && master.Election is { } election && election.DeviceId != me
            && master.Released != election.ElectionId;
    }
}

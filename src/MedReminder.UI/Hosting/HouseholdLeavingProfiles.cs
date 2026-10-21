using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Hosting;

// IHouseholdLeavingProfiles for the desktop (issue #88 of
// vger70/medreminder-mobile). The desktop does not offer leaving the
// household yet; this registration keeps LeaveHousehold resolvable (and
// the container valid when validated on build) and refuses to run.
internal sealed class HouseholdLeavingProfiles : IHouseholdLeavingProfiles
{
    public Task KeepAsync(string profileId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Leaving the household is not available on the desktop.");

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Leaving the household is not available on the desktop.");
}

namespace MedReminder.Application.Abstractions;

// Port owning the profiles.json registry
// (docs/ANALYSIS-MULTI-USER.md §2.3). The implementation lives in
// Infrastructure/Profiles.
//
// Invariants enforced on every mutating call:
// - At least one profile with Role == Admin must exist after the call
//   (§2.2). Delete() throws InvalidOperationException if id is the
//   last admin. Create() forces Role = Admin when the registry is
//   empty, regardless of the requested role.
// - Role is immutable after creation (§14a G). No Update(role) API.
//
// The port does not know "who is calling" — the admin / user gating
// is enforced by the UI, not here. This keeps the surface small and
// unit-testable without a fake current-profile.
public interface IProfileRegistry
{
    IReadOnlyList<Profile> ListProfiles();

    Profile? GetById(string id);

    // Hint about the last profile the app opened. Used by auto-start
    // and by the post-restart flow to skip the picker (§4.2). It is
    // not "the" runtime active profile — that lives in
    // ICurrentProfile once the boot flow has selected one.
    string? ActiveProfileIdHint { get; }

    // The role of the FIRST profile in an empty registry is forced to
    // Admin (§1.1a): the first-run wizard has nothing to compare
    // against, and there must always be one admin. On a non-empty
    // registry the caller decides. Callers must gate on IsAdmin
    // themselves.
    Profile Create(string displayName, ProfileRole role);

    void Rename(string id, string newDisplayName);

    // Throws InvalidOperationException if id is the last admin
    // (invariant §2.2) or is unknown. With deleteData=true also
    // removes the profile folder from disk; with deleteData=false the
    // folder is left intact for manual recovery (§3).
    void Delete(string id, bool deleteData);

    void SetActiveProfileHint(string id);

    // pin == null clears the PIN. A non-null pin is hashed with
    // PBKDF2-HMAC-SHA256 100_000 iterations and a fresh 16-byte salt
    // (§8.3).
    void SetPin(string id, string? pin);

    bool VerifyPin(string id, string pin);

    bool HasPin(string id);
}

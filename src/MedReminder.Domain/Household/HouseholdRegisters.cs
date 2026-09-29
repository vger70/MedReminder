using System.Globalization;
using MedReminder.Domain.Sync;

namespace MedReminder.Domain.Household;

// Last-writer-wins registers of the household (step H2): what each
// operation writes, and the state they add up to. A register keeps every
// version it received; the winner is the one with the greatest HLC, so
// every device that holds the same versions reads the same state
// whatever the order they arrived in.
public static class HouseholdRegisters
{
    // The entity of the installation settings: no profile has this id.
    public const string Installation = "";

    public const string Registered = "Registered";
    public const string Name = "Name";
    public const string Role = "Role";
    public const string Pin = "Pin";
    public const string Removed = "Removed";

    public static IReadOnlyList<(string Register, string? Value)> WritesOf(HouseholdOperationBody body) => body switch
    {
        ProfileRegistered r =>
        [
            (Registered, r.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            (Name, r.DisplayName),
            (Role, r.Role),
        ],
        ProfileRenamed r => [(Name, r.DisplayName)],
        ProfileRoleChanged r => [(Role, r.Role)],
        ProfilePinChanged p => [(Pin, PinValue(p.Hash, p.Salt, p.Iterations))],
        ProfileRemoved => [(Removed, "true")],
        HouseholdSettingChanged s => [(s.Setting, s.Value)],
        _ => throw new NotSupportedException($"No registers for {body.GetType().Name}."),
    };

    // "<iterations>:<salt>:<hash>", or null when the PIN is cleared.
    public static string? PinValue(string? hash, string? salt, int iterations)
        => string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt) || iterations <= 0
            ? null
            : $"{iterations.ToString(CultureInfo.InvariantCulture)}:{salt}:{hash}";

    // The parts of a PinValue; false for null or a malformed value.
    public static bool TryParsePin(string? value, out string hash, out string salt, out int iterations)
    {
        hash = salt = string.Empty;
        iterations = 0;
        var parts = value?.Split(':');
        if (parts is not { Length: 3 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations <= 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            iterations = 0;
            return false;
        }
        salt = parts[1];
        hash = parts[2];
        return true;
    }

    // The winning value of each installation setting that has a version.
    public static IReadOnlyDictionary<string, string?> Settings(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
        => versions
            .Where(v => v.ProfileId == Installation)
            .GroupBy(v => v.Register, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value, StringComparer.Ordinal);

    // The profiles the winning versions describe. A profile exists once it
    // is registered and until it is removed; a removed profile never comes
    // back.
    public static IReadOnlyList<HouseholdProfile> Profiles(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
    {
        var winners = versions
            .GroupBy(v => (v.ProfileId, v.Register))
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value);
        return [.. winners.Keys.Select(k => k.ProfileId).Distinct(StringComparer.Ordinal)
            .Where(id => winners.ContainsKey((id, Registered)) && !winners.ContainsKey((id, Removed)))
            .Order(StringComparer.Ordinal)
            .Select(id => new HouseholdProfile(
                id,
                winners.GetValueOrDefault((id, Name)) ?? string.Empty,
                winners.GetValueOrDefault((id, Role)) ?? HouseholdRole.User,
                winners.GetValueOrDefault((id, Pin))))];
    }
}

// A profile as the household holds it. Pin is the register value of
// HouseholdRegisters.PinValue, null without a PIN.
public sealed record HouseholdProfile(string ProfileId, string DisplayName, string Role, string? Pin);

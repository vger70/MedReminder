using System.Text.Json;
using MedReminder.Domain.Household;

namespace MedReminder.Application.Household;

// Wire form of the household operations (step H2): the type name and a
// camelCase JSON payload, as OperationCodec does for a profile group.
// Schema version 1 is the original catalogue.
public static class HouseholdOperationCodec
{
    public const int CurrentSchemaVersion = 2;

    private static readonly (string Name, Type Type)[] Catalogue =
    [
        ("ProfileRegistered", typeof(ProfileRegistered)),
        ("ProfileRenamed", typeof(ProfileRenamed)),
        ("ProfileRoleChanged", typeof(ProfileRoleChanged)),
        ("ProfilePinChanged", typeof(ProfilePinChanged)),
        ("ProfileRemoved", typeof(ProfileRemoved)),
        ("HouseholdSettingChanged", typeof(HouseholdSettingChanged)),
        ("DeviceKeyPublished", typeof(DeviceKeyPublished)),
        ("RecoveryKeyPublished", typeof(RecoveryKeyPublished)),
        ("RecoveryKeyGranted", typeof(RecoveryKeyGranted)),
        ("HouseholdKeyGranted", typeof(HouseholdKeyGranted)),
        ("ProfileKeyGranted", typeof(ProfileKeyGranted)),
        ("ProfileKeyRevoked", typeof(ProfileKeyRevoked)),
        ("ProfileKeyEscrowed", typeof(ProfileKeyEscrowed)),
        ("MasterElected", typeof(MasterElected)),
        ("MasterActivated", typeof(MasterActivated)),
        ("MasterReleased", typeof(MasterReleased)),
        ("DeviceRemoved", typeof(DeviceRemoved)),
        ("DeviceLeft", typeof(DeviceLeft)),
    ];

    private static readonly Dictionary<Type, string> NameByType =
        Catalogue.ToDictionary(c => c.Type, c => c.Name);

    private static readonly Dictionary<string, Type> TypeByName =
        Catalogue.ToDictionary(c => c.Name, c => c.Type, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static IReadOnlyCollection<string> TypeNames => TypeByName.Keys;

    public static int SchemaVersionOf(HouseholdOperationBody body) => SchemaVersionOfType(body.GetType());

    // Schema 2 introduced the device-key-wrapped grants used when a removal
    // keeps the shared passphrase; every other operation stays at schema 1.
    private static int SchemaVersionOfType(Type type)
        => type == typeof(RecoveryKeyGranted) || type == typeof(HouseholdKeyGranted) ? 2 : 1;

    public static (string Type, string Payload) Serialize(HouseholdOperationBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!NameByType.TryGetValue(body.GetType(), out var name))
            throw new NotSupportedException($"{body.GetType().Name} is not a household operation.");
        return (name, JsonSerializer.Serialize(body, body.GetType(), Options));
    }

    // NotSupportedException for an unknown type or a newer schema version:
    // the reader stops and asks for an update, as for a profile group.
    public static HouseholdOperationBody Deserialize(string type, int schemaVersion, string payload)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(payload);
        if (!TypeByName.TryGetValue(type, out var clr))
            throw new NotSupportedException($"Unknown household operation type '{type}'.");
        if (schemaVersion != SchemaVersionOfType(clr))
            throw new NotSupportedException($"Household operation schema version {schemaVersion} is not supported for '{type}'.");
        return (HouseholdOperationBody)(JsonSerializer.Deserialize(payload, clr, Options)
            ?? throw new JsonException($"Empty payload for household operation type '{type}'."));
    }
}

using System.Text.Json;
using MedReminder.Domain.Household;

namespace MedReminder.Application.Household;

// Wire form of the household operations (step H2): the type name and a
// camelCase JSON payload, as OperationCodec does for a profile group.
// Schema version 1 is the original catalogue.
public static class HouseholdOperationCodec
{
    public const int CurrentSchemaVersion = 1;

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
        ("ProfileKeyGranted", typeof(ProfileKeyGranted)),
        ("ProfileKeyRevoked", typeof(ProfileKeyRevoked)),
        ("ProfileKeyEscrowed", typeof(ProfileKeyEscrowed)),
        ("MasterElected", typeof(MasterElected)),
        ("MasterActivated", typeof(MasterActivated)),
        ("MasterReleased", typeof(MasterReleased)),
        ("DeviceRemoved", typeof(DeviceRemoved)),
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

    public static int SchemaVersionOf(HouseholdOperationBody body) => 1;

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
        if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
            throw new NotSupportedException($"Household operation schema version {schemaVersion} is not supported.");
        if (!TypeByName.TryGetValue(type, out var clr))
            throw new NotSupportedException($"Unknown household operation type '{type}'.");
        return (HouseholdOperationBody)(JsonSerializer.Deserialize(payload, clr, Options)
            ?? throw new JsonException($"Empty payload for household operation type '{type}'."));
    }
}

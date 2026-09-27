using System.Text.Json;
using System.Text.Json.Serialization;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// JSON form of the operation catalogue (B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.2). An operation is stored
// and published as (type name, schema version, payload); the type name
// is the record name, listed here explicitly so that renaming a C# type
// cannot change the format by accident.
//
// Payload rules: camelCase property names, enums by member name,
// DateOnly as "yyyy-MM-dd", TimeOnly as "HH:mm:ss[.fffffff]",
// DateTimeOffset as ISO 8601 with offset, decimals as JSON numbers.
public static class OperationCodec
{
    public const int CurrentSchemaVersion = 1;

    private static readonly (string Name, Type Type)[] Catalogue =
    [
        ("MedicineCreated", typeof(MedicineCreated)),
        ("MedicineFieldChanged", typeof(MedicineFieldChanged)),
        ("MedicineActivityChanged", typeof(MedicineActivityChanged)),
        ("ScheduleRowRecorded", typeof(ScheduleRowRecorded)),
        ("SlotSetRecorded", typeof(SlotSetRecorded)),
        ("StockEntryRecorded", typeof(StockEntryRecorded)),
        ("IntakeRecorded", typeof(IntakeRecorded)),
        ("StockCountRecorded", typeof(StockCountRecorded)),
        ("SuspensionRecorded", typeof(SuspensionRecorded)),
        ("SuspensionEndChanged", typeof(SuspensionEndChanged)),
        ("FactRetracted", typeof(FactRetracted)),
    ];

    private static readonly Dictionary<Type, string> NameByType =
        Catalogue.ToDictionary(c => c.Type, c => c.Name);

    private static readonly Dictionary<string, Type> TypeByName =
        Catalogue.ToDictionary(c => c.Name, c => c.Type, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = false,
    };

    public static IReadOnlyCollection<string> TypeNames => TypeByName.Keys;

    public static (string Type, string Payload) Serialize(SyncOperationBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!NameByType.TryGetValue(body.GetType(), out var name))
        {
            throw new ArgumentException($"{body.GetType().Name} is not in the operation catalogue.", nameof(body));
        }
        return (name, JsonSerializer.Serialize(body, body.GetType(), Options));
    }

    // Throws NotSupportedException for a type or a schema version this
    // build does not know: the caller must keep such an operation and
    // stop applying (R7), never drop it.
    public static SyncOperationBody Deserialize(string type, int schemaVersion, string payload)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(payload);
        if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
        {
            throw new NotSupportedException($"Operation schema version {schemaVersion} is not supported.");
        }
        if (!TypeByName.TryGetValue(type, out var clr))
        {
            throw new NotSupportedException($"Operation type '{type}' is not supported.");
        }
        return (SyncOperationBody)(JsonSerializer.Deserialize(payload, clr, Options)
            ?? throw new JsonException($"Empty payload for operation type '{type}'."));
    }
}

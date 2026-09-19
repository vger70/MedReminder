using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedReminder.Domain.Medicines;

// Translates a Schedule value object to / from its persisted form on
// MedicationScheduleHistory (ScheduleKind + optional JSON payload).
//
// FixedDaily has no payload: DosePerAdministration and
// AdministrationsPerDay are already stored as first-class columns on
// the history entry, so the payload is null and Deserialize reuses
// those columns to reconstruct the value object.
//
// See docs/ANALYSIS-A1-REGIMENS.md §4.
public static class ScheduleCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static (ScheduleKind Kind, string? Payload) Serialize(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return schedule switch
        {
            FixedDailySchedule => (ScheduleKind.FixedDaily, null),
            WeeklySchedule weekly => (ScheduleKind.Weekly, JsonSerializer.Serialize(
                new WeeklyPayload { Days = weekly.QuantitiesByDayOfWeek.ToArray() },
                JsonOptions)),
            CyclicSchedule cyclic => (ScheduleKind.Cyclic, JsonSerializer.Serialize(
                new CyclicPayload
                {
                    OnDays = cyclic.OnDays,
                    OffDays = cyclic.OffDays,
                    QuantityPerOnDay = cyclic.QuantityPerOnDay,
                }, JsonOptions)),
            TaperingSchedule tapering => (ScheduleKind.Tapering, JsonSerializer.Serialize(
                new TaperingPayload
                {
                    StartDose = tapering.StartDose,
                    EndDose = tapering.EndDose,
                    Step = tapering.Step,
                    IntervalDays = tapering.IntervalDays,
                }, JsonOptions)),
            PrnSchedule => (ScheduleKind.Prn, "{}"),
            _ => throw new InvalidOperationException(
                $"Unknown Schedule subtype: {schedule.GetType().FullName}"),
        };
    }

    public static Schedule Deserialize(
        ScheduleKind kind,
        string? payload,
        decimal legacyDosePerAdministration,
        int legacyAdministrationsPerDay)
    {
        // Unknown enum values (a future ScheduleKind read on an older
        // build) fall back to FixedDaily, so the projection stays
        // finite. Fail-safe, per §4 of the design doc.
        if (!Enum.IsDefined(typeof(ScheduleKind), kind))
        {
            return BuildFixedDaily(legacyDosePerAdministration, legacyAdministrationsPerDay);
        }

        switch (kind)
        {
            case ScheduleKind.FixedDaily:
                return BuildFixedDaily(legacyDosePerAdministration, legacyAdministrationsPerDay);
            case ScheduleKind.Weekly:
                {
                    var data = DeserializePayload<WeeklyPayload>(payload, kind);
                    if (data.Days is null || data.Days.Length != 7)
                    {
                        throw new InvalidOperationException(
                            $"Weekly schedule payload must carry exactly 7 quantities (got {data.Days?.Length ?? 0}).");
                    }
                    return new WeeklySchedule(data.Days);
                }
            case ScheduleKind.Cyclic:
                {
                    var data = DeserializePayload<CyclicPayload>(payload, kind);
                    return new CyclicSchedule(data.OnDays, data.OffDays, data.QuantityPerOnDay);
                }
            case ScheduleKind.Tapering:
                {
                    var data = DeserializePayload<TaperingPayload>(payload, kind);
                    return new TaperingSchedule(data.StartDose, data.EndDose, data.Step, data.IntervalDays);
                }
            case ScheduleKind.Prn:
                return new PrnSchedule();
            default:
                return BuildFixedDaily(legacyDosePerAdministration, legacyAdministrationsPerDay);
        }
    }

    private static FixedDailySchedule BuildFixedDaily(decimal dose, int adminsPerDay)
    {
        // Retro-compat: if the legacy fields are invalid (a corrupt
        // pre-A1 row) surface the failure with the same message shape
        // the constructor would.
        return new FixedDailySchedule(dose, adminsPerDay);
    }

    private static T DeserializePayload<T>(string? payload, ScheduleKind kind)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException(
                $"Schedule payload for kind {kind} is missing.");
        }
        try
        {
            var value = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (value is null)
            {
                throw new InvalidOperationException(
                    $"Schedule payload for kind {kind} deserialized to null.");
            }
            return value;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Schedule payload for kind {kind} is malformed: {ex.Message}", ex);
        }
    }

    private sealed class WeeklyPayload
    {
        [JsonPropertyName("days")]
        public decimal[]? Days { get; set; }
    }

    private sealed class CyclicPayload
    {
        [JsonPropertyName("onDays")]
        public int OnDays { get; set; }

        [JsonPropertyName("offDays")]
        public int OffDays { get; set; }

        [JsonPropertyName("quantityPerOnDay")]
        public decimal QuantityPerOnDay { get; set; }
    }

    private sealed class TaperingPayload
    {
        [JsonPropertyName("startDose")]
        public decimal StartDose { get; set; }

        [JsonPropertyName("endDose")]
        public decimal EndDose { get; set; }

        [JsonPropertyName("step")]
        public decimal Step { get; set; }

        [JsonPropertyName("intervalDays")]
        public int IntervalDays { get; set; }
    }
}

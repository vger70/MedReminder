using MedReminder.Application.Reporting;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Reporting;

// Fictitious therapy used by the report tests. No real personal data.
internal static class TherapyReportTestData
{
    public static readonly DateOnly ReportDate = new(2026, 9, 28);

    public static TherapyReportEntry Enalapril()
    {
        var m = new Medicine
        {
            Name = "Enalapril",
            ActiveIngredient = "enalapril maleate",
            Unit = "tablets",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 12, 31),
            DoctorName = "Dr. Example",
            Notes = "after meals",
        };
        return new TherapyReportEntry(m,
        [
            // Declared out of order: timed slots sort chronologically.
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 0.5m, Time = new TimeOnly(20, 0), Order = 1 },
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m, Time = new TimeOnly(8, 0), TimingLabel = "breakfast", Order = 0 },
        ]);
    }

    // No slots, no active ingredient, no dates, no doctor, no notes.
    public static TherapyReportEntry Metformin()
        => new(new Medicine
        {
            Name = "Metformin",
            Unit = "tablets",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 2,
        }, []);

    public static TherapyReportEntry InactiveAspirin()
        => new(new Medicine
        {
            Name = "Aspirin",
            Unit = "tablets",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 1,
            IsActive = false,
        }, []);

    public static TherapyReportEntry Numbered(int n)
        => new(new Medicine
        {
            Name = $"Medicine {n:D3}",
            Unit = "tablets",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 1,
        }, []);
}

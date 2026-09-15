using System.Globalization;
using System.Text;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Genera una scheda testuale della terapia corrente da mostrare/stampare
// per il medico (spec Incremento 10). Nessuna informazione clinica libera
// oltre a quanto già impostato dall'utente — MedReminder resta un
// promemoria organizzativo.
public sealed record TherapyReportEntry(
    Medicine Medicine,
    IReadOnlyList<MedicationAdministrationSlot> Slots);

public static class TherapyReport
{
    public static string Build(
        IReadOnlyList<TherapyReportEntry> entries,
        DateOnly reportDate,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var c = culture ?? CultureInfo.CurrentCulture;

        var sb = new StringBuilder();
        sb.Append("Scheda terapia — MedReminder").Append('\n');
        sb.Append("Data: ").Append(reportDate.ToString("d", c)).Append('\n').Append('\n');

        var active = entries.Where(e => e.Medicine.IsActive).ToList();
        if (active.Count == 0)
        {
            sb.Append("Nessuna medicina attiva.").Append('\n');
            return sb.ToString();
        }

        var i = 1;
        foreach (var entry in active.OrderBy(e => e.Medicine.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AppendMedicine(sb, i++, entry, c);
            sb.Append('\n');
        }

        sb.Append("— MedReminder: promemoria organizzativo, non è un dispositivo medico.");
        return sb.ToString();
    }

    private static void AppendMedicine(StringBuilder sb, int index, TherapyReportEntry entry, CultureInfo c)
    {
        var m = entry.Medicine;
        sb.Append(index).Append(". ").Append(m.Name);
        if (!string.IsNullOrWhiteSpace(m.ActiveIngredient))
        {
            sb.Append(" (").Append(m.ActiveIngredient).Append(')');
        }
        sb.Append('\n');

        if (entry.Slots.Count > 0)
        {
            foreach (var slot in entry.Slots.OrderBy(SortKey))
            {
                sb.Append("   - ").Append(FormatSlot(slot, m.Unit, c)).Append('\n');
            }
        }
        else
        {
            // Fallback modello legacy dose × frequenza.
            var freq = m.AdministrationsPerDay > 0 ? m.AdministrationsPerDay : 1;
            var doseText = m.DosePerAdministration.ToString("0.##", c);
            sb.Append("   - ").Append(doseText).Append(' ').Append(m.Unit)
              .Append(" × ").Append(freq).Append(" volte al giorno").Append('\n');
        }

        if (m.StartDate != default)
        {
            sb.Append("   Inizio: ").Append(m.StartDate.ToString("d", c));
            if (m.EndDate is { } end)
            {
                sb.Append(" · Fine: ").Append(end.ToString("d", c));
            }
            sb.Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(m.DoctorName))
        {
            sb.Append("   Medico: ").Append(m.DoctorName).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            sb.Append("   Note: ").Append(m.Notes).Append('\n');
        }
    }

    private static string FormatSlot(MedicationAdministrationSlot slot, string unit, CultureInfo c)
    {
        var doseText = slot.Dose.ToString("0.##", c);
        var sb = new StringBuilder();
        sb.Append(doseText).Append(' ').Append(unit);

        if (!string.IsNullOrWhiteSpace(slot.TimingLabel))
        {
            sb.Append(' ').Append(slot.TimingLabel);
        }
        if (slot.Time is { } t)
        {
            sb.Append(" (").Append(t.ToString("HH:mm", c)).Append(')');
        }
        return sb.ToString();
    }

    // Ordina prima gli slot con orario esplicito (in ordine cronologico),
    // poi quelli senza orario (nell'ordine dichiarato dall'utente).
    private static int SortKey(MedicationAdministrationSlot slot)
    {
        if (slot.Time is { } t) return t.Hour * 60 + t.Minute;
        return 10_000 + slot.Order;
    }
}

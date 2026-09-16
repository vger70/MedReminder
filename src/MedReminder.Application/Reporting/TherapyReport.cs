using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Genera una scheda testuale della terapia corrente da mostrare/stampare
// per il medico (spec Incremento 10). Nessuna informazione clinica libera
// oltre a quanto già impostato dall'utente — MedReminder resta un
// promemoria organizzativo.
//
// Uso Environment.NewLine (= "\r\n" su Windows) invece di '\n' perché
// la TextBox multiline di WinForms rende visibile una nuova riga SOLO
// quando incontra CRLF; con LF stampa tutto su una linea.
public sealed record TherapyReportEntry(
    Medicine Medicine,
    IReadOnlyList<MedicationAdministrationSlot> Slots);

public static class TherapyReport
{
    private static readonly string NL = Environment.NewLine;

    // Fallback italiano hardcoded — retrocompatibilità con i test
    // esistenti che invocano Build(entries, date) senza service.
    // Le stesse etichette esistono nel JSON (chiavi Reports.Therapy.*)
    // per la versione localizzata.
    private const string HeaderIt = "Scheda terapia — MedReminder";
    private const string DateLabelIt = "Data: ";
    private const string NoMedicinesIt = "Nessuna medicina attiva.";
    private const string TimesFallbackIt = " × {0} volte al giorno";
    private const string StartLabelIt = "   Inizio: ";
    private const string EndLabelIt = " · Fine: ";
    private const string DoctorLabelIt = "   Medico: ";
    private const string NotesLabelIt = "   Note: ";
    private const string DisclaimerIt = "— MedReminder: promemoria organizzativo, non è un dispositivo medico.";

    public static string Build(
        IReadOnlyList<TherapyReportEntry> entries,
        DateOnly reportDate,
        CultureInfo? culture = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        // Se il servizio è disponibile, la sua CurrentCulture ha priorità
        // per la formattazione di date/numeri; altrimenti si onora la
        // culture esplicita del caller (o CurrentCulture).
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;

        var sb = new StringBuilder();
        sb.Append(L(localization, "Reports.Therapy.Header", HeaderIt)).Append(NL);
        sb.Append(FormatOrDefault(localization, "Reports.Therapy.Date",
            DateLabelIt + reportDate.ToString("d", c), reportDate.ToString("d", c)))
          .Append(NL).Append(NL);

        var active = entries.Where(e => e.Medicine.IsActive).ToList();
        if (active.Count == 0)
        {
            sb.Append(L(localization, "Reports.Therapy.NoMedicines", NoMedicinesIt)).Append(NL);
            return sb.ToString();
        }

        var i = 1;
        foreach (var entry in active.OrderBy(e => e.Medicine.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AppendMedicine(sb, i++, entry, c, localization);
            sb.Append(NL);
        }

        sb.Append(L(localization, "Reports.Therapy.Disclaimer", DisclaimerIt));
        return sb.ToString();
    }

    private static void AppendMedicine(
        StringBuilder sb, int index, TherapyReportEntry entry,
        CultureInfo c, ILocalizationService? loc)
    {
        var m = entry.Medicine;
        sb.Append(index).Append(". ").Append(m.Name);
        if (!string.IsNullOrWhiteSpace(m.ActiveIngredient))
        {
            sb.Append(" (").Append(m.ActiveIngredient).Append(')');
        }
        sb.Append(NL);

        if (entry.Slots.Count > 0)
        {
            foreach (var slot in entry.Slots.OrderBy(SortKey))
            {
                sb.Append("   - ").Append(FormatSlot(slot, m.Unit, c)).Append(NL);
            }
        }
        else
        {
            // Fallback modello legacy dose × frequenza.
            var freq = m.AdministrationsPerDay > 0 ? m.AdministrationsPerDay : 1;
            var doseText = m.DosePerAdministration.ToString("0.##", c);
            if (loc is not null)
            {
                // Chiave completa "{0} {1} × {2} volte al giorno".
                sb.Append("   - ").Append(loc.Get("Reports.Therapy.Fallback.Times",
                    doseText, m.Unit, freq)).Append(NL);
            }
            else
            {
                sb.Append("   - ").Append(doseText).Append(' ').Append(m.Unit);
                sb.Append(string.Format(CultureInfo.InvariantCulture, TimesFallbackIt, freq)).Append(NL);
            }
        }

        if (m.StartDate != default)
        {
            var startText = m.StartDate.ToString("d", c);
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Start", startText));
                if (m.EndDate is { } end)
                {
                    sb.Append(loc.Get("Reports.Therapy.End", end.ToString("d", c)));
                }
            }
            else
            {
                sb.Append(StartLabelIt).Append(startText);
                if (m.EndDate is { } end)
                {
                    sb.Append(EndLabelIt).Append(end.ToString("d", c));
                }
            }
            sb.Append(NL);
        }
        if (!string.IsNullOrWhiteSpace(m.DoctorName))
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Doctor", m.DoctorName)).Append(NL);
            }
            else
            {
                sb.Append(DoctorLabelIt).Append(m.DoctorName).Append(NL);
            }
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Notes", m.Notes)).Append(NL);
            }
            else
            {
                sb.Append(NotesLabelIt).Append(m.Notes).Append(NL);
            }
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

    // Helper: usa il servizio se disponibile, altrimenti il fallback.
    private static string L(ILocalizationService? loc, string key, string fallback)
        => loc?.Get(key) ?? fallback;

    // Helper per stringhe con placeholder {0}: se loc è null, ricade
    // su un default già preformattato dal caller.
    private static string FormatOrDefault(ILocalizationService? loc, string key,
        string fullFallback, params object?[] args)
        => loc?.Get(key, args) ?? fullFallback;
}

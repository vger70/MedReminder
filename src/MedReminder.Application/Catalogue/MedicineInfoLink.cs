namespace MedReminder.Application.Catalogue;

// Public information page of an Italian package on Codifa
// (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §3), or null
// when the code is not a valid AIC. The URL is built only from a code
// that passed the AIC check digit, so nothing read from data reaches the
// shell. No request is ever made by the app: the page opens in the
// user's browser on a click.
public static class MedicineInfoLink
{
    private const string CodifaDetailBase = "https://codifa.it/farmaci/dettaglio/";

    public static Uri? ForNationalCode(string? nationalCode) =>
        ItalianPharmacode.NormalizeAic(nationalCode) is { } code
            ? new Uri(CodifaDetailBase + code)
            : null;
}

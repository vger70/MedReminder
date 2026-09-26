namespace MedReminder.Application.Catalogue;

// "Capture" section of appsettings.json. Tuning knobs for barcode
// input; see docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5B and §8.5.
public sealed class BarcodeCaptureOptions
{
    public const string SectionName = "Capture";

    // A scanner configured without an Enter / Tab suffix never
    // terminates its payload. After this many milliseconds without
    // keystrokes, a fast burst whose content parses is submitted.
    public int HidIdleCompleteMilliseconds { get; set; } = 300;

    // Upper bound on the average interval between keystrokes for the
    // input to count as a scanner burst. Wedge scanners type much
    // faster than people; the bound keeps a user who pauses while
    // typing a code by hand from having a partial code submitted.
    public int HidBurstMaxAverageIntervalMilliseconds { get; set; } = 50;

    // Printable character the scanner has been configured to emit in
    // place of the GS1 group separator (0x1D). Empty disables the
    // mapping. Only the first character is used.
    public string HidGroupSeparatorSubstitute { get; set; } = string.Empty;
}

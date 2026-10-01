using System.Drawing.Printing;
using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.UI.Forms;

namespace MedReminder.UI.Printing;

// Print preview and "Save as PDF" for a print document, with the same
// behaviour and messages as the therapy report (TherapyReportDialog):
// the PDF goes through the built-in "Microsoft Print to PDF" printer
// with the output file set programmatically. Files are written only
// where the user chooses; contents and paths are never logged.
internal static class PrintOutput
{
    public const string PdfPrinterName = "Microsoft Print to PDF";

    // A4 everywhere except regions that use US units (Letter).
    public static PaperKind DefaultPaper()
        => RegionInfo.CurrentRegion.IsMetric ? PaperKind.A4 : PaperKind.Letter;

    public static void Preview(IWin32Window owner, Func<PrintDocument> create, ILocalizationService loc)
    {
        try
        {
            using var printDoc = create();
            using var preview = new PrintPreviewDialog
            {
                Document = printDoc,
                Width = 900,
                Height = 700,
                StartPosition = FormStartPosition.CenterParent,
                UseAntiAlias = true,
            };
            preview.ShowDialog(owner);
        }
        catch (InvalidPrinterException ex)
        {
            UiMessageBox.Show(owner,
                loc.Get("Ui.TherapyReportDialog.NoPrinter", ex.Message),
                loc.Get("Ui.TherapyReportDialog.NoPrinter.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(owner, ex.Message,
                loc.Get("Ui.TherapyReportDialog.PrintError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // The printer spools the job, so the file may appear a moment after
    // Print() returns.
    public static void SaveAsPdf(IWin32Window owner, Func<PrintDocument> create, string dialogTitle,
        string fileName, ILocalizationService loc)
    {
        if (!IsPdfPrinterInstalled())
        {
            ShowPdfPrinterMissing(owner, loc);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = loc.Get("Ui.TherapyReportDialog.PdfDialog.Filter"),
            DefaultExt = "pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = fileName,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            // The user already confirmed the overwrite in the dialog.
            // Removing the old file first means a failed job cannot
            // leave a stale PDF that looks like the new one.
            if (File.Exists(dialog.FileName))
            {
                File.Delete(dialog.FileName);
            }

            using var doc = create();
            doc.PrinterSettings.PrinterName = PdfPrinterName;
            if (!doc.PrinterSettings.IsValid)
            {
                throw new InvalidPrinterException(doc.PrinterSettings);
            }
            doc.PrinterSettings.PrintToFile = true;
            doc.PrinterSettings.PrintFileName = dialog.FileName;
            doc.PrintController = new StandardPrintController();
            doc.Print();
        }
        catch (InvalidPrinterException)
        {
            ShowPdfPrinterMissing(owner, loc);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(owner, ex.Message,
                loc.Get("Ui.TherapyReportDialog.PdfError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void CopyText(IWin32Window owner, string text, ILocalizationService loc)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(owner, ex.Message,
                loc.Get("Ui.TherapyReportDialog.CopyError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowPdfPrinterMissing(IWin32Window owner, ILocalizationService loc)
        => UiMessageBox.Show(owner,
            loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing", PdfPrinterName),
            loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing.Title"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static bool IsPdfPrinterInstalled()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(name, PdfPrinterName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

using System.Drawing.Printing;
using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Reporting;
using MedReminder.UI.Printing;

namespace MedReminder.UI.Forms;

// Dialog that shows the therapy card built by TherapyCardBuilder.
// Allows:
//   - Include notes (off by default: notes are free text and may be
//     private) and choose the paper (A4 / Letter)
//   - Copy to the clipboard
//   - Save to file (.txt)
//   - Save as PDF through the "Microsoft Print to PDF" printer
//   - Print with preview, as a paginated table
// Files are written only where the user chooses in SaveFileDialog;
// the report content and the chosen paths are never logged.
internal sealed class TherapyReportDialog : MedReminderFormBase
{
    // Built-in Windows 10 / 11 virtual printer. It can be removed in
    // "Windows features" / "Optional features"; SaveAsPdf then tells
    // the user how to restore it.
    private const string PdfPrinterName = "Microsoft Print to PDF";

    private readonly ILocalizationService _loc;
    private readonly Func<TherapyCardOptions, TherapyCard> _buildCard;
    private readonly TextBox _reportBox;
    private readonly CheckBox _includeNotes;
    private readonly ComboBox _paper;
    private TherapyCard _card;

    public TherapyReportDialog(Func<TherapyCardOptions, TherapyCard> buildCard, ILocalizationService localization)
    {
        _loc = localization;
        _buildCard = buildCard;
        _card = buildCard(new TherapyCardOptions());

        Text = _loc.Get("Ui.TherapyReportDialog.Title");
        Width = 760;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        Font = new Font("Segoe UI", 9.75F);

        _reportBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
            Text = TherapyReport.RenderText(_card, _loc),
        };

        _includeNotes = new CheckBox
        {
            Text = _loc.Get("Ui.TherapyReportDialog.IncludeNotes"),
            AutoSize = true,
            Checked = false,
            Margin = new Padding(0, 6, 24, 0),
        };
        _includeNotes.CheckedChanged += (_, _) => RebuildCard();

        var paperLabel = new Label
        {
            Text = _loc.Get("Ui.TherapyReportDialog.Paper"),
            AutoSize = true,
            Margin = new Padding(0, 8, 6, 0),
        };
        _paper = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _paper.Items.Add(_loc.Get("Ui.TherapyReportDialog.Paper.A4"));
        _paper.Items.Add(_loc.Get("Ui.TherapyReportDialog.Paper.Letter"));
        _paper.SelectedIndex = DefaultPaper() == PaperKind.Letter ? 1 : 0;

        var optionsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 6, 12, 0),
        };
        optionsPanel.Controls.Add(_includeNotes);
        optionsPanel.Controls.Add(paperLabel);
        optionsPanel.Controls.Add(_paper);

        var copyButton = new Button { Text = _loc.Get("Ui.TherapyReportDialog.Copy"), AutoSize = true, Height = 32 };
        var saveButton = new Button { Text = _loc.Get("Ui.TherapyReportDialog.Save"), AutoSize = true, Height = 32 };
        var pdfButton = new Button { Text = _loc.Get("Ui.TherapyReportDialog.SavePdf"), AutoSize = true, Height = 32 };
        var printButton = new Button { Text = _loc.Get("Ui.TherapyReportDialog.Print"), AutoSize = true, Height = 32 };
        var closeButton = new Button { Text = _loc.Get("Ui.TherapyReportDialog.Close"), DialogResult = DialogResult.OK, AutoSize = true, Height = 32 };

        copyButton.Click += (_, _) => CopyToClipboard();
        saveButton.Click += (_, _) => SaveToFile();
        pdfButton.Click += (_, _) => SaveAsPdf();
        printButton.Click += (_, _) => PrintReport();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(printButton);
        buttonPanel.Controls.Add(pdfButton);
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(copyButton);

        Controls.Add(_reportBox);
        Controls.Add(optionsPanel);
        Controls.Add(buttonPanel);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    // A4 everywhere except regions that use US units (Letter).
    private static PaperKind DefaultPaper()
        => RegionInfo.CurrentRegion.IsMetric ? PaperKind.A4 : PaperKind.Letter;

    private PaperKind SelectedPaper => _paper.SelectedIndex == 1 ? PaperKind.Letter : PaperKind.A4;

    private void RebuildCard()
    {
        _card = _buildCard(new TherapyCardOptions(IncludeNotes: _includeNotes.Checked));
        _reportBox.Text = TherapyReport.RenderText(_card, _loc);
    }

    private TherapyCardPrintDocument CreatePrintDocument()
        => new(_card, SelectedPaper, _loc.Get("Reports.Therapy.Page"));

    private void CopyToClipboard()
    {
        try
        {
            Clipboard.SetText(_reportBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.TherapyReportDialog.CopyError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveToFile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = _loc.Get("Ui.TherapyReportDialog.SaveDialog.Title"),
            Filter = _loc.Get("Ui.TherapyReportDialog.SaveDialog.Filter"),
            DefaultExt = "txt",
            FileName = $"MedReminder-therapy-{DateTime.Now:yyyyMMdd}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _reportBox.Text, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.TherapyReportDialog.SaveError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Prints the table layout to "Microsoft Print to PDF" with the
    // output file set programmatically, so the printer's own "Save
    // Print Output As" prompt does not appear. The printer spools the
    // job, so the file may appear a moment after Print() returns.
    private void SaveAsPdf()
    {
        if (!IsPdfPrinterInstalled())
        {
            MessageBox.Show(this,
                _loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing", PdfPrinterName),
                _loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = _loc.Get("Ui.TherapyReportDialog.PdfDialog.Title"),
            Filter = _loc.Get("Ui.TherapyReportDialog.PdfDialog.Filter"),
            DefaultExt = "pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"MedReminder-therapy-{DateTime.Now:yyyyMMdd}.pdf",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // The user already confirmed the overwrite in the dialog.
            // Removing the old file first means a failed job cannot
            // leave a stale PDF that looks like the new one.
            if (File.Exists(dialog.FileName))
            {
                File.Delete(dialog.FileName);
            }

            using var doc = CreatePrintDocument();
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
            MessageBox.Show(this,
                _loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing", PdfPrinterName),
                _loc.Get("Ui.TherapyReportDialog.PdfPrinterMissing.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.TherapyReportDialog.PdfError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsPdfPrinterInstalled()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(name, PdfPrinterName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void PrintReport()
    {
        try
        {
            using var printDoc = CreatePrintDocument();
            using var preview = new PrintPreviewDialog
            {
                Document = printDoc,
                Width = 900,
                Height = 700,
                StartPosition = FormStartPosition.CenterParent,
                UseAntiAlias = true,
            };
            preview.ShowDialog(this);
        }
        catch (InvalidPrinterException ex)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.TherapyReportDialog.NoPrinter", ex.Message),
                _loc.Get("Ui.TherapyReportDialog.NoPrinter.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.TherapyReportDialog.PrintError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

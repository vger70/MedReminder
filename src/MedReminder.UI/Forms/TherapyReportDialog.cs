using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace MedReminder.UI.Forms;

// Dialog che mostra il testo della scheda terapia generato da
// TherapyReport.Build. Consente:
//   - Copia negli appunti
//   - Salva su file (.txt)
//   - Stampa con anteprima (PrintPreviewDialog + PrintDocument), con
//     paginazione automatica quando il testo eccede una pagina.
internal sealed class TherapyReportDialog : MedReminderFormBase
{
    private readonly TextBox _reportBox;

    public TherapyReportDialog(string reportText)
    {
        Text = "Scheda terapia";
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
            Text = reportText,
        };

        var copyButton = new Button { Text = "Copia negli appunti", AutoSize = true, Height = 32 };
        var saveButton = new Button { Text = "Salva su file…", AutoSize = true, Height = 32 };
        var printButton = new Button { Text = "Stampa…", AutoSize = true, Height = 32 };
        var closeButton = new Button { Text = "Chiudi", DialogResult = DialogResult.OK, AutoSize = true, Height = 32 };

        copyButton.Click += (_, _) => CopyToClipboard();
        saveButton.Click += (_, _) => SaveToFile();
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
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(copyButton);

        Controls.Add(_reportBox);
        Controls.Add(buttonPanel);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private void CopyToClipboard()
    {
        try
        {
            Clipboard.SetText(_reportBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore copia negli appunti",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveToFile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Salva scheda terapia",
            Filter = "File di testo (*.txt)|*.txt|Tutti i file (*.*)|*.*",
            DefaultExt = "txt",
            FileName = $"MedReminder-terapia-{DateTime.Now:yyyyMMdd}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _reportBox.Text, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore salvataggio",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrintReport()
    {
        // Il PrintDocument usa un cursore sul testo residuo. BeginPrint
        // lo ripristina ad ogni ciclo (preview rigenera più volte se
        // l'utente cambia stampante o orientamento). PrintPage misura
        // quanti caratteri entrano nella pagina e li disegna wrappati
        // dentro MarginBounds; il resto va sulla pagina successiva.
        string remaining = _reportBox.Text;
        using var printDoc = new PrintDocument
        {
            DocumentName = "MedReminder — Scheda terapia",
        };
        printDoc.BeginPrint += (_, _) => remaining = _reportBox.Text;
        printDoc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            var bounds = e.MarginBounds;

            using var font = new Font("Consolas", 10F);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                // LineLimit + fit-by-chars → non taglia una riga a metà;
                // se non ci sta intera va sulla pagina dopo.
                FormatFlags = StringFormatFlags.LineLimit,
                Trimming = StringTrimming.Word,
            };

            g.MeasureString(remaining, font, bounds.Size, format,
                out int charsFitted, out int linesFitted);
            if (charsFitted <= 0)
            {
                // Fallback difensivo: evita loop infinito se la
                // misurazione ritorna 0 (es. testo con soli newline).
                e.HasMorePages = false;
                return;
            }

            var pageText = remaining[..charsFitted];
            g.DrawString(pageText, font, Brushes.Black, bounds, format);

            remaining = remaining[charsFitted..];
            e.HasMorePages = remaining.Length > 0;
        };

        try
        {
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
                "Nessuna stampante disponibile.\n\n" + ex.Message,
                "Stampa non disponibile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore stampa",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

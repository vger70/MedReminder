using System.Windows.Forms;

namespace MedReminder.UI.Forms;

// Dialog che mostra il testo della scheda terapia generato da
// TherapyReport.Build. Consente all'utente di copiarlo negli appunti
// o salvarlo su file (spec Incremento 10 — "stampare un promemoria
// per il medico"). Il testo è read-only nella textbox; l'utente
// stampa dall'esterno (blocco note, editor) se serve.
internal sealed class TherapyReportDialog : Form
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
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _reportBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 10F),
            Text = reportText,
        };

        var copyButton = new Button { Text = "Copia negli appunti", AutoSize = true, Height = 32 };
        var saveButton = new Button { Text = "Salva su file…", AutoSize = true, Height = 32 };
        var closeButton = new Button { Text = "Chiudi", DialogResult = DialogResult.OK, AutoSize = true, Height = 32 };

        copyButton.Click += (_, _) => CopyToClipboard();
        saveButton.Click += (_, _) => SaveToFile();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(closeButton);
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
}

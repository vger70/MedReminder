using System.Windows.Forms;

namespace MedReminder.UI.Forms;

// Editor di UN singolo slot di somministrazione (Incremento 10).
// Usato dal MedicineEditDialog per aggiungere/modificare una riga.
// L'utente può indicare orario esatto opzionale + descrizione libera
// (con dropdown di preset comuni: "al mattino", "dopo cena", ecc.).
internal sealed class AdministrationSlotDialog : Form
{
    private static readonly string[] PresetLabels =
    {
        "Al mattino",
        "Al mattino a stomaco vuoto",
        "Prima di colazione",
        "Dopo colazione",
        "A metà mattina",
        "Prima di pranzo",
        "Dopo pranzo",
        "Nel pomeriggio",
        "Prima di cena",
        "Dopo cena",
        "Prima di dormire",
        "Durante la notte",
        "Al bisogno",
    };

    public AdministrationSlotEntry? Result { get; private set; }

    private readonly CheckBox _hasTime;
    private readonly DateTimePicker _timePicker;
    private readonly NumericUpDown _doseBox;
    private readonly ComboBox _labelBox;

    public AdministrationSlotDialog(string unit, decimal suggestedDose, AdministrationSlotEntry? seed = null)
    {
        Text = seed is null ? "Nuovo slot di somministrazione" : "Modifica slot";
        Width = 500;
        Height = 340;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _hasTime = new CheckBox { Text = "Con orario specifico", AutoSize = true, Checked = seed?.Time is not null };
        _timePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Dock = DockStyle.Left,
            Width = 100,
            Value = seed?.Time is { } t ? DateTime.Today.Add(t.ToTimeSpan()) : DateTime.Today.AddHours(8),
            Enabled = seed?.Time is not null,
        };
        _hasTime.CheckedChanged += (_, _) => _timePicker.Enabled = _hasTime.Checked;

        _doseBox = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = 1000m,
            DecimalPlaces = 2,
            Increment = 0.5m,
            Value = seed?.Dose ?? (suggestedDose > 0m ? suggestedDose : 1m),
            Dock = DockStyle.Left,
            Width = 100,
        };

        _labelBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _labelBox.Items.AddRange(PresetLabels);
        _labelBox.Text = seed?.TimingLabel ?? string.Empty;

        var note = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
            MaximumSize = new System.Drawing.Size(440, 0),
            Text = "È sufficiente specificare orario, descrizione, oppure entrambi. " +
                   "La descrizione può essere scelta dai preset o digitata liberamente " +
                   "(es. \"prima di dormire\", \"a stomaco vuoto con acqua\").",
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, "Orario", BuildTimeRow());
        AddRow(table, "Dose", BuildDoseRow(unit));
        AddRow(table, "Descrizione", _labelBox);
        AddRow(table, string.Empty, note);

        var okButton = new Button { Text = "Salva", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += OnConfirm;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(table);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private Control BuildTimeRow()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_hasTime);
        panel.Controls.Add(_timePicker);
        return panel;
    }

    private Control BuildDoseRow(string unit)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_doseBox);
        panel.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 8, 4, 4) });
        return panel;
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var hasTime = _hasTime.Checked;
        var hasLabel = !string.IsNullOrWhiteSpace(_labelBox.Text);
        if (!hasTime && !hasLabel)
        {
            MessageBox.Show(this,
                "Specifica almeno un orario o una descrizione.",
                "Dati mancanti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        TimeOnly? time = hasTime ? TimeOnly.FromDateTime(_timePicker.Value) : null;
        var label = hasLabel ? _labelBox.Text.Trim() : null;
        Result = new AdministrationSlotEntry(time, _doseBox.Value, label);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }
}

// Riga di slot mantenuta dal MedicineEditDialog. È il DTO UI-side che
// viene poi tradotto in AdministrationSlotInput per il use case.
internal sealed record AdministrationSlotEntry(TimeOnly? Time, decimal Dose, string? TimingLabel)
{
    public string TimeDisplay => Time?.ToString("HH:mm") ?? "—";

    public string LabelDisplay => string.IsNullOrEmpty(TimingLabel) ? "—" : TimingLabel;
}

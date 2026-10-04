using MedReminder.Application.Abstractions;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.UI.Forms;

// One dispensation of a repeatable prescription: the day it was collected
// and, if known, the packages. The prescription editor checks the whole
// list with PrescriptionRules when it is saved. Nothing is logged.
internal sealed class DispensationEditDialog : MedReminderFormBase
{
    private readonly DateTimePicker _collected;
    private readonly NumericUpDown _packages;

    public DispensationEditDialog(DateOnly collectedOn, int? packages, bool isNew, ILocalizationService localization)
    {
        Text = localization.Get(isNew ? "Ui.DispensationDialog.Title.New" : "Ui.DispensationDialog.Title.Edit");
        Width = 400;
        Height = 220;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        _collected = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = collectedOn.ToDateTime(TimeOnly.MinValue),
            Width = 150,
            Dock = DockStyle.Left,
        };
        _packages = new NumericUpDown
        {
            Minimum = 0,
            Maximum = PrescriptionRules.MaxPackages,
            Value = packages ?? 0,
            Width = 80,
            Dock = DockStyle.Left,
        };

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, localization.Get("Ui.DispensationDialog.CollectedOn"), _collected);
        DialogLayout.AddRow(table, localization.Get("Ui.DispensationDialog.Packages"), _packages);

        var okButton = DialogLayout.Button(localization.Get("Common.Save"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(localization.Get("Common.Cancel"), DialogResult.Cancel);
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
    }

    public DateOnly CollectedOn => DateOnly.FromDateTime(_collected.Value.Date);

    public int? Packages => _packages.Value > 0 ? (int)_packages.Value : null;
}

using System.Diagnostics;
using System.Text.RegularExpressions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.UI.Controls;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// Exact lookup used by the dialog to re-hydrate AIFA leaflet / SPC
// URLs on Edit mode. Returns null when the seeded national code is
// no longer present in the local catalogue (stale link after a
// snapshot refresh) — the dialog then silently hides the row.
internal delegate Task<ReferenceMedicine?> ReferenceMedicineLookupAsync(
    CountryCode country, string nationalCode, CancellationToken cancellationToken);

// Dependencies the medicine form needs to offer catalogue-driven
// autocomplete (M2). Null when the feature flag Catalogue:Enabled is
// off — the dialog then falls back to plain text entry.
internal sealed record CatalogueAutocompleteContext(
    MedicineAutocompleteBox.ReferenceMedicineSearchAsync SearchCommercialName,
    MedicineAutocompleteBox.ReferenceMedicineSearchAsync SearchActiveIngredient,
    ReferenceMedicineLookupAsync LookupByNationalCode,
    CountryCode Country);

// Dependencies of the "Scan barcode" button (A2). Offered only
// together with a CatalogueAutocompleteContext: a scan is useful only
// when the code can be looked up in the reference catalogue.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §3.4 and §4.
internal sealed record BarcodeScanContext(
    IBarcodeParser Parser,
    BarcodeCaptureOptions Options,
    ILogger Logger);

// Dialog used both for "new medicine" (Mode=Create) and for "edit"
// (Mode=Edit). At the end it exposes Result: null if the user
// cancels, otherwise a DTO with the valid fields. Persistence is
// performed by the caller (MainForm) by invoking the appropriate
// use case.
internal sealed class MedicineEditDialog : MedReminderFormBase
{
    public enum EditMode { Create, Edit }

    public MedicineEditResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly MedicineAutocompleteBox _nameBox;
    private readonly MedicineAutocompleteBox _ingredientBox;
    private readonly TextBox _packageBox;
    private readonly ComboBox _unitBox;
    private readonly NumericUpDown _doseBox;
    private readonly NumericUpDown _adminPerDayBox;
    private readonly DateTimePicker _startDatePicker;
    private readonly CheckBox _hasEndDate;
    private readonly DateTimePicker _endDatePicker;
    private readonly NumericUpDown _thresholdBox;
    private readonly TextBox _doctorBox;
    private readonly TextBox _notesBox;
    private readonly NumericUpDown _initialQtyBox;
    private readonly CheckBox _channelWindows;
    private readonly CheckBox _channelEmail;
    // A5 dose-time reminder opt-in. Enabled only when the medicine has
    // at least one timed slot AND stock > 0 (ANALYSIS-A5 §5.2); a
    // save-time clamp forces it back to false when disabled (§5.3).
    private readonly CheckBox _remindOnDose;
    private readonly Label _remindOnDoseHelp;
    private readonly ToolTip _remindOnDoseTip;
    private readonly CheckBox _isActiveBox;

    // Current on-hand stock for the RemindOnDose availability gate.
    // Create mode reads the live _initialQtyBox instead; in Edit mode
    // this carries the value MainForm computed from the stock ledger.
    private readonly decimal _currentStock;
    private readonly ListView _slotsList;
    private readonly Label _slotsSummary;
    private readonly List<AdministrationSlotEntry> _slots = new();
    private readonly EditMode _mode;

    // Simple / Advanced schedule editor (A1). Present in both Create
    // and Edit mode. In Edit mode it is seeded from the therapy's
    // current schedule; saving a changed shape creates a new versioned
    // schedule entry (see MainForm.ShowEditMedicineAsync). See
    // docs/ANALYSIS-A1-REGIMENS.md §5.2 and
    // docs/ANALYSIS-A1-STEPPED-TAPER.md §5.
    private readonly SchedulePanel? _schedulePanel;

    // Schedule the dialog was seeded with (Edit mode). Applied in
    // OnLoad, once the panel's controls have a native handle.
    private readonly Schedule? _seedSchedule;

    // Populated when the user picks a catalogue row; cleared as soon
    // as they diverge from it by editing either autocomplete field.
    private string? _linkedNationalCode;
    private AtcCode? _linkedAtcCode;
    private Guid? _linkedReferenceMedicineId;

    // AIFA leaflet / SPC links surfaced under the "Principio attivo"
    // row. Non-null only for the Italian catalogue (country == "IT"):
    // the other supported catalogues (EMA, ANSM, AEMPS) do not carry
    // per-package leaflet / SPC URLs. The row starts hidden and is
    // shown as soon as at least one URL is available (either from a
    // fresh autocomplete pick or from the Edit-mode seed lookup).
    private readonly CountryCode? _documentsCountry;
    private readonly ReferenceMedicineLookupAsync? _documentsLookup;
    private readonly FlowLayoutPanel? _documentsRow;
    private readonly LinkLabel? _leafletLink;
    private readonly LinkLabel? _spcLink;
    private string? _pendingSeedNationalCode;

    // Barcode scan (A2). Both non-null only when the catalogue is on
    // and the caller supplied a scan context.
    private readonly CatalogueAutocompleteContext? _catalogueContext;
    private readonly BarcodeScanContext? _barcodeContext;

    public MedicineEditDialog(
        EditMode mode,
        ILocalizationService localization,
        MedicineEditResult? seed = null,
        CatalogueAutocompleteContext? catalogueContext = null,
        decimal currentStock = 0m,
        BarcodeScanContext? barcodeContext = null)
    {
        _loc = localization;
        _mode = mode;
        _currentStock = currentStock;
        _catalogueContext = catalogueContext;
        _barcodeContext = catalogueContext is null ? null : barcodeContext;
        Text = _loc.Get(mode == EditMode.Create
            ? "Ui.MedicineEditDialog.Title.New"
            : "Ui.MedicineEditDialog.Title.Edit");
        // Wider than the original 620 so the catalogue autocomplete
        // dropdown has enough room to show AIFA rows without heavy
        // horizontal scrolling: a row like
        // "TACHIPIRINA — PARACETAMOLO — 500 MG COMPRESSE 20 …"
        // needs ~700 px to be legible. The dialog stays FixedDialog
        // so this size is what the user gets.
        Width = 880;
        Height = 820;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _nameBox = new MedicineAutocompleteBox { Dock = DockStyle.Fill };
        _ingredientBox = new MedicineAutocompleteBox { Dock = DockStyle.Fill };
        _packageBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };

        // Wire the autocomplete only when the catalogue is on and a
        // search delegate is available. Otherwise the boxes stay in
        // pure text-entry mode (nothing bound, no dropdown ever
        // shown) so the dialog remains usable when the feature flag
        // is off.
        if (catalogueContext is not null)
        {
            _nameBox.BindSearch(
                MedicineAutocompleteBox.SearchField.CommercialName,
                catalogueContext.SearchCommercialName,
                localization,
                catalogueContext.Country);
            _ingredientBox.BindSearch(
                MedicineAutocompleteBox.SearchField.ActiveIngredient,
                catalogueContext.SearchActiveIngredient,
                localization,
                catalogueContext.Country);

            _nameBox.ReferenceSelected += OnReferenceSelected;
            _ingredientBox.ReferenceSelected += OnReferenceSelected;
            _nameBox.TextEdited += (_, _) => ClearReferenceLinkage();
            _ingredientBox.TextEdited += (_, _) => ClearReferenceLinkage();

            // LINK_FI / LINK_RCP only exist on AIFA rows: keep the row
            // out of the layout entirely for non-Italian catalogues so
            // the label + empty flow panel don't leave dead space.
            if (catalogueContext.Country.Value == "IT")
            {
                _documentsCountry = catalogueContext.Country;
                _documentsLookup = catalogueContext.LookupByNationalCode;
                _leafletLink = MakeDocumentLink(_loc.Get("Ui.MedicineEditDialog.Documents.Leaflet"));
                _spcLink = MakeDocumentLink(_loc.Get("Ui.MedicineEditDialog.Documents.Spc"));
                _documentsRow = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.LeftToRight,
                    AutoSize = true,
                    WrapContents = false,
                    Margin = new Padding(0),
                    Visible = false,
                };
                _documentsRow.Controls.Add(_leafletLink);
                _documentsRow.Controls.Add(_spcLink);
            }
        }
        _unitBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        // Keep the DropDown style: the AIFA FORMA field carries many
        // pharmaceutical forms this list doesn't enumerate ("collirio",
        // "polvere per soluzione orale", …). When the map below finds
        // no hit the raw FORMA string is put here verbatim.
        _unitBox.Items.AddRange(new object[]
        {
            _loc.Get("Ui.MedicineEditDialog.Unit.Tablets"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Capsules"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Sachets"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Ml"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Doses"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Vials"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Granules"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Drops"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Suppositories"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Patches"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Grams"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Puffs"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Ampoules"),
        });
        _doseBox = MakeDecimalUpDown(0.01m, 1000m, 2, initial: 1m);
        _adminPerDayBox = MakeIntUpDown(1, 24, initial: 2);
        _startDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Value = DateTime.Today };
        _hasEndDate = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.HasEndDateFull"), AutoSize = true };
        _endDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Enabled = false, Value = DateTime.Today.AddMonths(1) };
        _hasEndDate.CheckedChanged += (_, _) => _endDatePicker.Enabled = _hasEndDate.Checked;
        _thresholdBox = MakeIntUpDown(0, 365, initial: 7);
        _doctorBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _notesBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical, MaxLength = 1000 };
        _initialQtyBox = MakeDecimalUpDown(0m, 100000m, 2, initial: 0m);
        _channelWindows = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.NotifyWindowsShort"), AutoSize = true, Checked = true };
        _channelEmail = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.NotifyEmailShort"), AutoSize = true, Checked = false };
        _remindOnDose = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.RemindOnDose"), AutoSize = true, Checked = false };
        _remindOnDoseHelp = new Label { AutoSize = true, ForeColor = System.Drawing.Color.DarkGray, Text = string.Empty, Margin = new Padding(20, 0, 4, 4) };
        _remindOnDoseTip = new ToolTip();
        _isActiveBox = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.IsActive"), AutoSize = true, Checked = true };

        _slotsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = true,
            Height = 140,
        };
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnTime"), 80);
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnDose"), 80);
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnLabel"), 300);
        _slotsList.DoubleClick += (_, _) => EditSelectedSlot();
        _slotsSummary = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
            Text = string.Empty,
        };

        // Dock=Top (not Fill) + AutoSize lets the table grow taller
        // than the surrounding AutoScroll Panel — Dock=Fill pins the
        // table height to the viewport and defeats the scrollbar,
        // which was why the Advanced sub-panel was hidden by the
        // Save / Cancel row on the previous PR round.
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Name"), BuildNameRow());
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.ActiveIngredient"), _ingredientBox);
        if (_documentsRow is not null)
        {
            AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Documents"), _documentsRow);
        }
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Package"), _packageBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Unit"), _unitBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.DosePerAdmin"), _doseBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.AdminsPerDay"), _adminPerDayBox);

        // A1: Simple / Advanced schedule editor lives right after the
        // dose / administrations fields it complements. Wired in both
        // Create and Edit mode. In Edit mode it is seeded (in OnLoad)
        // from the therapy's current schedule, and saving a changed
        // shape creates a new versioned schedule entry so the timeline
        // is preserved (see MainForm.ShowEditMedicineAsync). Overflow
        // (Weekly grid / Tapering summary) is handled by the outer
        // AutoScroll Panel — no dynamic dialog resize needed.
        _schedulePanel = new SchedulePanel(_loc);
        _schedulePanel.ModeChanged += (_, _) => SyncSimpleControlsEnabled();
        AddRow(table, _loc.Get("Ui.Schedule.Mode.Label"), _schedulePanel.Root);

        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.StartDateFull"), _startDatePicker);
        AddRow(table, string.Empty, _hasEndDate);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.EndDateFull"), _endDatePicker);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.WarnThreshold"), _thresholdBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.DoctorFull"), _doctorBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Notes"), _notesBox);
        if (_mode == EditMode.Create)
        {
            AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.InitialStock"), _initialQtyBox);
        }
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Channels"), BuildChannelsPanel());
        AddRow(table, string.Empty, BuildRemindOnDosePanel());
        if (_mode == EditMode.Edit)
        {
            AddRow(table, string.Empty, _isActiveBox);
        }

        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.SlotsOptional"), BuildSlotsPanel());
        AddRow(table, string.Empty, _slotsSummary);
        UpdateSlotsSummary();

        // A5: the initial-stock field feeds the RemindOnDose gate in
        // Create mode, so re-evaluate availability whenever it changes.
        _initialQtyBox.ValueChanged += (_, _) => UpdateRemindOnDoseAvailability();
        UpdateRemindOnDoseAvailability();

        // Now that every control is on the table, honor the initial
        // Simple selection by disabling nothing yet — SyncSimpleControlsEnabled
        // reads the current SchedulePanel state (Simple by default).
        if (_schedulePanel is not null) SyncSimpleControlsEnabled();

        var okButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Save"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += OnConfirmClick;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(table);

        Controls.Add(scroll);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        _seedSchedule = seed?.InitialSchedule;
        if (seed is not null) ApplySeed(seed);
    }

    // Fired once the dialog becomes visible.
    //
    // 1) Seed the schedule editor with the therapy's current schedule
    //    (Edit mode). Deferred to OnLoad, not the constructor, because
    //    NumericUpDown values assigned to still parent-less controls
    //    are not reflected once the controls are realized — the panel
    //    would show the right regime type but reset every dose /
    //    duration / day field to its default (same discipline as
    //    ChangeScheduleDialog, fix bda16f5). This was previously fixed
    //    in PR #42 and removed again in commit 0dbd0a4; reinstated
    //    here so F2 / double-click / Modifica reopens the SchedulePanel
    //    on the therapy's saved regime (weekly, cyclic, linear taper,
    //    stepped taper, PRN) instead of resetting to Simple.
    //
    // 2) In Edit mode with a seeded NationalCode, look up the current
    //    AIFA row to surface its LINK_FI / LINK_RCP. Fire-and-forget
    //    by design — a failed lookup must never block the dialog.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_schedulePanel is not null)
        {
            _schedulePanel.ApplySchedule(_seedSchedule);
            SyncSimpleControlsEnabled();
        }
        _ = HydrateSeededDocumentsAsync();
    }

    private void ApplySeed(MedicineEditResult seed)
    {
        _nameBox.InputText = seed.Name;
        _ingredientBox.InputText = seed.ActiveIngredient ?? string.Empty;
        _packageBox.Text = seed.Package ?? string.Empty;
        _linkedNationalCode = seed.NationalCode;
        _linkedAtcCode = seed.AtcCode;
        _linkedReferenceMedicineId = seed.LinkedReferenceMedicineId;
        _pendingSeedNationalCode = seed.NationalCode;
        _unitBox.Text = seed.Unit;
        _doseBox.Value = seed.DosePerAdministration;
        _adminPerDayBox.Value = seed.AdministrationsPerDay;
        _startDatePicker.Value = seed.StartDate.ToDateTime(TimeOnly.MinValue);
        _hasEndDate.Checked = seed.EndDate.HasValue;
        _endDatePicker.Enabled = seed.EndDate.HasValue;
        if (seed.EndDate is { } end) _endDatePicker.Value = end.ToDateTime(TimeOnly.MinValue);
        _thresholdBox.Value = seed.ThresholdDays;
        _doctorBox.Text = seed.DoctorName ?? string.Empty;
        _notesBox.Text = seed.Notes ?? string.Empty;
        _initialQtyBox.Value = seed.InitialQuantity;
        _channelWindows.Checked = (seed.NotificationChannels & NotificationChannels.Windows) != 0;
        _channelEmail.Checked = (seed.NotificationChannels & NotificationChannels.Email) != 0;
        _remindOnDose.Checked = seed.RemindOnDose;
        _isActiveBox.Checked = seed.IsActive;

        _slots.Clear();
        if (seed.Slots is not null)
        {
            _slots.AddRange(seed.Slots);
        }
        // RefreshSlotsList re-evaluates the RemindOnDose gate, which
        // clears the seeded checkbox if the medicine no longer has a
        // timed slot or is out of stock.
        RefreshSlotsList();
    }

    private void OnConfirmClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.InputText))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.Validation.NameRequired"),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitBox.Text))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.Validation.UnitRequired"),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (_hasEndDate.Checked && _endDatePicker.Value.Date < _startDatePicker.Value.Date)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.EndBeforeStart"),
                _loc.Get("Ui.MedicineEditDialog.InconsistentData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var channels = NotificationChannels.None;
        if (_channelWindows.Checked) channels |= NotificationChannels.Windows;
        if (_channelEmail.Checked) channels |= NotificationChannels.Email;

        // A5 save-time clamp (ANALYSIS-A5 §5.3): RemindOnDose may be
        // true only while the checkbox is actually enabled — i.e. a
        // timed slot exists AND stock > 0. A disabled checkbox always
        // persists false regardless of any stale seeded value.
        var remindOnDose = _remindOnDose.Enabled && _remindOnDose.Checked;

        // A1: build the Schedule value object when Advanced is
        // selected. Simple mode keeps InitialSchedule = null so
        // AddMedicine constructs FixedDaily from Dose × Admin
        // exactly like pre-A1.
        Schedule? initialSchedule = null;
        if (_schedulePanel is { AdvancedSelected: true })
        {
            initialSchedule = _schedulePanel.TryBuildSchedule(out var scheduleError);
            if (initialSchedule is null)
            {
                MessageBox.Show(this,
                    scheduleError ?? _loc.Get("Ui.Schedule.Validation.Generic"),
                    _loc.Get("Ui.MedicineEditDialog.InconsistentData.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }

        Result = new MedicineEditResult(
            Name: _nameBox.InputText.Trim(),
            ActiveIngredient: NullIfBlank(_ingredientBox.InputText),
            Package: NullIfBlank(_packageBox.Text),
            Unit: _unitBox.Text.Trim(),
            DosePerAdministration: _doseBox.Value,
            AdministrationsPerDay: (int)_adminPerDayBox.Value,
            StartDate: DateOnly.FromDateTime(_startDatePicker.Value.Date),
            EndDate: _hasEndDate.Checked ? DateOnly.FromDateTime(_endDatePicker.Value.Date) : null,
            ThresholdDays: (int)_thresholdBox.Value,
            DoctorName: NullIfBlank(_doctorBox.Text),
            Notes: NullIfBlank(_notesBox.Text),
            InitialQuantity: _initialQtyBox.Value,
            NotificationChannels: channels,
            IsActive: _isActiveBox.Checked,
            RemindOnDose: remindOnDose,
            // In Advanced mode slots × non-fixed combinations are out
            // of scope (§3.1) — drop the slots so the schedule owns
            // the daily rate uniquely.
            Slots: initialSchedule is null ? [.. _slots] : new List<AdministrationSlotEntry>(),
            NationalCode: _linkedNationalCode,
            AtcCode: _linkedAtcCode,
            LinkedReferenceMedicineId: _linkedReferenceMedicineId,
            InitialSchedule: initialSchedule);
    }

    // Disables the Simple-mode inputs (dose, admin/day, slot buttons)
    // whenever the user flips into Advanced, so it is unambiguous
    // which set of controls drives the projection. Called from the
    // SchedulePanel.ModeChanged event.
    private void SyncSimpleControlsEnabled()
    {
        if (_schedulePanel is null) return;
        var simple = !_schedulePanel.AdvancedSelected;
        _doseBox.Enabled = simple;
        _adminPerDayBox.Enabled = simple;
        _slotsList.Enabled = simple;
    }

    // Picking a catalogue row on either side populates the sibling
    // free-text fields (commercial name, active ingredient, package,
    // unit) and caches the reference linkage until the user diverges
    // by editing one of the two autocomplete boxes.
    private void OnReferenceSelected(object? sender, ReferenceMedicineSelectedEventArgs e) =>
        ApplyReference(e.Reference);

    // Shared by the autocomplete pick and the barcode scan, so both
    // leave the dialog in the same state.
    private void ApplyReference(ReferenceMedicine reference)
    {

        // Extend the commercial name with strength + pharmaceutical
        // form when we can compose them from the AIFA row — leaves
        // enough context in the saved Name to distinguish, e.g., a
        // 500 mg Tachipirina from a 1000 mg one.
        _nameBox.InputText = BuildExtendedCommercialName(reference);

        // Active ingredient side: mirror the reference row.
        _ingredientBox.InputText = reference.ActiveIngredients.Count == 0
            ? string.Empty
            : string.Join(" / ", reference.ActiveIngredients.Select(a => a.Name));

        // Package: the full AIFA DESCRIZIONE carries strength + form +
        // pack count + primary packaging material — that's what a user
        // sees on the box.
        if (!string.IsNullOrWhiteSpace(reference.Dosage))
        {
            _packageBox.Text = reference.Dosage;
        }

        // Unit: map FORMA to a localised unit label when the mapping
        // knows it; otherwise put the raw FORMA verbatim (the combo is
        // free-text, so any string is legal). Never overwrite an
        // existing unit with an empty value.
        var unitLabel = MapPharmaceuticalFormToUnit(reference.PharmaceuticalForm, _loc);
        if (!string.IsNullOrEmpty(unitLabel))
        {
            _unitBox.Text = unitLabel;
        }

        _linkedNationalCode = reference.NationalCode;
        _linkedAtcCode = reference.ActiveIngredients
            .Select(a => a.Atc)
            .FirstOrDefault(a => a.HasValue);
        _linkedReferenceMedicineId = reference.Id;

        UpdateDocumentLinks(reference.LinkLeaflet, reference.LinkSummaryOfProductCharacteristics);
    }

    // "TACHIPIRINA" + " 500 MG" + " compresse" → "TACHIPIRINA 500 MG compresse".
    // Falls back to just CommercialName when neither strength nor form
    // is available.
    private static string BuildExtendedCommercialName(ReferenceMedicine reference)
    {
        var parts = new List<string>(3) { reference.CommercialName };
        var strength = ExtractStrengthToken(reference.Dosage);
        if (!string.IsNullOrEmpty(strength))
        {
            parts.Add(strength);
        }
        if (!string.IsNullOrWhiteSpace(reference.PharmaceuticalForm))
        {
            parts.Add(reference.PharmaceuticalForm.Trim().ToLowerInvariant());
        }
        return string.Join(" ", parts);
    }

    // Extracts the first strength token from an AIFA DESCRIZIONE
    // string ("500 MG COMPRESSE 20 …" → "500 MG"). Matches an integer
    // or decimal number followed by a common pharmaceutical unit.
    // Deliberately conservative: on no match, returns null so the
    // extended name stays clean rather than picking up garbage.
    private static readonly Regex StrengthRegex = new(
        @"\b\d+([.,]\d+)?\s*(mg|g|mcg|µg|ug|ml|l|ui|iu|%)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string? ExtractStrengthToken(string? dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage)) return null;
        var match = StrengthRegex.Match(dosage);
        return match.Success ? match.Value : null;
    }

    // AIFA FORMA is a free-text column with many variants. We match
    // by contains on lowercased text against a short list of stems
    // that cover the common cases; anything else falls through to
    // returning the raw FORMA (the combo is DropDown, so free text
    // is fine).
    private static string? MapPharmaceuticalFormToUnit(string? forma, ILocalizationService loc)
    {
        if (string.IsNullOrWhiteSpace(forma)) return null;
        var lower = forma.ToLowerInvariant();

        static bool Has(string haystack, string needle) => haystack.Contains(needle, StringComparison.Ordinal);

        if (Has(lower, "compress")) return loc.Get("Ui.MedicineEditDialog.Unit.Tablets");
        if (Has(lower, "capsul")) return loc.Get("Ui.MedicineEditDialog.Unit.Capsules");
        if (Has(lower, "bustin")) return loc.Get("Ui.MedicineEditDialog.Unit.Sachets");
        if (Has(lower, "granulat")) return loc.Get("Ui.MedicineEditDialog.Unit.Granules");
        if (Has(lower, "gocce") || Has(lower, "collir")) return loc.Get("Ui.MedicineEditDialog.Unit.Drops");
        if (Has(lower, "sciropp") || Has(lower, "sospension") || Has(lower, "soluzion") || Has(lower, "sospens"))
            return loc.Get("Ui.MedicineEditDialog.Unit.Ml");
        if (Has(lower, "supposte") || Has(lower, "supposta")) return loc.Get("Ui.MedicineEditDialog.Unit.Suppositories");
        if (Has(lower, "cerott")) return loc.Get("Ui.MedicineEditDialog.Unit.Patches");
        if (Has(lower, "crema") || Has(lower, "unguent") || Has(lower, "pomata") || Has(lower, "gel"))
            return loc.Get("Ui.MedicineEditDialog.Unit.Grams");
        if (Has(lower, "spray") || Has(lower, "erogazion") || Has(lower, "polvere per inalazion"))
            return loc.Get("Ui.MedicineEditDialog.Unit.Puffs");
        if (Has(lower, "fiala") || Has(lower, "fiale")) return loc.Get("Ui.MedicineEditDialog.Unit.Ampoules");
        if (Has(lower, "flacon")) return loc.Get("Ui.MedicineEditDialog.Unit.Vials");
        if (Has(lower, "polvere")) return loc.Get("Ui.MedicineEditDialog.Unit.Sachets");

        // Unknown FORMA: keep the AIFA text as-is so nothing is lost.
        return forma.Trim();
    }

    // Commercial-name box, plus the "Scan barcode" button when a scan
    // context is available.
    private Control BuildNameRow()
    {
        if (_barcodeContext is null) return _nameBox;

        var scanButton = new Button
        {
            Text = _loc.Get("Ui.MedicineEditDialog.ScanBarcode"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(6, 0, 0, 0),
        };
        new ToolTip().SetToolTip(scanButton, _loc.Get("Ui.MedicineEditDialog.ScanBarcode.Tooltip"));
        scanButton.Click += OnScanBarcodeClick;

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(_nameBox, 0, 0);
        row.Controls.Add(scanButton, 1, 0);
        return row;
    }

    // Reads a barcode, looks the code up in the reference catalogue
    // of the profile's country and, on a hit, fills the form exactly
    // as an autocomplete pick does. A miss changes nothing.
    // See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §4.2.
    private async void OnScanBarcodeClick(object? sender, EventArgs e)
    {
        if (_barcodeContext is null || _catalogueContext is null) return;

        BarcodeContent? content;
        using (var dialog = new BarcodeScanDialog(
            _loc, _barcodeContext.Parser, _barcodeContext.Options, _barcodeContext.Logger))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            content = dialog.Result;
        }
        var code = content?.LookupKey;
        if (code is null) return;

        ReferenceMedicine? reference;
        try
        {
            reference = await _catalogueContext.LookupByNationalCode(
                _catalogueContext.Country, code, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // async void handler: never let the exception escape.
            _barcodeContext.Logger.LogError(ex, "Catalogue lookup after a barcode scan failed.");
            if (IsDisposed) return;
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.ScanBarcode.LookupError"),
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (IsDisposed) return;

        if (reference is null)
        {
            _barcodeContext.Logger.LogInformation(
                "Scanned code not found in the {Country} catalogue.", _catalogueContext.Country.Value);
            // A MessageBox supports Ctrl+C, so the user can copy the code.
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.ScanBarcode.NotInCatalogue", code),
                _loc.Get("Common.Information"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplyReference(reference);
    }

    private void ClearReferenceLinkage()
    {
        _linkedNationalCode = null;
        _linkedAtcCode = null;
        _linkedReferenceMedicineId = null;

        UpdateDocumentLinks(null, null);
    }

    private Control BuildChannelsPanel()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_channelWindows);
        panel.Controls.Add(_channelEmail);
        return panel;
    }

    // A5: the RemindOnDose opt-in plus its one-line helper / disabled
    // reason label, stacked vertically so the reason sits under the
    // checkbox. Uses the medicine's configured channels for delivery.
    private Control BuildRemindOnDosePanel()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_remindOnDose);
        panel.Controls.Add(_remindOnDoseHelp);
        return panel;
    }

    // Re-evaluates the RemindOnDose availability gate (ANALYSIS-A5 §5.2):
    // enabled only when the medicine has at least one timed slot AND
    // stock > 0. When disabled the checkbox is cleared and a localized
    // reason is shown both inline and as a tooltip. Called at open and
    // whenever the slots or the initial-stock field change.
    private void UpdateRemindOnDoseAvailability()
    {
        var hasTimedSlot = _slots.Any(s => s.Time.HasValue);
        var hasStock = CurrentStockForGate() > 0m;
        var enabled = Medicine.CanRemindOnDose(hasTimedSlot, CurrentStockForGate());

        _remindOnDose.Enabled = enabled;
        string helpKey;
        if (enabled)
        {
            helpKey = "Ui.MedicineEditDialog.RemindOnDose.Help";
        }
        else
        {
            _remindOnDose.Checked = false;
            // No-timed-slot takes precedence over no-stock in the message.
            helpKey = !hasTimedSlot
                ? "Ui.MedicineEditDialog.RemindOnDose.DisabledNoTime"
                : "Ui.MedicineEditDialog.RemindOnDose.DisabledNoStock";
        }

        var helpText = _loc.Get(helpKey);
        _remindOnDoseHelp.Text = helpText;
        _remindOnDoseTip.SetToolTip(_remindOnDose, helpText);
    }

    // Stock used by the RemindOnDose gate: the live initial-stock box
    // in Create mode, the ledger value MainForm passed in Edit mode.
    private decimal CurrentStockForGate()
        => _mode == EditMode.Create ? _initialQtyBox.Value : _currentStock;

    private Control BuildSlotsPanel()
    {
        var addButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.AddButton"), AutoSize = true, Height = 26 };
        var editButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.EditButton"), AutoSize = true, Height = 26 };
        var removeButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.RemoveButton"), AutoSize = true, Height = 26 };
        addButton.Click += (_, _) => AddSlot();
        editButton.Click += (_, _) => EditSelectedSlot();
        removeButton.Click += (_, _) => RemoveSelectedSlot();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(addButton);
        buttons.Controls.Add(editButton);
        buttons.Controls.Add(removeButton);

        var container = new Panel { Height = 180, Width = 400 };
        _slotsList.Dock = DockStyle.Fill;
        container.Controls.Add(_slotsList);
        container.Controls.Add(buttons);
        return container;
    }

    private string EffectiveUnit()
    {
        var t = _unitBox.Text.Trim();
        return t.Length == 0 ? _loc.Get("Ui.MedicineEditDialog.DefaultUnit") : t;
    }

    private void AddSlot()
    {
        using var dialog = new AdministrationSlotDialog(EffectiveUnit(), _doseBox.Value, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _slots.Add(dialog.Result);
        RefreshSlotsList();
    }

    private void EditSelectedSlot()
    {
        var index = SelectedSlotIndex();
        if (index < 0) return;
        using var dialog = new AdministrationSlotDialog(EffectiveUnit(), _doseBox.Value, _loc, _slots[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _slots[index] = dialog.Result;
        RefreshSlotsList();
    }

    private void RemoveSelectedSlot()
    {
        var index = SelectedSlotIndex();
        if (index < 0) return;
        _slots.RemoveAt(index);
        RefreshSlotsList();
    }

    private int SelectedSlotIndex()
    {
        return _slotsList.SelectedIndices.Count == 0 ? -1 : _slotsList.SelectedIndices[0];
    }

    private void RefreshSlotsList()
    {
        _slotsList.BeginUpdate();
        _slotsList.Items.Clear();
        foreach (var slot in _slots)
        {
            var row = new ListViewItem(slot.TimeDisplay);
            row.SubItems.Add(slot.Dose.ToString("0.##"));
            row.SubItems.Add(slot.LabelDisplay);
            _slotsList.Items.Add(row);
        }
        _slotsList.EndUpdate();
        UpdateSlotsSummary();
        // Adding / removing a timed slot can flip the RemindOnDose gate.
        UpdateRemindOnDoseAvailability();
    }

    private void UpdateSlotsSummary()
    {
        if (_slots.Count == 0)
        {
            _slotsSummary.Text = _loc.Get("Ui.MedicineEditDialog.Slots.SummaryNone");
            return;
        }
        var total = _slots.Sum(s => s.Dose);
        _slotsSummary.Text = _loc.Get("Ui.MedicineEditDialog.Slots.Summary",
            _slots.Count, total.ToString("0.##"), EffectiveUnit());
    }

    private static NumericUpDown MakeDecimalUpDown(decimal min, decimal max, int decimals, decimal initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = 0.5m,
            Value = initial,
            Dock = DockStyle.Left,
            Width = 120,
        };

    private static NumericUpDown MakeIntUpDown(int min, int max, int initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = 0,
            Increment = 1,
            Value = initial,
            Dock = DockStyle.Left,
            Width = 120,
        };

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task HydrateSeededDocumentsAsync()
    {
        if (_documentsLookup is null || _documentsCountry is null) return;
        var code = _pendingSeedNationalCode;
        _pendingSeedNationalCode = null;
        if (string.IsNullOrWhiteSpace(code)) return;

        ReferenceMedicine? row;
        try
        {
            row = await _documentsLookup(_documentsCountry.Value, code, CancellationToken.None);
        }
        catch
        {
            // Best-effort: a failing catalogue lookup must never crash
            // the medicine dialog. The row simply stays hidden and the
            // user can still edit their medicine.
            return;
        }
        if (row is null || IsDisposed) return;
        UpdateDocumentLinks(row.LinkLeaflet, row.LinkSummaryOfProductCharacteristics);
    }

    private LinkLabel MakeDocumentLink(string text)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Visible = false,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, 4, 16, 4),
        };
        link.LinkClicked += OnDocumentLinkClicked;
        return link;
    }

    private void UpdateDocumentLinks(string? leafletUrl, string? spcUrl)
    {
        if (_documentsRow is null || _leafletLink is null || _spcLink is null) return;

        var safeLeaflet = IsSafeAifaUrl(leafletUrl) ? leafletUrl : null;
        var safeSpc = IsSafeAifaUrl(spcUrl) ? spcUrl : null;

        _leafletLink.Tag = safeLeaflet;
        _spcLink.Tag = safeSpc;
        _leafletLink.LinkVisited = false;
        _spcLink.LinkVisited = false;
        _leafletLink.Visible = safeLeaflet is not null;
        _spcLink.Visible = safeSpc is not null;
        _documentsRow.Visible = safeLeaflet is not null || safeSpc is not null;
    }

    private void OnDocumentLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (sender is not LinkLabel link) return;
        if (link.Tag is not string url) return;
        if (!IsSafeAifaUrl(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            link.LinkVisited = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.Documents.OpenError", ex.Message),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Restrict browser-launched URLs to the AIFA-owned domains that
    // ship these links in the open-data feed. A corrupted or spoofed
    // catalogue row could otherwise become a redirect vector when the
    // user clicks the label. The scheme must be HTTPS, and the host
    // must be one of AIFA's own domains (bare or subdomain).
    private static bool IsSafeAifaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host == "aifa.gov.it"
            || host.EndsWith(".aifa.gov.it", StringComparison.Ordinal)
            || host == "agenziafarmaco.gov.it"
            || host.EndsWith(".agenziafarmaco.gov.it", StringComparison.Ordinal);
    }
}

// Carries data between the dialog and the caller in both directions
// (seed for Edit). Immutable on the dialog side: the user gets a new
// instance after Save.
internal sealed record MedicineEditResult(
    string Name,
    string? ActiveIngredient,
    string? Package,
    string Unit,
    decimal DosePerAdministration,
    int AdministrationsPerDay,
    DateOnly StartDate,
    DateOnly? EndDate,
    int ThresholdDays,
    string? DoctorName,
    string? Notes,
    decimal InitialQuantity,
    NotificationChannels NotificationChannels,
    bool IsActive,
    bool RemindOnDose = false,
    IReadOnlyList<AdministrationSlotEntry>? Slots = null,
    string? NationalCode = null,
    AtcCode? AtcCode = null,
    Guid? LinkedReferenceMedicineId = null,
    Schedule? InitialSchedule = null)
{
    public AddMedicineCommand ToAddCommand() => new(
        Name: Name,
        Unit: Unit,
        DosePerAdministration: DosePerAdministration,
        AdministrationsPerDay: AdministrationsPerDay,
        StartDate: StartDate,
        ThresholdDays: ThresholdDays,
        NotificationChannels: NotificationChannels,
        ActiveIngredient: ActiveIngredient,
        Package: Package,
        EndDate: EndDate,
        DoctorName: DoctorName,
        Notes: Notes,
        InitialQuantity: InitialQuantity,
        AdministrationSlots: MapSlots(),
        NationalCode: NationalCode,
        AtcCode: AtcCode,
        LinkedReferenceMedicineId: LinkedReferenceMedicineId,
        InitialSchedule: InitialSchedule,
        RemindOnDose: RemindOnDose);

    // Always pass the slots (even empty): the UpdateMedicine use case
    // distinguishes null=leave-as-is vs [] = clear. Here the user has
    // explicitly confirmed the current list, so we want it applied
    // (atomic replacement).
    //
    // The Catalogue block is always sent too: the user's Save always
    // encodes an explicit intent (either linked to a reference row
    // they picked, or fully free-text / unlinked). Sending null there
    // would leave a stale linkage from a previous edit untouched.
    public UpdateMedicineCommand ToUpdateCommand(Guid id) => new(
        MedicineId: id,
        Name: Name,
        ActiveIngredient: ActiveIngredient,
        Package: Package,
        Unit: Unit,
        ThresholdDays: ThresholdDays,
        NotificationChannels: NotificationChannels,
        EndDate: EndDate,
        DoctorName: DoctorName,
        Notes: Notes,
        IsActive: IsActive,
        RemindOnDose: RemindOnDose,
        AdministrationSlots: MapSlots() ?? [],
        Catalogue: new CatalogueLink(NationalCode, AtcCode, LinkedReferenceMedicineId));

    private IReadOnlyList<AdministrationSlotInput>? MapSlots()
    {
        if (Slots is null || Slots.Count == 0) return null;
        return Slots.Select(s => new AdministrationSlotInput(s.Dose, s.Time, s.TimingLabel)).ToList();
    }
}

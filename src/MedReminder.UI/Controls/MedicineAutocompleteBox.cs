using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.UI.Controls;

// WinForms autocomplete for the reference-catalogue medicine form
// (ANALYSIS-DRUG-CATALOGUE.md §3.3 M2).
//
// Composition: a plain TextBox for user input plus a floating
// borderless ListBox that anchors under it while typing. Keystrokes
// reset a WinForms Timer whose one-shot Tick triggers the actual
// catalogue query; the debounce is enforced at 150 ms per §3.3 to
// avoid firing one search per key. Only the last completed query
// updates the dropdown, so a fast typist never sees stale rows.
//
// Two search fields are supported (SearchField.CommercialName or
// SearchField.ActiveIngredient), so the medicine form can host two
// instances — one on each side — using the same class.
public sealed class MedicineAutocompleteBox : UserControl
{
    public enum SearchField
    {
        CommercialName,
        ActiveIngredient,
    }

    // Debounce window between the last keystroke and the actual
    // catalogue query. Tight enough to feel instant, wide enough to
    // batch bursts of typing.
    private const int DebounceMilliseconds = 150;

    // Hard cap on rows requested per query — matches
    // SearchCatalogueUseCase.DefaultLimit. Never widen this without
    // also updating the dropdown height calculation below.
    private const int ResultLimit = SearchCatalogueUseCase.DefaultLimit;

    // Withdrawn / suspended AIFA marketing-status detection. Free-text
    // column: match any row whose STATO_AMMINISTRATIVO contains any
    // of these substrings, case-insensitive. Deliberately permissive
    // because AIFA writes several long-form variants ("Sospesa",
    // "Ritirato dal commercio", "Revocata"), none of which is an
    // enum.
    private static readonly string[] WithdrawnMarkers =
    {
        "sospesa",
        "ritirat",
        "revocata",
    };

    // Delegate the actual query so the caller can create a fresh DI
    // scope per keystroke — the reference-catalogue services are
    // scoped over a DbContext, and reusing one scope across many
    // concurrent autocomplete searches inside a long-lived dialog
    // would keep that DbContext hot for the whole session.
    public delegate Task<IReadOnlyList<ReferenceMedicine>> ReferenceMedicineSearchAsync(
        string prefix, CountryCode country, CancellationToken cancellationToken);

    private readonly TextBox _input;
    private readonly ListBox _dropdown;
    private readonly ToolTip _tooltip;
    private readonly System.Windows.Forms.Timer _debounce;

    private SearchField _field = SearchField.CommercialName;
    private ReferenceMedicineSearchAsync? _searchFn;
    private ILocalizationService? _localization;
    private CountryCode _country = CountryCode.Parse("IT");

    private CancellationTokenSource? _inFlight;
    private bool _suppressChange;
    private IReadOnlyList<ReferenceMedicine> _lastResults = Array.Empty<ReferenceMedicine>();

    public MedicineAutocompleteBox()
    {
        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _input.TextChanged += OnInputChanged;
        _input.KeyDown += OnInputKeyDown;
        _input.LostFocus += (_, _) => HideDropdownDeferred();

        _dropdown = new ListBox
        {
            Visible = false,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
        };
        _dropdown.DrawItem += OnDrawItem;
        _dropdown.MouseClick += (_, _) => CommitSelection();

        _tooltip = new ToolTip
        {
            InitialDelay = 400,
            AutoPopDelay = 15_000,
            ReshowDelay = 200,
        };

        _debounce = new System.Windows.Forms.Timer
        {
            Interval = DebounceMilliseconds,
        };
        _debounce.Tick += async (_, _) => await RunSearchAsync();

        Controls.Add(_input);
        Height = _input.PreferredHeight;
    }

    // Populated by the medicine form via BindSearch(...) after
    // construction. Held as fields so the designer-friendly
    // parameterless ctor still works.
    public void BindSearch(
        SearchField field,
        ReferenceMedicineSearchAsync searchFn,
        ILocalizationService localization,
        CountryCode country)
    {
        ArgumentNullException.ThrowIfNull(searchFn);
        ArgumentNullException.ThrowIfNull(localization);

        _field = field;
        _searchFn = searchFn;
        _localization = localization;
        _country = country;

        _tooltip.SetToolTip(_input, localization.Get("medicine.autocomplete.hint"));
    }

    // Text displayed in the input. Reads and writes bypass the
    // debounce so the medicine form can seed the value (Edit mode)
    // or reflect a pick made in the sibling autocomplete without
    // triggering a spurious search.
    //
    // The control is only constructed in code (never dropped from
    // the WinForms designer), so the runtime string carried here
    // must not be baked into any generated InitializeComponent.
    // The two attributes together silence the WFO1000 analyzer and
    // keep the designer honest if this control ever gets dragged
    // onto a Form later.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string InputText
    {
        get => _input.Text;
        set
        {
            _suppressChange = true;
            try
            {
                _input.Text = value ?? string.Empty;
            }
            finally
            {
                _suppressChange = false;
            }
        }
    }

    // Fired every time the user picks a row (mouse click or Enter).
    // The medicine form uses this to populate the sibling
    // autocomplete and the hidden reference-linkage fields.
    public event EventHandler<ReferenceMedicineSelectedEventArgs>? ReferenceSelected;

    // Fired whenever the user edits the input free-form (typing or
    // programmatic clear). Consumers use this to reset the linkage:
    // once the user diverges from the last picked reference the
    // NationalCode / AtcCode / LinkedReferenceMedicineId must be
    // cleared so the save is treated as user-authored.
    public event EventHandler? TextEdited;

    private void OnInputChanged(object? sender, EventArgs e)
    {
        if (_suppressChange) return;
        TextEdited?.Invoke(this, EventArgs.Empty);

        // Restart the debounce window on every keystroke.
        _debounce.Stop();
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            HideDropdown();
            return;
        }
        _debounce.Start();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_dropdown.Visible) return;

        switch (e.KeyCode)
        {
            case Keys.Down:
                _dropdown.SelectedIndex = Math.Min(
                    _dropdown.SelectedIndex + 1, _dropdown.Items.Count - 1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                _dropdown.SelectedIndex = Math.Max(
                    _dropdown.SelectedIndex - 1, 0);
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                if (_dropdown.SelectedIndex >= 0)
                {
                    CommitSelection();
                    e.SuppressKeyPress = true;
                }
                break;
            case Keys.Escape:
                HideDropdown();
                e.SuppressKeyPress = true;
                break;
        }
    }

    private async Task RunSearchAsync()
    {
        _debounce.Stop();
        if (_searchFn is null) return;

        var query = _input.Text.Trim();
        if (query.Length == 0)
        {
            HideDropdown();
            return;
        }

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;

        IReadOnlyList<ReferenceMedicine> rows;
        try
        {
            rows = await _searchFn(query, _country, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // A failing catalogue query must never crash the medicine
            // form. Best-effort: hide the dropdown and let the user
            // keep typing free-text.
            HideDropdown();
            return;
        }

        // Ignore late results if a newer search has already been
        // scheduled — protects against out-of-order completions on a
        // fast typist.
        if (!ReferenceEquals(_inFlight, cts)) return;

        _lastResults = rows;
        RenderDropdown(rows);
    }

    private void RenderDropdown(IReadOnlyList<ReferenceMedicine> rows)
    {
        _dropdown.BeginUpdate();
        _dropdown.Items.Clear();
        if (rows.Count == 0)
        {
            _dropdown.Items.Add(_localization?.Get("medicine.autocomplete.noMatches") ?? "No matches");
            _dropdown.Enabled = false;
        }
        else
        {
            _dropdown.Enabled = true;
            foreach (var row in rows)
            {
                _dropdown.Items.Add(FormatRow(row));
            }
            _dropdown.SelectedIndex = 0;
        }
        _dropdown.EndUpdate();
        ShowDropdown();
    }

    // Row layout: "commercial_name — active_ingredient — dosage"
    // (§3.3 M2). Missing pieces collapse cleanly (a fallback dash
    // rather than an empty gap).
    private static string FormatRow(ReferenceMedicine row)
    {
        var ingredient = row.ActiveIngredients.Count == 0
            ? "—"
            : string.Join(" / ", row.ActiveIngredients.Select(a => a.Name));
        var dosage = string.IsNullOrWhiteSpace(row.Dosage) ? "—" : row.Dosage;
        return $"{row.CommercialName} — {ingredient} — {dosage}";
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var text = _dropdown.Items[e.Index]?.ToString() ?? string.Empty;

        e.DrawBackground();
        var isReference = e.Index < _lastResults.Count && _dropdown.Enabled;

        var textBounds = e.Bounds;
        if (isReference && IsWithdrawn(_lastResults[e.Index].MarketingStatus))
        {
            // Withdrawn badge: a filled red circle glyph before the
            // text with a matching tooltip on hover. Colour picked
            // for AA contrast against both the default and highlight
            // ListBox backgrounds.
            const string BadgeGlyph = "●"; // ●
            using var badgeFont = new Font(e.Font ?? _dropdown.Font, FontStyle.Bold);
            var badgeSize = e.Graphics.MeasureString(BadgeGlyph, badgeFont);
            var badgeColor = Color.FromArgb(198, 40, 40); // Material red 700
            e.Graphics.DrawString(BadgeGlyph, badgeFont, new SolidBrush(badgeColor),
                e.Bounds.X + 2, e.Bounds.Y + 2);
            textBounds = new Rectangle(
                e.Bounds.X + (int)badgeSize.Width + 8,
                e.Bounds.Y,
                e.Bounds.Width - (int)badgeSize.Width - 8,
                e.Bounds.Height);
        }

        var foreColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
            ? SystemColors.HighlightText
            : (_dropdown.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
        using var brush = new SolidBrush(foreColor);
        using var format = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        e.Graphics.DrawString(text, e.Font ?? _dropdown.Font, brush, textBounds, format);

        e.DrawFocusRectangle();
    }

    private static bool IsWithdrawn(string? marketingStatus)
    {
        if (string.IsNullOrWhiteSpace(marketingStatus)) return false;
        foreach (var marker in WithdrawnMarkers)
        {
            if (marketingStatus.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private void ShowDropdown()
    {
        var form = FindForm();
        if (form is null) return;

        if (_dropdown.Parent is null)
        {
            form.Controls.Add(_dropdown);
            _dropdown.BringToFront();
        }

        var origin = _input.PointToScreen(new Point(0, _input.Height));
        var formOrigin = form.PointToClient(origin);
        _dropdown.Location = formOrigin;
        _dropdown.Width = Math.Max(_input.Width, 360);

        var visibleRows = Math.Min(Math.Max(_dropdown.Items.Count, 1), ResultLimit);
        _dropdown.Height = visibleRows * _dropdown.ItemHeight + 4;
        _dropdown.Visible = true;
        _dropdown.BringToFront();
    }

    // A hard Hide on LostFocus would swallow the click on the
    // dropdown itself — post it to the message loop instead so the
    // click handler runs first.
    private void HideDropdownDeferred()
    {
        BeginInvoke(new Action(() =>
        {
            if (_dropdown.Focused) return;
            HideDropdown();
        }));
    }

    private void HideDropdown()
    {
        _dropdown.Visible = false;
    }

    private void CommitSelection()
    {
        if (!_dropdown.Enabled) { HideDropdown(); return; }
        var index = _dropdown.SelectedIndex;
        if (index < 0 || index >= _lastResults.Count) { HideDropdown(); return; }

        var reference = _lastResults[index];
        _suppressChange = true;
        try
        {
            _input.Text = _field == SearchField.CommercialName
                ? reference.CommercialName
                : (reference.ActiveIngredients.Count > 0
                    ? string.Join(" / ", reference.ActiveIngredients.Select(a => a.Name))
                    : reference.CommercialName);
        }
        finally
        {
            _suppressChange = false;
        }
        HideDropdown();

        ReferenceSelected?.Invoke(this, new ReferenceMedicineSelectedEventArgs(reference));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _tooltip.Dispose();
            _inFlight?.Cancel();
            _inFlight?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class ReferenceMedicineSelectedEventArgs : EventArgs
{
    public ReferenceMedicine Reference { get; }

    public ReferenceMedicineSelectedEventArgs(ReferenceMedicine reference)
    {
        Reference = reference;
    }
}

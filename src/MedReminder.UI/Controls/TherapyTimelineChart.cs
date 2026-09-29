using System.Drawing.Drawing2D;
using MedReminder.Application.Timeline;

namespace MedReminder.UI.Controls;

// Gantt-like, read-only chart of the therapy timeline: one row per
// medicine, days on the horizontal axis, a dashed "today" line.
// Rendering only — rows, segments and markers come from
// TherapyTimelineBuilder, the wording from TherapyTimelineText.
//
// All sizes derive from Font.Height, so the chart follows the form
// font and Windows scaling. Keyboard: Up/Down/Home/End/PageUp/PageDown
// select a row, Left/Right ask the owner to move the window by a week,
// Enter activates the row. Shift + mouse wheel also moves the window.
// Every drawn element has a tooltip, and each row exposes its summary
// through the accessibility tree.
internal sealed class TherapyTimelineChart : Control
{
    public const int KeyboardShiftDays = 7;

    private readonly VScrollBar _scroll;
    private readonly ToolTip _toolTip;
    private readonly List<HitRegion> _hits = [];

    private TherapyTimeline? _timeline;
    private TherapyTimelineText? _text;
    private string _todayLabel = string.Empty;
    private string _emptyText = string.Empty;
    private int _selected = -1;
    private string? _toolTipShown;

    private sealed record HitRegion(RectangleF Bounds, int Row, string Text);

    public TherapyTimelineChart()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);
        TabStop = true;

        _scroll = new VScrollBar { Dock = DockStyle.Right, Visible = false, TabStop = false };
        _scroll.ValueChanged += (_, _) => Invalidate();
        Controls.Add(_scroll);

        _toolTip = new ToolTip { ShowAlways = true };
    }

    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? RowActivated;
    public event EventHandler<int>? WindowShiftRequested;

    public IReadOnlyList<TherapyTimelineRow> Rows
        => _timeline?.Rows ?? (IReadOnlyList<TherapyTimelineRow>)[];

    public int SelectedIndex => _selected;

    public TherapyTimelineRow? SelectedRow
        => _selected >= 0 && _selected < Rows.Count ? Rows[_selected] : null;

    internal TherapyTimelineText? TextProvider => _text;

    // Replaces the content, keeping the selection on the same medicine
    // when it is still present.
    public void SetContent(
        TherapyTimeline timeline,
        TherapyTimelineText text,
        string todayLabel,
        string emptyText,
        Guid? selectMedicineId)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _todayLabel = todayLabel;
        _emptyText = emptyText;
        HideToolTip();

        var index = -1;
        if (selectMedicineId is { } id)
        {
            for (var i = 0; i < timeline.Rows.Count; i++)
            {
                if (timeline.Rows[i].MedicineId == id) { index = i; break; }
            }
        }
        if (index < 0 && timeline.Rows.Count > 0) index = 0;

        _selected = -1;
        UpdateScrollBar();
        SelectIndex(index);
        Invalidate();
    }

    public void SelectIndex(int index)
    {
        if (Rows.Count == 0) index = -1;
        else index = Math.Clamp(index, 0, Rows.Count - 1);
        if (index == _selected) return;

        _selected = index;
        EnsureVisible(index);
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        if (index >= 0)
        {
            AccessibilityNotifyClients(AccessibleEvents.Selection, index);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }
    }

    // ------------------ Layout ------------------

    private int LineHeight => Font.Height;
    private int HeaderHeight => LineHeight * 2 + LineHeight / 2;
    private int RowHeight => (int)Math.Ceiling(LineHeight * 2.4f);
    private int ContentHeight => Rows.Count * RowHeight;
    private int ViewportHeight => Math.Max(0, ClientSize.Height - HeaderHeight);
    private int ScrollOffset => _scroll.Visible ? _scroll.Value : 0;
    private int PlotRight => ClientSize.Width - (_scroll.Visible ? _scroll.Width : 0) - LineHeight / 2;

    private int LabelWidth()
    {
        var min = LineHeight * 6;
        var max = Math.Max(min, (int)(ClientSize.Width * 0.3f));
        var widest = 0;
        foreach (var row in Rows)
        {
            var w = TextRenderer.MeasureText(RowLabel(row), Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            if (w > widest) widest = w;
        }
        return Math.Clamp(widest + LineHeight, min, max);
    }

    private string RowLabel(TherapyTimelineRow row)
        => row.IsActive || _text is null
            ? row.Name
            : row.Name + " " + _text.InactiveTag;

    private Rectangle RowBounds(int index)
        => new(0, HeaderHeight + index * RowHeight - ScrollOffset,
            ClientSize.Width - (_scroll.Visible ? _scroll.Width : 0), RowHeight);

    private void UpdateScrollBar()
    {
        var needed = ContentHeight > ViewportHeight && ViewportHeight > 0;
        _scroll.Visible = needed;
        if (!needed)
        {
            _scroll.Value = 0;
            return;
        }
        _scroll.Minimum = 0;
        _scroll.Maximum = ContentHeight;
        _scroll.LargeChange = Math.Max(1, ViewportHeight);
        _scroll.SmallChange = RowHeight;
        var maxValue = Math.Max(0, ContentHeight - ViewportHeight);
        if (_scroll.Value > maxValue) _scroll.Value = maxValue;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || !_scroll.Visible) return;
        var top = index * RowHeight;
        var bottom = top + RowHeight;
        var maxValue = Math.Max(0, ContentHeight - ViewportHeight);
        if (top < _scroll.Value) _scroll.Value = Math.Min(top, maxValue);
        else if (bottom > _scroll.Value + ViewportHeight)
        {
            _scroll.Value = Math.Clamp(bottom - ViewportHeight, 0, maxValue);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
        EnsureVisible(_selected);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateScrollBar();
        Invalidate();
    }

    // ------------------ Painting ------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var p = TimelinePainter.Current;
        var stroke = TimelinePainter.StrokeWidth(Font);
        g.Clear(p.Background);
        _hits.Clear();

        if (_timeline is null || _text is null) return;

        if (Rows.Count == 0)
        {
            TextRenderer.DrawText(g, _emptyText, Font, ClientRectangle, p.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }

        var window = _timeline.Window;
        var labelWidth = LabelWidth();
        var plotLeft = (float)labelWidth;
        var plotRight = (float)Math.Max(labelWidth + 1, PlotRight);
        var dayWidth = (plotRight - plotLeft) / window.Days;
        float X(DateOnly day) => plotLeft + (day.DayNumber - window.Start.DayNumber) * dayWidth;

        var rowsArea = new Rectangle(0, HeaderHeight, ClientSize.Width, ViewportHeight);
        var state = g.Save();
        g.SetClip(rowsArea);

        // Pass 1: row backgrounds, month grid lines.
        for (var i = 0; i < Rows.Count; i++)
        {
            var r = RowBounds(i);
            if (!r.IntersectsWith(rowsArea)) continue;
            if (i % 2 == 1)
            {
                using var alt = new SolidBrush(p.AlternateRow);
                g.FillRectangle(alt, r);
            }
            if (i == _selected && !SystemInformation.HighContrast)
            {
                using var sel = new SolidBrush(Color.FromArgb(40, p.Selection));
                g.FillRectangle(sel, r);
            }
        }
        using (var grid = new Pen(p.Grid, stroke))
        {
            foreach (var month in MonthStarts(window))
            {
                var x = X(month);
                g.DrawLine(grid, x, HeaderHeight, x, ClientSize.Height);
            }
            g.DrawLine(grid, plotLeft, HeaderHeight, plotLeft, ClientSize.Height);
        }

        // Pass 2: labels, bars, markers.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (var i = 0; i < Rows.Count; i++)
        {
            var r = RowBounds(i);
            if (!r.IntersectsWith(rowsArea)) continue;
            PaintRow(g, p, stroke, Rows[i], i, r, labelWidth, X, dayWidth, window);
        }

        // Today line across all rows.
        if (window.Contains(_timeline.Today))
        {
            var x = X(_timeline.Today) + dayWidth / 2f;
            TimelinePainter.DrawTodayLine(g, x, HeaderHeight, ClientSize.Height, p, stroke);
        }
        g.Restore(state);

        PaintHeader(g, p, stroke, window, plotLeft, plotRight, X, dayWidth);
    }

    private void PaintRow(
        Graphics g, TimelinePainter.Palette p, float stroke,
        TherapyTimelineRow row, int index, Rectangle r, int labelWidth,
        Func<DateOnly, float> x, float dayWidth, TimelineWindow window)
    {
        var text = _text!;
        var lh = LineHeight;

        // Label column.
        var labelRect = new Rectangle(lh / 2, r.Y, labelWidth - lh, r.Height);
        TextRenderer.DrawText(g, RowLabel(row), Font, labelRect,
            row.IsActive ? p.Text : p.MutedText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        _hits.Add(new HitRegion(labelRect, index, text.Summarize(row)));

        // Bars.
        var barTop = r.Y + r.Height * 0.42f;
        var barHeight = r.Height * 0.38f;
        foreach (var segment in row.Segments)
        {
            var left = x(segment.Start);
            var right = x(segment.End.AddDays(1));
            var bar = new RectangleF(left, barTop, Math.Max(stroke * 2, right - left), barHeight);
            if (segment.Kind == TimelineSegmentKind.Suspended)
            {
                TimelinePainter.DrawSuspended(g, bar, p, stroke);
            }
            else
            {
                TimelinePainter.DrawActive(g, bar, p, stroke);
            }
            _hits.Add(new HitRegion(bar, index,
                row.Name + Environment.NewLine + text.DescribeSegment(row, segment)));
        }

        // Dosage-change markers straddle the top edge of the bar.
        var markerSize = lh * 0.8f;
        foreach (var marker in row.Markers)
        {
            var cx = x(marker.Date) + dayWidth / 2f;
            var filled = marker.Kind is TimelineMarkerKind.InitialSchedule or TimelineMarkerKind.ScheduleChange;
            TimelinePainter.DrawDiamond(g, new PointF(cx, barTop), markerSize, p, filled, stroke);
            var hit = new RectangleF(cx - markerSize / 2f, barTop - markerSize / 2f, markerSize, markerSize);
            hit.Inflate(stroke * 2, stroke * 2);
            _hits.Add(new HitRegion(hit, index,
                row.Name + Environment.NewLine + text.DescribeMarker(row, marker)));
        }

        // Run-out estimate.
        if (row.RunOutDateToDraw is { } runOut && window.Contains(runOut))
        {
            var cx = x(runOut) + dayWidth / 2f;
            var size = lh * 0.7f;
            var tipY = r.Y + stroke + size;
            TimelinePainter.DrawRunOut(g, new PointF(cx, tipY), size, barTop + barHeight, p, stroke);
            var hit = new RectangleF(cx - size / 2f, r.Y, size, barTop + barHeight - r.Y);
            hit.Inflate(stroke * 2, 0);
            _hits.Add(new HitRegion(hit, index,
                row.Name + Environment.NewLine + text.DescribeRunOut(row)));
        }

        if (index == _selected)
        {
            using var pen = new Pen(p.Selection, stroke * 2f);
            var sel = r;
            sel.Inflate(-(int)stroke, -(int)stroke);
            g.DrawRectangle(pen, sel);
            if (Focused && ShowFocusCues)
            {
                var focus = r;
                focus.Inflate(-(int)(stroke * 3), -(int)(stroke * 3));
                ControlPaint.DrawFocusRectangle(g, focus);
            }
        }
    }

    private void PaintHeader(
        Graphics g, TimelinePainter.Palette p, float stroke, TimelineWindow window,
        float plotLeft, float plotRight, Func<DateOnly, float> x, float dayWidth)
    {
        var lh = LineHeight;
        var header = new Rectangle(0, 0, ClientSize.Width, HeaderHeight);
        using (var back = new SolidBrush(p.Background))
        {
            g.FillRectangle(back, header);
        }

        var culture = _text!.Culture;
        var months = MonthStarts(window).ToList();
        // Label of the partial first month, when there is room before
        // the first month boundary.
        var boundaries = new List<DateOnly> { window.Start };
        boundaries.AddRange(months.Where(m => m > window.Start));

        using var tick = new Pen(p.Grid, stroke);
        for (var i = 0; i < boundaries.Count; i++)
        {
            var start = boundaries[i];
            var left = x(start);
            var right = i + 1 < boundaries.Count ? x(boundaries[i + 1]) : plotRight;
            if (start.Day == 1)
            {
                g.DrawLine(tick, left, lh / 4f, left, HeaderHeight);
            }
            var label = start.ToString("MMM yyyy", culture);
            var size = TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.NoPrefix);
            if (right - left >= size.Width + lh / 2f)
            {
                TextRenderer.DrawText(g, label, Font,
                    new Point((int)(left + lh / 4f), lh / 4), p.Text, TextFormatFlags.NoPrefix);
            }
        }

        // "Today" caption on the second header line.
        if (window.Contains(_timeline!.Today))
        {
            var cx = x(_timeline.Today) + dayWidth / 2f;
            var size = TextRenderer.MeasureText(_todayLabel, Font, Size.Empty, TextFormatFlags.NoPrefix);
            var textX = Math.Clamp(cx - size.Width / 2f, plotLeft, Math.Max(plotLeft, plotRight - size.Width));
            TextRenderer.DrawText(g, _todayLabel, Font,
                new Point((int)textX, lh + lh / 4), p.Today, TextFormatFlags.NoPrefix);
            TimelinePainter.DrawTodayLine(g, cx, lh * 2 + lh / 4f, HeaderHeight, p, stroke);
        }

        using var baseLine = new Pen(p.Grid, stroke);
        g.DrawLine(baseLine, 0, HeaderHeight - stroke / 2f, ClientSize.Width, HeaderHeight - stroke / 2f);
    }

    private static IEnumerable<DateOnly> MonthStarts(TimelineWindow window)
    {
        var month = new DateOnly(window.Start.Year, window.Start.Month, 1);
        if (month < window.Start) month = month.AddMonths(1);
        for (; month <= window.End; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    // ------------------ Input ------------------

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Home:
            case Keys.End:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Enter:
                return (keyData & (Keys.Control | Keys.Alt)) == 0;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Control || e.Alt) return;

        var pageRows = Math.Max(1, ViewportHeight / Math.Max(1, RowHeight) - 1);
        switch (e.KeyCode)
        {
            case Keys.Up: SelectIndex(Math.Max(0, _selected - 1)); break;
            case Keys.Down: SelectIndex(_selected + 1); break;
            case Keys.Home: SelectIndex(0); break;
            case Keys.End: SelectIndex(Rows.Count - 1); break;
            case Keys.PageUp: SelectIndex(Math.Max(0, _selected - pageRows)); break;
            case Keys.PageDown: SelectIndex(_selected + pageRows); break;
            case Keys.Left: WindowShiftRequested?.Invoke(this, -KeyboardShiftDays); break;
            case Keys.Right: WindowShiftRequested?.Invoke(this, KeyboardShiftDays); break;
            case Keys.Enter:
                if (SelectedRow is not null) RowActivated?.Invoke(this, EventArgs.Empty);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private int RowAt(int y)
    {
        if (y < HeaderHeight) return -1;
        var index = (y - HeaderHeight + ScrollOffset) / RowHeight;
        return index >= 0 && index < Rows.Count ? index : -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var index = RowAt(e.Y);
        if (index >= 0) SelectIndex(index);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (RowAt(e.Y) >= 0 && SelectedRow is not null) RowActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            WindowShiftRequested?.Invoke(this, e.Delta > 0 ? -KeyboardShiftDays : KeyboardShiftDays);
            return;
        }
        if (!_scroll.Visible) return;
        var maxValue = Math.Max(0, ContentHeight - ViewportHeight);
        var step = RowHeight * Math.Max(1, SystemInformation.MouseWheelScrollLines / 3) * Math.Sign(-e.Delta);
        _scroll.Value = Math.Clamp(_scroll.Value + step, 0, maxValue);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? text = null;
        // Markers are added after bars, so walk backwards to prefer them.
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Bounds.Contains(e.Location))
            {
                text = _hits[i].Text;
                break;
            }
        }
        if (text is null)
        {
            HideToolTip();
            return;
        }
        if (text == _toolTipShown) return;
        _toolTipShown = text;
        _toolTip.Show(text, this, e.X + LineHeight, e.Y + LineHeight);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HideToolTip();
    }

    private void HideToolTip()
    {
        if (_toolTipShown is null) return;
        _toolTipShown = null;
        _toolTip.Hide(this);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
        if (_selected >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, _selected);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    // ------------------ Accessibility ------------------

    protected override AccessibleObject CreateAccessibilityInstance()
        => new ChartAccessibleObject(this);

    private sealed class ChartAccessibleObject : ControlAccessibleObject
    {
        private readonly TherapyTimelineChart _chart;

        public ChartAccessibleObject(TherapyTimelineChart chart) : base(chart)
        {
            _chart = chart;
        }

        public override AccessibleRole Role => AccessibleRole.List;

        public override int GetChildCount() => _chart.Rows.Count;

        public override AccessibleObject? GetChild(int index)
            => index >= 0 && index < _chart.Rows.Count
                ? new RowAccessibleObject(_chart, this, index)
                : null;

        public override AccessibleObject? GetFocused()
            => _chart.Focused && _chart._selected >= 0 ? GetChild(_chart._selected) : base.GetFocused();

        public override AccessibleObject? GetSelected()
            => _chart._selected >= 0 ? GetChild(_chart._selected) : base.GetSelected();
    }

    private sealed class RowAccessibleObject : AccessibleObject
    {
        private readonly TherapyTimelineChart _chart;
        private readonly AccessibleObject _parent;
        private readonly int _index;

        public RowAccessibleObject(TherapyTimelineChart chart, AccessibleObject parent, int index)
        {
            _chart = chart;
            _parent = parent;
            _index = index;
        }

        private TherapyTimelineRow? Row
            => _index < _chart.Rows.Count ? _chart.Rows[_index] : null;

        public override string? Name
            => Row is { } row && _chart._text is { } text ? text.Summarize(row) : Row?.Name;

        public override string? Description
            => Row is { } row && _chart._text is { } text ? text.DescribeRow(row) : null;

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleObject Parent => _parent;

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (_index == _chart._selected)
                {
                    state |= AccessibleStates.Selected;
                    if (_chart.Focused) state |= AccessibleStates.Focused;
                }
                return state;
            }
        }

        public override Rectangle Bounds => _chart.RectangleToScreen(_chart.RowBounds(_index));

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) != 0)
            {
                _chart.SelectIndex(_index);
                if ((flags & AccessibleSelection.TakeFocus) != 0) _chart.Focus();
            }
        }

        public override void DoDefaultAction() => Select(AccessibleSelection.TakeSelection);
    }
}

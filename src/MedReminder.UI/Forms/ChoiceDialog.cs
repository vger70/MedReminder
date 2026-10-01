using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// A question answered by picking one of a few options, each with a short
// note (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.3). Replaces the
// TaskDialog command links, which stay light in dark mode. Returns the
// index of the option chosen, or -1 for Cancel, Esc or the close box.
internal sealed class ChoiceDialog : MedReminderFormBase
{
    private const int ContentWidth = 460;

    private int _chosen = -1;

    private ChoiceDialog(ILocalizationService loc, string caption, string heading, string? text,
        IReadOnlyList<(string Title, string Note)> choices)
    {
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 520;
        Height = 200;
        DialogLayout.GrowToContent(this);

        var rows = new List<Control>
        {
            new Label
            {
                Text = heading,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Font = UiTheme.Fonts.Heading(),
                UseMnemonic = false,
            },
        };
        if (!string.IsNullOrWhiteSpace(text))
        {
            rows.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), UseMnemonic = false });
        }
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var link = new CommandLink(choices[i].Title, choices[i].Note) { Width = ContentWidth };
            link.Click += (_, _) =>
            {
                _chosen = index;
                DialogResult = DialogResult.OK;
                Close();
            };
            rows.Add(link);
        }

        var cancel = DialogLayout.Button(loc.Get("Common.Cancel"), DialogResult.Cancel);
        Controls.Add(DialogLayout.Stack([.. rows]));
        Controls.Add(DialogLayout.ButtonBar(this, cancel, cancel));
        // Enter must not pick Cancel by default; the options are chosen
        // with the mouse, or Tab and Enter/Space.
        AcceptButton = null;
    }

    public static int Show(IWin32Window owner, ILocalizationService loc, string caption, string heading, string? text,
        IReadOnlyList<(string Title, string Note)> choices)
    {
        using var dialog = new ChoiceDialog(loc, caption, heading, text, choices)
        {
            StartPosition = FormStartPosition.CenterParent,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._chosen : -1;
    }

    // One option: its title, a note under it in the secondary colour, an
    // arrow on the left, as a Windows command link; drawn from the palette.
    private sealed class CommandLink : Control
    {
        private readonly string _note;
        private bool _hover;
        private Font? _titleFont;

        public CommandLink(string title, string note)
        {
            Text = title;
            _note = note;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = title;
            AccessibleDescription = note;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        private int ArrowWidth => Font.Height * 2;
        private int Pad => Font.Height / 2;
        private Font TitleFont => _titleFont ??= new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Regular);

        // Height follows the text at the current width and font, so the
        // option grows with Large text and a long note wraps.
        private void FitHeight()
        {
            var textWidth = Math.Max(1, Width - ArrowWidth - Pad * 2);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            var title = TextRenderer.MeasureText(Text, TitleFont, new Size(textWidth, int.MaxValue), flags);
            var note = string.IsNullOrEmpty(_note)
                ? Size.Empty
                : TextRenderer.MeasureText(_note, Font, new Size(textWidth, int.MaxValue), flags);
            Height = Pad * 2 + title.Height + note.Height;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _titleFont?.Dispose();
            _titleFont = null;
            base.OnFontChanged(e);
            FitHeight();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FitHeight();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
            => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = UiTheme.Palette;
            var g = e.Graphics;
            using (var back = new SolidBrush(_hover ? p.Hover : Parent?.BackColor ?? p.Background))
            {
                g.FillRectangle(back, ClientRectangle);
            }
            if (_hover || Focused)
            {
                using var border = new Pen(Focused ? p.Accent : p.Border);
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            var arrowSize = Font.Height;
            var arrow = Mdl2Glyph.Create("", arrowSize, p.Accent); // Forward
            g.DrawImage(arrow, Pad, Pad + (TitleFont.Height - arrowSize) / 2, arrowSize, arrowSize);

            var left = ArrowWidth;
            var width = Math.Max(1, Width - left - Pad);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            var titleSize = TextRenderer.MeasureText(g, Text, TitleFont, new Size(width, int.MaxValue), flags);
            TextRenderer.DrawText(g, Text, TitleFont, new Rectangle(left, Pad, width, titleSize.Height), p.Accent, flags);
            if (!string.IsNullOrEmpty(_note))
            {
                TextRenderer.DrawText(g, _note, Font,
                    new Rectangle(left, Pad + titleSize.Height, width, Height - Pad - titleSize.Height), p.TextSecondary, flags);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont?.Dispose();
            base.Dispose(disposing);
        }
    }
}

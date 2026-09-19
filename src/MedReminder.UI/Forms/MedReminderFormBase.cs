namespace MedReminder.UI.Forms;

// Base class for every form in the app. Single source of truth for
// the icon (shown in title bar + Alt-Tab + task switcher): by
// deriving from here, MainForm and every dialog get the icon without
// having to repeat the assignment in each constructor.
//
// Other common conventions (font, StartPosition, DPI awareness) can
// be added here later without touching the derived classes.
internal class MedReminderFormBase : Form
{
    protected MedReminderFormBase()
    {
        var icon = AppIcon.Default;
        if (icon is not null)
        {
            Icon = icon;
        }
    }
}

using System.Windows.Forms;

namespace MedReminder.UI.Forms;

// Base class per tutte le form dell'app. Un solo punto di verità
// per l'icona (mostrata in title bar + Alt-Tab + task switcher):
// derivando da qui, MainForm e ogni dialog ricevono l'icona senza
// dover ripetere l'assegnazione in ciascun costruttore.
//
// Altre convenzioni comuni (font, StartPosition, DPI awareness)
// possono essere aggiunte qui in futuro senza toccare le derivate.
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

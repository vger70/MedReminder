using System;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormsAppConfig = System.Windows.Forms.ApplicationConfiguration;
using WinForm = System.Windows.Forms.Form;
using WinFormStartPosition = System.Windows.Forms.FormStartPosition;

namespace MedReminder.UI;

// Nota: non facciamo `using System.Windows.Forms;` per evitare che
// l'identificatore `Application` venga risolto come il namespace
// `MedReminder.Application` (presente nella nostra solution). Gli alias
// sopra mantengono il codice conciso e disambiguato.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinFormsAppConfig.Initialize();
        WinFormsApp.Run(new BootstrapForm());
    }
}

// Placeholder form: sostituito nell'Incremento 6 dalla MainForm reale.
internal sealed class BootstrapForm : WinForm
{
    public BootstrapForm()
    {
        Text = "MedReminder";
        Width = 480;
        Height = 240;
        StartPosition = WinFormStartPosition.CenterScreen;
    }
}

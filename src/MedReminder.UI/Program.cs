using System;
using WinFormsApp = System.Windows.Forms.Application;
using WinForm = System.Windows.Forms.Form;
using WinFormStartPosition = System.Windows.Forms.FormStartPosition;

namespace MedReminder.UI;

// Nota: non facciamo `using System.Windows.Forms;` per evitare che
// l'identificatore `Application` venga risolto come il namespace
// `MedReminder.Application` (presente nella nostra solution).
//
// `ApplicationConfiguration` NON risiede in System.Windows.Forms:
// è un tipo source-generated dal WinForms SDK nel root namespace del
// progetto (qui `MedReminder.UI`). Va quindi lasciato senza alias e
// senza fully-qualifier: si risolve da solo nel namespace corrente.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
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

using System;
using System.Windows.Forms;

namespace MedReminder.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BootstrapForm());
    }
}

// Placeholder form: replaced in Incremento 6 by the real MainForm.
// Present here so the UI project builds end-to-end after Incremento 0.
internal sealed class BootstrapForm : Form
{
    public BootstrapForm()
    {
        Text = "MedReminder";
        Width = 480;
        Height = 240;
        StartPosition = FormStartPosition.CenterScreen;
    }
}

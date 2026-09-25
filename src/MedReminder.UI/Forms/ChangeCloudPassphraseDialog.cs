using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.UI.Forms;

// Small modal for setting or rotating the C.3+ automatic-cloud-backup
// passphrase (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §5.1).
// Enforces the same minimum length as C.3 exports (§4.5) so the derived
// archive key matches the strength of a user-typed export passphrase.
// Confirmation field must match; passphrases are cleared in Dispose.
internal sealed class ChangeCloudPassphraseDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly ICloudBackupPassphraseStore _store;

    private readonly TextBox _passphraseBox;
    private readonly TextBox _confirmBox;
    private readonly Label _statusLabel;
    private readonly Button _saveButton;

    public ChangeCloudPassphraseDialog(
        ILocalizationService loc,
        ICloudBackupPassphraseStore store)
    {
        _loc = loc;
        _store = store;

        Text = _loc.Get("Ui.CloudBackup.Passphrase.SetTitle");
        Width = 480;
        Height = 260;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(440, 0),
            Text = _loc.Get("Ui.CloudBackup.Passphrase.Intro"),
        };

        var passLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.Label"),
        };
        _passphraseBox = new TextBox
        {
            Width = 300,
            UseSystemPasswordChar = true,
        };

        var confirmLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.CloudBackup.Passphrase.Confirm.Label"),
        };
        _confirmBox = new TextBox
        {
            Width = 300,
            UseSystemPasswordChar = true,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(440, 0),
            ForeColor = System.Drawing.Color.DarkOrange,
        };

        _saveButton = new Button
        {
            Text = _loc.Get("Common.Save"),
            AutoSize = true,
            Height = 30,
        };
        _saveButton.Click += (_, _) => Save();

        var cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            AutoSize = true,
            Height = 30,
            DialogResult = DialogResult.Cancel,
        };

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
        };
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(cancelButton);

        var container = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
        };
        container.Controls.Add(intro);
        container.Controls.Add(passLabel);
        container.Controls.Add(_passphraseBox);
        container.Controls.Add(confirmLabel);
        container.Controls.Add(_confirmBox);
        container.Controls.Add(_statusLabel);
        container.Controls.Add(actions);

        Controls.Add(container);
        AcceptButton = _saveButton;
        CancelButton = cancelButton;
    }

    private void Save()
    {
        var pass = _passphraseBox.Text;
        var confirm = _confirmBox.Text;

        if (pass.Length < ExportFormat.MinPassphraseLength)
        {
            _statusLabel.Text = _loc.Get(
                "Ui.CloudBackup.Passphrase.TooShort",
                ExportFormat.MinPassphraseLength);
            return;
        }

        if (!string.Equals(pass, confirm, StringComparison.Ordinal))
        {
            _statusLabel.Text = _loc.Get("Ui.CloudBackup.Passphrase.Mismatch");
            return;
        }

        var buffer = pass.ToCharArray();
        try
        {
            _store.SetPassphrase(buffer);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var box in new[] { _passphraseBox, _confirmBox })
            {
                var text = box.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    box.Text = new string('\0', text.Length);
                    box.Text = string.Empty;
                }
            }
        }
        base.Dispose(disposing);
    }
}

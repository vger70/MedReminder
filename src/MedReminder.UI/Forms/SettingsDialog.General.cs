using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Export;
using MedReminder.Application.UseCases;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Settings;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Controls;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Settings → General (the section list and the dialog frame are in
// SettingsDialog.cs).
internal sealed partial class SettingsDialog
{
    // General section (Incremento 16b).
    private Panel BuildGeneralTab()
    {
        var page = new Panel();

        var languageLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.Language"),
        };

        // ComboBox con Items = SupportedLanguage records. DisplayMember
        // = DisplayName localized for the current language.
        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        foreach (var lang in SupportedLanguages.All)
        {
            var localizedName = _loc.Get(LanguageDisplayKey(lang.Code));
            _languageCombo.Items.Add(new LanguageChoice(lang.Code, localizedName));
            if (string.Equals(lang.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _languageCombo.SelectedIndex = _languageCombo.Items.Count - 1;
            }
        }
        _languageCombo.DisplayMember = nameof(LanguageChoice.DisplayName);

        _tooltips.SetToolTip(_languageCombo, _loc.Get("Ui.SettingsDialog.Tooltip.Language"));

        // Reference country (M2). Sits under the language row.
        var referenceCountryLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("settings.referenceCountry.label"),
            Margin = GroupMargin,
        };
        _referenceCountryCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        PopulateReferenceCountryCombo();
        // Household step H2b (D-14): the reference country belongs to the
        // installation and is changed by an administrator.
        _referenceCountryCombo.Enabled = _currentProfile.IsAdmin;
        _tooltips.SetToolTip(_referenceCountryCombo, _loc.Get(_currentProfile.IsAdmin
            ? "settings.referenceCountry.help"
            : "settings.referenceCountry.adminOnly"));

        var referenceCountryHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("settings.referenceCountry.help"),
        };

        _checkUpdatesBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.CheckUpdates"),
            Margin = GroupMargin,
            Checked = _userMonitor.CurrentValue.CheckForUpdatesOnStartup,
        };
        _tooltips.SetToolTip(_checkUpdatesBox,
            _loc.Get("Ui.SettingsDialog.Tooltip.CheckUpdates"));

        // Diagnostics: the SQL commands in the log. Shown to every profile,
        // changed by an administrator; a user's save keeps the value.
        _logQueriesBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.LogQueries"),
            Margin = GroupMargin,
            Checked = _userMonitor.CurrentValue.LogDatabaseQueries,
            Enabled = _currentProfile.IsAdmin,
        };
        _tooltips.SetToolTip(_logQueriesBox, _loc.Get(_currentProfile.IsAdmin
            ? "Ui.SettingsDialog.General.LogQueries.Help"
            : "Ui.SettingsDialog.General.LogQueries.AdminOnly"));
        var logQueriesHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.General.LogQueries.Help"),
        };

        var textSizeLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.TextSize"),
            Margin = GroupMargin,
        };
        _textSizeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        PopulateTextSizeCombo();
        var textSizeHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.General.TextSize.Help"),
        };
        _tooltips.SetToolTip(_textSizeCombo, textSizeHelp.Text);

        var appearanceLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.Appearance"),
            Margin = GroupMargin,
        };
        _appearanceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        PopulateAppearanceCombo();
        var appearanceHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.General.Appearance.Help"),
        };
        _tooltips.SetToolTip(_appearanceCombo, appearanceHelp.Text);

        var saveButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.General.Save"),
            Margin = GroupMargin,
            AutoSize = true,
            Height = 30,
        };
        saveButton.Click += async (_, _) => await SaveGeneralAsync();

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.General.Note"),
        };

        // A top-down flow wraps into a second column when the tab is
        // shorter than its content (Large text, 150 % scaling); it
        // scrolls instead.
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
            AutoScroll = true,
        };
        panel.Controls.Add(languageLabel);
        panel.Controls.Add(_languageCombo);
        panel.Controls.Add(referenceCountryLabel);
        panel.Controls.Add(_referenceCountryCombo);
        panel.Controls.Add(referenceCountryHelp);
        panel.Controls.Add(_checkUpdatesBox);
        panel.Controls.Add(textSizeLabel);
        panel.Controls.Add(_textSizeCombo);
        panel.Controls.Add(textSizeHelp);
        panel.Controls.Add(appearanceLabel);
        panel.Controls.Add(_appearanceCombo);
        panel.Controls.Add(appearanceHelp);
        panel.Controls.Add(_logQueriesBox);
        panel.Controls.Add(logQueriesHelp);
        panel.Controls.Add(saveButton);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }

    // The selection shows the saved value, which differs from the size
    // in use when the user saved without restarting.
    private void PopulateTextSizeCombo()
    {
        var saved = ProfileUiSettingsFile.ReadTextSize(_currentProfile.DataDirectory);
        foreach (var size in Enum.GetValues<TextSize>())
        {
            _textSizeCombo.Items.Add(new TextSizeChoice(size, _loc.Get(TextSizeDisplayKey(size))));
            if (size == saved)
            {
                _textSizeCombo.SelectedIndex = _textSizeCombo.Items.Count - 1;
            }
        }
        _textSizeCombo.DisplayMember = nameof(TextSizeChoice.DisplayName);
    }

    // Like the text size, the selection shows the saved value.
    private void PopulateAppearanceCombo()
    {
        var saved = ProfileUiSettingsFile.ReadAppearance(_currentProfile.DataDirectory);
        foreach (var mode in Enum.GetValues<AppearanceMode>())
        {
            _appearanceCombo.Items.Add(new AppearanceChoice(mode, _loc.Get(AppearanceDisplayKey(mode))));
            if (mode == saved)
            {
                _appearanceCombo.SelectedIndex = _appearanceCombo.Items.Count - 1;
            }
        }
        _appearanceCombo.DisplayMember = nameof(AppearanceChoice.DisplayName);
    }

    private static string AppearanceDisplayKey(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => "Ui.SettingsDialog.General.Appearance.Light",
        AppearanceMode.Dark => "Ui.SettingsDialog.General.Appearance.Dark",
        _ => "Ui.SettingsDialog.General.Appearance.System",
    };

    private static string TextSizeDisplayKey(TextSize size) => size switch
    {
        TextSize.Large => "Ui.SettingsDialog.General.TextSize.Large",
        TextSize.ExtraLarge => "Ui.SettingsDialog.General.TextSize.ExtraLarge",
        _ => "Ui.SettingsDialog.General.TextSize.Normal",
    };

    // Fill the reference-country dropdown with the distinct countries
    // present in the local catalogue plus the synthetic "EU" entry
    // (supranational). "IT" is always offered even on an empty DB so
    // the user has something meaningful to pick before the first
    // snapshot import completes. Every country with a remote feed is
    // offered too: a feed-only country (US) has no rows until its first
    // download, which runs once it is selected
    // (docs/analysis/ANALYSIS-CATALOGUE-US-GB-SOURCES.md §5.2).
    private void PopulateReferenceCountryCombo()
    {
        var options = new SortedSet<string>(StringComparer.Ordinal) { "IT", "EU" };
        foreach (var feed in CatalogueFeedDescriptor.All)
        {
            options.Add(feed.Country.Value);
        }
        try
        {
            var present = _catalogueQuery
                .ListAvailableCountriesAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            foreach (var code in present)
            {
                options.Add(code.Value);
            }
        }
        catch
        {
            // Best-effort: an empty or unavailable catalogue must not
            // stop the Settings dialog from opening.
        }

        var selected = _userMonitor.CurrentValue.ReferenceCountry ?? "IT";
        foreach (var option in options)
        {
            _referenceCountryCombo.Items.Add(option);
            if (string.Equals(option, selected, StringComparison.OrdinalIgnoreCase))
            {
                _referenceCountryCombo.SelectedIndex = _referenceCountryCombo.Items.Count - 1;
            }
        }
        if (_referenceCountryCombo.SelectedIndex < 0 && _referenceCountryCombo.Items.Count > 0)
        {
            _referenceCountryCombo.SelectedIndex = 0;
        }
    }

    // Household step H2b: through UpdateGeneralSettings, which records the
    // reference country in the household.
    // Returns false when the save failed (the error was shown). quiet
    // skips the "saved" confirmation when the dialog saves on closing.
    private async Task<bool> SaveGeneralAsync(bool quiet = false)
    {
        if (_languageCombo.SelectedItem is not LanguageChoice choice) return true;

        var referenceCountry = _referenceCountryCombo.SelectedItem as string ?? "IT";
        var settings = new UserSettings
        {
            Language = choice.Code,
            ReferenceCountry = referenceCountry,
            CheckForUpdatesOnStartup = _checkUpdatesBox.Checked,
            LogDatabaseQueries = _logQueriesBox.Checked,
        };

        var textSize = (_textSizeCombo.SelectedItem as TextSizeChoice)?.Size ?? TextSize.Normal;
        var appearance = (_appearanceCombo.SelectedItem as AppearanceChoice)?.Mode ?? AppearanceMode.System;

        try
        {
            await RunUseCaseAsync<UpdateGeneralSettings>(u => u.ExecuteAsync(settings, CancellationToken.None));
            ProfileUiSettingsFile.Write(_currentProfile.DataDirectory, textSize, appearance);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        MarkSaved(GeneralSection);

        // If neither the language, the text size nor the appearance
        // changed, no restart needed. The query log follows the file
        // within a second. A ReferenceCountry change alone is picked up
        // at the next opening of the medicine form via IOptionsMonitor
        // (user.settings.json is watched with reloadOnChange=true).
        // The text size is applied when each window loads and the
        // appearance before the first one is created; the main window
        // is already open, hence the restart.
        var languageChanged = !string.Equals(choice.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase);
        var textSizeChanged = TextSizes.ScaleOf(textSize) != MedReminderFormBase.TextScale;
        var appearanceChanged = appearance != UiTheme.Appearance;
        var profileChanged = textSizeChanged || appearanceChanged;
        if (!languageChanged && !profileChanged)
        {
            if (!quiet)
            {
                UiMessageBox.Show(this,
                    _loc.Get("Ui.SettingsDialog.General.Saved"),
                    _loc.Get("Common.Ok"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get(profileChanged
                ? "Ui.SettingsDialog.General.RestartPrompt.Changes"
                : "Ui.SettingsDialog.General.RestartPrompt"),
            _loc.Get("Ui.SettingsDialog.General.RestartPrompt.Title"),
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return true;

        if (profileChanged)
        {
            // Text size and appearance belong to this profile: reopen
            // it rather than the picker.
            _restarter.RestartAndExit(new[] { "--profile", _currentProfile.Id });
        }
        else
        {
            _restarter.RestartAndExit();
        }
        return true;
    }

    // Runs a use case of the installation settings in its own scope.
    private async Task RunUseCaseAsync<T>(Func<T, Task> action) where T : notnull
    {
        if (_scopes is null) throw new InvalidOperationException("The settings dialog has no service scope.");
        await using var scope = _scopes.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<T>());
    }

    private sealed record LanguageChoice(string Code, string DisplayName);

    private sealed record TextSizeChoice(TextSize Size, string DisplayName);

    private sealed record AppearanceChoice(AppearanceMode Mode, string DisplayName);

    // Mappa un codice ISO 639-1 sulla chiave JSON che restituisce il
    // the language name in the current UI language. Unknown codes
    // ricadono su Language.English (fail-safe).
    private static string LanguageDisplayKey(string code) => code switch
    {
        "en" => "Language.English",
        "it" => "Language.Italian",
        "fr" => "Language.French",
        "es" => "Language.Spanish",
        "de" => "Language.German",
        _ => "Language.English",
    };
}

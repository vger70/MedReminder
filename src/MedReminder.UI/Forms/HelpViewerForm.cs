using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Markdig;
using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MedReminder.UI.Forms;

// Guida utente integrata (Incremento 14).
//
// Il file docs/USER_GUIDE.md è embedded come resource; a ogni apertura
// viene renderizzato in HTML con Markdig (in-memory, no I/O) e caricato
// in un WebView2 con NavigateToString (nessun file temporaneo scritto
// su disco).
//
// WebView2 richiede l'Edge Runtime, che è pre-installato su Windows 11
// e Windows 10 con aggiornamenti recenti. Se l'inizializzazione fallisce
// mostriamo un messaggio di fallback che offre di aprire la guida
// direttamente su GitHub nel browser predefinito.
internal sealed class HelpViewerForm : MedReminderFormBase
{
    // Il pulsante "Apri su GitHub" punta alla versione MD della guida
    // nella lingua UI corrente. GitHub rende automaticamente il markdown.
    private string GithubGuideUrl =>
        $"https://github.com/vger70/MedReminder/blob/main/docs/USER_GUIDE.{_loc.CurrentLanguage}.md";

    private readonly ILocalizationService _loc;
    private readonly WebView2 _webView;
    private readonly ToolStripButton _btnBack;
    private readonly ToolStripButton _btnForward;
    private readonly ToolStripButton _btnOpenBrowser;
    private readonly Label _fallbackLabel;

    public HelpViewerForm(ILocalizationService localization)
    {
        _loc = localization;
        Text = _loc.Get("Ui.HelpViewer.Title");
        Width = 900;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(560, 400);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        _fallbackLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = _loc.Get("Ui.HelpViewer.Loading"),
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = System.Drawing.Color.DarkGray,
            Visible = true,
        };

        _btnBack = new ToolStripButton(_loc.Get("Ui.HelpViewer.Back"))
        {
            Image = Mdl2Glyph.Create("", size: 20), // Back
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Enabled = false,
        };
        _btnForward = new ToolStripButton(_loc.Get("Ui.HelpViewer.Forward"))
        {
            Image = Mdl2Glyph.Create("", size: 20), // Forward
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Enabled = false,
        };
        _btnOpenBrowser = new ToolStripButton(_loc.Get("Ui.HelpViewer.OpenGithub"))
        {
            Image = Mdl2Glyph.Create("", size: 20), // Globe
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
        };
        _btnBack.Click += (_, _) =>
        {
            if (_webView.CoreWebView2?.CanGoBack == true) _webView.CoreWebView2.GoBack();
        };
        _btnForward.Click += (_, _) =>
        {
            if (_webView.CoreWebView2?.CanGoForward == true) _webView.CoreWebView2.GoForward();
        };
        _btnOpenBrowser.Click += (_, _) => OpenOnGithub();

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
        };
        toolbar.Items.Add(_btnBack);
        toolbar.Items.Add(_btnForward);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_btnOpenBrowser);

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(_fallbackLabel);
        container.Controls.Add(_webView);
        Controls.Add(container);
        Controls.Add(toolbar);

        _webView.NavigationCompleted += (_, _) => UpdateNavButtons();

        Load += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // EnsureCoreWebView2Async carica l'Edge Runtime e prepara
            // l'istanza. Fallisce se il runtime non è installato.
            await _webView.EnsureCoreWebView2Async(null);

            var html = BuildHtmlFromEmbeddedGuide();
            _webView.NavigateToString(html);
            _fallbackLabel.Visible = false;
            _webView.Visible = true;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException
                                     or InvalidOperationException
                                     or COMException
                                     or FileNotFoundException)
        {
            _ = ex;
            ShowFallback(_loc.Get("Ui.HelpViewer.RuntimeMissing"));
        }
        catch (Exception ex)
        {
            ShowFallback(_loc.Get("Ui.HelpViewer.LoadError", ex.Message));
        }
    }

    private void ShowFallback(string message)
    {
        _webView.Visible = false;
        _fallbackLabel.Text = message;
        _fallbackLabel.ForeColor = System.Drawing.Color.Firebrick;
        _fallbackLabel.Visible = true;
    }

    private void UpdateNavButtons()
    {
        var core = _webView.CoreWebView2;
        _btnBack.Enabled = core?.CanGoBack ?? false;
        _btnForward.Enabled = core?.CanGoForward ?? false;
    }

    private void OpenOnGithub()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GithubGuideUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.HelpViewer.BrowserError", ex.Message),
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildHtmlFromEmbeddedGuide()
    {
        // Guida localizzata (Incremento 16e). Ordine di risoluzione:
        //   1. File nella lingua utente sotto <bin>/localization/
        //      USER_GUIDE.<lang>.md (Content copiato dal csproj).
        //   2. File embedded nella lingua utente.
        //   3. Come 1-2 ma sulla lingua di default ("en").
        //   4. Se manca tutto, messaggio "Guida non disponibile".
        var lang = _loc.CurrentLanguage;
        var markdown =
            TryLoadGuideFromDisk(lang) ??
            TryLoadGuideFromEmbedded(lang) ??
            TryLoadGuideFromDisk("en") ??
            TryLoadGuideFromEmbedded("en") ??
            _loc.Get("Ui.HelpViewer.NoResource");

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions() // tabelle, task list, autolink, footnote, ecc.
            .Build();
        var body = Markdown.ToHtml(markdown, pipeline);

        // Template HTML minimale con CSS inline: font di sistema, larghezza
        // controllata, tema adattivo (chiaro/scuro) via prefers-color-scheme.
        // Nessun asset esterno — tutto self-contained per NavigateToString.
        return $@"<!DOCTYPE html>
<html lang=""it"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Guida MedReminder</title>
<style>
  :root {{
    --bg: #ffffff;
    --fg: #1f2328;
    --muted: #6a737d;
    --accent: #1F5AA6;
    --code-bg: #f5f5f5;
    --border: #e1e4e8;
  }}
  @media (prefers-color-scheme: dark) {{
    :root {{
      --bg: #1e1e1e;
      --fg: #e6e6e6;
      --muted: #a0a0a0;
      --accent: #4A9EFF;
      --code-bg: #2a2a2a;
      --border: #3a3a3a;
    }}
  }}
  html, body {{ background: var(--bg); color: var(--fg); }}
  body {{
    font-family: 'Segoe UI', 'Segoe UI Variable', 'Segoe UI Emoji', Arial, sans-serif;
    font-size: 14.5px;
    line-height: 1.55;
    max-width: 860px;
    margin: 0 auto;
    padding: 24px 36px 60px 36px;
  }}
  h1, h2, h3, h4 {{ color: var(--fg); font-weight: 600; margin-top: 1.8em; }}
  h1 {{ font-size: 1.85em; border-bottom: 1px solid var(--border); padding-bottom: 0.3em; }}
  h2 {{ font-size: 1.4em; border-bottom: 1px solid var(--border); padding-bottom: 0.2em; }}
  h3 {{ font-size: 1.15em; }}
  a {{ color: var(--accent); text-decoration: none; }}
  a:hover {{ text-decoration: underline; }}
  code {{
    background: var(--code-bg);
    padding: 0.15em 0.4em;
    border-radius: 3px;
    font-family: 'Cascadia Code', Consolas, 'Courier New', monospace;
    font-size: 0.92em;
  }}
  pre {{
    background: var(--code-bg);
    padding: 12px 14px;
    border-radius: 6px;
    overflow-x: auto;
  }}
  pre code {{ background: transparent; padding: 0; }}
  blockquote {{
    border-left: 4px solid var(--accent);
    margin: 1em 0;
    padding: 0.3em 1em;
    color: var(--muted);
    background: var(--code-bg);
    border-radius: 0 4px 4px 0;
  }}
  table {{ border-collapse: collapse; margin: 1em 0; }}
  th, td {{ border: 1px solid var(--border); padding: 6px 10px; text-align: left; }}
  th {{ background: var(--code-bg); }}
  hr {{ border: none; border-top: 1px solid var(--border); margin: 2em 0; }}
  ul, ol {{ padding-left: 1.6em; }}
  li + li {{ margin-top: 0.25em; }}
</style>
</head>
<body>
{body}
</body>
</html>";
    }

    // Cerca <bin>/localization/USER_GUIDE.<lang>.md. Null se assente
    // o illeggibile.
    private static string? TryLoadGuideFromDisk(string languageCode)
    {
        try
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "localization",
                $"USER_GUIDE.{languageCode}.md");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }
    }

    // Cerca fra le resource embedded di TUTTI gli assembly caricati
    // una che finisce in "USER_GUIDE.<lang>.md" — stesso pattern robusto
    // usato per i dizionari (LocalizationService).
    private static string? TryLoadGuideFromEmbedded(string languageCode)
    {
        var suffix = $"USER_GUIDE.{languageCode}.md";
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] resourceNames;
            try { resourceNames = assembly.GetManifestResourceNames(); }
            catch { continue; }
            foreach (var name in resourceNames)
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }
        return null;
    }
}


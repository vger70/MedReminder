using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Markdig;
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
    private const string GuideResourceName = "MedReminder.UI.USER_GUIDE.md";
    private const string GithubGuideUrl =
        "https://github.com/vger70/MedReminder/blob/main/docs/USER_GUIDE.md";

    private readonly WebView2 _webView;
    private readonly ToolStripButton _btnBack;
    private readonly ToolStripButton _btnForward;
    private readonly ToolStripButton _btnOpenBrowser;
    private readonly Label _fallbackLabel;

    public HelpViewerForm()
    {
        Text = "Guida utente — MedReminder";
        Width = 900;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(560, 400);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        _fallbackLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Inizializzazione del visualizzatore integrato…",
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = System.Drawing.Color.DarkGray,
            Visible = true,
        };

        _btnBack = new ToolStripButton("Indietro")
        {
            Image = Mdl2Glyph.Create("", size: 20), // Back
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Enabled = false,
        };
        _btnForward = new ToolStripButton("Avanti")
        {
            Image = Mdl2Glyph.Create("", size: 20), // Forward
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Enabled = false,
        };
        _btnOpenBrowser = new ToolStripButton("Apri su GitHub")
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
            ShowFallback(
                "Il visualizzatore integrato richiede Microsoft Edge WebView2 Runtime, " +
                "che non risulta installato o non è avviabile su questo PC.\n\n" +
                "Puoi comunque aprire la guida su GitHub cliccando \"Apri su GitHub\" " +
                "nella barra strumenti in alto.");
        }
        catch (Exception ex)
        {
            ShowFallback(
                "Impossibile mostrare la guida integrata: " + ex.Message +
                "\n\nProva ad aprire la versione online cliccando \"Apri su GitHub\".");
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
                "Impossibile aprire il browser: " + ex.Message,
                "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string BuildHtmlFromEmbeddedGuide()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(GuideResourceName);
        string markdown;
        if (stream is null)
        {
            markdown =
                "# Guida non disponibile\n\n" +
                "La risorsa `USER_GUIDE.md` non è embedded in questa build. " +
                "Apri la versione online cliccando \"Apri su GitHub\".";
        }
        else
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            markdown = reader.ReadToEnd();
        }

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
}


# ANALYSIS — A6: Donation / "Support Development" feature

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/donation-support` (per `CLAUDE.md` §5).
Corresponds to `EVOLUTION.md` §3.6 (Group A, item A6) and to the
requirement brief in `DONATION-SUPPORT-FEATURE.md`. Follows the
structure of `ANALYSIS-A1-REGIMENS.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree), `[INFERRED]`
(deduction from verified facts / from `DONATION-SUPPORT-FEATURE.md`),
`[UNCERTAIN]` (hypothesis pending confirmation). This analysis was
originally prepared from the documentation set and **not** re-checked
against a working copy of `src/`. Since the 2026-09-20 revision,
`ANALYSIS.md` (the Phase 1/2 baseline) is available: references to
content that `ANALYSIS.md` specifies are tagged `[VERIFIED against
ANALYSIS.md]` — this confirms the design was specified that way, not
that the current tree still implements it verbatim; a tree check at
implementation time still applies. Remaining `[INFERRED from docs]`
tags mark claims about surfaces added after the MVP (e.g. a Help
menu entry point) that `ANALYSIS.md` does not document.

A6 has **no dependencies** and is the cheapest item in the roadmap
(`EVOLUTION.md` §3.6: 3–5 developer-days, ship first). It touches
no database schema and no clinical logic.

---

## 1. Scope

### 1.1 Problem

There is today no way for a user to voluntarily support the
project. `EVOLUTION.md` §3.6 and `DONATION-SUPPORT-FEATURE.md` ask
for a small, self-contained "Support Development" surface that
sends the user to an **externally hosted** payment page.

### 1.2 Goal

A single, unobtrusive **"Support Development"** screen that lets the
user pick a donation amount and a provider (Stripe or PayPal) and
then opens the provider's **public hosted payment page** in the
default browser. The app never handles money, never holds secrets,
and never claims a payment succeeded.

### 1.3 What A6 is NOT

Hard boundaries, taken verbatim from
`DONATION-SUPPORT-FEATURE.md` and restated here so implementation
cannot drift:

- **No backend / no server.** No ASP.NET Core, no hosted API, no
  webhooks in v1 (`DONATION-SUPPORT-FEATURE.md` "Important
  constraints" + "do not fake functionality").
- **No secrets in the client.** No Stripe Secret Key, no Stripe
  restricted keys, no PayPal Client Secret, no access tokens, no
  private keys, no API credentials. Only **public payment URLs**.
- **No dynamic transaction creation.** No server-side API calls to
  mint a charge; only pre-configured Payment Links.
- **No payment verification / no false confirmation.** The app must
  not read the browser URL, scrape results, or claim "payment
  completed" merely because the browser opened.
- **No embedded browser.** No legacy `WebBrowser` control, no
  in-app rendering of the payment page.
- **No card data.** The app never sees or handles PAN / CVV / card
  data.
- **No nagware.** No auto-popup on launch, no repeated prompts.
- **Ko-fi / Buy Me a Coffee are not implemented in v1** — only the
  extensibility seam is created.

---

## 2. Preconditions — what already exists

Re-confirm against the tree before coding.

- **Clean Architecture layering.** UI → Application → Domain, with
  Infrastructure implementing Application ports (`CLAUDE.md` §3,
  `ANALYSIS.md` §2.1). The donation port belongs in Application; the
  browser-launch and config-loading adapters belong in
  Infrastructure; the form belongs in UI. `[VERIFIED — CLAUDE.md §3,
  ANALYSIS.md §2.1]`
- **Dependency injection + composition root.** The app builds an
  `IHost` (`Program.cs`) into which services are registered
  (`ANALYSIS.md` §2.6 + Increment 5, `ANALYSIS-MULTI-USER.md`). A6
  registers its service and provider adapters there. `[VERIFIED
  against ANALYSIS.md §2.6]`
- **Configuration system — JSON files under
  `%LOCALAPPDATA%\MedReminder\`.** The app already reads shared,
  admin-managed JSON settings such as `smtp.settings.json`,
  `backup.settings.json`, `user.settings.json` (`CLAUDE.md` §6,
  `ANALYSIS-MULTI-USER.md` §3; the data-folder choice is
  `ANALYSIS.md` §1.1 item 13). A6 adds one more shared JSON file
  of the same class (§4). No `appsettings.json` shipping-in-the-ZIP
  assumption is required. `[VERIFIED — CLAUDE.md §6]`
- **Logging.** A daily rolling log exists at
  `%LOCALAPPDATA%\MedReminder\logs\` with a "no secrets / no PII"
  rule (`CLAUDE.md` §6, `ANALYSIS.md` §2.10). A6 logs through the
  same sink and obeys the same rule (§10). `[VERIFIED — CLAUDE.md
  §6, ANALYSIS.md §2.10]`
- **Five-language localization.** `assets/localization/strings.<lang>.json`
  for `en`, `it`, `fr`, `es`, `de`, with `DictionaryParityTests`
  failing the build on a missing key (`CLAUDE.md` §2, §8;
  `ANALYSIS-A1-REGIMENS.md` §6). A6's user-facing strings go there
  (§11). `[VERIFIED — CLAUDE.md §8]`
- **WinForms host + tray + menus.** The app is a WinForms host with a
  main form, a toolbar and a tray icon (`CLAUDE.md` §1, §3;
  `ANALYSIS.md` §2.7, §2.13). A6 adds one menu entry and one dialog
  (§8). `[VERIFIED against ANALYSIS.md §2.7, §2.13]` **Caveat:** the
  `ANALYSIS.md` MVP documents a toolbar ("New", "Edit", "Add stock",
  "Register intake", "Check now", "Settings") and a tray menu
  ("Open", "Check now", "Settings", "Exit") — it does **not**
  document a **Help** menu. A Help surface (`HelpViewerForm`,
  `CLAUDE.md` line 40) was added after the MVP; the exact Help entry
  point A6 hangs off must be confirmed against the tree (see §8.1
  and §17 item 4). `[INFERRED from docs]`
- **No new NuGet package is needed.** `Process.Start` with
  `UseShellExecute = true` is a BCL call; MailKit / EF Core are
  untouched. A6 introduces **zero** new dependencies
  (`DONATION-SUPPORT-FEATURE.md` "Technical requirements" point
  10; `ANALYSIS.md` §2.2 keeps the dependency set deliberately
  minimal). `[INFERRED]`

---

## 3. Architecture

The feature is a thin vertical slice that respects the existing
layering. No Domain entity is added — donations are not part of the
medical/stock domain; they are an application-level concern.

```
UI (MedReminder.UI)
  DonateForm ──────────────► DonationService (Application)
                                   │  uses
                                   ├── IDonationProvider  (port, Application)
                                   │      ├── StripeDonationProvider  (Infrastructure)
                                   │      └── PayPalDonationProvider  (Infrastructure)
                                   ├── IUrlLauncher       (port, Application)
                                   │      └── ShellUrlLauncher        (Infrastructure)
                                   └── DonationOptions    (config, bound from JSON)
```

### 3.1 Application layer (ports + service + models)

- `IDonationProvider` — the provider port. Per the brief:

  ```csharp
  public interface IDonationProvider
  {
      DonationProvider Kind { get; }        // enum, see §5
      string Name { get; }                  // display name

      // No backend: this resolves a *public* payment URL for the
      // requested amount; it does NOT create a server-side charge.
      DonationLaunchResult Resolve(decimal amount);
  }
  ```

  **Deviation from the brief, justified.** The brief sketches
  `Task<DonationLaunchResult> CreateDonationAsync(...)`. Because v1
  is a pure Payment-Link lookup with **no I/O and no backend
  call**, an async `Task` signature would be misleading (there is
  nothing to await). A synchronous `Resolve` is honest about the
  v1 behavior. The v2 migration (§14) reintroduces `Task<...>` when
  a real backend call appears — that is a deliberate, documented
  signature change at the point where async actually starts to
  mean something. `[UNCERTAIN — confirm signature preference in
  §17]`

- `IUrlLauncher` — a one-method port wrapping the browser launch,
  so the launch is testable without spawning a real browser:

  ```csharp
  public interface IUrlLauncher
  {
      bool TryLaunch(Uri httpsUrl, out string? error);
  }
  ```

  This port is the key testability decision: the "successful
  browser launch" and "browser launch failure" tests
  (`DONATION-SUPPORT-FEATURE.md` "Testing") drive a fake
  `IUrlLauncher`, never `Process.Start`.

- `DonationService` — the single orchestrator the UI talks to. It
  owns **all** validation (§6) and never lets the UI touch a
  provider directly (`DONATION-SUPPORT-FEATURE.md` "Implementation
  requirements": "The UI should communicate with the donation
  service rather than containing payment-provider-specific logic").

- `DonationLaunchResult` — result value object:

  ```csharp
  public sealed record DonationLaunchResult(
      bool Success,
      DonationFailureReason? Reason,   // enum: Disabled, ProviderDisabled,
                                       // InvalidAmount, UnsupportedAmount,
                                       // MissingUrl, MalformedUrl, NotHttps,
                                       // LaunchFailed, IncompleteConfig
      Uri? ResolvedUrl,                // for logging/launch only, never shown raw
      string? UserMessageKey);         // localization key, not literal text
  ```

  The result carries a **localization key**, not a rendered
  string, so the UI stays the single place that resolves text
  (§11).

### 3.2 Infrastructure layer (adapters)

- `StripeDonationProvider`, `PayPalDonationProvider` — each maps an
  amount to the configured Payment Link from `DonationOptions`
  (§4). They contain **no** provider SDK, **no** HTTP client,
  **no** secret. They are pure lookups over the bound config.
- `ShellUrlLauncher` — the only place `Process.Start` is called:

  ```csharp
  public bool TryLaunch(Uri httpsUrl, out string? error)
  {
      try
      {
          Process.Start(new ProcessStartInfo
          {
              FileName = httpsUrl.ToString(),
              UseShellExecute = true,   // hand off to the default browser
          });
          error = null;
          return true;
      }
      catch (Exception ex)
      {
          error = ex.Message;           // logged, never shown raw (§10)
          return false;
      }
  }
  ```

- `JsonDonationOptionsProvider` (or reuse the existing settings
  loader) — reads `donations.settings.json` (§4) using the same
  mechanism as the other shared JSON settings. `[INFERRED from
  docs — confirm the exact loader type]`

### 3.3 UI layer

- `DonateForm` — a single dialog matching the app's visual style
  (§8). It binds to `DonationService`, renders amounts and
  providers, and shows result messages. It contains **no** URL
  literals and **no** provider-specific branching beyond selecting
  the `DonationProvider` enum value to pass to the service.

---

## 4. Configuration model + file

### 4.1 `DonationOptions`

Bound from JSON, adapted to the project's existing settings style
(`DONATION-SUPPORT-FEATURE.md` "Configuration model"):

```csharp
public sealed class DonationOptions
{
    public bool Enabled { get; set; }
    public string Currency { get; set; } = "EUR";
    public ProviderOptions Stripe { get; set; } = new();
    public ProviderOptions PayPal { get; set; } = new();
}

public sealed class ProviderOptions
{
    public bool Enabled { get; set; }
    // Key = amount as integer string ("2","5","10","20");
    // Value = public Payment Link URL (HTTPS).
    public Dictionary<string, string> PaymentLinks { get; set; } = new();
}
```

A single `ProviderOptions` type serves both providers — they differ
only by which links they hold, not by shape. This is what makes
adding Ko-fi / Buy Me a Coffee later a config-only + one-adapter
change (§5, §14).

### 4.2 `donations.settings.json` — shared, admin-managed

Placed at the **root** of `%LOCALAPPDATA%\MedReminder\` alongside
the other shared, admin-managed settings (`CLAUDE.md` §6,
`ANALYSIS-MULTI-USER.md` §3), **not** per-profile — donation links
are an app-wide, not a per-patient, concern.

```json
{
  "Donations": {
    "Enabled": true,
    "Currency": "EUR",
    "Stripe": {
      "Enabled": true,
      "PaymentLinks": {
        "2":  "https://donate.stripe.com/xxx_2eur",
        "5":  "https://donate.stripe.com/xxx_5eur",
        "10": "https://donate.stripe.com/xxx_10eur",
        "20": "https://donate.stripe.com/xxx_20eur"
      }
    },
    "PayPal": {
      "Enabled": true,
      "PaymentLinks": {
        "2":  "https://www.paypal.com/donate/?hosted_button_id=xxx2",
        "5":  "https://www.paypal.com/donate/?hosted_button_id=xxx5",
        "10": "https://www.paypal.com/donate/?hosted_button_id=xxx10",
        "20": "https://www.paypal.com/donate/?hosted_button_id=xxx20"
      }
    }
  }
}
```

- **No DPAPI.** These are public URLs, not secrets, so the file is
  plain JSON — unlike `smtp.protected` (`CLAUDE.md` §6,
  `ANALYSIS.md` §2.11). The file is safe to ship as a template and
  safe to sit unencrypted.
- **Missing file / missing section.** Treated as
  `Enabled = false` → the whole feature is silently off and the
  menu entry is hidden or disabled (§8). No crash, no error toast
  on launch.
- **Editable without recompiling** (`DONATION-SUPPORT-FEATURE.md`
  "Configuration"): shipping a template and letting the maintainer
  paste real links satisfies this.

---

## 5. Provider abstraction + custom amount

### 5.1 The enum

```csharp
public enum DonationProvider
{
    Stripe,
    PayPal,
    KoFi,          // reserved, not implemented in v1
    BuyMeACoffee,  // reserved, not implemented in v1
}
```

The enum lists future providers so the UI and service switch on a
stable type; only `Stripe` and `PayPal` have adapters registered in
v1 (`DONATION-SUPPORT-FEATURE.md` "Provider abstraction"). Adding
Ko-fi later = one adapter + config, no UI/service rewrite.

### 5.2 Amounts — fixed tiers €2 / €5 / €10 / €20

v1 offers the four fixed tiers, each backed by a **fixed-amount
Payment Link** (`DONATION-SUPPORT-FEATURE.md` "Donation amounts").
The amount → URL mapping is exactly the `PaymentLinks` dictionary
in §4.

### 5.3 Custom amount — `[UNCERTAIN]`, off by default in v1

The brief allows a custom amount **only** if the provider
officially supports it **without secrets or a backend**
(`DONATION-SUPPORT-FEATURE.md` "Donation amounts"). Reality:

- **Stripe Payment Links** can be configured to let the *customer
  choose* the amount, but that is a property **of the link**, not
  something the client can inject via URL. So a Stripe "customer
  chooses amount" link is just one more entry in the config (e.g.
  key `"custom"`), and the client cannot pass a number.
- **PayPal donate** links can accept an `amount` query parameter on
  some hosted-button configurations, but this is inconsistent and
  provider-account-dependent; the brief explicitly forbids
  inventing a URL amount mechanism the provider does not officially
  support.

**Decision for v1:** custom-amount is **disabled** and the four
fixed tiers are the only options, unless the maintainer supplies a
provider-native "choose your amount" Payment Link, in which case it
is surfaced as an extra fixed entry (config key `"custom"`) that
opens that link **without** the app passing a number. The
limitation is documented (`DONATION-SUPPORT-FEATURE.md` "Donation
amounts": "Otherwise, temporarily limit the available amounts to
the configured fixed values and clearly document this limitation").
Revisit in §17.

---

## 6. `DonationService` + validation

All validation lives in `DonationService`, before any URL is
launched (`DONATION-SUPPORT-FEATURE.md` "Validation"). Ordered
checks, each short-circuiting with a specific
`DonationFailureReason` (§3.1):

1. **Feature enabled** — `DonationOptions.Enabled == true`, else
   `Disabled`.
2. **Provider enabled** — the chosen provider's `Enabled == true`,
   else `ProviderDisabled`.
3. **Amount valid** — `amount > 0` and not absurdly large
   (reject `<= 0`, reject `> configured max tier` unless a native
   custom link exists), else `InvalidAmount` / `UnsupportedAmount`.
4. **URL exists** — the `PaymentLinks` dictionary has an entry for
   the amount key, else `MissingUrl`.
5. **URL is HTTPS** — `uri.Scheme == Uri.UriSchemeHttps`, else
   `NotHttps`.
6. **URL is syntactically valid** — parses as an absolute `Uri`,
   else `MalformedUrl`.
7. **Incomplete config** — provider enabled but its `PaymentLinks`
   empty/null → `IncompleteConfig`.

Only if all pass does the service call `IUrlLauncher.TryLaunch`; a
launch exception maps to `LaunchFailed`.

**The end user never types a URL** (`DONATION-SUPPORT-FEATURE.md`
"Validation": "Do not allow arbitrary URLs to be entered by the end
user"). The only URLs that can ever be launched are the ones the
maintainer put in `donations.settings.json`, each re-validated at
launch time.

---

## 7. Browser launch

Exactly the BCL mechanism the brief mandates
(`DONATION-SUPPORT-FEATURE.md` "Browser handling") — see
`ShellUrlLauncher` in §3.2. Constraints, restated:

- `Process.Start` with `UseShellExecute = true` → default Windows
  browser.
- **No** legacy `WebBrowser` control; **no** embedded payment page.
- Exceptions are caught, logged (§10), and surfaced to the user as
  a **non-technical** message
  (`Ui.Donate.Error.LaunchFailed` → "Unable to open the support
  page. Please try again later.").

---

## 8. UI

### 8.1 Entry point — one unobtrusive menu item

A single entry under a **Help** menu (and/or the tray menu), keyed
`Ui.MenuHelp.SupportDevelopment` (`DONATION-SUPPORT-FEATURE.md`
"User experience", `EVOLUTION.md` §3.6). No auto-popup on launch,
no recurring nag, no startup dialog. The entry is hidden or
disabled when `DonationOptions.Enabled == false` (§4.2).

`[INFERRED from docs]` — **the "Help menu" is not documented in the
`ANALYSIS.md` MVP.** `ANALYSIS.md` §2.7 lists a toolbar and §2.13 a
tray menu (Open / Check now / Settings / Exit); neither includes a
Help menu. A Help surface exists post-MVP — `CLAUDE.md` line 40
references `HelpViewerForm` rendering the shipped user guides — so a
Help entry point plausibly exists, but its presence and exact
placement must be confirmed against the tree at implementation time.
If no Help menu exists, the fallback is the tray menu or a small new
Help menu; the choice is an open decision (§17 item 4).

`[UNCERTAIN]` — the emoji "❤️" from the brief's suggested title is
**not** used in code/menu text by default: `CLAUDE.md` §8 says "no
emoji unless the user explicitly asks". The menu label is plain
localized text ("Support Development"). Confirm in §17 whether the
heart is wanted in the *UI string* (it is allowed there — it is a
user-visible localized value, not repo prose — but the default is
no emoji to match house style).

### 8.2 The dialog `DonateForm`

Layout mirrors the brief:

```
Support Development

Do you like this application? If you want, you can voluntarily
support its development with a small contribution.

Amount:      [ €2 ] [ €5 ] [ €10 ] [ €20 ]

Payment:     ( ) Stripe    ( ) PayPal

             [ Continue with Stripe ]      [ Close ]
```

- The primary button label is dynamic: "Continue with {provider}"
  (`Ui.Donate.Continue` formatted with the selected provider name).
- Copy states clearly the contribution is **voluntary** and not
  required to use the app (`DONATION-SUPPORT-FEATURE.md` "WinForms
  UI").
- Only enabled providers are shown/selectable (driven by §4 config).
- Custom-amount input is shown **only** if §5.3 native-custom is
  configured; otherwise it is absent.

### 8.3 Post-launch message — no false confirmation

After a **successful** `TryLaunch`, show only
(`DONATION-SUPPORT-FEATURE.md` "Payment status"):

> "A payment page has been opened in your browser. Thank you for
> supporting the project!" (`Ui.Donate.Launched`)

optionally followed by "You can return to the application when
you're finished." The app **never** says the payment completed —
it cannot know, and claiming otherwise is forbidden (§1.3).

---

## 9. Security

The single most important axis of this feature
(`DONATION-SUPPORT-FEATURE.md` "Security" + "Final security
check"). The design guarantees, by construction:

- **No secrets anywhere in the client** — the config schema (§4)
  has no field for a secret; the adapters (§3.2) hold no SDK and no
  credential. A reviewer can grep the repo and the shipped ZIP for
  `sk_live`, `sk_test`, `client_secret`, `access_token`,
  `-----BEGIN` and find nothing. This is an explicit release check.
- **Only public payment URLs** are stored and launched (§4).
- **HTTPS enforced** at validation time (§6 step 5) — a non-HTTPS
  link is rejected and never opened.
- **No card data** ever reaches the app (§1.3).
- **No false payment confirmation** (§8.3, §1.3).
- **No secret is written to the log** (§10), consistent with
  `CLAUDE.md` §6, §9 and `ANALYSIS.md` §2.10, §2.11.

Because the config file holds no secret, it is **not** DPAPI-
encrypted (contrast `smtp.protected`, `ANALYSIS.md` §2.11) —
encrypting public URLs would add complexity for no security gain.

---

## 10. Logging

Through the existing sink (`CLAUDE.md` §6, `ANALYSIS.md` §2.10).
**Log** (`DONATION-SUPPORT-FEATURE.md` "Logging"):

- Selected provider (enum name).
- Selected amount tier (e.g. "10 EUR").
- "Attempting to open checkout" + outcome (success / failure).
- Launch exception message on failure.
- Validation-failure reason (the `DonationFailureReason` enum).

**Never log**: card data, secrets, API credentials, access tokens,
private keys — none of which the app even possesses. The resolved
URL is logged at most at a low/verbose level and only as the
configured public link; it is never shown raw to the user. No PII
is involved (donations are anonymous from the app's side).

---

## 11. Localization

New keys in **all five** dictionaries under `assets/localization/`
(`CLAUDE.md` §8); `DictionaryParityTests` enforces parity
(`ANALYSIS-A1-REGIMENS.md` §6). Proposed keys:

- `Ui.MenuHelp.SupportDevelopment` — menu entry.
- `Ui.Donate.Title` — dialog title.
- `Ui.Donate.Intro` — voluntary-contribution blurb.
- `Ui.Donate.AmountLabel`, `Ui.Donate.PaymentLabel`.
- `Ui.Donate.Continue` — "Continue with {0}".
- `Ui.Donate.Close`.
- `Ui.Donate.Launched` — post-launch thank-you.
- `Ui.Donate.Error.Disabled`, `...ProviderDisabled`,
  `...InvalidAmount`, `...UnsupportedAmount`, `...MissingUrl`,
  `...MalformedUrl`, `...NotHttps`, `...LaunchFailed`,
  `...IncompleteConfig` — one non-technical message per failure
  reason (§6), e.g. `Ui.Donate.Error.LaunchFailed` = "Unable to
  open the support page. Please try again later."

**Italian strings**: same posture as A1/A5 — the PR may ship
`strings.it.json` with `TODO(it): <english fallback>` placeholders
if final wording is pending (`ANALYSIS-A1-REGIMENS.md` §12 point
1), per the maintainer's preference. All other four languages
supply real strings.

The shipped user guides (`USER_GUIDE.*.md`) gain a short "Support
Development" section explaining the feature is voluntary, opens the
default browser, and that MedReminder never processes the payment
itself.

---

## 12. Tests

No real payments, no real browser (`DONATION-SUPPORT-FEATURE.md`
"Testing"). Providers and the launcher are mocked/faked.

### 12.1 `MedReminder.Application.Tests` — `DonationService`

- **Valid amount** (each of €2/€5/€10/€20) → resolves the right
  URL, `TryLaunch` invoked once, `Success`.
- **Zero amount** → `InvalidAmount`, no launch.
- **Negative amount** → `InvalidAmount`, no launch.
- **Amount exceeding configured max** → `UnsupportedAmount`, no
  launch.
- **Unsupported amount** (e.g. €7 with no link) → `MissingUrl` /
  `UnsupportedAmount`, no launch.
- **Feature disabled** (`Enabled=false`) → `Disabled`, no launch.
- **Provider disabled** (`Stripe.Enabled=false`) →
  `ProviderDisabled`, no launch.
- **Missing payment URL** (empty dict) → `MissingUrl` /
  `IncompleteConfig`, no launch.
- **Malformed URL** ("not a url") → `MalformedUrl`, no launch.
- **Non-HTTPS URL** ("http://…") → `NotHttps`, no launch.
- **Successful browser launch** → fake `IUrlLauncher` returns
  true → `Success`, correct log line.
- **Browser launch failure** → fake `IUrlLauncher` returns false
  → `LaunchFailed`, error logged, non-technical message key
  returned.
- **Incomplete configuration** (provider enabled, no links) →
  `IncompleteConfig`, no launch.

### 12.2 `MedReminder.Infrastructure.Tests`

- `StripeDonationProvider` / `PayPalDonationProvider` map amount →
  configured URL correctly and return the right failure reason when
  the key is absent.
- `JsonDonationOptionsProvider` binds a sample JSON to
  `DonationOptions`; a missing file → `Enabled=false`.
- (Optional) a **repo/artefact scan** test asserting no
  secret-shaped string is present in the sample config — a
  defensive guard for the §9 release check. `[UNCERTAIN — nice to
  have]`

`ShellUrlLauncher` itself is not unit-tested against a live
browser; it is covered indirectly via the `IUrlLauncher`
abstraction and manual QA.

---

## 13. Non-goals recap

- No backend, no webhooks, no server-side verification, no PayPal
  order capture, no Stripe secret-key calls (§1.3).
- No automatic payment confirmation, no browser-result scraping.
- No Ko-fi / Buy Me a Coffee adapters in v1 (enum reserved only).
- No embedded browser / no `WebBrowser` control.
- No new NuGet dependency, no schema change, no per-profile data.
- No emoji in repo prose; emoji in the UI string only if confirmed
  (§8.1, §17).

---

## 14. Future backend migration (v2)

The abstraction is chosen so v2 is an adapter swap, not a rewrite
(`DONATION-SUPPORT-FEATURE.md` "Future architecture"):

- `IDonationProvider` becomes `Task<DonationLaunchResult>
  CreateDonationAsync(...)` again (the async signature reserved for
  real I/O, §3.1). The **UI and `DonationService` shape stay the
  same**; only the provider adapters change to call a backend that
  mints a real Checkout Session / PayPal order and returns a
  one-time URL.
- `IUrlLauncher` is unchanged — v2 still opens a URL in the
  browser; the difference is the URL is now backend-minted and the
  backend can verify completion via webhook.
- The config gains a backend base-URL (still no secret in the
  client; the secret lives on the backend).

No UI redesign, no data migration.

---

## 15. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| A secret accidentally committed to config/repo | High (security) | Schema has no secret field (§4); explicit §9 release grep; optional scan test (§12.2); `.gitignore` already covers `*.pfx`/`*.p12` (`CLAUDE.md` §8, `ANALYSIS.md` §2.11) |
| App falsely implies payment succeeded | Medium (trust) | Post-launch copy says only "a page was opened" (§8.3); no verification code exists (§1.3) |
| Non-HTTPS or malformed link launched | Medium | Validation rejects before launch (§6 steps 5–6) |
| Nagware perception | Low | Single Help-menu entry, no auto-popup (§8.1) |
| Assumed Help menu does not exist in the tree | Low | Entry point is confirmed against the tree at implementation time; tray-menu or new Help menu fallback (§8.1, §17 item 4) |
| Provider changes its Payment-Link URL format | Low | URLs are config, editable without recompiling (§4.2) |
| Emoji in menu text violates house style | Low | Default no-emoji; heart only if confirmed (§8.1, §17) |
| Custom amount misimplemented via URL injection | Medium | Forbidden; only provider-native "choose amount" links allowed (§5.3) |

---

## 16. Implementation plan

One PR on `feature/donation-support` — the cheapest roadmap item,
recommended to ship first (`EVOLUTION.md` §3.6). Per `CLAUDE.md`
§5, the PR opens **after the first commit**, with a `CHANGE_LOG.md`
entry prepended when it opens. Indicative commit order:

1. Application: `DonationProvider` enum, `IDonationProvider`,
   `IUrlLauncher`, `DonationLaunchResult`,
   `DonationFailureReason`, `DonationOptions` / `ProviderOptions`.
2. Application: `DonationService` with the full validation pipeline
   (§6) + tests (§12.1) — TDD, before any adapter.
3. Infrastructure: `StripeDonationProvider`,
   `PayPalDonationProvider`, `ShellUrlLauncher`,
   `JsonDonationOptionsProvider` + tests (§12.2).
4. Composition root (`Program.cs`): register options, providers,
   launcher, service.
5. UI: `DonateForm` + Help-menu entry; wire to `DonationService`.
6. Localization: keys in all five dictionaries (Italian may be
   `TODO(it)` per §11).
7. Config: ship `donations.settings.json` **template** with
   placeholder links + documented steps to create real Stripe /
   PayPal Payment Links (README / PACKAGING or user guide).
8. `CHANGE_LOG.md` entry; "Support Development" section in
   `USER_GUIDE.*.md`; run the §9 final security check.

Run `dotnet build` and `dotnet test` before every source commit
(`CLAUDE.md` §8).

**Effort.** 3–5 developer-days (`EVOLUTION.md` §3.6). `[INFERRED]`

---

## 17. Decisions still to confirm

1. **Provider port signature** (§3.1). This analysis uses a
   synchronous `Resolve(amount)` for v1 (no I/O), reserving
   `Task<...>CreateDonationAsync` for the v2 backend. Confirm this
   over keeping the async signature from the brief now.
2. **Custom amount** (§5.3). Recommended **off** in v1 (fixed
   tiers only) unless a provider-native "choose your amount"
   Payment Link is supplied as a config entry. Confirm.
3. **Emoji in the UI label** (§8.1). Default: no emoji, plain
   "Support Development". Confirm whether the "❤️" from the brief
   is wanted in the *localized string* (allowed) or dropped
   (house-style default).
4. **Menu placement** (§8.1). Proposed under **Help** (and tray).
   `ANALYSIS.md` documents only a toolbar and a tray menu — a Help
   menu is a post-MVP surface (`HelpViewerForm`) whose presence
   must be confirmed against the tree. Confirm Help vs tray-only vs
   a dedicated top-level entry.
5. **Config file name/location** (§4.2). Proposed
   `donations.settings.json` at the `%LOCALAPPDATA%\MedReminder\`
   root, shared/admin-managed, plain JSON. Confirm the name.
6. **Where to document Payment-Link setup** — README, PACKAGING.md,
   or the user guides. Proposed: a short maintainer section in
   `PACKAGING.md` plus a user-facing note in the guides.

---

## Change log for this document

- 2026-09-20 — initial draft (pre-implementation). Derived from
  `DONATION-SUPPORT-FEATURE.md`, `EVOLUTION.md` §3.6, `ANALYSIS.md`
  and `ANALYSIS-MULTI-USER.md`. Key design choices beyond the
  brief: a synchronous v1 provider port (`Resolve`) with the async
  signature reserved for the v2 backend; an `IUrlLauncher` port to
  make browser launch unit-testable; `donations.settings.json` as a
  plain-JSON shared setting (no DPAPI, since it holds only public
  URLs); custom-amount deferred unless a provider-native link is
  configured; no emoji in repo prose per house style.
- 2026-09-20 — revision after `ANALYSIS.md` (the Phase 1/2 baseline)
  became available. Upgraded the Clean-Architecture layering, the
  DI/composition-root (`IHost`/`Program.cs`), the logging sink and
  the WinForms-host/tray preconditions from `[INFERRED from docs]`
  to `[VERIFIED against ANALYSIS.md]` (§§2.1, 2.6, 2.7, 2.10, 2.11,
  2.13). Corrected §8.1: `ANALYSIS.md` documents a toolbar and a
  tray menu only — **no Help menu** — so the "existing Help menu"
  A6 hangs off is a post-MVP surface (`HelpViewerForm`,
  `CLAUDE.md` line 40) that must be confirmed against the tree;
  added the corresponding caveat in §2, a risk row in §15 and
  detail to §17 item 4.

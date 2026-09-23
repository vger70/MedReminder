# ANALYSIS — Public Presentation Website for MedReminder

Design document, **prior** to implementation. This document defines
the requirements, structure, content, design constraints, and
technical approach for a public-facing presentation website that
accompanies the MedReminder desktop application.

> **This is not a speculative analysis.** Every decision is
> technically motivated. The "Decisions still to confirm" section is
> the only zone of ambiguity that needs input before work starts.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (fact established against the existing repository or
public knowledge), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (hypothesis pending confirmation).

**Relation to the application.** The website is a marketing and
distribution artifact, not a part of the application binary. It
shares the application's language policy (`CLAUDE.md` §2) — all
English in the repository, user-facing text in all supported
languages — but its code lives in a separate repository or in a
dedicated `website/` subtree, not in `src/`.

---

## 1. Scope

### 1.1 Problem

MedReminder is currently distributed via a GitHub Releases page
only (`PACKAGING.md` §14). That page is addressed at developers
and technically literate users; it is not designed to:

- make the app's value proposition clear at a glance to a
  non-technical audience (an elderly user or their family member);
- answer the most common pre-download questions (system
  requirements, privacy, the "not a medical device" disclaimer,
  what to do about the SmartScreen warning);
- provide a donation entry point for users who are not in the
  Windows app at the moment;
- surface the app in search engine results for queries like
  "medicine reminder Windows" or "promemoria medicine Windows".

A dedicated website closes all four gaps.

### 1.2 Goal

Produce a **multilingual static website** that:

1. Presents MedReminder attractively to a non-technical audience.
2. Drives downloads via a prominent, always-visible call to action.
3. Provides a donation surface that mirrors the in-app A6 feature
   without any backend.
4. Answers the most common pre-download questions (FAQ).
5. Links to the in-app user guides and to the GitHub repository.
6. Ranks for the natural-language queries that a user managing
   medication would type.
7. Is maintainable by a single developer without a CMS or a
   server.

### 1.3 What this website is NOT

- **Not a web application.** No user accounts, no data storage, no
  API calls except the donation redirect (which opens the user's
  browser, same as the in-app A6 flow).
- **Not a replacement for the in-app user guide.** The
  `USER_GUIDE.*.md` files are rendered inside the app via
  `HelpViewerForm`; the website may link to their GitHub-rendered
  versions but does not duplicate or maintain separate copies.
- **Not a medical-information resource.** The website must not
  give therapy advice, list medicines, or describe dosage
  calculations. The "not a medical device" disclaimer is prominent
  on every page, matching the posture of `CLAUDE.md` §1.
- **Not a tracked analytics platform.** No Google Analytics, no
  Facebook Pixel, no fingerprinting. A simple, privacy-respecting
  page-view counter is used instead — resolved to Cloudflare Web
  Analytics (§3.3, §8.4): no cookies, no cross-site tracking, no
  cookie consent banner required.
- **Not a forum or community platform.** Support and questions go
  to the GitHub Issues tracker.

---

## 2. Target audience

### 2.1 Primary audience

Adults managing long-term medication for themselves or a family
member. Typically not technically proficient with software. They
arrive from a search engine after typing something like "remind me
prescription medicine Windows" or "programma promemoria farmaci
Windows". They need to understand in under ten seconds what the
app does, that it is free and safe, and how to get it.

### 2.2 Secondary audience

Developers and technically literate users who found the GitHub
repository and want a better overview of the project's goals and
architecture before reading the code.

### 2.3 Implications

- Text must be simple and jargon-free on the user-facing sections.
- Technical details (architecture, technology stack) go in a
  collapsible or separate section, not above the fold.
- The download button must be reachable without scrolling on a
  1366×768 screen (the modal average for Windows laptops).
  `[INFERRED — 1366×768 remains a significant Windows laptop
  resolution segment as of writing]`
- Screenshots of the actual UI carry more persuasive weight than
  abstract feature lists for this audience.

---

## 3. Technical stack

### 3.1 Guiding constraints

The website must:

- be hostable for free or near-free, with no server-side runtime;
- require no CMS, no database, no Docker;
- be deployable via a single CI pipeline push (ideally the same
  GitHub Actions infrastructure used for the app releases);
- be writable and maintainable by a single developer who is not
  a web specialist;
- load in under 2 seconds on a 4G connection (Lighthouse
  Performance score ≥ 90 on mobile). `[INFERRED target]`

### 3.2 Recommended stack

**Static site generator: [Hugo](https://gohugo.io/).**

Rationale:

- Single binary, no Node.js runtime required for the maintainer.
- First-class multilingual support (content in `content/<lang>/`
  directories, language switcher built-in) — covers the five
  required languages without a plugin.
- Extremely fast build times (<1 s for a site of this size).
- Cloudflare Pages and GitHub Pages both support the build
  output natively; the output is a flat directory of HTML, CSS,
  and JS files with no runtime dependency.
- Well-documented, stable, actively maintained.

Alternatives considered:

| Option | Why not recommended |
|---|---|
| Eleventy (11ty) | Requires Node.js; adds a runtime dependency for the maintainer |
| Jekyll | GitHub Pages native support but slower and Ruby-dependent |
| Next.js / Astro | Overkill for a static marketing site; larger maintenance surface |
| Hand-written HTML/CSS | Maintainable for one language; breaks for five without a build step |
| WordPress | Requires a server and a database; contradicts the no-backend posture |

**Styling: CSS custom properties + a lightweight utility layer.**

No large CSS framework (Bootstrap, Tailwind CDN). Reason: this
site will be authored once and not changed frequently; a small,
purposeful CSS file is faster and easier to maintain than a
utility-class framework that requires a purge step. Estimated
uncompressed CSS: < 20 KB.

**JavaScript: minimal, no framework.**

The only runtime JavaScript needed is:

- the language switcher menu toggle on mobile;
- the donation amount selector (mirrors the A6 in-app UI);
- optional: a lazy-loading handler for the screenshot gallery.

No React, Vue, or Angular. No bundler. Total JS: < 10 KB
unminified.

**Image format: WebP with JPEG fallback (via `<picture>`).**

Screenshots and social preview images are WebP for modern
browsers; JPEG or PNG fallbacks for IE and older Safari. `[INFERRED
— WebP support is universal on the modern Windows browser set]`

### 3.3 Hosting

**Recommended: Cloudflare Pages.**

Rationale for choosing Cloudflare Pages over GitHub Pages:

| Criterion | GitHub Pages | Cloudflare Pages |
|---|---|---|
| Cost | Free | Free (unlimited requests and bandwidth) |
| CDN coverage | Fastly (~60 PoP) | Cloudflare (300+ PoP) |
| Europe coverage (primary audience) | Good | Excellent |
| Server-side redirects | ✗ (JS/meta-refresh only) | ✅ `_redirects` file |
| `Accept-Language` detection | ✗ (client-side JS only) | ✅ via Cloudflare Workers |
| Built-in privacy-first analytics | ✗ | ✅ (no cookies, GDPR-compliant) |
| PR preview deployments | ✗ | ✅ |
| DDoS protection | Basic | Robust (Cloudflare network layer) |
| GitHub Actions integration | Native | Via Wrangler or direct GitHub connection |
| Vendor lock-in | Minimal | Low (`_redirects` is the only specific file) |
| Initial setup complexity | Very low | Low |

**Key technical advantages for this site specifically.**

The five-language routing model requires a redirect from `/` to
the correct language prefix. On GitHub Pages this can only be done
with a JavaScript redirect or a `<meta http-equiv="refresh">`
tag — both degrade First Contentful Paint and cannot read the
`Accept-Language` HTTP header reliably. Cloudflare Pages resolves
this cleanly via a `_redirects` file for simple cases:

```
/ /en/ 302
```

For full `Accept-Language` detection (redirecting Italian browser
users to `/it/`, French users to `/fr/`, etc.), a Cloudflare
Worker function of under 20 lines reads the HTTP header and issues
the correct redirect at the edge, before the HTML is served.
This is included in the Cloudflare Pages free tier (100 000
Worker invocations/day). `[INFERRED — Worker limits on the free
tier; verify at implementation time]`

**Analytics.** Cloudflare Web Analytics is included in the free
plan, requires no cookie banner, and meets GDPR requirements
without a third-party service. This supersedes the earlier
Plausible (€9/month) and self-hosted Umami candidates; the
decision is recorded as resolved in §15.a — use Cloudflare Web
Analytics.

**Custom domain.** Cloudflare manages TLS automatically (Universal
SSL, Let's Encrypt). The custom domain is configured in the
Cloudflare dashboard; DNS must point to Cloudflare nameservers
(or a CNAME if the domain is hosted elsewhere). Recommended naming
pattern: `medreminder.app` or similar
(`[UNCERTAIN — domain availability must be checked]`).

**GitHub Pages as fallback.** If the maintainer prefers to stay
within GitHub only, GitHub Pages is a fully functional
alternative. The site works correctly on GitHub Pages with a
JavaScript language-redirect in `static/index.html` as the only
concession on the multilingual routing. All other sections of this
document apply unchanged to that scenario.

### 3.4 Deployment pipeline

A dedicated GitHub Actions workflow (`.github/workflows/website.yml`)
in the same repository (or in a sibling `website/` repository).
Two equivalent approaches are available; **Option A is recommended.**

**Option A — GitHub Actions + Wrangler (recommended).**

Keeps the build and deploy logic explicit and version-controlled
in the repository, independently of Cloudflare's own build
infrastructure. The Hugo version is pinned in the workflow,
avoiding unexpected build differences if Cloudflare changes its
bundled Hugo version.

```
push to main (website source files)
  |
  v
Hugo build (pinned version) → /public
  |
  v
wrangler pages deploy /public --project-name=medreminder
```

Secrets required: `CLOUDFLARE_API_TOKEN` and
`CLOUDFLARE_ACCOUNT_ID` stored as GitHub Actions secrets.

**Option B — Cloudflare Pages direct GitHub connection.**

Connect the repository (or the `website/` subtree) directly in
the Cloudflare Pages dashboard. Cloudflare detects pushes to
`main`, runs Hugo, and deploys automatically — no GitHub Actions
workflow needed. Simpler initial setup; less control over the
Hugo version and build environment.

In both options, pushes to non-`main` branches or pull requests
automatically receive a Cloudflare Pages preview URL, enabling
review of translations and layout changes before merging.

The workflow runs only when files under `website/` (or the
dedicated website repository root) change, not on every app commit.

---

## 4. Repository structure

If the website lives in the same repository as the app:

```
website/
  config/
    _default/
      config.toml       Hugo base config (title, baseURL, params)
      languages.toml    Five language definitions (en, it, fr, es, de)
      menus.toml        Navigation entries per language
  content/
    en/                 English content (index.md, faq.md, about.md)
    it/                 Italian content
    fr/                 French content
    es/                 Spanish content
    de/                 German content
  layouts/
    _default/           Base templates (baseof.html, single.html, list.html)
    partials/           Reusable components (header, footer, nav, donate)
    index.html          Home page template
  static/
    img/                Screenshots, hero image, social preview
    favicon.ico
    apple-touch-icon.png
    _redirects          Cloudflare Pages server-side redirects (/ → /en/, etc.)
  assets/
    css/                Source CSS (one or two files)
    js/                 Source JS files (language-switcher.js, donate.js)
  functions/
    _middleware.js      Cloudflare Worker for Accept-Language detection
                        (optional — needed only for auto language redirect)
  i18n/
    en.yaml             UI string translations (button labels, form strings)
    it.yaml
    fr.yaml
    es.yaml
    de.yaml
```

**Workflow location.** GitHub Actions only discovers workflows
under `.github/workflows/` at the **repository root**; nested
workflow directories are ignored. In the co-located case the
website build/deploy workflow therefore lives at
`.github/workflows/website.yml` in the repo root (alongside the
existing `dotnet-desktop.yml`), scoped with a `paths: [website/**]`
filter so it only runs when the website source changes. In the
separate-repository case the workflow lives at
`.github/workflows/website.yml` in that repository's root, without
the path filter, and the tree above collapses without the
`website/` prefix.

**Rationale for co-location vs. separation.** A dedicated
repository keeps the app CI and the website CI independent, avoids
Hugo build output polluting the app release workflow, and lets the
website evolve at its own cadence. A separate repository is
recommended if the website will have its own contributors.
Co-location is simpler for a single-maintainer project.
`[UNCERTAIN — decide based on whether the maintainer wants to
version the website alongside the app or independently]`

---

## 5. Page structure and navigation

The website is a **single-language-per-URL** multilingual site.
URLs follow the Hugo default pattern:

```
/          → redirects to /en/  (or to the browser language)
/en/       → English home page
/it/       → Italian home page
/fr/       → French home page
/es/       → Spanish home page
/de/       → German home page
/en/faq/   → English FAQ
/it/faq/   → Italian FAQ
…
```

Navigation bar links (all languages):

```
[Logo / App name]   Features   Screenshots   Download   Donate   About   FAQ
                                                                  [🌐 Language picker]
```

On mobile the navigation collapses to a hamburger menu.

---

## 6. Page sections — home page

The home page is the primary landing page and contains all
major content blocks as named anchors. Individual sections are
also reachable as sub-pages (`/en/faq/`, `/en/about/`) for SEO
and direct linking.

---

### 6.1 Hero section (`#hero`)

**Purpose.** Communicate the app's value proposition in one sentence,
provide a screenshot, and drive the primary conversion (download).

**Content.**

- **Tagline** (one sentence, ≤ 12 words): communicates the "what"
  and "for whom" without jargon.
  English draft:
  *"Never run out of your prescription — MedReminder tells you
  when to call the doctor."*
  The other four languages carry equivalent translations; the
  Italian version is the reference for tone and friendliness.
- **Sub-tagline** (one sentence): names the platform and the
  free/open-source nature.
  English draft:
  *"Free and open-source Windows app. No account, no cloud, no
  subscription."*
- **Primary CTA button**: **Download for Windows** (links to the
  stable download URL `https://github.com/vger70/MedReminder/
  releases/latest/download/MedReminder-win-x64.zip`). The button
  label must include the platform to set expectations; do not
  write "Download" alone.
- **Secondary CTA link**: **View on GitHub** (opens in a new tab).
- **Hero screenshot or product mockup**: one high-quality
  screenshot of the main medicine list view, showing the color-
  coded status badges and a plausible set of entries. Must be a
  real screenshot, not a placeholder. Width: ~600–700 px on
  desktop, full-width on mobile.
- **Disclaimer strip** (small, below the tagline or at the bottom
  of the hero): *"MedReminder is an organizational reminder, not a
  medical device. Every therapy decision must be made with your
  doctor."* This text must appear on every page, but it is most
  important here.

**Design notes.**

- The hero takes the full viewport height on desktop (100 vh),
  with the CTA button visible without scrolling on 1366×768.
- Colour palette is clean and trustworthy, not clinical. Avoid
  red (associated with errors) and hospital green. A calm blue or
  teal with a white background and a warm-grey secondary tone is
  the reference direction. `[Design decision — subject to
  confirmation]`
- The app icon (`assets/medreminder.ico`, a pill graphic) is used
  as the site favicon and beside the app name in the nav bar.

---

### 6.2 Features section (`#features`)

**Purpose.** Enumerate the key capabilities in a scannable grid.
A user who is comparing options must be able to verify the
features they need in under 30 seconds.

**Content format.** A 3-column grid of feature cards on desktop,
1-column on mobile. Each card has an icon, a short title (3–5
words), and a one-sentence description. No bullet sub-lists inside
cards.

**Feature cards — recommended set** (order reflects priority for
the target audience):

| Icon theme | Title | One-sentence description |
|---|---|---|
| Bell / calendar | Prescription reminder | Warns you when stock will run out, with a configurable lead time in days. |
| Pill / count | Stock tracking | Records every package opened, dose change, and correction, with full movement history. |
| Clock | Dose-time reminder | Sends a Windows notification at each scheduled dose time, so you never miss a tablet. |
| Shield / lock | Your data stays local | Everything is stored only on your PC. No account, no internet connection required. |
| Email | Email alerts | Optional email notification to your inbox or a caregiver's address when stock is low. |
| People | Multiple profiles | One installation manages multiple people, each with their own data and PIN. |
| Globe | Five languages | Interface available in English, Italian, French, Spanish and German. |
| Folder / cloud | Automatic backup | Daily encrypted backup to any folder, including cloud-synced ones (OneDrive, Dropbox, iCloud Drive). |
| Book / search | Medicine catalogue | Built-in reference catalogue for Italy, EU, Spain and France for quick medicine look-up. |
| Download / arrow | Free and open-source | Apache 2.0 license — free forever, no subscription, source code on GitHub. |

The feature card set may be shortened to 6–8 items if the grid
looks crowded; choose the items most relevant to the primary
audience. Dose-time reminder, local data, and free/open-source
are mandatory.

---

### 6.3 How it works (`#how-it-works`)

**Purpose.** Walk the primary audience through the three-step
workflow in plain language. Reduces hesitation before downloading.

**Content format.** A numbered step-flow: three or four steps,
each with an illustration (screenshot crop or a simple diagram)
and a short paragraph. On mobile, the steps stack vertically.

**Recommended steps:**

1. **Add your medicines.**
   Open MedReminder, add each medicine you take regularly. Enter
   the name, current stock, dose, and how many times a day you
   take it. A medicine look-up catalogue is included for Italy,
   the EU, Spain and France.
   *[Screenshot: the Add/Edit medicine form.]*

2. **Set your warning threshold.**
   For each medicine, choose how many days before running out you
   want to be reminded. MedReminder calculates the estimated
   run-out date automatically.
   *[Screenshot: the threshold field, with the estimated run-out
   date displayed.]*

3. **MedReminder notifies you in time.**
   When stock falls below your threshold, a Windows notification
   appears and, optionally, an email is sent. You see at a glance
   which medicines need a new prescription.
   *[Screenshot: the main list with colored status badges showing
   "Low stock" on one row.]*

4. *(Optional step)* **Restore on a new PC.**
   If you get a new computer, use the encrypted export / import
   to transfer all your data — no cloud service needed.
   *[Screenshot: the Export dialog.]*

---

### 6.4 Screenshots (`#screenshots`)

**Purpose.** Show the real UI to users who want to evaluate the
app before downloading. Authenticity matters: the screenshots must
be real, not mockups.

**Format.** A horizontal scroll gallery (on mobile) or a masonry
grid (on desktop) of 4–6 screenshots. Each screenshot has a short
caption. Clicking a thumbnail opens a lightbox (a minimal,
dependency-free implementation or a CSS-only one).

**Required screenshots:**

1. Main medicine list — several entries, mixed status badges
   (green / amber / red).
2. Edit medicine dialog — showing the schedule, dose time slots,
   and notification channel checkboxes.
3. Settings dialog — Backup section with automatic backup enabled.
4. Dose-time reminder toast notification (Windows notification
   center screenshot).
5. Export / import dialog.
6. Multi-profile selection screen (profile picker on startup).

All screenshots must be taken from a real installation with
plausible but non-personal data (no real names, no real medication
names that could be mistaken for a recommendation).

---

### 6.5 System requirements (`#requirements`)

**Purpose.** Prevent support requests from users who cannot run
the app. Presented inline (not in a separate page) because it is a
pre-download decision point.

**Content:**

- **Operating system:** Windows 10 version 22H2 or Windows 11
  (64-bit). `[VERIFIED — README.md]`
- **Architecture:** x64 only. `[VERIFIED — PACKAGING.md §5]`
- **Runtime:** none needed — the self-contained build includes the
  .NET 10 runtime. `[VERIFIED — PACKAGING.md §5]`
- **Disk space:** approximately 100 MB extracted. `[INFERRED from
  self-contained .NET 10 WinForms publish size]`
- **Network:** optional. Email notification requires an SMTP
  server; medicine catalogue look-up uses the local embedded
  database. The app never connects to the internet on its own.
- **SmartScreen note** (prominent — see §6.6): the binaries are
  not code-signed; a SmartScreen dialog will appear on first
  launch.

---

### 6.6 SmartScreen explanation (`#smartscreen`)

**Purpose.** Pre-empt the single most common reason users abandon
the download: the Windows SmartScreen warning. `[INFERRED — known
friction point for unsigned binaries]`

**Content format.** A short, honest, reassuring block with a
screenshot of the SmartScreen dialog and a numbered
click-through guide.

**Content:**

*When you first launch MedReminder.exe, Windows may show a blue
dialog: "Windows protected your PC". This is expected: the app
is not code-signed because code-signing certificates have a
recurring yearly cost that a volunteer-maintained free app
cannot currently absorb.*

*MedReminder is built automatically and transparently by a public
GitHub Actions pipeline from the tagged source code in this
repository — you can inspect every step.*

*To proceed:*
1. *Click "More info".*
2. *Click "Run anyway".*
3. *Windows remembers your choice — the dialog does not appear
   again for this file.*

*The UAC dialog raised by the MSI installer shows "Unknown
Publisher" for the same reason. This is expected.*

This section should link directly to the GitHub Actions workflow
file so technically literate users can verify the build
provenance.

---

### 6.7 Download section (`#download`)

**Purpose.** The primary conversion point. Must be unambiguous
and provide all the information needed to make the download
decision.

**Content:**

- **Large primary button:** "Download for Windows (64-bit)"
  — links to the stable URL:
  `https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip`
- **File info line** (below the button, small text):
  ZIP archive · Self-contained · ~100 MB
  (`[INFERRED size; verify after a real publish]`)
- **Version badge** (GitHub Releases API-driven, or a static
  string updated on each release; see §8.3 for the API
  approach): "Latest: v1.x.x"
- **Alternative installer link:**
  "Also available as an MSI installer — see all releases on
  GitHub" → links to the GitHub Releases page.
- **Hash / verification** (optional for v1): SHA-256 checksum
  published on the releases page for users who want to verify
  the download. `[PACKAGING.md §22 lists this as a future
  enhancement]`
- **License reminder** (small text):
  "Free and open-source · Apache License 2.0"

---

### 6.8 Donate / Support Development section (`#donate`)

**Purpose.** Provide a donation entry point for users who are not
currently in the Windows app. Mirrors the A6 in-app feature
exactly: same providers, same tiers, same constraint (no backend,
no payment confirmation, no secrets).

**Design constraint.** The donation flow on the website is
strictly:

```
User clicks amount → page opens provider's hosted payment URL
in the same or new tab → app makes no further network call
```

No server involved. The payment URLs are public Stripe Payment
Links and PayPal hosted donate button URLs, identical to those
configured in `donations.settings.json` (`PACKAGING.md` §23).

**Content:**

- **Section heading:** "Support development"
- **Short paragraph:** "MedReminder is free and will remain so.
  If it saves you time or worry, a small donation helps keep the
  project alive — renewing tooling, signing certificates, and
  developer time for future improvements."
- **Provider tabs or toggle:** Stripe / PayPal (default: Stripe,
  or the last-selected provider stored in `localStorage`).
- **Amount selector (radio buttons):** €2 / €5 / €10 / €20 /
  Custom. Matches the A6 in-app tiers exactly.
- **Donate button** (per-provider, per-amount): opens the correct
  provider URL in a new tab. The URL is hardcoded in the static
  HTML at build time, identical to the values in
  `donations.settings.json`.
- **Post-click message** (shown immediately after button click,
  before the tab opens):
  "A payment page has been opened in your browser. Thank you."
  This is the same honest copy as the in-app A6 flow — the site
  cannot confirm whether the payment was completed.
- **Non-obtrusive placement:** this section appears after the
  download section, not before it. Users are never interrupted
  by a donation request before or during the download flow.

**Implementation note.** The donation URLs must be updated in the
static source whenever the maintainer rotates the Stripe/PayPal
links. There is no dynamic fetch from an API. This is a
maintainer responsibility, not a technical automation.

---

### 6.9 Privacy statement (`#privacy`)

**Purpose.** GDPR and ePrivacy compliance; also a trust signal
for the medical-adjacent audience.

**Content:**

- The website itself collects no personal data from visitors.
- The site uses Cloudflare Web Analytics (see §3.3 and §8.4).
  The collected data is limited to: page views, referrer domain,
  operating system, browser family, and country derived from the
  IP address; the IP itself is not stored. No cookies are set. No
  cross-site tracking. No cookie consent banner is required under
  the ePrivacy Directive.
- Donation payments are processed entirely on the provider's
  platform (Stripe / PayPal). MedReminder's website receives no
  payment data.
- The desktop application stores all data locally on the user's
  PC. See `README.md` and the user guides for the full data
  layout.
- Contact for privacy questions: GitHub Issues.

The privacy statement is a short inline block on the home page
and also available as `/en/privacy/` for linking from cookie
banners or app store listings.

---

### 6.10 FAQ (`#faq` / `/en/faq/`)

**Purpose.** Answer the ten most common questions before the user
has to contact the maintainer. Reduces GitHub Issues volume for
non-technical questions.

**Format.** Accordion (expandable / collapsible items), so the
page does not become overwhelming. CSS-only accordion is preferred
(`<details>` / `<summary>` HTML elements — no JavaScript needed).

**Recommended question set:**

1. **Is MedReminder free?**
   Yes, completely free of charge. It is open-source software
   released under the Apache 2.0 license. There is no
   subscription, no premium tier, and no in-app purchase.

2. **Does it work on Mac or Linux?**
   No. MedReminder is a Windows desktop application and requires
   Windows 10 (22H2 or later) or Windows 11, 64-bit. A mobile
   companion is planned for the future but is not yet available.

3. **Does it need an internet connection?**
   No. All data is stored locally on your PC. An internet
   connection is only needed if you configure optional email
   notifications — the app uses your own SMTP server or email
   provider for that.

4. **Is my data sent to any server?**
   No. MedReminder never connects to the internet on its own. All
   your medicine data stays on your PC, in a folder under
   `%LOCALAPPDATA%\MedReminder\`. See the user guide for details.

5. **Windows shows a security warning when I open the app.
   Is it safe?**
   Yes. The warning appears because the binaries are not
   code-signed (see the SmartScreen explanation above). The app
   is built transparently by a public GitHub Actions pipeline from
   source code that anyone can read. Click "More info" then
   "Run anyway" to proceed; Windows will not ask again for that file.

6. **Can I use it to manage medication for multiple people?**
   Yes. MedReminder supports multiple profiles (one per person)
   from a single installation. Each profile has its own data and
   an optional PIN.

7. **Can I back up and restore my data?**
   Yes. The app includes automatic daily backup and manual
   export/import. Backups can be stored in any folder, including
   cloud-synced folders like OneDrive or Dropbox, using the
   encrypted archive format.

8. **Is this a medical device?**
   No. MedReminder is an organizational reminder. It helps you
   remember to request a prescription — it does not provide
   diagnoses, therapy advice, dose calculations, or any clinical
   information. Every therapy decision must be made with your
   doctor.

9. **What languages does the app support?**
   English, Italian, French, Spanish and German. The interface,
   email notifications, and the integrated user guide are all
   available in these five languages. Switch from Settings →
   General.

10. **How do I report a bug or request a feature?**
    Open an issue on the GitHub repository:
    `https://github.com/vger70/MedReminder/issues`. Please search
    for existing issues before opening a new one.

---

### 6.11 About section (`#about` / `/en/about/`)

**Purpose.** Humanize the project, explain its origin, and
establish trust for a medical-adjacent tool.

**Content:**

- **Origin paragraph.** MedReminder was created to solve a
  personal problem: keeping track of when to ask the doctor for
  a new prescription for several medicines, across different
  schedules and dosages. The first version was a spreadsheet;
  the app replaced it.
- **Philosophy paragraph.** Local-first: your medical data
  belongs on your device, not on a server you do not control.
  Free forever: no subscription model, no tiered features, no
  tracking. Open-source: the full source code is on GitHub under
  the Apache 2.0 license.
- **Technology paragraph** (brief, not technical): Built with
  C# and .NET 10, using Windows Forms for the interface and
  SQLite for local storage. It targets Windows 10 and 11 only.
- **Roadmap teaser** (one paragraph, non-committal): Future plans
  include a mobile companion app for Android and iOS, cloud-folder
  backup (with no cloud API dependency), and a barcode scanner for
  medicine look-up. See the GitHub repository for the full
  evolution document.
- **Not a medical device** (restated here explicitly): every
  paragraph that touches the product's purpose must include or
  follow the standard disclaimer.
- **License:** Apache 2.0. Link to the `LICENSE` file on GitHub.
- **GitHub repository link.**

---

### 6.12 Footer

Present on every page. Content:

- App name + icon.
- Navigation links: Features · Screenshots · Download · Donate ·
  FAQ · About · Privacy.
- Language switcher (same as the nav bar, repeated for users who
  reach the bottom).
- License line: "MedReminder is free and open-source software,
  released under the Apache License 2.0."
- Disclaimer: "MedReminder is an organizational reminder, not a
  medical device."
- GitHub link.
- Copyright line: "© 2024–\[current year\] MedReminder contributors."

---

## 7. Localization

### 7.1 Languages

The website is fully localized in the same five languages
as the application: **English (en), Italian (it), French (fr),
Spanish (es), German (de)**.

English is the canonical source language. All other languages are
translations of the English content. The Italian content is a
special case: for tone calibration, the Italian version is
reviewed by the maintainer (who is Italian), and the Italian
phrasing may diverge from a mechanical translation to match the
register of the intended audience.

### 7.2 Language detection and switching

On first visit, the preferred language is inferred from the
`Accept-Language` HTTP header. Hugo is a static site generator and
cannot read HTTP headers at runtime, so the detection happens at
the edge via the Cloudflare Worker function documented in §3.3
(`functions/_middleware.js`), which issues a 302 redirect from
`/` to the matching language prefix (`/en/`, `/it/`, …). If the
site is deployed on GitHub Pages instead (fallback in §3.3), the
Worker is unavailable and the redirect degrades to a
JavaScript-based fallback in `static/index.html`. The user can
switch language at any time via the language picker in the nav
bar. The selected language is stored in `localStorage` and applied
on subsequent visits, overriding the `Accept-Language` result.
`[INFERRED — standard pattern for Hugo + edge-detected language
routing]`

### 7.3 Content organization

Hugo's multilingual content model:

```
content/en/index.md       English home page content
content/it/index.md       Italian home page content
...
content/en/faq.md
content/it/faq.md
...
```

UI strings (button labels, form labels, generic headings) are
in `i18n/<lang>.yaml` rather than in the Markdown content files,
so they can be maintained separately from the narrative text.

### 7.4 SEO per language

Each language version has its own `<link rel="canonical">` and
`<link rel="alternate" hreflang="...">` tags generated by Hugo.
The page title, meta description, and Open Graph tags are
translated for each language. `[INFERRED — standard Hugo SEO
configuration]`

### 7.5 Translation maintenance

The English content is the source of truth. When the English
content changes, the corresponding changes in the four other
languages must be made in the same commit if the change is
structural (a new section, a changed factual claim). Minor
copy improvements may lag. A `TODO: translate` comment in the
content file signals an untranslated section.

---

## 8. SEO and discoverability

### 8.1 Target keywords

The primary audience searches in their native language. The five
highest-value query patterns, per language, are of the form:

- "medicine reminder Windows" (en)
- "promemoria medicine Windows" / "promemoria ricetta medica" (it)
- "rappel médicament Windows" (fr)
- "recordatorio medicamento Windows" (es)
- "Medikamenten-Erinnerung Windows" (de)

Each language's home page `<title>` and `<meta description>` must
target these patterns naturally (not keyword-stuffed).

### 8.2 Metadata per page

Every page must carry:

- `<title>`: unique, ≤ 60 characters.
- `<meta name="description">`: unique, 130–160 characters.
- `<meta property="og:image">`: a 1200×630 social preview image
  (the hero screenshot with the app name overlaid).
- `<meta name="robots" content="index, follow">` on all public
  pages; `noindex` on the `/privacy/` sub-page is optional but
  acceptable.

### 8.3 Dynamic version badge

The download section shows the latest release version. Two
implementation options:

**Option A — Static string updated by the maintainer.** The
version is hardcoded in the Hugo configuration and updated on each
release. Simple, no external dependency.

**Option B — GitHub Releases API badge.** A `<img>` pointing to
`https://img.shields.io/github/v/release/vger70/MedReminder?label=Latest`
(Shields.io) renders a live badge with no JavaScript. This
requires outbound requests to Shields.io's CDN on each page load.

Recommended: **Option A** for simplicity; adopt Option B if the
maintainer finds manual version updates error-prone.

### 8.4 Analytics

**Resolved — Cloudflare Web Analytics (§3.3).**

Cloudflare Web Analytics is included in the free Cloudflare Pages
plan. It requires adding a single `<script>` tag to the base
template; no cookies are set, no fingerprinting occurs, and no
cookie consent banner is required under the ePrivacy Directive.
The data (page views, referrer domain, country, browser, OS) is
visible in the Cloudflare dashboard. This replaces the earlier
Plausible / Umami candidates; the decision is recorded as
resolved in §15.a.

### 8.5 Sitemap and robots.txt

Hugo generates `sitemap.xml` and `robots.txt` automatically.
No manual intervention needed.

---

## 9. Accessibility

### 9.1 Target standard

WCAG 2.1 Level AA. `[INFERRED — the standard expected for a
medical-adjacent application in the EU]`

### 9.2 Specific requirements

- **Colour contrast:** all text on background must meet the 4.5:1
  ratio for normal text and 3:1 for large text. The colour palette
  must be verified with a contrast checker before release.
- **Keyboard navigation:** all interactive elements (nav, language
  picker, FAQ accordion, donation amount selector, buttons) must
  be reachable and operable via keyboard only.
- **Focus indicators:** visible focus ring on all interactive
  elements. Do not remove the browser's default outline without
  providing an equivalent custom indicator.
- **Images:** every non-decorative image has an `alt` attribute.
  Screenshots have descriptive alt text that summarizes what the
  screenshot shows (not "screenshot of MedReminder").
- **Semantic HTML:** use `<nav>`, `<main>`, `<section>`,
  `<article>`, `<header>`, `<footer>`, `<h1>`–`<h6>` in correct
  order. One `<h1>` per page.
- **`lang` attribute:** the `<html lang="...">` attribute must
  match the page's language.
- **Motion:** no autoplay animations; respect
  `prefers-reduced-motion` for any transition effects.

---

## 10. Performance

### 10.1 Targets

- Lighthouse Performance score ≥ 90 on mobile (simulated 4G,
  Moto G4 profile). `[INFERRED target]`
- First Contentful Paint < 1.5 s on desktop.
- Total page weight (compressed) < 500 KB excluding screenshots;
  < 2 MB with full screenshot gallery loaded. `[INFERRED]`

### 10.2 Tactics

- **CSS inlined in `<head>`** for above-the-fold styles (critical
  path CSS); the rest loaded asynchronously.
- **Screenshots lazy-loaded** via `loading="lazy"` on all `<img>`
  tags below the fold.
- **WebP images** with `<picture>` fallback.
- **No web fonts from Google Fonts or other CDN.** Use the
  system font stack:
  `font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;`
  This eliminates a render-blocking external request and matches
  the Segoe UI typeface used in the Windows app. `[INFERRED —
  system-ui resolves to Segoe UI on Windows]`
- **No third-party JavaScript** except the Cloudflare Web
  Analytics beacon (< 5 KB gzipped, served from the same
  Cloudflare edge as the site). No jQuery, no Bootstrap JS, no
  Google Tag Manager.
- Hugo's asset pipeline minifies and fingerprints CSS and JS
  automatically.

---

## 11. Design direction

### 11.1 Tone

Calm, trustworthy, human. The target audience is managing
medication — the site must convey reliability and simplicity, not
technical sophistication. No aggressive CTAs, no countdown timers,
no fake urgency.

### 11.2 Colour palette

The reference palette is a starting point subject to review:

| Role | Suggested value | Rationale |
|---|---|---|
| Primary (CTA, links) | `#2563EB` (calm blue) | Trustworthy, high contrast on white |
| Primary dark (hover) | `#1D4ED8` | Darker blue, accessible contrast |
| Background | `#FFFFFF` | Clean, easy to read |
| Surface | `#F8FAFC` | Subtle off-white for section alternation |
| Text | `#1E293B` | Near-black, comfortable reading contrast |
| Text secondary | `#475569` | Secondary labels, captions |
| Success / positive | `#059669` | Status badge: stock OK |
| Warning | `#D97706` | Status badge: stock low |
| Danger | `#DC2626` | Status badge: stock critical |

The status badge colours (success / warning / danger) must match
the colours used in the app itself. `[VERIFIED intent — the
README describes colored status badges; exact hex values must
be confirmed from a running instance]`

Avoid dark backgrounds for the main content. The app itself uses
a light theme; the website must not look tonally different.

### 11.3 Typography

System font stack (see §10.2): no external web fonts. Font sizes:

- Body: 16 px (1 rem).
- H1: 2.5–3 rem.
- H2: 1.75–2 rem.
- H3: 1.25 rem.
- Small / caption: 0.875 rem.

Line-height: 1.6 for body text. This is comfortable for an
audience that includes elderly users.

### 11.4 Illustration style

Real screenshots of the app are the primary visual asset. Do not
use stock photography of people holding medicines — the medical-
adjacent context makes this imagery feel either clinical or
manipulative. Flat, line-art icons (a single style set, not mixed
from multiple libraries) accompany the feature cards.

A free, permissively licensed icon set is required: Heroicons,
Feather, or Phosphor Icons are all acceptable (MIT or MIT-equivalent
license). The chosen set must be documented in the website's
`README.md`.

---

## 12. Content governance

### 12.1 What changes on every release

- Version number in the download section (§8.3, Option A).
- Donation URLs if the maintainer has rotated Stripe/PayPal links.
- Screenshots if the UI has changed significantly.
- FAQ if new common questions have emerged.

### 12.2 What must never change without review

- The "not a medical device" disclaimer. It must appear verbatim
  on the home page, the footer, and the about page.
- The SmartScreen explanation (§6.6). If the app gains code
  signing in a future release (`PACKAGING.md` §22), this section
  must be removed or replaced.
- The donation URLs. Incorrect URLs open the wrong payment page;
  verify after every change.

### 12.3 Language of repository content

Per `CLAUDE.md` §2: all files in the repository (including the
website source) are in English, with the exception of the
translated content files under `content/<lang>/` and
`i18n/<lang>.yaml`, which carry user-facing text in their
respective languages. Comments, configuration, template code, and
commit messages are English.

---

## 13. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Screenshots go stale after a UI change | Medium — damages credibility | Add a "refresh screenshots" step to the release checklist; store source screenshots in the repository |
| Donation URLs become stale or invalid | High — a broken payment link is a lost donation | Add URL verification (HTTP HEAD check) as a step in the website CI |
| SmartScreen section is left in after code signing is introduced | Low — mildly confusing | Add a conditional Hugo variable `codeSigningEnabled` that removes the section when `true` |
| Language translations lag significantly behind English | Medium — poor UX for non-English visitors | Tag untranslated sections with `TODO: translate`; display English fallback for those sections |
| Custom domain SSL certificate expires | High — site becomes unreachable | Cloudflare manages TLS renewal automatically (Universal SSL); no manual action required |
| Site references features not yet shipped (future roadmap items) | Medium — user downloads expecting a feature that is absent | The "roadmap" section is explicitly a "planned" list, not a "features" list; keep them separate |
| The "not a medical device" disclaimer is omitted from a new page | High — legal and reputational risk | Mandate it in the Hugo base template so it appears on every page automatically |

---

## 14. Non-goals

- No CMS, no admin UI, no server-side rendering.
- No user accounts, no login.
- No community forum or comment system.
- No in-site medicine database or drug interaction information.
- No analytics beyond a privacy-preserving page-view counter.
- No push notifications.
- No progressive web app (PWA) — the app itself is Windows-only
  desktop; a PWA would mislead mobile users.
- No payment processing on the website — donation URLs open the
  provider's hosted page.
- No dark mode in v1 (acceptable future addition; adds CSS
  complexity that is not justified for the first cut).

---

## 15. Decisions still to confirm

1. **Screenshot set.** Confirm which specific UI states to capture
   and whether screenshots are retaken after every release or only on
   significant UI changes. Screenshots must be real, not mockups.

2. **Roadmap section.** Include a "What's coming" section on the About
   page sourced from `EVOLUTION.md`? Must be labelled as planned, not
   promised.

3. **Dark mode in v1.** Omit (recommended) or include.

### 15.a Decisions already resolved

- **Hosting.** Cloudflare Pages, with GitHub Pages as a documented
  fallback (§3.3). Resolved 2026-09-22.
- **Analytics tool.** Cloudflare Web Analytics (§3.3, §8.4).
  Replaces the earlier Plausible / Umami candidates. Resolved
  2026-09-22.
- **Deployment pipeline.** GitHub Actions + Wrangler (Option A,
  §3.4), with Cloudflare direct connection (Option B) as a
  simpler alternative. Resolved 2026-09-22.
- **Repository placement.** Separate repository `vger70/medreminder-website` 
  for clean CI separation. Resolved 2026-09-23.
- **Custom domain.** Use the Cloudflare Pages default `*.pages.dev`. Resolved 2026-09-23.
- **Donation URLs.** The donate section requires real Stripe Payment
  Links and PayPal hosted button URLs are available in `donations.settings.json`. Resolved 2026-09-23.
- **Version badge.** Static string, manually updated per release. Resolved 2026-09-23.

---

## 16. Implementation plan

One repository (new or existing) and one PR. If the work happens
in this repository (co-located case), `CLAUDE.md` §5 governs:
branch prefix `claude/` or `feature/`, PR opened after the first
commit, `CHANGE_LOG.md` entry prepended when the PR opens. If the
work happens in a dedicated `vger70/medreminder-website`
repository, that repository sets its own conventions; the
`CHANGE_LOG.md` update does not apply. Indicative commit order:

1. **Scaffold.** Hugo project structure: `config/`, `layouts/`,
   `static/`, `assets/`, `i18n/`, `functions/`. Empty English
   content files. `static/_redirects` with the root-to-language
   redirect. Cloudflare Pages project created; GitHub Actions
   workflow (`.github/workflows/website.yml`) with Wrangler deploy
   (Option A, §3.4). Verify the pipeline deploys an empty Hugo
   site and the `_redirects` rule works.

2. **Design system.** CSS custom properties (colour palette,
   typography, spacing scale). Base template (`baseof.html`,
   header, footer, nav). Language switcher markup and JS toggle.
   No content yet — just the shell.

3. **English content — home page.** Hero (§6.1), Features (§6.2),
   How it works (§6.3). Real screenshots acquired and added to
   `static/img/`.

4. **English content — remaining sections.** Screenshots gallery
   (§6.4), Requirements + SmartScreen (§6.5–6.6), Download (§6.7),
   Donate (§6.8), Privacy (§6.9), FAQ (§6.10), About (§6.11).
   Footer (§6.12).

5. **Localization — Italian.** Translate all content files and
   `i18n/it.yaml`. Italian is the priority second language;
   review for tone by the maintainer.

6. **Localization — French, Spanish, German.** Translate remaining
   three languages. Mark untranslated sections with `TODO`.

7. **SEO and meta.** `<title>`, `<meta description>`, `og:image`
   for each language and each page. `sitemap.xml` and
   `robots.txt` auto-generated by Hugo.

8. **Accessibility audit.** Run the Lighthouse accessibility
   audit (target: score ≥ 90). Fix contrast issues, missing alt
   text, keyboard-navigation gaps.

9. **Performance audit.** Lighthouse Performance on mobile
   (target: score ≥ 90). Compress images, inline critical CSS,
   lazy-load screenshots.

10. **Docs.** `CHANGE_LOG.md` entry when the PR opens. A short
    `website/README.md` documenting the Hugo version, how to run
    locally (`hugo serve`), and the screenshot refresh procedure.

**Effort.** 2–3 developer-weeks for a single developer (English
content + Italian + one more language, full design, accessibility
and performance pass). The remaining three languages add 1–2 days
each if the content is translated by the maintainer.
`[INFERRED]`

---

## Change log for this document

- 2026-09-22 — initial draft. Defines the full requirements for
  the MedReminder public presentation website: audience analysis
  (§2), Hugo + GitHub Pages stack with rationale (§3), repository
  structure (§4), navigation model (§5), all twelve home page
  sections with content requirements and design notes (§6),
  five-language localization model (§7), SEO and keyword targets
  (§8), WCAG 2.1 AA accessibility requirements (§9), performance
  targets and tactics (§10), design direction with colour palette
  and typography (§11), content governance rules (§12), risk table
  (§13), non-goals (§14), eight open decisions (§15), and a
  ten-step implementation plan (§16).
- 2026-09-22 — hosting recommendation changed from GitHub Pages to
  **Cloudflare Pages** following a comparative analysis. Updated:
  §3.2 (removed GitHub Pages-specific rationale), §3.3 (full
  provider comparison table; Cloudflare Pages rationale; GitHub
  Pages documented as fallback), §3.4 (two-option deployment
  pipeline: Wrangler via GitHub Actions recommended; Cloudflare
  direct connection as alternative; PR preview deployments noted),
  §4 repository structure (added `static/_redirects` and
  `functions/_middleware.js`), new §8.4 (Cloudflare Web Analytics
  resolves the analytics decision), §13 risks (SSL risk updated),
  §15 decision #1 (Cloudflare project detail) and decision #3
  (analytics resolved — no further input needed), §16
  implementation plan step 1 (Wrangler and `_redirects` scaffold).
- 2026-09-22 — internal-consistency pass. §7.2 rewritten to state
  that `Accept-Language` detection runs at the Cloudflare Worker
  edge (not Hugo, which is a static generator); §6.9 and §10.2
  updated to name Cloudflare Web Analytics as the analytics
  provider (previously still referenced Plausible / Umami); §8.4
  numbering fixed (former duplicate §8.4 "Sitemap and robots.txt"
  renumbered to §8.5); §6.10 typo fixed (`:details` →
  `<details>`); §4 clarified that GitHub Actions workflows must
  live at the repository root even in the co-located layout
  (`.github/workflows/website.yml` with a `paths: [website/**]`
  filter), not inside `website/.github/`; §16 clarified that
  `CLAUDE.md` §5 governs only the co-located case; §15
  restructured — the resolved decisions (hosting, analytics,
  deployment pipeline) moved out of the "still to confirm" list
  into a new §15.a "Decisions already resolved" subsection, and
  the remaining decisions renumbered accordingly (former #4–#8
  become #3–#7). §15 decision #2 also updated to reference the
  Cloudflare Pages default URL (`*.pages.dev`) as the no-domain
  fallback.

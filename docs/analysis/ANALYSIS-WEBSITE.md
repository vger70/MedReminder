Ecco il documento integrato e aggiornato con tutti i punti richiesti (**A, B, C, D**).

Il testo sottostante rappresenta la versione integrale e definitiva di **`ANALYSIS-WEBSITE.md`**, pronta per essere salvata come file Markdown `.md` (scaricabile/copiabile).

---

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
languages — but its code lives in a separate repository (`vger70/medreminder-website`), not in `src/`.

---

## 1. Scope

### 1.1 Problem

MedReminder is currently distributed via a GitHub Releases page
only (`PACKAGING.md` §14). That page is addressed at developers
and technically literate users; it is not designed to:

* make the app's value proposition clear at a glance to a
non-technical audience (an elderly user or their family member);
* answer the most common pre-download questions (system
requirements, privacy, the "not a medical device" disclaimer,
what to do about the SmartScreen warning);
* provide a donation entry point for users who are not in the
Windows app at the moment;
* surface the app in search engine results for queries like
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

* **Not a web application.** No user accounts, no data storage, no
API calls except the donation redirect (which opens the user's
browser, same as the in-app A6 flow).
* **Not a replacement for the in-app user guide.** The
`USER_GUIDE.*.md` files are rendered inside the app via
`HelpViewerForm`; the website may link to their GitHub-rendered
versions but does not duplicate or maintain separate copies.
* **Not a medical-information resource.** The website must not
give therapy advice, list medicines, or describe dosage
calculations. The "not a medical device" disclaimer is prominent
on every page, matching the posture of `CLAUDE.md` §1.
* **Not a tracked analytics platform.** No Google Analytics, no
Facebook Pixel, no fingerprinting. A simple, privacy-respecting
page-view counter is used instead — resolved to Cloudflare Web
Analytics (§3.3, §8.4): no cookies, no cross-site tracking, no
cookie consent banner required.
* **Not a forum or community platform.** Support and questions go
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

* Text must be simple and jargon-free on the user-facing sections.
* Technical details (architecture, technology stack) go in a
collapsible or separate section, not above the fold.
* The download button must be reachable without scrolling on a
1366×768 screen (the modal average for Windows laptops).
`[INFERRED — 1366×768 remains a significant Windows laptop resolution segment as of writing]`
* Screenshots of the actual UI carry more persuasive weight than
abstract feature lists for this audience.

---

## 3. Technical stack

### 3.1 Guiding constraints

The website must:

* be hostable for free or near-free, with no server-side runtime;
* require no CMS, no database, no Docker;
* be deployable via a single CI pipeline push (ideally the same
GitHub Actions infrastructure used for the app releases);
* be writable and maintainable by a single developer who is not
a web specialist;
* load in under 2 seconds on a 4G connection (Lighthouse
Performance score ≥ 90 on mobile). `[INFERRED target]`

### 3.2 Recommended stack

**Static site generator: [Hugo](https://gohugo.io/?utm_source=gemini).**

Rationale:

* Single binary, no Node.js runtime required for the maintainer.
* First-class multilingual support (content in `content/<lang>/`
directories, language switcher built-in) — covers the five
required languages without a plugin.
* Extremely fast build times (<1 s for a site of this size).
* Cloudflare Pages and GitHub Pages both support the build
output natively; the output is a flat directory of HTML, CSS,
and JS files with no runtime dependency.
* Well-documented, stable, actively maintained.

Alternatives considered:

| Option | Why not recommended |
| --- | --- |
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

The runtime JavaScript needed is limited to:

* the language switcher menu toggle on mobile and client-side `localStorage` preference evaluation;
* dynamic release link resolution via GitHub API with static fallback;
* the donation amount selector (mirrors the A6 in-app UI);
* optional: a lazy-loading handler for the screenshot gallery.

No React, Vue, or Angular. No bundler. Total JS: < 12 KB
unminified.

**Image format: WebP with JPEG fallback (via `<picture>`).**

Screenshots and social preview images are WebP for modern
browsers; JPEG or PNG fallbacks for IE and older Safari. `[INFERRED — WebP support is universal on the modern Windows browser set]`

### 3.3 Hosting

**Recommended: Cloudflare Pages.**

Rationale for choosing Cloudflare Pages over GitHub Pages:

| Criterion | GitHub Pages | Cloudflare Pages |
| --- | --- | --- |
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
the correct language prefix. Cloudflare Pages resolves this cleanly
via a `_redirects` file or Edge Workers.

For full `Accept-Language` detection with explicit `localStorage` override support (§7.2), a Cloudflare Worker function reads the preference chain before the HTML is served.

**Analytics.** Cloudflare Web Analytics is included in the free
plan, requires no cookie banner, and meets GDPR requirements
without a third-party service.

**Custom domain.** Uses Cloudflare Pages default `*.pages.dev` or custom domain.

### 3.4 Deployment pipeline

A dedicated GitHub Actions workflow (`.github/workflows/website.yml`)
in the `vger70/medreminder-website` repository using GitHub Actions + Wrangler.

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
`CLOUDFLARE_ACCOUNT_ID`.

---

## 4. Repository structure

Repository: `vger70/medreminder-website`:

```
config/
  _default/
    config.toml       Hugo base config (title, baseURL, params)
    languages.toml    Five language definitions (en, it, fr, es, de)
    menus.toml        Navigation entries per language
content/
  en/                 English content
    img/              Localized English screenshots
  it/                 Italian content
    img/              Localized Italian screenshots
  fr/                 French content
    img/              Localized French screenshots
  es/                 Spanish content
    img/              Localized Spanish screenshots
  de/                 German content
    img/              Localized German screenshots
layouts/
  _default/           Base templates (baseof.html, single.html, list.html)
  partials/           Reusable components (header, footer, nav, donate)
  index.html          Home page template
static/
  img/                Shared assets (hero background, icons, fallback screenshots)
  favicon.ico
  apple-touch-icon.png
  _redirects          Cloudflare Pages server-side redirects
assets/
  css/                Source CSS
  js/                 Source JS files (release-checker.js, language-switcher.js, donate.js)
functions/
  _middleware.js      Cloudflare Worker for Accept-Language + localStorage routing
i18n/
  en.yaml             UI string translations
  it.yaml
  fr.yaml
  es.yaml
  de.yaml

```

---

## 5. Page structure and navigation

The website is a **single-language-per-URL** multilingual site:

```
/          → redirects to browser language or saved preference
/en/       → English home page
/it/       → Italian home page
/fr/       → French home page
/es/       → Spanish home page
/de/       → German home page
/en/faq/   → English FAQ
/it/faq/   → Italian FAQ

```

Navigation bar links (all languages):

```
[Logo / App name]   Features   Screenshots   Download   Donate   About   FAQ
                                                                  [🌐 Language picker]

```

---

## 6. Page sections — home page

### 6.1 Hero section (`#hero`)

**Purpose.** Communicate the app's value proposition in one sentence,
provide a localized screenshot, and drive the primary conversion (download).

**Content.**

* **Tagline**: *"Never run out of your prescription — MedReminder tells you when to call the doctor."* (and localized equivalents).
* **Sub-tagline**: *"Free and open-source Windows app. No account, no cloud, no subscription."*
* **Primary CTA button**: **Download for Windows** (dynamically resolved to the latest release `.zip` via client-side GitHub API check, falling back to static URL `[https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip](https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip)`).
* **Secondary CTA link**: **View on GitHub**.
* **Hero screenshot**: high-quality localized screenshot matching the page language.
* **Disclaimer strip**: *"MedReminder is an organizational reminder, not a medical device. Every therapy decision must be made with your doctor."*

---

### 6.2 Features section (`#features`)

3-column grid of feature cards. Each card has an icon, a short title, and a one-sentence description.

| Icon theme | Title | Description |
| --- | --- | --- |
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

---

### 6.3 How it works (`#how-it-works`)

Numbered step-flow:

1. **Add your medicines.**
2. **Set your warning threshold.**
3. **MedReminder notifies you in time.**
4. *(Optional)* **Restore on a new PC.**

---

### 6.4 Screenshots (`#screenshots`)

**Format.** Lightbox gallery showing localized screenshots according to the active site language (`/content/<lang>/img/` bundles with fallback to `/static/img/`).

**Required screenshots (localized per language):**

1. Main medicine list — several entries, mixed status badges.
2. Edit medicine dialog.
3. Settings dialog — Backup section.
4. Dose-time reminder toast notification.
5. Export / import dialog.
6. Multi-profile selection screen.

---

### 6.5 System requirements (`#requirements`)

* **OS:** Windows 10 (22H2+) / Windows 11 (64-bit).
* **Architecture:** x64.
* **Runtime:** Included (.NET 10 self-contained).
* **Disk space:** ~100 MB.
* **Network:** Optional.

---

### 6.6 SmartScreen explanation (`#smartscreen`)

Honest explanation for Windows SmartScreen warning on unsigned binaries with step-by-step instructions ("More info" → "Run anyway") and link to GitHub Actions build pipeline.

---

### 6.7 Download section (`#download`)

* **Primary Button:** "Download for Windows (64-bit)".
* **Dynamic Release Resolution (Point A):** Client-side JavaScript fetches `[https://api.github.com/repos/vger70/MedReminder/releases/latest](https://api.github.com/repos/vger70/MedReminder/releases/latest)` to resolve exact version string and direct release asset URL. If API fails or is rate-limited, degrades seamlessly to the static latest release fallback link.
* **File info line:** ZIP archive · Self-contained · ~100 MB.
* **Version badge:** Displays resolved release tag (e.g. `v1.2.0`).
* **Alternative installer link:** MSI installer via GitHub Releases.
* **License:** Apache License 2.0.

---

### 6.8 Donate / Support Development section (`#donate`)

Direct client-side payment redirect matching in-app A6 feature. Stripe and PayPal links loaded statically from configuration matching `donations.settings.json`.

---

### 6.9 Privacy statement (`#privacy`)

Details local-first data posture, Cloudflare Web Analytics (no cookies/tracking), and payment processor redirects.

---

### 6.10 FAQ (`#faq` / `/en/faq/`)

Accordion HTML (`<details>` / `<summary>`) covering 10 core questions including cost, OS compatibility, offline data security, multi-profile support, backup/restore, and bug reporting.

---

### 6.11 About section (`#about` / `/en/about/`)

Origin story, local-first philosophy, tech stack (.NET 10, WinForms, SQLite), roadmap teaser, and explicit medical disclaimer.

**Medical & Clinical Disclaimer Precision (Point C):**
In addition to the standard organizational tool disclaimer, the About section and FAQ explicitly state:
*"MedReminder is strictly an inventory and schedule tracking assistant. It does NOT automatically calculate, adjust, or suggest clinical dosage changes or medical therapy variations. All dosage modifications must be prescribed by a qualified healthcare professional."*

---

### 6.12 Footer

Nav links, language picker, copyright, Apache 2.0 license, and mandatory medical disclaimer.

---

## 7. Localization

### 7.1 Languages

Supported languages: **English (en), Italian (it), French (fr), Spanish (es), German (de)**.

### 7.2 Language detection and preference routing (Point B)

The routing pipeline strictly respects user explicit preferences:

```
1. Check localStorage for 'user_lang_preference'.
   If present -> Route to /<user_lang_preference>/
2. If no preference stored -> Read HTTP 'Accept-Language' header via Edge Worker.
   Route to matching language prefix (or /en/ as default fallback).
3. On manual language switch via UI picker -> Save choice to localStorage ('user_lang_preference')
   and navigate to target language URL.

```

### 7.3 Localized Assets & Screenshots (Point D)

Hugo content bundles organize localized screenshots directly inside content directories:
`content/<lang>/img/main-list.webp`.

Templates render language-specific UI screenshots when available, falling back to English assets in `/static/img/` if a localized screenshot is missing.

---

## 8. SEO and discoverability

* **Keywords:** Targeted to natural search queries per language.
* **Metadata:** Unique `<title>`, `<meta description>`, Open Graph tags, and `hreflang` links per language page.
* **Analytics:** Cloudflare Web Analytics (privacy-preserving, no cookies).

---

## 9. Accessibility

WCAG 2.1 Level AA compliance: contrast ratios ≥ 4.5:1, keyboard navigation, visible focus indicators, descriptive image `alt` text, semantic HTML, and system font stack.

---

## 10. Performance

* Lighthouse Performance score ≥ 90 on mobile.
* Critical CSS inlined.
* Lazy-loaded images with WebP format.
* Zero external CDN dependencies (system font stack: `font-family: system-ui, -apple-system, 'Segoe UI', sans-serif`).

---

## 11. Design direction

* **Tone:** Calm, trustworthy, accessible.
* **Color Palette:** Primary Blue (`#2563EB`), Surface (`#F8FAFC`), Text (`#1E293B`), Status badges matching the desktop application (Success `#059669`, Warning `#D97706`, Danger `#DC2626`).

---

## 12. Non-goals

No CMS, no backend DB, no user accounts, no tracking cookies, no PWA, no dark mode in v1.

---

## 13. Decisions resolved

* **Hosting:** Cloudflare Pages.
* **Analytics:** Cloudflare Web Analytics.
* **Pipeline:** GitHub Actions + Wrangler.
* **Repo:** Separate repository `vger70/medreminder-website`.
* **Domain:** `*.pages.dev` default.
* **Release Resolution:** Client-side GitHub API + static fallback (Point A).
* **Localized Assets:** Language-specific screenshot bundles (Point D).

---

## 14. Decisions confirmed at implementation start (2026-09-25)

The four items previously listed as open were confirmed by the
product owner before the first commit of the implementation.

* **Repository target.** `vger70/medreminder-website` — separate
  repository, in agreement with §13. The `CHANGE_LOG.md` update
  rule from `CLAUDE.md` §5 does not apply; the website repository
  sets its own conventions.
* **Roadmap teaser on the About page.** Omitted for v1. The About
  page describes the current app and its philosophy; a "What's
  coming" section is not shipped in the initial release.
* **Dark mode.** Omitted for v1, matching the non-goal in §12. The
  site ships light-only.
* **Screenshot refresh cadence.** Screenshots are retaken only when
  a UI change materially alters what a shot depicts, not on every
  release. The refresh procedure is documented in the website
  repository's `README.md`.
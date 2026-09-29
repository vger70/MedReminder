# Implementation prompt — Public presentation website for MedReminder

This file is the self-contained briefing for the Claude Code session
that will implement the MedReminder public presentation website. Read it
completely before touching any file.

---

## 0. What you are about to implement

A **multilingual static website** that presents MedReminder to a
non-technical audience, drives downloads, surfaces a donation entry
point, answers common pre-download questions, and ranks in search
engines for queries like "medicine reminder Windows".

The authoritative design lives in one document — read it before
coding:

- **`docs/analysis/ANALYSIS-WEBSITE.md`** — the approved design.
  Every section of this prompt derives from it. Where this prompt and
  the analysis document disagree, **the analysis document wins**.

Also read before making any decision:

- **`CLAUDE.md`** — mandatory rules covering language policy, branch
  naming, PR workflow, and the things to never do.
- **`docs/PACKAGING.md`** — the release pipeline and the stable
  download URL.
- **`docs/EVOLUTION.md`** — the project's prospective work backlog,
  which supplies the "What's coming" content for the About section.

---

## 1. Hard boundaries — do NOT cross these

The website is scoped strictly as a **static marketing site**
(`ANALYSIS-WEBSITE.md` §1.3). The following are out of scope:

- **No server-side runtime.** The site is HTML, CSS, and minimal JS
  only. No Node.js backend, no PHP, no Python.
- **No user accounts, no database, no API calls.** The donation flow
  opens the provider's hosted payment URL in the browser; the site
  makes no further network call.
- **No Google Analytics, no Facebook Pixel, no fingerprinting.**
  Analytics is Cloudflare Web Analytics exclusively — a single
  `<script>` tag, no cookies, no consent banner required.
- **No CMS, no admin UI.** Hugo is the build tool; content is
  Markdown files maintained in the repository.
- **No medicine database, no drug-interaction information.** The
  website must not give therapy advice. The "not a medical device"
  disclaimer is mandatory on every page.
- **No payment processing.** Donation URLs open the provider's hosted
  page; the website receives no payment data and confirms nothing.
- **No dark mode in v1.** Add CSS complexity only when explicitly
  requested.
- **No PWA.** The app is Windows-only desktop; a PWA would mislead
  mobile users.
- **No React, Vue, Angular, or bundler.** Total JS must stay under
  10 KB unminified. No jQuery, no Bootstrap JS, no Google Tag Manager.
- **No web fonts from an external CDN.** System font stack only.

---

## 2. Unresolved decisions — confirm before starting

The following items from `ANALYSIS-WEBSITE.md` §15 are open. Confirm
each with the product owner before coding begins:

1. **Screenshot set.** Confirm which specific UI states to capture
   and whether screenshots are retaken after every release or only on
   significant UI changes. Screenshots must be real, not mockups.

2. **Roadmap section.** Include a "What's coming" section on the About
   page sourced from `EVOLUTION.md`? Must be labelled as planned, not
   promised.

3. **Dark mode in v1.** Omit (recommended) or include.

Record each answer in `ANALYSIS-WEBSITE.md` §15.a before the first
commit.

---

## 3. Resolved decisions — not open for re-debate

These items in `ANALYSIS-WEBSITE.md` §15.a are decided and must not
be re-opened without the product owner's explicit sign-off:

- **Hosting.** Cloudflare Pages, with GitHub Pages as a documented
  fallback (`ANALYSIS-WEBSITE.md` §3.3). Resolved 2026-09-22.
- **Analytics.** Cloudflare Web Analytics — no cookies, no consent
  banner (`ANALYSIS-WEBSITE.md` §3.3, §8.4). Resolved 2026-09-22.
- **Deployment pipeline.** GitHub Actions + Wrangler (Option A,
  `ANALYSIS-WEBSITE.md` §3.4). Resolved 2026-09-22.
- **Repository placement.** Separate repository `vger70/medreminder-website` 
  for clean CI separation. Resolved 2026-09-23.
- **Custom domain.** Use the Cloudflare Pages default `*.pages.dev`. Resolved 2026-09-23.
- **Donation URLs.** The donate section requires real Stripe Payment
  Links and PayPal hosted button URLs are available in `donations.settings.json`. Resolved 2026-09-23.
- **Version badge.** Static string, manually updated per release. Resolved 2026-09-23.

---

## 4. Branch and PR

Per `CLAUDE.md` §4 (co-located case only):

- Branch name **MUST** be: **`feature/public-website`**, based on `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

If the work happens in a dedicated `vger70/medreminder-website`
repository, that repository sets its own conventions and the
`CHANGE_LOG.md` update does not apply.

---

## 5. Technical stack

(`ANALYSIS-WEBSITE.md` §3.2 and §3.3)

| Layer | Choice | Constraint |
|---|---|---|
| Static site generator | Hugo (single binary, first-class i18n, no Node.js) | Pin the Hugo version in the CI workflow |
| Styling | CSS custom properties + purposeful utility layer | No Bootstrap CDN, no Tailwind CDN; uncompressed CSS < 20 KB |
| JavaScript | Minimal, no framework | Language switcher toggle, donate amount selector, optional lazy-load handler; total JS < 10 KB unminified |
| Image format | WebP with `<picture>` + JPEG/PNG fallback | No external image CDN |
| Font | System font stack: `system-ui, -apple-system, 'Segoe UI', sans-serif` | No Google Fonts or any external font CDN |
| Hosting | Cloudflare Pages | `_redirects` file for server-side language routing |
| Language routing | Cloudflare Worker (`functions/_middleware.js`) for `Accept-Language` detection; `localStorage` stores the selected language | GitHub Pages fallback: JavaScript redirect in `static/index.html` |
| Analytics | Cloudflare Web Analytics (`<script>` tag in `baseof.html`) | No other analytics, no cookies |

---

## 6. Repository structure

(`ANALYSIS-WEBSITE.md` §4)

In the co-located case, the website lives under `website/` in this
repository with the following layout:

```
website/
  config/
    _default/
      config.toml       Hugo base config (title, baseURL, params)
      languages.toml    Five language definitions (en, it, fr, es, de)
      menus.toml        Navigation entries per language
  content/
    en/                 index.md, faq.md, about.md, privacy.md
    it/
    fr/
    es/
    de/
  layouts/
    _default/           baseof.html, single.html, list.html
    partials/           header.html, footer.html, nav.html, donate.html,
                        disclaimer.html
    index.html          Home page template
  static/
    img/                Screenshots (WebP + JPEG fallback), hero,
                        social preview (1200×630)
    favicon.ico
    apple-touch-icon.png
    _redirects          Cloudflare Pages redirects (/ → /en/)
  assets/
    css/                main.css (critical path) + rest.css
    js/                 language-switcher.js, donate.js
  functions/
    _middleware.js      Cloudflare Worker for Accept-Language detection
  i18n/
    en.yaml             UI string translations (button labels, generic headings)
    it.yaml
    fr.yaml
    es.yaml
    de.yaml
  README.md             Hugo version, how to run locally, screenshot refresh procedure
```

The GitHub Actions workflow lives at `.github/workflows/website.yml`
at the repository root (not inside `website/`), with a
`paths: [website/**]` filter so it only runs when website source
changes.

---

## 7. Implementation order

Follow `ANALYSIS-WEBSITE.md` §16. Each step must leave the Hugo build
(`hugo`) green before the next commit. Steps 1 and 2 must be complete
before any content is added.

### Step 1 — Scaffold

Hugo project structure: `config/`, `layouts/`, `static/`, `assets/`,
`i18n/`, `functions/`. Empty English content files. `static/_redirects`
with `/ /en/ 302`. Cloudflare Pages project created (via dashboard);
GitHub Actions workflow (`.github/workflows/website.yml`) with the
Wrangler deploy step. Verify the pipeline deploys an empty Hugo site
and the `_redirects` rule redirects correctly. All five `i18n/<lang>.yaml`
files created with placeholder entries.

### Step 2 — Design system

CSS custom properties using the palette from `ANALYSIS-WEBSITE.md`
§11.2 (primary `#2563EB`, background `#FFFFFF`, surface `#F8FAFC`,
text `#1E293B`, text-secondary `#475569`, success `#059669`,
warning `#D97706`, danger `#DC2626`). Base template (`baseof.html`)
with `<html lang="{{ .Language.Lang }}">`, header, footer, and nav
partials. Language switcher markup and JS toggle. Cloudflare Web
Analytics `<script>` tag in `baseof.html`. The "not a medical device"
disclaimer partial (`disclaimer.html`) included in the base template
so it appears on every page automatically. No content yet — the
shell only.

Typography scales per §11.3: body 16 px / 1.6 line-height, H1
2.5–3 rem, H2 1.75–2 rem, H3 1.25 rem.

Confirm WCAG 2.1 AA colour contrast (4.5:1 for normal text, 3:1 for
large text) before proceeding.

### Step 3 — English content: Hero, Features, How it works

Implement sections §6.1, §6.2, §6.3 of `ANALYSIS-WEBSITE.md` in
`content/en/index.md` and the home page template.

Key content:

- **Hero tagline:** "Never run out of your prescription — MedReminder
  tells you when to call the doctor."
- **Sub-tagline:** "Free and open-source Windows app. No account, no
  cloud, no subscription."
- **Primary CTA:** "Download for Windows" linking to
  `https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip`
- **Secondary CTA:** "View on GitHub" (opens in new tab).
- **Hero takes 100 vh on desktop**; CTA visible without scrolling on
  1366×768.
- **Feature grid:** 3-column desktop, 1-column mobile. Each card:
  icon, title (3–5 words), one-sentence description. Use icons from
  Heroicons, Feather, or Phosphor Icons (MIT license) — document the
  chosen set in `website/README.md`.
- **How it works:** 3–4 numbered steps with screenshot crops.

Acquire real screenshots for the step illustrations and add them to
`static/img/` (WebP + JPEG fallback).

### Step 4 — English content: remaining sections

Implement §6.4–§6.12 of `ANALYSIS-WEBSITE.md`:

- **Screenshots gallery** (`#screenshots`): 4–6 required screenshots
  (list at §6.4). Horizontal scroll on mobile, masonry or grid on
  desktop. CSS-only or minimal-JS lightbox.
- **System requirements** (`#requirements`): Windows 10 22H2 or 11,
  x64, no runtime needed, ~100 MB, optional network.
- **SmartScreen explanation** (`#smartscreen`): honest, reassuring,
  with numbered click-through guide and link to the GitHub Actions
  workflow file.
- **Download** (`#download`): large primary button, file info line,
  version label (Option A: static string from Hugo config), MSI
  alternative link, Apache 2.0 reminder.
- **Donate** (`#donate`): only if donation URLs are confirmed. If not
  ready, omit the section for v1. When present: provider tabs
  (Stripe / PayPal), amount selector (€2 / €5 / €10 / €20), honest
  post-click message, placement after Download.
- **Privacy** (`#privacy`): inline on home page and `/en/privacy/`.
  Covers website analytics (Cloudflare, no cookies), donation
  payments (external provider), app data (local only), contact via
  GitHub Issues.
- **FAQ** (`#faq` and `/en/faq/`): CSS-only accordion (`<details>`
  / `<summary>`). Ten-question set from §6.10.
- **About** (`#about` and `/en/about/`): origin, philosophy,
  technology, roadmap teaser (if confirmed), disclaimer, license,
  GitHub link.
- **Footer**: present on every page via the base template partial.
  App name + icon, nav links, language switcher, license line,
  "not a medical device" disclaimer, GitHub link, copyright.

### Step 5 — Localization: Italian

Translate all `content/en/*.md` files to `content/it/*.md`.
Translate `i18n/en.yaml` to `i18n/it.yaml`. Italian is the priority
second language; tone must match the target audience (adults managing
long-term medication). Maintainer review required before merging
Italian content.

### Step 6 — Localization: French, Spanish, German

Translate remaining three languages. For any untranslated section,
add a `TODO: translate` comment so the fallback is visible to the
maintainer. Ship English fallback for those sections.

### Step 7 — SEO and meta

Per `ANALYSIS-WEBSITE.md` §8:

- `<title>` (≤ 60 chars) and `<meta name="description">` (130–160
  chars) for each page and each language. Target the natural-language
  keyword patterns in §8.1 without keyword stuffing.
- `<meta property="og:image">` with the 1200×630 social preview image.
- `<link rel="canonical">` and `<link rel="alternate" hreflang="...">`.
- Hugo generates `sitemap.xml` and `robots.txt` automatically.

### Step 8 — Accessibility audit

Run Lighthouse accessibility audit (target: score ≥ 90). Fix:

- Colour contrast failures (must meet WCAG 2.1 AA).
- Missing or non-descriptive `alt` attributes on screenshots.
- Keyboard-navigation gaps (nav, language picker, FAQ accordion,
  donation selector, buttons).
- Visible focus rings on all interactive elements.
- Correct semantic HTML hierarchy (one `<h1>` per page).
- `lang` attribute matching each page's language.
- `prefers-reduced-motion` respected for any transitions.

### Step 9 — Performance audit

Run Lighthouse Performance on mobile, simulated 4G (target: score
≥ 90). Apply per §10.2:

- Critical-path CSS inlined in `<head>`; rest loaded asynchronously.
- `loading="lazy"` on all below-the-fold `<img>` tags.
- WebP images with `<picture>` fallback.
- System font stack — no external font requests.
- Hugo asset pipeline: CSS and JS minified and fingerprinted.
- Verify: total page weight (compressed) < 500 KB excluding
  screenshots, < 2 MB with full gallery.

### Step 10 — Documentation

- **`CHANGE_LOG.md`** (co-located case): prepend an entry when the PR
  opens. Follow the format at the top of that file.
- **`website/README.md`**: Hugo version pinned in CI, how to run
  locally (`hugo serve`), which icon set was chosen and its license,
  the screenshot refresh procedure (which states to capture, format
  requirements), and how to update the version string.

---

## 8. Content governance rules

Apply these rules from `ANALYSIS-WEBSITE.md` §12 throughout
implementation:

- **"Not a medical device" disclaimer** must appear verbatim on the
  home page, the footer, and the About page. It is included
  automatically via the `disclaimer.html` partial in `baseof.html`.
- **Donation URLs** must be verified after every change. Incorrect
  URLs open the wrong payment page.
- **Screenshots** must be real (not mockups) and taken from an
  installation with plausible but non-personal data (no real names, no
  real medicine names that could be mistaken for a recommendation).
- **SmartScreen section** must be removed or replaced if the app gains
  code signing. Use the Hugo boolean parameter `codeSigningEnabled` to
  control visibility.
- **Version string** (Option A): update in Hugo config on every
  release.

---

## 9. Conventions and constraints (from `CLAUDE.md`)

- All files in the repository (templates, config, JS, CSS, Markdown
  pages, commit messages) must be in **English**. The only exceptions
  are the translated content files under `content/<lang>/` (other
  than `en`) and `i18n/<lang>.yaml`, which carry user-facing text in
  their respective languages.
- Comments, configuration, template code, and `i18n/` keys are English.
- No secrets in the repository. Stripe/PayPal URLs are public Payment
  Link identifiers, not credentials — still, do not embed Cloudflare
  API tokens or any other credentials in repository files.
- Match the tone of existing Markdown: sober, factual, no marketing
  language, no emoji, concise in description but detailed in
  action-required sections.
- The icon set used must be MIT-licensed (or equivalent). Document the
  choice in `website/README.md`.

---

## 10. Risk mitigations to build in from the start

(`ANALYSIS-WEBSITE.md` §13)

| Risk | Built-in mitigation |
|---|---|
| Screenshots go stale | Document the refresh procedure in `website/README.md`; store source screenshots in `static/img/` |
| Donation URLs become stale | Add an HTTP HEAD check step in the website CI (`.github/workflows/website.yml`) |
| SmartScreen section remains after code signing | Hugo parameter `codeSigningEnabled` (default `false`); section renders only when `false` |
| Translation lag | `TODO: translate` comments; English fallback for untranslated sections |
| "Not a medical device" disclaimer omitted | Mandatory in `baseof.html` partial; appears on every page automatically |

---

## 11. Acceptance criteria

The PR is ready to merge when:

1. `hugo` builds without errors or warnings.
2. The Cloudflare Pages deploy pipeline (`.github/workflows/website.yml`)
   completes successfully on a push to `main`.
3. `/ ` redirects to `/en/` (or to the browser's language prefix when
   the Worker is active).
4. All five languages are reachable and display correct content.
5. Lighthouse Performance score ≥ 90 on mobile (simulated 4G).
6. Lighthouse Accessibility score ≥ 90.
7. The "not a medical device" disclaimer is visible on the home page,
   the footer, and the About page in all five languages.
8. The primary CTA "Download for Windows" resolves to the correct
   stable GitHub Releases URL.
9. The FAQ accordion is operable without JavaScript.
10. All screenshots have descriptive `alt` text.
11. All interactive elements are reachable and operable via keyboard.
12. The HTTP HEAD check for donation URLs passes (if the donate section
    is included in v1).
13. `website/README.md` documents Hugo version, `hugo serve` command,
    icon set, and screenshot refresh procedure.
14. `CHANGE_LOG.md` has a new entry for this PR (co-located case only).

---

*Generated 2026-09-23. Authoritative source:
`docs/analysis/ANALYSIS-WEBSITE.md`.*

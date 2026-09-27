# Implementation prompt — Website refresh for v2.6.0 (content and screenshots)

Target repository: **`vger70/medreminder-website`**. Nothing in this
prompt changes `vger70/MedReminder`.

## 0. What you are about to do

The first content refresh (`PROMPT-WEBSITE-CONTENT-REFRESH.md`) is
live on <https://medreminder26.pages.dev/>. Since then the app went
from v2.5.0 to v2.6.0 and gained four user-visible features the site
does not mention. The screenshot section still shows six text
placeholders. This prompt covers both: align the text with v2.6.0 and
replace the placeholders with real screenshots supplied by the
maintainer.

A comparison of the live English page against the app tree on
2026-09-26 found the items below. Your job is to fix the content and
publish the screenshots, not to redesign the site.

Read first:

- `CLAUDE.md` and `README.md` of the website repository (language
  policy, build, deploy, "Screenshot refresh procedure").
- `docs/analysis/ANALYSIS-WEBSITE.md` in `vger70/MedReminder` — the
  design authority, especially §6.2 (features), §6.4 (screenshots),
  §7.3 (localized assets), §9 (accessibility), §10 (performance) and
  §12 (non-goals).
- `CHANGE_LOG.md` entries for PRs #71, #73, #74 and #75 and
  `docs/USER_GUIDE.en.md` in `vger70/MedReminder` — what the app does
  today.
- The description of the previous content-refresh PR in the website
  repository, for its "Waiting for maintainer input" list.

## 1. Hard boundaries

- Content and config: `data/*_<lang>.yaml`, `i18n/<lang>.yaml`,
  `content/<lang>/*.md`, `config/_default/config.toml`, image files.
- One layout change is allowed: the screenshot section template must
  render real images (see §4.3). No other layout change, no new
  section types, no JS beyond an existing lightbox, if any.
- **All five languages in the same PR** (en, it, fr, es, de). English
  first, then translate. A language you cannot translate with
  confidence ships the English text with a `# TODO: translate`
  comment at the top of the file, per the website `CLAUDE.md` §2.
- No clinical claims. Keep the non-medical-device wording as is.
- No claim that you have not checked against the app tree or the
  user guide. If a fact cannot be verified, leave it out.
- Do not mention features that are not shipped in v2.6.0. In
  particular: large text mode, the printable medication card and
  automatic updates are proposals (`docs/prompt/`, `docs/AUTO_UPDATE.md`),
  not features.
- Do not mention the intake log (taken / skipped registration).
- Sober, factual tone; no emojis. No "adherence", no "never miss a
  dose" promise.

## 2. Out-of-date facts — must fix

1. **Static version fallback.** `config/_default/config.toml`
   `currentVersion` reads `v2.5.0`. The latest published release is
   `v2.6.0` and carries `MedReminder-win-x64.zip`,
   `MedReminder-win-x64-net10.zip` and `MedReminder-win-x64.msi`
   (checked on the GitHub Releases API on 2026-09-26). Set it to
   `v2.6.0`.
2. **Download sizes.** The requirements and download blocks say
   "about 55 MB" (framework-dependent ZIP, extracted) and "about
   170 MB" (self-contained ZIP). Measure the extracted size of both
   v2.6.0 ZIPs and round to the nearest 5 MB; if you cannot download
   them, ask the maintainer (§5, M3).
3. **Screenshots notice.** The sentence "Screenshots will be added on
   the first release of the site." goes away once the images ship. If
   the images do not arrive, keep it.

Do not change the ZIP naming in the requirements text: it is correct.
`MedReminder-win-x64.zip` is framework-dependent and
`MedReminder-win-x64-net10.zip` is self-contained
(`.github/workflows/dotnet-desktop.yml` in the app repository).

## 3. Shipped features missing from the site — add

The grid has 12 cards. Keep it at most 14 by merging where noted.
Check each item against the user guide section named in brackets.

- **Therapy timeline** (new card) [Therapy timeline]: one row per
  medicine over a movable period showing active periods, suspensions,
  dosage changes, taper stages, today and the estimated run-out date.
  Read-only. State that run-out dates are estimates for planning
  refills.
- **Prescription request for the doctor** (merge with "Therapy report
  for the doctor" into one card, e.g. "For your doctor") [Request a
  prescription from your doctor]: MedReminder prepares an editable
  request with the medicine name, package and product code, to copy,
  open in the mail program or send by the configured email account
  after confirmation. No dosage or clinical detail is included, and
  nothing is sent without the user's action.
- **Count stock** (merge into "Stock tracking") [Count stock]: enter
  the counted quantity; the app shows the difference from the
  expected stock and the run-out date before and after, then records
  one correction.
- **Barcode scanning** (merge into "Medicine catalogue") [Scan the
  package barcode]: with the reference catalogue on, a USB barcode
  scanner (or the code typed by hand) fills in the medicine from the
  catalogue. Italian AIC codes (Code 32) are the reliable case; do
  not promise webcam scanning or 2D DataMatrix resolution.

Privacy section, "App data" paragraph: the email point must also say
that a prescription request is sent to the doctor address only when
the user presses **Send…** and confirms. The doctor address is stored
locally in the profile.

FAQ: add one entry, "Does MedReminder contact my doctor?" — No; it
prepares a request that you copy or send yourself.

"How it works", step 1: optionally add that the medicine can be
filled in by scanning the package barcode. Keep the step short.

## 4. Screenshots

### 4.1 Set

Nine shots, in this order. The slug is the file name without
extension and is identical in every language.

| # | Slug | What it shows |
|---|------|---------------|
| 1 | `main-list` | Main window: 5–6 medicines with mixed status (OK, warning, run-out soon), toolbar visible. |
| 2 | `edit-medicine` | Edit medicine form with a non-simple schedule (weekly or stepped taper) and the **Scan barcode…** button visible. |
| 3 | `therapy-timeline` | Therapy timeline with at least one suspension, one dosage change and the run-out marker. |
| 4 | `dose-reminder` | Windows toast "Time to take …" at the bottom right of the screen. |
| 5 | `prescription-request` | Request prescription dialog with the drafted message. |
| 6 | `stock-count` | Count stock dialog with a non-zero difference and both run-out dates. |
| 7 | `backup-settings` | Settings → Backup tab with the cloud-folder backup configured. |
| 8 | `export-dialog` | Export all data (encrypted) dialog. |
| 9 | `profile-picker` | Profile selection at startup with 2–3 profiles. |

Shots 1, 2, 5, 6, 8 and 9 replace or extend the six in
`ANALYSIS-WEBSITE.md` §6.4; 3, 5 and 6 are new. Update §6.4's list
through a separate PR in `vger70/MedReminder` only if the maintainer
asks; do not touch that repository in this PR.

### 4.2 Source files supplied by the maintainer

The app is WinForms and cannot run in a Linux container, so the
maintainer captures the shots on Windows and commits the originals
to the website repository on the working branch:

```
screenshots-src/<lang>/<slug>.png
```

`screenshots-src/` is at the repository root, outside `static/`,
`content/` and `assets/`, so Hugo never publishes it. English is
required; other languages are optional and fall back to English. The
toast (`dose-reminder`) follows the Windows display language, not the
app language, so it may exist in English only.

If `screenshots-src/` is missing or incomplete when you start, do the
text work in §2–§3, keep the placeholders for the missing shots, and
list what is missing in the PR.

### 4.3 Processing and publishing

- Check each PNG before converting: no real names, email addresses,
  file paths with a Windows user name, or other personal data. If you
  find any, do not publish that shot; list it in the PR.
- Convert each PNG to `static/img/<lang>/<slug>.webp` (quality about
  80) and `static/img/<lang>/<slug>.jpg` (quality about 85), longest
  side at most 1600 px, no upscaling. Keep the PNG originals
  untouched.
- If the website `README.md` names a different target folder
  (`ANALYSIS-WEBSITE.md` §7.3 mentions `content/<lang>/img/`), follow
  the README and state the choice in the PR; update the README
  procedure to the final layout and to the nine slugs.
- Template: render `<picture>` with the WebP source and the JPEG
  fallback, `alt` from `data/screenshots_<lang>.yaml`, explicit
  `width`/`height`, `loading="lazy"`. Missing localized file → English
  file. Missing English file → keep the text placeholder for that
  tile.
- `data/screenshots_<lang>.yaml`: nine entries with slug and alt
  text; the alt text describes what the image shows, in the page
  language.

## 5. Inputs to request from the maintainer

**Before the first commit**, ask in a single message, numbered as
here, stating the default for each. Record the answers in the PR
description.

- **M1 — Screenshots.** Which languages are in `screenshots-src/`,
  and whether any shot is still to come. Default: publish what is
  there, English fallback for the rest.
- **M2 — Carried-over items.** Re-ask only the items still listed as
  "Waiting for maintainer input" in the previous refresh PR (for
  example logo and social preview, contact channel, custom domain).
  Default: the default recorded in that PR.
- **M3 — Download sizes.** The extracted sizes of the two v2.6.0 ZIPs,
  if you cannot measure them. Default: keep the current figures and
  flag them.
- **M4 — Native review.** Whether a native speaker reviews the
  it / fr / es / de text before merge. Default: ship the translations
  and list the changed strings per language in the PR.

## 6. Branch, PR, checks

- Branch `claude/<short-name>` or `feature/<short-name>` in the
  website repository; one PR.
- `hugo --minify --gc` builds without warnings.
- Every language page renders the new cards, the FAQ entry and the
  nine screenshot tiles (image or placeholder); the version badge
  fallback reads `v2.6.0`.
- Lighthouse mobile Performance stays at 90 or above with the images
  in place (`ANALYSIS-WEBSITE.md` §10); report the score and the total
  WebP weight per language.
- PR description lists every §2 item, every §3 feature and every
  §4.1 shot per language with "done", "needs maintainer input" or
  "skipped, because …", plus the answers to M1–M4.

## 7. Acceptance criteria

- No sentence on the site contradicts v2.6.0 or the user guide.
- The four features in §3 appear on the site, merged as described;
  no unshipped feature and no intake log appear.
- The screenshot section shows real images for every shot the
  maintainer supplied, with localized alt text and English fallback.
- No published image contains personal data.
- All five languages updated or marked `# TODO: translate`.

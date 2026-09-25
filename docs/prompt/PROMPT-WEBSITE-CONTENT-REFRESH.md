# Implementation prompt — Website content refresh (align v1 with the app)

Target repository: **`vger70/medreminder-website`**. Nothing in this
prompt changes `vger70/MedReminder`.

## 0. What you are about to do

Website v1 (`EVOLUTION-DONE.md` §10) went live on 2026-09-25. A
comparison of its content against the application tree on the same
day found claims that are wrong, claims that are out of date, and
shipped features the site does not mention. This prompt lists them.
Your job is to fix the content, not to redesign the site.

Read first:

- `CLAUDE.md` of the website repository (language policy, build,
  deploy).
- `docs/analysis/ANALYSIS-WEBSITE.md` in `vger70/MedReminder` — the
  design authority, especially §6 (sections) and §12 (non-goals).
- `docs/EVOLUTION-DONE.md` and `docs/USER_GUIDE.en.md` in
  `vger70/MedReminder` — what the app does today.

## 1. Hard boundaries

- Content and config only: `data/*_<lang>.yaml`, `i18n/<lang>.yaml`,
  `content/<lang>/*.md`, `config/_default/config.toml`. No layout
  redesign, no new JS, no new section types.
- **All five languages in the same PR** (en, it, fr, es, de). English
  first, then translate. A language you cannot translate with
  confidence ships the English text with a `# TODO: translate`
  comment at the top of the file, per the website `CLAUDE.md` §2.
- No clinical claims. Keep the non-medical-device wording as is.
- No claim that you have not checked against the app tree or the
  user guide. If a fact cannot be verified, leave it out.
- Sober, factual tone; no emojis.

## 2. Wrong claims — must fix

Each item cites where the site says it and what the app does.

1. **"encrypted SQLite database"** — `data/faq_<lang>.yaml`, "Where
   is my data stored?". The database is a plain SQLite file under
   `%LOCALAPPDATA%\MedReminder\profiles\<id>\medreminder.db`; there is
   no database encryption (no SQLCipher, no key pragma). Say: stored
   only on your PC, in a local database in your Windows user folder.
2. **"Backups are encrypted with Windows DPAPI"** —
   `data/about_<lang>.yaml`, "Technology". Wrong on both counts.
   Correct statement:
   - the automatic backup to a local folder is a plain copy of the
     database (not encrypted);
   - export archives and cloud-folder snapshots (`.mrz`) are encrypted
     with a user passphrase (Argon2id + AES-GCM), deliberately not
     DPAPI so they can be restored on another PC;
   - DPAPI protects only the stored SMTP password and the cached
     cloud-backup passphrase.
3. **"Daily encrypted backup to any folder, including cloud-synced
   ones"** — `data/features_<lang>.yaml`, "Automatic backup".
   Two targets exist: a plain database copy to a local folder, and an
   encrypted snapshot to a folder the user's own sync app (OneDrive,
   Google Drive, Dropbox, iCloud Drive) uploads. Only the second is
   encrypted. State that it is a backup, not real-time sync.
4. **Download fallback MSI URL** — `config/_default/config.toml`,
   `releaseMsiUrl` points to `MedReminder-Setup-win-x64.msi`. The
   release workflow (`.github/workflows/dotnet-desktop.yml` in the app
   repo) publishes `MedReminder-win-x64.msi`. The static link 404s
   whenever `assets/js/version.js` cannot reach the GitHub API. Change
   it to `.../releases/latest/download/MedReminder-win-x64.msi`.
5. **"How do I move to a new PC?"** — `data/faq_<lang>.yaml` and the
   "Restore on a new PC" step in `data/steps_<lang>.yaml`. The
   recommended path is now **Settings → Backup → Export all data** on
   the old PC and **Import from export** on the new one (encrypted
   `.mrz`, passphrase-protected, works across Windows accounts). The
   cloud-folder backup with **Restore from cloud folder…** is the
   second option. A raw `.db` backup is not the recommended migration
   path.
5b. **Static version fallback** — `config/_default/config.toml`
   `currentVersion = "v1.2.0"`. The app is at 2.4.1
   (`Directory.Build.props` `VersionPrefix` in the app repo). The badge
   shows the stale value whenever the GitHub API call fails. Set it to
   the latest published release tag; confirm the tag with the
   maintainer (see §5, item M6).

## 3. Incomplete or misleading claims — should fix

6. **Network use.** `data/features_<lang>.yaml` ("no internet
   connection required"), `data/privacy_<lang>.yaml` ("App data …
   Nothing is uploaded") and the hero sub-tagline ("No account, no
   cloud"). Facts:
   - the app checks GitHub for a new release at startup, on by
     default, and can be turned off in Settings (a request to
     `api.github.com`, nothing about the user is sent);
   - the cloud-folder backup writes a local file; any upload is done
     by the user's own sync app, never by MedReminder;
   - email is sent only if the user configures SMTP.
   Keep "no account" and "your data stays on your PC", but make the
   privacy paragraph state these three points.
7. **Website privacy section.** The site itself calls the GitHub
   Releases API from the visitor's browser (`assets/js/version.js`).
   Add one sentence saying the page asks GitHub for the latest version
   number, so the visitor's browser contacts GitHub. The analytics
   paragraph describes Cloudflare Web Analytics, but
   `cloudflareAnalyticsToken` is empty, so the beacon is not rendered
   today; the paragraph must match whatever the maintainer decides in
   §5, item M5.
8. **"Email alerts"** — `data/features_<lang>.yaml`. Emails cover
   low stock and, if enabled per medicine, dose-time reminders. The
   optional caregiver address receives every email the user receives,
   including dose-time reminders, not only low-stock ones.
9. **Requirements** — `data/requirements_<lang>.yaml`. "About
    100 MB" is not verified; measure the extracted size of the
    current `MedReminder-win-x64.zip` and round it, or remove the
    line. Optionally mention the smaller framework-dependent ZIP
    (`MedReminder-win-x64-net10.zip`, requires the .NET 10 Desktop
    Runtime).

## 4. Shipped features missing from the site — add

Add to `data/features_<lang>.yaml`. The grid has 10 cards; keep it
readable (about 12–14) by merging into existing cards where noted
rather than adding one card per item. Every item below is required.
Check each against the user guide section named in brackets.

- **Flexible schedules** (new card) [Complex regimens]: weekly
  patterns, on/off cycles, tapering (linear or in steps) and
  as-needed medicines; the run-out estimate follows the schedule.
- **Encrypted export and import** (new card) [Export and import]:
  one passphrase-protected file with a profile's data, for moving to
  a new PC or keeping a personal copy; works across Windows accounts;
  format documented publicly.
- **Backup to a cloud-synced folder and restore on another PC**
  (replaces the "Automatic backup" card, see item 3) [Database
  backup, Cloud folder backup]: daily local backup of every profile,
  plus an optional encrypted snapshot into a folder synchronized by
  the user's own cloud app, and **Restore from cloud folder** on a
  second PC. State that it is not real-time sync.
- **Therapy report for the doctor** (new card): printable summary of
  the current medicines, doses and times.
- **Caregiver email** (merge into "Email alerts") [Caregiver
  notifications]: an optional second address per profile receives
  the same emails.
- **Profiles with roles** (extend "Multiple profiles") [Multiple
  profiles and admin/user roles]: administrator and user roles; the
  PIN is optional.
- **Therapy pauses** (merge into "Stock tracking" or "Prescription
  reminder"): a medicine can be suspended for a period; the stock
  projection accounts for it.
- **Runs quietly in the background** (new card or merge into
  "Prescription reminder") [Notification area icon, Windows automatic
  startup]: notification-area icon, optional start with Windows,
  "Check now".
- **Built-in user guide** (merge into "Five languages"): the guide
  opens with F1, in the same five languages as the interface.
- **Official leaflet links** (merge into "Medicine catalogue")
  [Reference catalogue]: for Italian medicines, links to the official
  leaflet and summary of product characteristics.
- **Update notice** (merge into "Free and open-source"): the app can
  tell you when a new version is on GitHub; nothing is downloaded or
  installed automatically. Must match the privacy wording of item 6.

Do not add the intake log (taken / skipped registration): it is
deliberately left off the site.

Wording must stay organizational: no "adherence", no "never miss a
dose" promise. The current dose-time card says "so you never miss a
tablet"; soften to "a reminder at each scheduled dose time".

Update the screenshot alt texts in `data/screenshots_<lang>.yaml`
only if a new card makes an existing alt text misleading.

## 5. Inputs to request from the maintainer

Some changes cannot be made from the repositories alone. **Before
the first commit**, ask the maintainer for the items below in a
single message, grouped and numbered as here, and state for each one
what you will do if no answer comes (the "Default" line). Do not
invent any of these values. Record the answers in the PR
description.

**Content and assets**

- **M1 — Screenshots.** The six tiles are text placeholders
  (`layouts/index.html` renders only `.alt`). Ask for the six shots
  listed in `ANALYSIS-WEBSITE.md` §6.4 and in the website `README.md`
  ("Screenshot refresh procedure"): main list, edit medicine,
  settings / backup, dose-time toast, export / import dialog, profile
  picker. Ask whether one set per language or English only for now,
  and remind the maintainer of the rules: plausible non-personal
  data, WebP plus JPEG fallback, files under `static/img/<lang>/`.
  The app is WinForms and cannot be run in a Linux container, so the
  shots must come from the maintainer's Windows machine.
  Default: keep the placeholders; do not ship stock or generated
  images.
- **M2 — Logo, favicon and social preview.** The favicon set and
  `static/img/social-preview.png` were added as placeholders
  (website PR #4). Ask for the final app icon (the app ships
  `assets/medreminder.ico` in the app repository; ask whether to
  derive the web icons from it) and a 1200×630 social preview.
  Default: keep the placeholders.
- **M3 — About → Origin.** The text says the app "was written by a
  family caregiver". This is a claim about the author that cannot be
  verified from the repositories. Ask the maintainer to confirm or
  rewrite it. Default: remove the sentence about the author and keep
  the part about why local-first matters.

**Contact and legal**

- **M4 — Contact channel.** The site names GitHub Issues as the only
  channel, including for privacy questions. Ask whether a contact
  email address should appear in the Privacy section and in the
  footer, and which address. A website that runs analytics
  generally needs to name who operates it and how to reach them
  `[INFERRED — GDPR art. 13; obtain qualified advice]`; ask also
  whether an operator name (and, for German-speaking visitors, an
  Impressum) is wanted. Default: keep GitHub Issues only and flag
  the open question in the PR.
- **M5 — Analytics.** `cloudflareAnalyticsToken` is empty. Ask for
  the Cloudflare Web Analytics site token, or confirmation that
  analytics stay off. Default: leave the token empty and rewrite the
  privacy paragraph to say the site does not currently count visits.

**Release and domain**

- **M6 — Current release.** Ask for the latest published release tag
  and confirm that the release carries `MedReminder-win-x64.zip`,
  `MedReminder-win-x64-net10.zip` and `MedReminder-win-x64.msi`, so
  item 4 and item 5b point to real files. Default: use the tag of
  the latest GitHub release if you can read it; otherwise leave
  `currentVersion` unchanged and flag it.
- **M7 — Download size.** For item 9, ask for the extracted size of
  the current self-contained ZIP, or permission to remove the size
  line. Default: remove the line.
- **M8 — Custom domain.** The site runs on
  `https://medreminder26.pages.dev/`. Ask whether a custom domain is
  planned; if yes, `baseURL`, `robots.txt` and the social preview
  URLs change with it. Default: no change.

**Translations**

- **M9 — Native review.** Ask whether a native speaker will review
  the it / fr / es / de text before merge. Default: ship the
  translations and list the changed strings per language in the PR
  so a reviewer can check them.

Items whose answer is missing when the PR is ready go into a
"Waiting for maintainer input" list in the PR description, with the
default that was applied.

## 6. Screenshots

Covered by M1. If the maintainer supplies the images, follow the
refresh procedure in the website `README.md` and
`ANALYSIS-WEBSITE.md` §7.3, and update `data/screenshots_<lang>.yaml`
alt texts to match.

## 7. Branch, PR, checks

- Branch `claude/<short-name>` or `feature/<short-name>` in the
  website repository; one PR.
- `hugo --minify --gc` must build without warnings.
- Check every language page renders the new cards and FAQ answers,
  and that the MSI link resolves.
- PR description lists items 1–9 (including 5b) and every §4
  feature with "done", "needs maintainer input" or "skipped,
  because …", plus the answers to M1–M9 or the default applied.

## 8. Acceptance criteria

- No sentence on the site contradicts §2 above.
- Every §4 feature appears on the site, as its own card or merged
  into an existing one; the intake log does not.
- All five languages updated or marked `# TODO: translate`.
- Privacy section states the app's startup update check and the
  site's GitHub API call, and its analytics paragraph matches the
  M5 decision.
- The M1–M9 questions were sent to the maintainer before the first
  commit.

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
   Releases API from the visitor's browser (`assets/js/version.js`)
   and loads the Cloudflare Web Analytics beacon. Add one sentence
   saying the page asks GitHub for the latest version number, so the
   visitor's browser contacts GitHub.
8. **"Email alerts"** — `data/features_<lang>.yaml`. Emails cover
   low stock and, if enabled per medicine, dose-time reminders. The
   optional caregiver address receives every email the user receives,
   not only low-stock ones.
9. **Donate "custom" link, Stripe** — `config/_default/config.toml`
   `[params.donate.stripe]`: `custom` has the same URL as `"20"`.
   Confirm with the maintainer whether a separate "choose your
   amount" Payment Link exists. Do not invent a URL; if none exists,
   ask whether to hide the Custom button for Stripe.
10. **Requirements** — `data/requirements_<lang>.yaml`. "About
    100 MB" is not verified; measure the extracted size of the
    current `MedReminder-win-x64.zip` and round it, or remove the
    line. Optionally mention the smaller framework-dependent ZIP
    (`MedReminder-win-x64-net10.zip`, requires the .NET 10 Desktop
    Runtime).

## 4. Shipped features missing from the site — add

Add to `data/features_<lang>.yaml`. The grid has 10 cards; keep it
readable (about 12 at most) by merging where natural rather than
adding one card per item.

- **Flexible schedules**: weekly patterns, on/off cycles, tapering
  (linear or in steps) and as-needed medicines; the run-out estimate
  follows the schedule.
- **Encrypted export and import**: one passphrase-protected file with
  all of a profile's data, for moving to a new PC or keeping a
  personal copy; format documented publicly.
- **Backup to a cloud-synced folder + restore on another PC**
  (replaces the current "Automatic backup" card, see item 3).
- **Caregiver email** (can be merged into "Email alerts").
- **Therapy card for the doctor**: printable summary of current
  medicines and doses.
- **Intake log**: mark a dose as taken or skipped.
- Optional, lower priority: pause a therapy (suspensions), start with
  Windows / tray icon, built-in user guide (F1), links to the official
  leaflet for Italian medicines.

Wording must stay organizational: no "adherence", no "never miss a
dose" promise. The current dose-time card says "so you never miss a
tablet"; soften to "a reminder at each scheduled dose time".

## 5. Screenshots

The six screenshot tiles are text placeholders
(`layouts/index.html` renders `.alt` only). Out of scope for this
prompt unless the maintainer supplies the images; if supplied, follow
the refresh procedure in the website `README.md` and
`ANALYSIS-WEBSITE.md` §7.3.

## 6. Branch, PR, checks

- Branch `claude/<short-name>` or `feature/<short-name>` in the
  website repository; one PR.
- `hugo --minify --gc` must build without warnings.
- Check every language page renders the new cards and FAQ answers,
  and that the MSI link resolves.
- PR description lists items 1–10 above with "fixed", "needs
  maintainer input" (item 9, possibly 10) or "skipped, because …".

## 7. Acceptance criteria

- No sentence on the site contradicts §2 above.
- Features grid covers §4 at least for flexible schedules, export /
  import, cloud-folder backup and restore, and the therapy card.
- All five languages updated or marked `# TODO: translate`.
- Privacy section states the app's startup update check and the
  site's GitHub API call.

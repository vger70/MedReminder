# Change Log

All notable changes to MedReminder are recorded here, grouped by pull
request. Each PR gets a single entry, added when the PR is opened and
updated only if the PR's scope changes materially before it merges.

## How this file is maintained

- Every time a new pull request is created for this repository, an
  entry is prepended below in reverse-chronological order (newest
  first).
- The entry title is `## PR #<number> — <one-line summary>` and links
  back to the PR on GitHub.
- The body lists the observable changes as terse bullet points,
  focused on **what changed** and **why**, not on implementation
  detail. Reference the affected paths when it helps a future reader
  locate the change.
- If a PR is later closed without merging, mark the entry as
  `**Status:** closed (not merged)` — do not delete it.
- Once a PR merges, mark it as `**Status:** merged (<merge-date>)`
  under the title.
- Do not squash entries across releases: this log tracks pull
  requests, not versions. Release-level history belongs in the GitHub
  Releases page.

Format loosely inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
with the classification adapted to per-PR granularity: **Added**,
**Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**,
**Docs**, **Build**.

---

## PR #13 — Add drug reference catalogue design analysis

Link: [vger70/MedReminder#13](https://github.com/vger70/MedReminder/pull/13)
**Status:** open
Branch: `claude/database-principi-attivi-gl3rnw`

### Docs

- `docs/ANALYSIS-DRUG-CATALOGUE.md` (new): engineering plan for
  the future reference catalogue of medicinal products (commercial
  name ↔ active ingredient ↔ ATC), country-aware from the start.
  Covers scope, Clean-Architecture impact on the four projects,
  Domain / Application / Infrastructure additions, SQLite schema
  sketch, snapshot layout and attribution obligations for AIFA
  (CC BY 4.0) and EMA Article 57, milestones M0–M5, testing
  strategy, localisation notes for the five shipped languages
  (`de`, `en`, `es`, `fr`, `it`), risks, effort estimate and the
  seven §12 decisions with their current status.
- Follow-up to the discovery notes in
  [#9](https://github.com/vger70/MedReminder/issues/9).
- No source code changes; no runtime behaviour changes.

## PR #8 — Bump WebView2 to 1.0.4191.47; drop unused WPF reference

Link: [vger70/MedReminder#8](https://github.com/vger70/MedReminder/pull/8)
**Status:** open
Branch: `webview2-strip-wpf-ref`

### Changed

- `Microsoft.Web.WebView2` bumped from `1.0.2792.45` to
  `1.0.4191.47`.

### Build

- New MSBuild target `RemoveUnusedWebView2Wpf` in
  `src/MedReminder.UI/MedReminder.UI.csproj`, running
  `AfterTargets="ResolveAssemblyReferences"`, removes the unused
  `Microsoft.Web.WebView2.Wpf` reference and its copy-local entry
  from the WinForms-only host. This suppresses the `MSB3277`
  warning introduced by the new package version (its WPF assembly
  requires `WindowsBase 5.0.0.0`, unified against .NET 10's
  `WindowsBase 4.0.0.0`) and removes the dead ~50 KB DLL from the
  published output. `HelpViewerForm` and the native
  `WebView2Loader.dll` under `runtimes/` are unaffected.

### Fixed

- `MSB3277` warning about conflicting `WindowsBase` versions that
  appeared on every build after the WebView2 bump.
## PR #7 — Document SmartScreen warning; add French and Spanish user guides

Link: [vger70/MedReminder#7](https://github.com/vger70/MedReminder/pull/7)
**Status:** open
Branch: `smartscreen-advice`
(previously `claude/jolly-mccarthy-xk8lc4`; renamed after first push)

### Added

- `docs/USER_GUIDE.fr.md` and `docs/USER_GUIDE.es.md`: native user
  guides for the French and Spanish UI locales, matching the
  content of `USER_GUIDE.en.md`. `HelpViewerForm` already resolves
  `USER_GUIDE.<lang>.md` dynamically — only the csproj wiring
  changed (`Content` + `EmbeddedResource`).

### Docs

- `README.md`: new "Windows SmartScreen warning on first run"
  section after the Download block, explaining the SmartScreen
  dialog and UAC "Unknown Publisher" prompt triggered by the
  intentionally unsigned release, with the two-click bypass steps.
- `docs/USER_GUIDE.en.md`, `docs/USER_GUIDE.it.md`,
  `docs/USER_GUIDE.fr.md`, `docs/USER_GUIDE.es.md`: matching
  "SmartScreen on first launch" subsection under *First start* /
  *Primo avvio* / *Premier démarrage* / *Primer inicio*, using the
  localized Windows dialog labels for each language.
- `README.md`: drop the obsolete "guide localized in EN and IT
  only" known limitation now that all four shipped languages have
  a native guide.
- `CLAUDE.md`: extend the English-only exceptions and the
  repository layout section to list all four shipped user guides.

### Build

- `src/MedReminder.UI/MedReminder.UI.csproj`: register the two new
  guides as `Content` (copied to `bin/localization/`) and
  `EmbeddedResource` (single-file publish fallback). Comment on
  the block updated to reflect the four supported languages.

---

## PR #5 — Add CLAUDE.md, English-only docs, strip .pdb/.xml in Release

Link: [vger70/MedReminder#5](https://github.com/vger70/MedReminder/pull/5)
**Status:** open
Branch: `claude/translate-in-english`
(previously `claude/compassionate-pasteur-qmmt3h`; renamed after
opening)

### Added

- `CLAUDE.md` at the repository root, with repository conventions,
  the English-only language policy (exception: `USER_GUIDE.it.md`
  and the JSON translation dictionaries under
  `assets/localization/`), build / test / publish commands, branch
  policy, data locations and pointers to CHANGE_LOG.md maintenance.
- `CHANGE_LOG.md` (this file) with per-PR entries and maintenance
  rules.

### Changed

- `docs/ANALYSIS.md`, `docs/ANALYSIS-MULTI-USER.md` and
  `packaging/msix/Assets/README.md` translated from Italian to
  English. `USER_GUIDE.en.md` and `USER_GUIDE.it.md` intentionally
  left as-is — they are shipped to the end user in each locale.
- Every Italian comment, log message, exception message and
  hardcoded fallback string in the C# source (Domain, Application,
  Infrastructure, UI and test projects) translated to English.
  Identifiers and public APIs are untouched; hardcoded fallback
  strings in `NotificationTexts` and `TherapyReport` now default to
  English (matching the app's default locale) — used only when no
  `ILocalizationService` is registered.
- Italian comments in the ancillary config files translated to
  English: `.csproj`, `.pubxml` publish profiles, WiX `Product.wxs`
  and `MedReminder.wixproj`, MSIX `Package.appxmanifest`,
  `MedReminder.mapping.txt` and `priconfig.xml`, packaging scripts
  (`build-installer.ps1`, `make-selfsigned-cert.ps1`,
  `sign-artifact.ps1`), the GitHub Actions workflow, WiX
  `License.rtf` and `assets/build/generate_icon.py`.

### Build

- New MSBuild target `StripReleaseDebugArtifacts` in
  `Directory.Build.props` that runs after `Build` and `Publish` when
  `$(Configuration) == Release`, deleting `*.pdb` and `*.xml` from
  `$(OutputPath)` and `$(PublishDir)`. Test projects are excluded.
  This keeps the shipped ZIP / MSI / MSIX free of debug symbols and
  documentation dumps without changing the CI workflow (which already
  builds `-c Release`).
- The GitHub Actions release workflow
  (`.github/workflows/dotnet-desktop.yml`) now also builds the WiX
  MSI installer from the same self-contained publish output and
  attaches `MedReminder-win-x64.msi` alongside the existing ZIP to
  every GitHub Release. The MSI version tracks the pushed `vX.Y.Z`
  tag. Italian strings introduced by the MSI step
  (`throw` messages) are translated to English inline with the
  rest of the workflow.

### Docs

- `README.md` license section updated from "MIT" to "Apache 2.0"
  (matching the `LICENSE` file).

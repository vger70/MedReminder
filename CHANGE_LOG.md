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

## PR #25 — Increment 15d: ProfilesManagerForm, admin/user gating, restore-into-profile

Link: [vger70/MedReminder#25](https://github.com/vger70/MedReminder/pull/25)
**Status:** open
Branch: `claude/incremento-15d`

Fourth sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §7.4, §11.3, §12, §15d). Adds the
admin-only profile-management UI, gates the SettingsDialog by role,
surfaces the active profile in the main window and lets the admin
restore any profile from the Backup tab. PIN prompt polish and the
user-guide entries remain for 15e.

### Added

- `MedReminder.UI.Forms.ProfilesManagerForm` — admin-only CRUD for
  profiles (§12.4). Columns: name, role, PIN status, last-used
  (`dd/MM HH:mm`), active-indicator. Inline dialogs for New / Rename
  / Change PIN and a **type-name-to-confirm** delete dialog with an
  "also delete data on disk" checkbox that defaults to OFF (§13).
  Delete button is disabled for the currently active profile
  (§14a H) and for the last remaining admin (§2.2). Defense-in-depth
  guards inside the form back up the button-disable logic in case
  the form is opened by a non-admin caller.
- `File → Change profile…` menu entry (everyone): opens the
  `ProfilePickerForm`, sets the hint on confirm and restarts through
  `IApplicationRestarter` (§6.1).
- `Tools → Manage profiles…` menu entry (admin only, hidden for
  non-admins — §12.2).
- New **Notifications** tab in `SettingsDialog` (§7.4). Visible to
  every profile; contains only the per-profile `ToAddress`. Persists
  to `<DataDirectory>\notifications.settings.json`.
- **Restore-into-profile** dropdown in the Backup tab (§11.3).
  Extracts the `profileId` from the backup filename
  (`medreminder-<profileId>-YYYYMMDD-HHmmss.db`) as the default
  selection, falling back to the active profile when the filename
  does not follow the convention. `RestartAndExit` is called only
  when the target profile is the active one — restoring into an
  inactive profile does not touch the live `DbContext` connection.

### Changed

- `SettingsDialog` — Email and Backup tabs are hidden for non-admin
  profiles. The Email tab no longer contains the recipient field;
  it lives in the new Notifications tab (visible to everyone). The
  Save button on the Email tab writes only SMTP; the Notifications
  tab has its own Save button that writes only the per-profile
  file. `IProfileRegistry` was added to the constructor so the
  Backup dropdown can enumerate profiles.
- `MainForm` — title bar shows the profile name
  (`MedReminder — Grandma`, §12.1). StatusStrip carries a
  `Profile: <name>` label on the left, bold + dark-blue with the
  `(admin)` suffix when the current profile is an admin
  (distinctive badge, §12.1). The constructor now injects
  `ICurrentProfile`, `IProfileRegistry` and `IApplicationRestarter`.

### Localisation

- **60 new keys** added to every dictionary
  (`assets/localization/strings.{en,it,fr,es,de}.json`) — menus,
  StatusStrip, ProfilesManagerForm dialogs, Notifications tab,
  restore-into-profile chooser. All 5 dictionaries stay at
  **parity at 438 keys each** — `DictionaryParityTests` remain
  green.

### Invariants (unchanged, enforced twice)

- **At least one admin** — Delete refuses in `ProfileRegistry` and
  the Delete button is disabled for the last admin.
- **Active profile not deletable** — Delete button is disabled;
  the form also shows an explanatory warning if a script triggers
  the click (§14a H).
- **Immutable role** — no promote/demote path exists in the UI or
  in the registry API (§14a G). Explicit hint label at the bottom
  of the form.
- **Restore into inactive profile skips restart** — only the
  active-profile restore triggers `RestartAndExit` (§11.2).

### Out of scope (still)

- PIN prompt polish, tooltip wording pass, user-guide entries —
  15e.
- Promote/demote flow, cross-profile consolidated view — non-goals
  for Increment 15 (§16).

---

## PR #24 — Increment 15c: multi-profile boot flow and per-profile services

Link: [vger70/MedReminder#24](https://github.com/vger70/MedReminder/pull/24)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15c` (stacked on `claude/incremento-15b` from PR #23)

Third and largest sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §4, §7, §11, §15c). Wires the
15a / 15b groundwork into the boot flow. After this PR the app
opens with a picker when more than one profile exists, runs the
first-run wizard on a clean install, gates the DB and per-profile
recipient behind `ICurrentProfile`, backs up every profile on each
successful automatic-backup tick, and enforces the profile PIN
when one is set. Admin/user UI gating remains for 15d; PIN prompt
polish and user-guide entries remain for 15e.

### Added

- `MedReminder.UI.Forms.FirstRunWizardForm` — mandatory wizard
  shown when the registry is empty. Collects the admin name and
  an optional PIN, then creates the profile through
  `IProfileRegistry.Create` (which forces `Role = Admin` on an
  empty registry). Cannot be dismissed with the window `X`;
  Exit closes the app (§12.3).
- `MedReminder.UI.Forms.ProfilePickerForm` — boot picker shown
  when more than one profile exists. `ListView` with name / role
  badge / `dd/MM HH:mm` last-used (decision §14 D), sorted by
  `LastUsedAt` descending, `ActiveProfileIdHint` pre-selected.
- `MedReminder.UI.Forms.PinPromptForm` — three in-memory attempts
  (§8.3). Returns `DialogResult.Abort` on lockout so the caller
  bails out of the boot flow. Tooltip already carries the
  "friction, not security" note; the polish pass lands in 15e.

### Changed

- `IBackupService` — replaced `ExportAsync(dir, ct)` /
  `ImportAsync(src, ct)` with per-profile
  `ExportProfileAsync(profileId, dir, ct)` /
  `ImportProfileAsync(profileId, src, ct)` (§11.2). File name
  becomes `medreminder-<profileId>-YYYYMMDD-HHmmss.db` so
  different profiles can share a folder.
- `BackupService` — implements the new API. The retention regex
  captures the `profileId` group so
  `PruneOldBackupsAsync` applies retention per-profile: the most
  recent backup of profile A does not shield old backups of
  profile B (§11.1). `ImportProfileAsync` only closes the
  currently-active `DbContext` connection when the target matches
  the DB path — an import of an inactive profile no longer
  touches the live connection.
- `AutomaticBackupHostedService` — each tick now enumerates
  `IProfileRegistry.ListProfiles()` and calls `ExportProfileAsync`
  for every profile. A failure on one profile is logged but does
  not stop the others. Retention runs once on the shared folder.
  The tick is marked successful when at least one profile
  exported, so a partial failure never masks days without any
  backup (§11.1).
- `SmtpSettings` — dropped `ToAddress`. The recipient moved to
  `NotificationSettings.ToAddress` in
  `<DataDirectory>\notifications.settings.json` (§7.1).
  `IsConfigured` no longer checks the recipient.
- `MailKitEmailNotificationService` — now takes
  `IOptionsMonitor<SmtpSettings>` **and**
  `IOptionsMonitor<NotificationSettings>`. Throws a specific
  `InvalidOperationException` when the per-profile recipient is
  missing (§7.1).
- `AddMedReminderInfrastructure` — takes `ICurrentProfile` in
  place of a raw `databasePath`. Registers the current profile
  as a singleton, registers `IProfileRegistry` (built from
  `AppDataPaths`), and binds `NotificationSettings` from the
  configuration chain.
- `Program.Main` — new multi-profile boot flow (§4.1): run the
  V1 → V2 migrator, list profiles, pick one (first-run wizard /
  hint / `--profile` / picker), prompt for the PIN if the profile
  has one, then build the host. `--minimized` skips the picker
  and uses the hint (§4.2). `--profile <id>` bypasses the picker
  (§4.3). The single-instance mutex stays per Windows account,
  independent of the profile (§10.1).
- `Program.BuildHost` — adds
  `notifications.settings.json` (per-profile path from
  `ICurrentProfile.NotificationSettingsPath`) to the configuration
  chain with `reloadOnChange: true`.
- `SettingsDialog` — reads / writes `NotificationSettings` for
  the recipient (per-profile file). Export / import buttons now
  call the new per-profile backup APIs against
  `_currentProfile.Id`. The Backup tab still shows every existing
  option to the current user; the admin/user gating and the
  "Restore into profile…" dropdown land in 15d.
- `MainForm.ShowSettings` — passes the new
  `IOptionsMonitor<NotificationSettings>` and `ICurrentProfile`
  dependencies through the DI scope.
- `MedReminder.Infrastructure.Profiles.ProfileRegistry`,
  `CurrentProfile`, `MedReminder.Infrastructure.Migration.MigrationV1toV2`
  are now `public sealed class` so `Program.Main` (in
  `MedReminder.UI`) can build them at boot without expanding
  `InternalsVisibleTo`.

### Tests

- `MailKitEmailNotificationServiceTests` updated to the new
  two-monitor constructor. New test:
  `Send_throws_when_recipient_is_missing`.
- Existing `MigrationV1toV2Tests` and `ProfileRegistryTests`
  unchanged and still green: the migrator is now called at boot
  but its API is untouched.

### Localisation

- 27 new keys added to every dictionary
  (`assets/localization/strings.{en,it,fr,es,de}.json`) —
  `Common.Exit`, migration-failure banner, PIN prompt,
  profile picker, first-run wizard. All 5 dictionaries stay at
  parity (378 keys each) — `DictionaryParityTests` remain green.

### Out of scope (still)

- `ProfilesManagerForm`, admin/user UI gating, `File → Change
  profile…` menu entry, restore-into-profile dropdown — 15d.
- PIN prompt polish, tooltips wording pass, user-guide entries —
  15e.
- Promote/demote flow and consolidated admin view — non-goals
  for Increment 15 (§16).

---

## PR #23 — Increment 15b: V1 → V2 on-disk migration

Link: [vger70/MedReminder#23](https://github.com/vger70/MedReminder/pull/23)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15b` (stacked on `claude/incremento-15` from PR #22)

Second sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §5, §15b). Adds the data-lossless
migrator that moves an existing V1 installation to the V2 on-disk
layout under `%LOCALAPPDATA%\MedReminder\`. **The migrator is
dormant**: `Program.Main` does not call it in this PR. Wiring lands
in 15c together with the boot flow, so the app still boots as
single-user and no user-visible behavior changes.

### Added

- `MedReminder.Infrastructure.Migration.MigrationV1toV2` — one-shot
  idempotent V1 → V2 migrator with mandatory pre-migration backup
  and full rollback on any post-backup failure (§5.2).
  - Idempotence guard: runs only when `profiles.json` is missing
    AND a legacy `medreminder.db` sits at the app-data root (§5.1).
  - Step 1 copies `medreminder.db` (+ `-wal` / `-shm`) and
    `smtp.settings.json` into
    `backups\pre-migration-YYYYMMDD-HHmmss\`. The folder is
    self-describing and never overwritten — user is responsible
    for manual cleanup (§14 F).
  - Steps 2-3 move the DB files into `profiles\default\`.
  - Step 5 extracts `Smtp.ToAddress` from the legacy
    `smtp.settings.json` into
    `profiles\default\notifications.settings.json` and rewrites
    the source with the key removed. Empty / missing `ToAddress`
    is handled gracefully.
  - Step 6 seeds `profiles.json` via a new internal
    `ProfileRegistry.SeedFromV1Migration(id, displayName)` — the
    migrated profile is always `Role = admin`, `Id = "default"`,
    `DisplayName = "User"` (§5.2 step 6). The registry refuses to
    seed a non-empty file.
  - Any exception between steps 2 and 6 triggers
    `RollbackFromPreBackup`: `profiles.json` and
    `profiles\default\` are dropped, the DB files are restored
    from the pre-backup, and `smtp.settings.json` is restored
    verbatim. The pre-backup itself is preserved.
- `MigrationOutcome` public enum (`NotNeeded`, `Migrated`) — returned
  by `MigrationV1toV2.Run()` so the future boot flow can log the
  outcome.
- `tests/MedReminder.Infrastructure.Tests/Migration/MigrationV1toV2Tests.cs`
  — 6 integration tests exercising the migrator against a fake V1
  tree under `Path.GetTempPath()`:
  - fresh install (no legacy DB) → `NotNeeded`
  - already migrated (`profiles.json` present) → `NotNeeded`
  - full V1 tree with `ToAddress` → V2 layout, per-profile
    notifications file, `ToAddress` stripped from source
  - V1 without `smtp.settings.json` → DB migrated, no
    notifications file
  - V1 with empty `ToAddress` → key stripped, no notifications file
  - forced mid-migration failure → rollback restores the V1 state
    and the pre-migration backup is preserved
- Two extra `ProfileRegistryTests` covering the new
  `SeedFromV1Migration` internal (default admin seeding + refusal on
  a populated registry).

### Changed

- `MedReminder.Infrastructure.Profiles.ProfileRegistry`: added the
  internal `SeedFromV1Migration(string id, string displayName)`
  hook. It writes the initial `profiles.json` with a caller-chosen
  `Id` (the migrator uses the literal `"default"`) and forces
  `Role = Admin`. Throws if the registry is already populated so
  the migrator cannot silently be re-run.

### Docs / rollback semantics

- No changes to `docs/ANALYSIS-MULTI-USER.md`: the design is
  authoritative and will be marked as implemented at the bottom
  by 15e.
- No new localisation keys — the migrator is silent, log-only in
  15b. UI wiring for the outcome banner (if any) can be added in
  15c when the boot flow calls the migrator.

---

## PR #22 — Increment 15a: profile registry and ICurrentProfile abstraction

Link: [vger70/MedReminder#22](https://github.com/vger70/MedReminder/pull/22)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15`

First sub-increment of the multi-user support work designed in
[`docs/ANALYSIS-MULTI-USER.md`](docs/ANALYSIS-MULTI-USER.md) §15a. The
app still boots as single-user: this PR introduces the abstractions
and the on-disk registry (`profiles.json`) but does not yet wire
them into the boot flow — that lands in 15c. Zero user-visible
behavior change.

### Added

- `MedReminder.Application.Abstractions.ProfileRole` (User / Admin,
  `docs/ANALYSIS-MULTI-USER.md` §1.1a).
- `MedReminder.Application.Abstractions.Profile` — immutable record
  exposed by the registry (`Id`, `DisplayName`, `Role`, `CreatedAt`,
  `LastUsedAt`, `HasPin`).
- `MedReminder.Application.Abstractions.IProfileRegistry` — port
  owning `%LOCALAPPDATA%\MedReminder\profiles.json` with atomic
  writes, tolerant deserialization and the "at least one admin"
  invariant enforced on every mutation (§2.2, §2.3).
- `MedReminder.Application.Abstractions.ICurrentProfile` — read-only
  view of the profile the running process opened; `IsAdmin` is
  exposed here (§2.4).
- `MedReminder.Application.Abstractions.NotificationSettings` —
  per-profile POCO holding only `ToAddress`. Not yet consumed by
  `MailKitEmailNotificationService`; wiring lands in 15c (§7.1).
- `MedReminder.Infrastructure.Profiles.ProfileRegistry` — JSON
  persistence with PBKDF2-HMAC-SHA256 (100_000 iterations, 16-byte
  salt) for the optional PIN (§8.3), tmp + `File.Move` atomic
  writes (same pattern as `BackupStateStore`), and fail-safe
  "unknown role → user" deserialization.
- `MedReminder.Infrastructure.Profiles.CurrentProfile` — the
  concrete `ICurrentProfile` assembled at boot in 15c.
- `MedReminder.Infrastructure.Storage.DatabasePathProvider` —
  internal singleton fed by the composition root so
  `BackupService` shares the same DB path as EF Core without
  reaching back to `AppDataPaths`.
- `tests/MedReminder.Infrastructure.Tests/Profiles/ProfileRegistryTests.cs`
  — 19 unit tests covering CRUD, atomic save, PBKDF2 PIN roundtrip,
  last-admin refusal, tolerant `Role` deserialization, and the
  first-profile-forced-to-admin rule.

### Changed

- `MedReminder.Infrastructure.Storage.AppDataPaths` — removed the
  implicit `GetDatabasePath()` (moved to `ICurrentProfile.DatabasePath`
  in 15c); added `GetProfilesRootDirectory()`,
  `GetProfilesRegistryPath()` and `GetProfileDataDirectory(id)`
  (§2.5, §3). `BuildSqliteConnectionString` now requires an
  explicit path — no per-machine default.
- `MedReminder.Infrastructure.InfrastructureServiceCollectionExtensions.AddMedReminderInfrastructure`
  — new required `string databasePath` parameter. Registers the
  new `DatabasePathProvider` singleton.
- `MedReminder.Infrastructure.Backup.BackupService` — takes
  `DatabasePathProvider` from DI; `DatabasePath` now flows through
  the provider so the export target matches the EF Core
  connection string. Signature change to `ExportProfileAsync` /
  `ImportProfileAsync` is postponed to 15c (§11.2).
- `MedReminder.Infrastructure.Persistence.MedReminderDbContextFactory`
  — design-time factory passes an explicit legacy path (only used
  by `dotnet ef` tooling for schema generation).
- `MedReminder.UI.Program.BuildHost` — passes the legacy
  single-user path
  (`%LOCALAPPDATA%\MedReminder\medreminder.db`) to
  `AddMedReminderInfrastructure`. The multi-profile boot flow
  arrives in 15c; upgrades continue to open the historical file
  until then.
- `CLAUDE.md` §5 — updated the "current working branch" to
  `claude/incremento-15`.

### Docs

- No changes to `docs/ANALYSIS-MULTI-USER.md`: it stays the
  authoritative design document and will be marked as implemented
  at the bottom by 15e.
- No new localisation keys — the registry has no UI surface in
  15a. The 15-30 keys mentioned in the plan land in 15c / 15d /
  15e.

---

## PR #21 — Reference catalogue: suspend M4b (UK / DE) — sources not readily obtainable

Link: [vger70/MedReminder#21](https://github.com/vger70/MedReminder/pull/21)
**Status:** merged (2026-09-18)
Branch: `M4b_UK_DE_national_catalogues`

Documents the decision to **suspend M4b** (the UK MHRA and Germany
BfArM national catalogues described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.5) for lack of an easily obtainable, licence-clear bulk source.
No code, snapshots, tests or localisation keys change: the shipped
country set stays at IT + EU + ES + FR (M4 baseline).

### Docs

- `docs/ANALYSIS-DRUG-CATALOGUE.md` §3.5 — M4 status table extended
  with a "Suspended" row for UK and DE, spelling out why:
  - **UK / MHRA:** the `products.mhra.gov.uk` portal does not offer
    a bulk structured export of the Products dictionary. The
    realistic alternative (NHS BSA dm+d) ships under a licence that
    is not compatible with redistribution inside the shipped binary
    without a separate agreement. Post-Brexit UK is also the only
    country in the confirmed target set that would need
    `IncludesEuCentralised = false` — the override is already
    encoded in `StaticCountryProfileProvider.NonEuCovered` and stays
    dormant.
  - **DE / BfArM:** the AMIS-öffentlich / AMIce distribution has
    changed shape multiple times, and the current portal does not
    surface a stable bulk export with a clearly declared open-data
    licence at the point of download. Reopening the milestone
    requires that both prerequisites are met simultaneously.
- `docs/CATALOGUE-DATA.md` §7 — rewritten from "not shipping yet"
  to "suspended", with the rationale mirroring the analysis note
  and pointers back to the criteria that would need to be met to
  reopen the milestone.
- No changes to `THIRD-PARTY-NOTICES.md`: nothing new is
  redistributed. No changes to the About dialog, localisation
  dictionaries, parsers, tests or embedded assets.

### Changed

- `CHANGE_LOG.md` — the PR #20 entry (M4 ES + FR) is transitioned
  from `open` to `merged (2026-09-18)` in the same commit that
  prepends this entry, per the "update the entry when the PR's
  scope changes materially, and mark it merged / closed once the
  PR resolves" rule at the top of this file. PR #20 landed on
  `main` on 2026-09-18 as commit `ead9f5d`.

---

## PR #20 — Reference catalogue: AEMPS (Spain) + BDPM (France) national catalogues (M4)

Link: [vger70/MedReminder#20](https://github.com/vger70/MedReminder/pull/20)
**Status:** merged (2026-09-18)
Branch: `M4_Additional_national_catalogues`

Implements **M4** of the drug reference catalogue described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.5 — the first two additional national catalogues, Spain (AEMPS
CIMA) and France (ANSM BDPM). ES and FR are EU member states, so
the existing `IncludesEuCentralised = true` default in
`StaticCountryProfileProvider` covers them without any change:
searches from `userCountry = ES` now scope to `{ ES, EU }`, and
searches from `userCountry = FR` scope to `{ FR, EU }`.

### Added

- Two new snapshots embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/es/aemps-202609.zip`
  (2.7 MB, XLSX-in-ZIP wrapper) and
  `src/MedReminder.Infrastructure/Assets/Catalogue/fr/bdpm-202609.zip`
  (1.6 MB, three ISO-8859-15 TSVs at the archive root). Picked up
  automatically by two new `<EmbeddedResource>` globs and served
  under `MedReminder.Infrastructure.Assets.Catalogue.{es,fr}.<file>`
  — mirrors the existing IT / EU convention, no changes to
  `EmbeddedSnapshotProvider` needed.
- `AempsCimaParser` (`src/MedReminder.Infrastructure/Catalogue/Parsers/`):
  `IReferenceSnapshotParser` for country `ES`. Reads a single-XLSX
  ZIP entry (`aemps.xlsx` at the archive root) via
  `System.IO.Compression.ZipArchive` + `System.Xml.XmlReader`
  streaming — no new XLSX library dependency. Handles both the
  shared-string cell type (`t="s"`, the shape the real CIMA export
  emits) and the inline-string cell type (`t="inlineStr"`, the
  shape openpyxl-generated fixtures emit) via the same code path.
  Validates the fixed 15-column header at parse time, maps every
  row to `country = "ES"`, copies the row-level `Cód. ATC` onto
  every ingredient (pattern shared with `AifaSnapshotParser` and
  `EmaEparParser`), splits `Principios Activos` on `", "`, and
  populates `DispensingRegime` verbatim from the free-text
  `Observaciones` column. Registered next to `AifaSnapshotParser`
  in `InfrastructureServiceCollectionExtensions`.
- `AnsmBdpmParser`: `IReferenceSnapshotParser` for country `FR`.
  Reads `CIS_bdpm.txt` joined on CIS with `CIS_COMPO_bdpm.txt`
  from the ZIP archive. Encoding is **Windows-1252** — the ANSM
  portal documents it as ISO-8859-15 but the actual bytes contain
  cp1252-only 0x92 (curly single-quote `’`) used as apostrophe in
  French denominations; the code page provider is registered
  defensively on first use, requiring a new
  `System.Text.Encoding.CodePages` package reference on
  Infrastructure. **No header row** (columns are positional and
  hard-coded per the ANSM description); a shape-validation guard
  asserts the first non-short row's Statut column starts with
  `Autorisation` so a future column-order change in the ANSM
  export trips the parser instead of silently corrupting every
  row's MAH / MarketingStatus. Skips rows whose `Type de procédure
  AMM` starts with `Enreg homéo` — analogue of the `Omeopatico`
  filter in `AifaSnapshotParser`. `CIS_CIP_bdpm.txt` is kept in
  the shipped ZIP for symmetry with what ANSM publishes but is
  not consumed (the reference catalogue keys on CIS, and upstream
  CIP ships with a divergent UTF-8 encoding).
- `CatalogueRefreshHostedService.ImportOrder` extended from
  `{ IT, EU }` to `{ IT, EU, ES, FR }`. Each country still runs in
  its own transaction so a broken snapshot for one never blocks
  the others.
- Two new localisation keys `about.dataSources.aemps` and
  `about.dataSources.bdpm` in every dictionary
  (`en/it/fr/es/de`). Parity holds at 350 keys per dictionary;
  `DictionaryParityTests` stays green. `MainForm.ShowAboutDialog`
  appends both attributions below the existing AIFA and EMA EPAR
  lines, matching the four rows `THIRD-PARTY-NOTICES.md` carries.
- Curated fixtures:
  `tests/fixtures/catalogue/aemps-cima-sample.xlsx` (145 rows +
  header, stratified across Estado / multi-ingredient / brands,
  with three mandatory pins for the `Nº P. Activos`-aware split
  code path — REZAFUNGINA/NEVIRAPINA/TETRAKIS),
  `tests/fixtures/catalogue/bdpm-cis-sample.txt` (102 CIS in
  Windows-1252 — includes 9 homeopathic rows for the skip filter
  and two mandatory pins carrying the curly single-quote byte
  0x92, CELSIOR and CARMIN D'INDIGO),
  `bdpm-compo-sample.txt` (224 COMPO rows, 57 CIS with 2+
  ingredients), `bdpm-cip-sample.txt` (129 rows, kept only for
  fixture symmetry).
- `AempsCimaParserTests` (13 tests): row-count invariant on the
  fixture, ES-country invariant on every row, three-Estado
  coverage, multi-ingredient `", "` splitter guided by
  `Nº P. Activos` (mono-ingredient rows with intra-name commas
  like `REZAFUNGINA, ACETATO DE` stay intact), row-level ATC copied
  onto every ingredient, `Observaciones` on `DispensingRegime`,
  `PharmaceuticalForm` / `Dosage` / `LinkLeaflet` / `LinkSpc` all
  null, non-seekable stream. One dedicated test builds an XLSX in
  memory using `t="s"` + `sharedStrings.xml` so the code path
  used by the real AEMPS export is covered even though the
  openpyxl-generated fixture emits `t="inlineStr"`. Another
  dedicated test builds an XLSX with a leading blank row so the
  header-latch skip is exercised.
- `AnsmBdpmParserTests` (13 tests): 93 rows + 9 homeopathic
  skipped, FR-country invariant, Windows-1252 round-trip on
  accented denominations, curly single-quote (byte 0x92) preserved
  on CELSIOR / CARMIN D'INDIGO fixture pins, multi-ingredient
  join, ATC always null, MAH leading-space trim, status coverage,
  non-seekable stream. One dedicated test synthesises a broken
  BDPM row whose Statut column does not start with `Autorisation`
  and asserts the shape-validation `InvalidDataException`.
- `CsvReferenceCatalogueImporterTests` M4 additions: importing
  IT + EU + ES + FR in order populates each country row count as
  expected (168 + 70 + 145 + 93 = 476 rows, 4 distinct countries);
  a newer ES snapshot never touches FR rows at their older
  `snapshot_version`.
- `SqliteReferenceCatalogueQueryServiceTests` M4 additions:
  search from `userCountry = ES` returns rows scoped to
  `{ ES, EU }`; search from `userCountry = FR` returns rows
  scoped to `{ FR, EU }`; `ListAvailableCountriesAsync` surfaces
  all four countries after loading `IT + EU + ES + FR`.
- `StaticCountryProfileProviderTests` M4 additions: explicit
  `GetSearchScope("ES") = { ES, EU }` and
  `GetSearchScope("FR") = { FR, EU }` — the default "any country
  not explicitly listed" branch already covered them, but M4 gets
  an explicit assertion.

### Changed

- New `System.Text.Encoding.CodePages` package reference in
  `MedReminder.Infrastructure.csproj` (Microsoft, MIT). Only the
  BDPM parser needs it — every other parser stays on UTF-8.
- `THIRD-PARTY-NOTICES.md`: two new sections — AEMPS CIMA
  (Spain, reused under Ley 37/2007, attribution *"Fuente: AEMPS"*)
  and ANSM BDPM (France, reused under Licence Ouverte Etalab 2.0).
  Each section documents source URL, licence, applied filters,
  snapshot path and any deviation from the row schema.

### Docs

- `docs/CATALOGUE-DATA.md`: the "What ships" table gains ES and
  FR rows. New §5 documents the AEMPS refresh procedure (portal
  URL, XLSX-inside-ZIP layout, naming convention, drop path, boot
  log line). New §6 does the same for BDPM (portal URL,
  three-TSV-inside-ZIP layout, no-header positional columns,
  ISO-8859-15 encoding note for CIP-only, homeopathic-skip filter).
  §7 renames the former §5 "other countries" section and shrinks
  the pending list to UK + DE. Fixture-regen notes for the two new
  fixtures land in §4.3 (AEMPS) and §4.4 (BDPM), following the
  §4.2 template.
- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: the "Reference catalogue
  (Italy + EU)" section is renamed to "Reference catalogue
  (multi-country)" and gains a "Spanish and French national
  catalogues" subsection. Each guide is written in its own
  language and points to Settings → General → Reference country
  for switching. The "Data sources and terms" paragraph now
  mentions AEMPS (Ley 37/2007) and ANSM (Licence Ouverte Etalab
  2.0) alongside AIFA and EMA.

### Deviations from `docs/ANALYSIS-DRUG-CATALOGUE.md`

- **One PR for ES + FR instead of one PR per country.** §3.5 M4
  says "One PR per country". This PR ships ES + FR together as
  requested. Ordering rationale (§3.5): ES and FR are the two
  most mature open-data offerings on the M4 shortlist and share
  the "EU member → `IncludesEuCentralised = true`" profile, so
  they can land in a single reviewable slice without any code
  divergence. UK (which needs the per-country EU-flag override
  documented in §12.7) and DE stay on the M4 backlog.
- **AEMPS XLSX instead of the XML "Prescripción" bundle.** AEMPS
  ships two alternative dumps of the same registry — a tabular
  XLSX (~2.7 MB) and a relational XML bundle (~16 MB compressed,
  ~200 MB decompressed). The XLSX is ~6× smaller, needs no new
  library dependency (`System.Xml.XmlReader` +
  `System.IO.Compression` from the BCL cover it), and covers every
  field the autocomplete uses. `PharmaceuticalForm` / `Dosage` /
  `LinkLeaflet` / `LinkSpc` land as null — form and dose are
  embedded in the CommercialName text (same as AIFA
  `DENOMINAZIONE`); a future increment can switch to the XML
  variant without changing the row schema.
- **BDPM CIP file present but not parsed.** `CIS_CIP_bdpm.txt`
  ships in the embedded ZIP for symmetry with what ANSM publishes,
  but the M4 parser only consumes CIS + COMPO. The reference
  catalogue keys on CIS, not on packaging-level CIP, and upstream
  CIP has a divergent UTF-8 encoding (mismatch with the ISO-8859-15
  CIS / COMPO files); ignoring CIP keeps the parser on a single
  encoding path.

---

## PR #19 — Reference catalogue: EU centralised authorisations (EPAR) (M3)

Link: [vger70/MedReminder#19](https://github.com/vger70/MedReminder/pull/19)
**Status:** open
Branch: `M3-drug-reference-catalogue`

Implements **M3** of the drug reference catalogue described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.4 — the supranational `EU` catalogue. Instead of the Article 57
dataset the analysis mentions, this ships the EMA EPAR (European
public assessment reports) dataset, which is the file that
concretely materialises the "EU-centralised authorisations" concept
the design targets. Article 57 (pan-EEA per-country
authorisations) stays out of scope; the deviation is documented in
the PR body and in `THIRD-PARTY-NOTICES.md`.

### Added

- First real EMA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-202609.zip`
  (2.1 MB uncompressed, 486 KB compressed). Picked up automatically
  by a new `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.eu.ema-epar-202609.zip`
  — mirrors the existing IT convention, no changes to
  `EmbeddedSnapshotProvider` needed.
- `EmaEparParser` (`src/MedReminder.Infrastructure/Catalogue/Parsers/`):
  `IReferenceSnapshotParser` for country `EU`, reads a single-CSV ZIP
  entry (`ema-epar.csv`) at the archive root, matches EPAR header
  columns case-insensitively, filters `Category != 'Human'` (395
  veterinary rows dropped from the full 2 734-row export). Every
  emitted row carries `country = 'EU'` via
  `CountryCode.Parse("European Union")` — the long form never
  reaches the DB. `NationalCode` = EMA product number
  (e.g. `EMEA/H/C/004556`), guaranteed unique per row so the
  `UNIQUE (country, national_code)` constraint stays trivially
  satisfied without any synthetic derivation. Active substances
  split on `;` only (comma stays inside a single substance
  description); the row-level ATC is copied onto every ingredient
  of the row, same convention as `AifaSnapshotParser`. Registered
  next to `AifaSnapshotParser` in
  `InfrastructureServiceCollectionExtensions`.
- Extended `CatalogueRefreshHostedService` to iterate over
  `{ IT, EU }` instead of importing IT only. Each country runs in
  its own transaction (owned by the importer); a per-country
  `try/catch` around the import ensures a broken snapshot for one
  country never blocks the other.
- Curated fixture `tests/fixtures/catalogue/ema-epar-sample.csv`
  (73 rows: 70 Human + 3 Veterinary) covering every non-veterinary
  `Medicine status` EMA emits (`Authorised`, `Withdrawn`, `Refused`,
  `Suspended`, `Lapsed`, `Application withdrawn`, `Expired`,
  `Revoked`, `Opinion`), plus multi-substance rows (DuoPlavin,
  Symtuza, Qdenga) to exercise the `;` splitter, Gardasil 9 for the
  "comma-inside-a-single-substance" case, three no-ATC rows
  (Camcevi, Myqorzo, Vafseo), and a handful of well-known brands so
  assertions stay readable.
- New `EmaEparParserTests` (11 tests) exercising the fixture:
  row-count invariants, EU-country invariant on every yielded row,
  `;` vs `,` splitter behaviour, `EMEA/H/...` uniqueness, status
  pass-through, no-ATC handling, `Medicine URL` mirrored onto both
  leaflet + SPC links, non-seekable-stream buffering.
- `CsvReferenceCatalogueImporterTests` M3 additions:
  IT-then-EU import populates both countries with no dedup
  (168 + 70 = 238 rows visible with `country IN ('IT','EU')`),
  EU-only import leaves IT rows alone, a newer EU snapshot never
  touches IT rows at their older `snapshot_version`, no row ever
  lands with the `'European Union'` long form in `country`.
- `SqliteReferenceCatalogueQueryServiceTests` M3 additions:
  cross-country search after loading the EU fixture on top of the
  Italian one — scope `{ IT, EU }` returns Symtuza (an EU-only
  medicine that never appears in the Italian fixture); scope
  `{ EU }` returns only EU rows; `ListAvailableCountriesAsync`
  surfaces both `IT` and `EU`.

### Changed

- `about.dataSources.emaArticle57` value updated in all 5
  dictionaries (`en/it/fr/es/de`) from the M2 "reserved for a
  future release" placeholder to the real EPAR attribution line.
  The **key name** is left unchanged for parity stability — it
  is an internal identifier, the user-visible text is what
  changed. Parity holds at 348 keys per dictionary;
  `DictionaryParityTests` stays green.
- `THIRD-PARTY-NOTICES.md`: replaced the EMA placeholder section
  with the real attribution — EPAR dataset, EMA legal notice
  (Commission decision 2011/833/EU on reuse of Commission
  documents), snapshot path, applied filter (`Category = 'Human'`),
  and an explicit note that EPAR ≠ Article 57.

### Docs

- `docs/CATALOGUE-DATA.md`: new §3 "Refresh procedure (EU — EMA
  EPAR)" covering the XLSX-to-CSV conversion, the ZIP layout
  (single `ema-epar.csv` at archive root), the naming convention
  (`ema-epar-<yyyymm>.zip`), the drop path and the boot
  verification. §1 table gains the EU row; §5 renames from "other
  countries" and drops EMA from the pending list. Fixture-regen
  notes for AIFA move to §4.1, EPAR gets §4.2 with the exact
  status buckets the fixture must cover.
- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: "Reference catalogue
  (Italy)" section becomes "Reference catalogue (Italy + EU)"
  with a new "EU centrally authorised medicines" subsection
  explaining what EPAR is, that the autocomplete shows EU rows
  automatically when the reference country is any EU member
  (default IT), and that setting reference country to `EU`
  narrows the list to EU-only. Each guide in its own language.

### Deviations from `docs/ANALYSIS-DRUG-CATALOGUE.md`

- **EPAR instead of Article 57.** The design doc §1.3 lists "EMA
  Article 57" as the source for the `EU` country. The public
  Article 57 dump is actually the pan-EEA per-country
  authorisation register (~160 000 rows, one per (product ×
  authorisation country)) and has no explicit "centralised" flag —
  importing all of it as `country = 'EU'` would mis-label 160k
  national authorisations as supranational. EPAR is the concrete
  dataset that matches "EU-centralised medicines valid across the
  EU/EEA" (~2 700 rows, 1 568 currently Authorised), and it also
  ships ATC, MAH and the EMA product number as structured columns.
  File names, parser class and doc language use "EPAR" throughout;
  the localisation key `about.dataSources.emaArticle57` is kept
  as-is for compatibility with the M2 dictionaries.
- **Category = 'Human' filter.** Veterinary rows (395 of 2 734)
  are skipped at parse time — MedReminder targets human medicine
  reminders, and mixing veterinary products into the autocomplete
  would confuse users. Recorded as `Skipped` in `ImportReport`.

---

## PR #18 — Reference catalogue: foundations + AIFA autocomplete (Italy) (M1 + M2)

Link: [vger70/MedReminder#18](https://github.com/vger70/MedReminder/pull/18)
**Status:** open
Branch: `claude/sleepy-turing-s6fwzy`

### M2 — Autocomplete Italy (`src/MedReminder.UI` + snapshot embedded)

#### Added

- First real AIFA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-202609.zip`
  (94 MB uncompressed, ~5 MB compressed), picked up automatically by
  a `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.it.aifa-202609.zip`.
- `MedicineAutocompleteBox` WinForms control
  (`src/MedReminder.UI/Controls/`): text input + owner-drawn ListBox
  with 150 ms debounce on keystrokes, hard 20-row limit per query,
  row template `commercial_name — active_ingredient — dosage`, red
  circle badge on rows whose AIFA `STATO_AMMINISTRATIVO` contains
  `sospesa`, `ritirat` or `revocata` (case-insensitive substring —
  free-text column, no enum assumed), horizontal scrollbar with
  measured `HorizontalExtent`, dropdown anchored at the form's left
  edge and spanning the full dialog width.
- `CatalogueRefreshHostedService` (`src/MedReminder.UI/Hosting/`):
  boot-time importer that runs on a background thread only when
  `Catalogue:Enabled == true`, opens the embedded snapshot via
  `EmbeddedSnapshotProvider` and short-circuits when the recorded
  `snapshot_version` matches. Registered conditionally in
  `Program.BuildHost`.
- `MedicineEditDialog` hosts two `MedicineAutocompleteBox` instances
  — one on "Commercial name" and one on "Active ingredient".
  Picking a catalogue row on either side fills the sibling text
  field plus `Package` (from AIFA `DESCRIZIONE`), `Unit` (mapped
  from AIFA `FORMA` to a localised label via a 13-entry stem table,
  falling back to raw FORMA when nothing matches), and an extended
  commercial name of the shape `<CommercialName> <strength> <form>`
  where strength is extracted from `DESCRIZIONE` with a
  conservative regex (`mg / g / mcg / µg / ug / ml / l / ui / iu / %`).
  Free-text edits clear the reference linkage so a manually-typed
  entry saves unlinked.
- Settings dialog — General tab: new "Reference country" dropdown
  populated from `IReferenceCatalogueQueryService.ListAvailableCountriesAsync`
  plus the fixed `IT` and `EU` options. Save writes Language +
  ReferenceCountry atomically to `user.settings.json`.
- Root `THIRD-PARTY-NOTICES.md` listing the AIFA attribution
  (CC BY 4.0) and a placeholder for EMA Article 57 (M3). The two
  `about.dataSources.*` strings are surfaced in the About dialog.
- `docs/CATALOGUE-DATA.md`: monthly AIFA-refresh procedure — where
  to download, how to build the ZIP, where to drop it, how to
  regenerate the reduced fixture.
- Seven new unit keys in all five dictionaries (`Granules`,
  `Drops`, `Suppositories`, `Patches`, `Grams`, `Puffs`,
  `Ampoules`) — the medicine form's unit combo grew from 6 to 13
  options and picks up AIFA FORMA values automatically. Ten new
  M2 keys (`medicine.field.*`, `medicine.autocomplete.*`,
  `settings.referenceCountry.*`, `about.dataSources.*`). Parity
  verified across the 5 dictionaries (348 keys each).

#### Changed

- `UserSettings` gains `ReferenceCountry` (default `"IT"`).
  `user.settings.json` is now loaded with `reloadOnChange: true`
  so a country change takes effect at the next medicine-form open
  without a restart.
- `IReferenceCatalogueQueryService` gains
  `ListAvailableCountriesAsync` — one method, needed by the
  Settings dropdown.
- `AddMedicineCommand` and `UpdateMedicineCommand` gain trailing
  optional catalogue-linkage parameters (`NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` on Add; a `CatalogueLink`
  block on Update). Existing named-arg callers stay
  source-compatible; the two use-case implementations copy the
  linkage into the Medicine entity when set.
- `CatalogueFeatureOptions.Enabled` defaults to `true` in
  `appsettings.json`, activating the feature end-to-end.
- `MedicineEditDialog` width bumped from 620 to 880 px so the
  autocomplete dropdown has enough room for long AIFA rows without
  heavy horizontal scrolling; German `Unit.Vials` translation fixed
  from "Ampullen" (semantically ambiguous) to "Fläschchen", freeing
  "Ampullen" for the new `Ampoules` key that maps AIFA "fiale".

#### Docs

- `USER_GUIDE.{en,it,fr,es,de}.md`: new "Reference catalogue
  (Italy)" section covering autocomplete behaviour, the free-text
  fallback, the reference-country setting, and the AIFA source with
  its CC BY 4.0 licence — each guide in its own language.

---

### M1 — Foundations (`src/MedReminder.Domain` + `Application` + `Infrastructure`)

#### Added

- Domain (`net10.0`): `CountryCode` value object (normalises
  `European Union` → `EU`, validates ISO 3166-1 alpha-2) and
  `AtcCode` value object (7-character WHO ATC pattern), plus the
  `ReferenceMedicine` and `ReferenceActiveIngredient` read models
  under `src/MedReminder.Domain/Catalogue/`.
- Application (`net10.0`): `IReferenceCatalogueQueryService`,
  `IReferenceCatalogueImporter`, `ImportReport`,
  `SearchCatalogueUseCase`, `LinkMedicineToReferenceUseCase`,
  `ICountryProfileProvider` / `StaticCountryProfileProvider` (the
  sole owner of the "national ∪ EU" filter — default `true`;
  explicitly `false` for `GB` / `UK` per §12 point 7) and
  `CatalogueFeatureOptions` (off by default).
- Infrastructure (`net10.0-windows`): `AifaSnapshotParser` reads
  `confezioni_fornitura.csv` joined on `CODICE_AIC` with
  `PA_confezioni.csv` inside a ZIP archive, skipping
  `TIPO_PROCEDURA = 'Omeopatico'` and `PRINCIPIO_ATTIVO = 'N.D.'`;
  every row lands with `country = 'IT'`; `dispensing_regime` /
  `link_leaflet` / `link_spc` are mapped from `FORNITURA` /
  `LINK_FI` / `LINK_RCP`; 9-digit AIC leading zeros preserved.
- `CsvReferenceCatalogueImporter` (transactional replace,
  short-circuits on same recorded `snapshot_version`),
  `SqliteReferenceCatalogueQueryService` (raw-SQL adapter hitting
  the indexed `_norm` columns), `CatalogueTextNormalizer` (shared
  lowercase + diacritics stripping) and an `EmbeddedSnapshotProvider`
  stub (M2 will ship the first real snapshot).
- DI wiring for the catalogue ports; feature flag registered off by
  default, so no runtime behaviour changes.
- Tests: 35 new Domain tests (`CountryCode`, `AtcCode`), 25 new
  Application tests (fake-port union semantics for
  `SearchCatalogueUseCase`, `StaticCountryProfileProvider`,
  `LinkMedicineToReferenceUseCase`) and five new Infrastructure
  test files (`CatalogueSchemaTests`, `AifaSnapshotParserTests`,
  `CsvReferenceCatalogueImporterTests`,
  `SqliteReferenceCatalogueQueryServiceTests`, `CatalogueFixtures`)
  exercising the M0 fixture (168 kept / 29 Omeopatico skipped,
  idempotent replay, newer-version replace, Aspirina M2M).

#### Changed

- `Medicine` gains three optional catalogue fields — `NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` — surfaced on the entity
  and mapped in `MedicineConfiguration` for fresh DBs.
- `DatabaseInitializer.InitializeAsync` now applies the catalogue
  DDL unconditionally on every boot (additive, idempotent — no
  `EnsureCreated` shortcut for the catalogue tables per §2.4) and
  adds the three Medicine columns to pre-existing DBs via
  `AddColumnIfMissingAsync`.
- `MedReminder.Infrastructure.Tests.csproj` copies the
  `tests/fixtures/catalogue/*.csv` files as content to the test
  output directory.

#### Docs

- No changes to `docs/ANALYSIS-DRUG-CATALOGUE.md`. Four
  M1-time deviations flagged in the PR description
  (fixture uses ASPIRINA not Augmentin, port carries a pre-computed
  `countryScope`, AIFA parser copies `CODICE_ATC` onto every
  ingredient of a row, catalogue table ids stored as `TEXT` to
  match how EF stores Guids elsewhere).

---

## PR #13 — Add drug reference catalogue design analysis (with M0 findings)

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
- §3.1 M0 marked as completed. Full field mapping and
  cardinalities are recorded in the M0 comment on issue
  [#9](https://github.com/vger70/MedReminder/issues/9#issuecomment-5718752090):
  85,697 imported packages, 9,619 commercial names,
  5,750 active ingredients, 2,269 ATC codes after applying the
  two documented import filters.
- §2.4 schema: three new optional columns on `reference_medicines`
  — `dispensing_regime` (from AIFA `FORNITURA`), `link_leaflet`
  (from `LINK_FI`), `link_spc` (from `LINK_RCP`).
- §3.2 M1: documented two AIFA import filters — skip
  `TIPO_PROCEDURA = 'Omeopatico'` (74k rows, 46%) and skip
  `PRINCIPIO_ATTIVO = 'N.D.'` in `PA_confezioni` (53k rows).
- §7 Risks: replaced the speculative snapshot-size row with the
  measured baseline (gzipped snapshot ~17–22 MB, SQLite growth
  ~60–80 MB).
- §12.1 marked Resolved with the resolved dataset choice
  (`confezioni_fornitura.csv` joined with `PA_confezioni.csv`).
- Follow-up to the discovery notes in
  [#9](https://github.com/vger70/MedReminder/issues/9).
- No source code changes; no runtime behaviour changes.

### Added

- `tests/fixtures/catalogue/aifa-confezioni-sample.csv`,
  `aifa-pa-sample.csv`, `aifa-atc-sample.csv` — 198 + 253 + 76
  rows stratified from real AIFA open data. Cover `Sospesa`
  status, `Procedura Centralizzata` (EU-authorised), OTC,
  hospital-only, well-known brands (Augmentin, Aspirina,
  Tachipirina, Cardura, Eutirox, Coumadin, Zoloft, …), common
  active ingredients (paracetamolo, ibuprofene, metformina,
  olmesartan, atorvastatina, simvastatina, omeprazolo,
  amoxicillina, ramipril, bisoprololo), and 37 multi-ingredient
  combinations so the M2M table is exercised. Consumed by M1
  integration tests once M1 lands.

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

# Implementation prompt — Multi-user G/I: role change and consolidated admin view

Briefing for the Claude Code session that implements
`docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` (ranking item 3).
Read it fully, then read the referenced files before changing code.
Do not start until the product owner has confirmed decisions D1–D7 of
the analysis (§9); if a decision was changed, the confirmed value wins
over this prompt.

---

## 1. Goal

1. **G** — An admin changes the role of another profile (promote a
   user to admin, demote an admin to user) from Tools → Manage
   profiles. The registry never ends without an admin; the open
   profile's role cannot be changed.
2. **I** — An admin opens Tools → All profiles… and sees, read-only,
   the medicines of every profile with the same stock, run-out date and
   status that `MainForm` would show if that profile were opened today,
   and can switch to one of them.

## 2. Context to read first

- `CLAUDE.md` (language policy, constraints §7, PR workflow §4).
- `docs/ANALYSIS.md` §4.4 (`WriteGate`), §5.2 (profiles), §7 (process
  lifecycle), §8.1 (schema patches), §8.2 (backup).
- `docs/analysis/ANALYSIS-MULTI-USER.md` §1.1a, §2.2, §8.2, §9.1,
  §12, §14a G/H/I.
- `docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` (all).
- Code:
  - `src/MedReminder.Application/Abstractions/IProfileRegistry.cs`,
    `ProfileRole.cs`, `ICurrentProfile.cs`
  - `src/MedReminder.Infrastructure/Profiles/ProfileRegistry.cs`
  - `src/MedReminder.Application/UseCases/ProfileSettingsUseCases.cs`
    (`RenameProfile`: pattern for a profile use case)
  - `src/MedReminder.UI/Forms/ProfilesManagerForm.cs`
  - `src/MedReminder.UI/Forms/MainForm.cs` (Tools menu, admin gating,
    profile switch via `_restarter.RestartAndExit(["--profile", id])`)
  - `src/MedReminder.Application/Overview/MedicineOverviewLoader.cs`,
    `MedicineListItem.cs`
  - `src/MedReminder.Application/Monitoring/ConsumptionCatchUp.cs`
  - `src/MedReminder.Infrastructure.Portable/PortableInfrastructureServiceCollectionExtensions.cs`,
    `Persistence/SqliteConnectionStrings.cs`,
    `Persistence/DatabaseInitializer.cs`
  - `src/MedReminder.Infrastructure/Backup/BackupService.cs`
    (`ExportProfileAsync`)
  - `src/MedReminder.Infrastructure/Export/ExportService.cs`
    (snapshot → read pattern, admin check in `ResolveTarget`)
  - `src/MedReminder.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
  - `tests/MedReminder.Infrastructure.Tests/Profiles/ProfileRegistryTests.cs`,
    `tests/MedReminder.Application.Tests/UseCases/RenameProfileTests.cs`

## 3. Work split

One PR per step, in this order. Each PR leaves the app building and
working. Open the PR right after the first commit (`CLAUDE.md` §4) and
add its `CHANGE_LOG.md` entry.

### Step G1 — Role change

- `IProfileRegistry.SetRole(string id, ProfileRole role)`; implement in
  `ProfileRegistry` under the existing lock: unknown id →
  `InvalidOperationException`; demoting the last admin →
  `InvalidOperationException` (same message style as `Delete`); same
  role → return without saving.
- Rewrite the "role is immutable" comments in `IProfileRegistry.cs`,
  `ProfileRole.cs`, `ProfilesManagerForm.cs` header.
- `ChangeProfileRole` in `Application/UseCases` (register in
  `AddMedReminderApplication`). Checks, in order: caller is admin,
  target is not the current profile (D1), target exists; then calls
  `SetRole` and maps the last-admin refusal. Throw one exception type
  with a reason enum so the UI can pick the message. Log one
  `Information` line with profile id, old role, new role. No display
  name in logs.
- `ProfilesManagerForm`: button **Change role…**; re-lay out the button
  row so all six buttons fit (the form is 640 px, fixed). Disabled for
  the active profile and for the last admin, with tooltips. Confirmation
  dialog, default button No; for a promotion of a profile without PIN
  add the PIN recommendation (D2). `Reload()` after success.
- Strings: new keys in all five `assets/localization/strings.*.json`;
  replace `Ui.ProfilesManagerForm.Hint.ImmutableRole` with a new hint
  key and delete the old one everywhere; remove the "cannot be changed"
  sentence from `Ui.FirstRunWizardForm.Explanation`. Check with a grep
  that no other string or guide still says the role is immutable.
- Tests: `ProfileRegistryTests` (promote, demote, no-op, last admin,
  unknown id, persisted after reload); `ChangeProfileRoleTests` in
  Application.Tests with fakes (each refusal, each success).

### Step I1 — Overview reader and query

- `Infrastructure.Portable`: a way to build a **non-pooled** connection
  string for a given file (for example
  `SqliteConnectionStrings.ForFile(path, pooling: false)` or a separate
  method), and an overload/option of
  `AddMedReminderPortableInfrastructure` that uses it. Keep the default
  behavior unchanged for the live database. No Windows API in this
  project.
- `Application/Abstractions`: `IProfileOverviewReader` and
  `ProfileOverview` (profile id + `IReadOnlyList<MedicineListItem>`).
- `Application/Overview/ConsolidatedOverviewQuery`: refuses a non-admin
  caller; lists profiles from `IProfileRegistry`; reads each through
  the reader; a failure on one profile becomes an error entry for that
  profile (logged with the profile id), the others are still returned.
  Register in `AddMedReminderApplication`.
- `Infrastructure/Overview/ProfileOverviewReader` (register scoped in
  `AddMedReminderInfrastructure`). Per call:
  1. Scratch directory `%LOCALAPPDATA%\MedReminder\tmp\overview-<guid>\`
     (D7; derive the root from `AppDataPaths`). On entry, delete stale
     `overview-*` folders older than one day, best effort.
  2. `IBackupService.ExportProfileAsync(profileId, scratch)`.
  3. Build a throw-away `ServiceCollection`:
     portable infrastructure on the scratch file with pooling off,
     `AddMedReminderApplication()`, plus the host's `TimeProvider` and
     `ILocalizationService` instances as singletons. Add only what
     resolving the three services below needs; do not add hosted
     services, sync transport credentials or DPAPI stores. If
     `AddMedReminderApplication` pulls a dependency that cannot be
     satisfied, resolve it with a minimal registration and explain the
     choice in a comment.
  4. In one scope: `DatabaseInitializer.InitializeAsync`,
     `ConsumptionCatchUp.RunAsync`, `MedicineOverviewLoader.LoadAsync`.
  5. `finally`: dispose the provider, delete the scratch directory.
     **Do not call `SqliteConnection.ClearAllPools()`**: it would drop
     the live profile's connections (`CLAUDE.md` §7). If the file is
     still locked, that means pooling was not really off: fix it, do
     not work around it.
  - The reader must never open the source profile's live file other
    than through `ExportProfileAsync`.
  - `ConsumptionCatchUp.RunAsync` takes the static `WriteGate`; the
    reader must not be called while the caller holds the gate. State
    this in the class comment.
- Tests (Infrastructure.Tests, Windows):
  - DB built in a temp root with one medicine, a daily schedule and
    derived rows ending N days ago → returned stock equals the stock
    after N more days of consumption; the source database's rows are
    unchanged (compare a dump of every table, not file bytes: a WAL
    checkpoint on close may rewrite the file without changing data).
  - Source DB missing a column added by a schema patch → read succeeds.
  - Scratch directory absent after success and after a thrown error.
  - Query: non-admin refused; one failing profile isolated.
  If `AppDataPaths` makes the scratch root hard to redirect in tests,
  inject the root through the constructor as `ProfileRegistry` does.

### Step I2 — Overview window

- `ConsolidatedOverviewForm` (derive from `MedReminderFormBase`, follow
  `ProfilesManagerForm` layout conventions, honor `TextScale`).
- Per-profile summary line (name, count of Warning and Empty, "last
  opened" from `Profile.LastUsedAt`, sync marker from
  `ISyncProfileStatus.IsSyncEnabled`, error text when the read failed).
- Grid columns and status colors as `MainForm` (reuse its formatting
  helper if one exists; extract one if the code is duplicated, keep the
  extraction minimal). Inactive medicines hidden. Filter "Only
  medicines needing attention" on by default. Sort by days remaining.
- Load off the UI thread with progress and Cancel; Refresh button.
- Open profile (button and double-click, not for the current profile):
  confirmation, then the same switch path `MainForm` already uses,
  including the target's PIN prompt at boot.
- `MainForm` Tools menu: entry visible only for admin and when the
  registry has at least two profiles (D6); guard again inside the
  handler as `ShowProfilesManager` does.
- Strings in all five dictionaries.

### Step D — Documentation

- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: update "Roles" (role change,
  open profile not changeable, last admin); new section on the All
  profiles view, stating it is read-only, that it shows PIN-protected
  profiles to the admin (D4), and that a synced profile shows its data
  as of its last opening.
- `docs/ANALYSIS.md` §5.2 and §12; `ANALYSIS-MULTI-USER.md` §14a G, I
  and §16 (pointer to the new analysis); `docs/STATUS.md` §3.5;
  `docs/EVOLUTION.md` intro; mark the new analysis as implemented at
  the bottom, listing the PRs; move this prompt to
  `docs/prompt/Completed/`.
- Step D may be folded into I2 if the PR stays reviewable.

## 4. Constraints

- English for code, comments, logs, exceptions, docs, commits and PRs;
  translations only in the dictionaries and user guides.
- Clean Architecture boundaries (`CLAUDE.md` §1): no Infrastructure or
  UI type in Application; nothing Windows-specific in
  Infrastructure.Portable.
- No schema change, no `profiles.json` format change, no export
  format change.
- Keep the single-instance mutex and the "one process owns each
  profile database" rule. No write to another profile's live database.
- No notifications for inactive profiles.
- Nothing written outside `%LOCALAPPDATA%\MedReminder\`.
- No display names, medicine names or notes in logs; profile ids only.
- Every new UI key in all five dictionaries.
- Ask the user to run `dotnet build` and `dotnet test` on Windows
  before each commit that touches source code; Infra tests do not run
  on Linux.

## 5. Done when

- An admin can promote and demote another profile; the open profile
  and the last admin are refused in the use case, the registry and the
  UI; all new tests pass.
- The All profiles window shows, for every profile, the same values
  the main window shows once that profile is opened on the same day
  (verify manually with two profiles, one not opened for several
  days).
- The source database of an inactive profile has the same rows before
  and after opening the window.
- No text in the app or in the guides still says the role is
  immutable.
- Docs and `CHANGE_LOG.md` updated; PRs green.

# ANALYSIS — Multi-user G/I: role change and consolidated admin view

Design document, **prior** to implementation. It reopens the two
non-goals of Increment 15 (`ANALYSIS-MULTI-USER.md` §14a points G and
I, §16):

- **G** — change the role of an existing profile (promote a user to
  admin, demote an admin to user).
- **I** — a read-only view, for an admin, of the stock status of every
  profile without switching profile.

Status on 2026-09-29: item G is implemented by step H2 of the
household feature (`ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §8), with the
recommendations of D1 and D2 (§9) and the design of §3, plus a
household operation per change. Item I is still not planned.

Status on 2026-09-28: `docs/STATUS.md` §3.5 and `docs/EVOLUTION.md`
record both as "not planned, picked up only if a concrete need
emerges". This document exists because the product owner asked for it
(ranking item 3). It becomes work only after the decisions in §9 are
confirmed.

Reading conventions: **[INFERRED]** marks a deduction from the code or
documents, **[UNCERTAIN]** a claim not verified. Untagged statements
were checked against the code at commit `c443e9e` (v2.10.0).

---

## 1. Current state (verified in code)

| Area | Where | Behavior today |
|---|---|---|
| Role storage | `Infrastructure/Profiles/ProfileRegistry.cs` | `profiles.json`, field `Role` = `"admin"` / `"user"`; unknown values read as `User` |
| Role API | `Application/Abstractions/IProfileRegistry.cs` | No mutator for the role. `Create` forces `Admin` on an empty registry; `Delete` refuses the last admin |
| Current profile | `ICurrentProfile` (singleton) | `Role` / `IsAdmin` fixed at boot; switching profile restarts the process (`IApplicationRestarter.RestartAndExit(["--profile", id])`, `MainForm.cs`) |
| Admin gating | `MainForm.cs` (Tools → Manage profiles), `SettingsDialog.cs` (Email, Backup tabs), `ExportService.ResolveTarget` (export of another profile) | UI-only, plus one Infrastructure check in `ExportService` |
| Role and sync | `ProfileSetting` / `ProfileSettingChanged` | Replicates display name and notification addresses. **Role is not replicated** and is not restored by import: it is local to the installation |
| Cross-profile reads | `BackupService.ExportProfileAsync(profileId, dir)` | Online-backup API copy of any profile's DB; used by the automatic backup and by `ExportService` (admin may export another profile) |
| Medicine list | `Application/Overview/MedicineOverviewLoader.cs` | Builds `MedicineListItem` rows (stock, daily rate, days remaining, run-out date, status) from the scoped repositories of the **current** profile |
| Derived ledger | `Application/Monitoring/ConsumptionCatchUp.cs` | Writes automatic consumption "through yesterday" when the profile is open. A profile not opened since day X has no derived rows for X..yesterday |
| Schema upgrade | `Infrastructure.Portable/Persistence/DatabaseInitializer.cs` | Idempotent patches applied at boot to the **current** profile only. An inactive profile can be on an older schema |

User-visible text that states the role is immutable:
`Ui.FirstRunWizardForm.Explanation`,
`Ui.ProfilesManagerForm.Hint.ImmutableRole` (5 dictionaries each),
`docs/USER_GUIDE.<lang>.md` §"Multiple profiles and admin/user roles"
→ "Roles" (5 guides).

---

## 2. Scope

### 2.1 Included

- G: change a profile's role from `ProfilesManagerForm`, admin only.
- I: a new read-only window listing the medicines of every profile,
  admin only, with a way to switch to a listed profile.

### 2.2 Excluded

- Editing another profile's data from the consolidated view. Writes
  stay "one profile at a time" (§14a I keeps its meaning for writes).
- Notifications (toast, email, dose reminders) for inactive profiles.
  `ANALYSIS-MULTI-USER.md` §9.1 is unchanged: only the active profile
  notifies.
- Replicating the role through sync (§3.4).
- More than two roles, per-feature permissions, audit trail.
- Any change to the single-instance mutex or to the "one process owns
  each profile database" rule (`CLAUDE.md` §7, `ANALYSIS.md` §7).

---

## 3. G — Role change

### 3.1 Rules

1. Only a caller whose current profile is admin may change a role.
2. The registry keeps **at least one admin** after every write
   (same invariant as `Delete`).
3. The **active profile's role cannot be changed** (decision D1,
   recommended). Reason: `ICurrentProfile.Role` is read at boot and
   drives menu, Settings tabs and `ExportService`; changing it without
   a restart leaves the running UI inconsistent. It mirrors §14a H
   ("the active profile cannot be deleted; switch first"). Consequence:
   an admin who wants to demote themselves switches to another admin
   profile first. Promoting the active profile never applies (it is
   already admin, by rule 1).
4. Setting the same role is a no-op (no write, no log line).
5. Promoting a profile without a PIN shows a non-blocking warning
   (decision D2): an admin without a PIN lets anyone at the PC open
   the admin profile (`ANALYSIS-MULTI-USER.md` §13, "self-promote"
   row).

With rule 3, "demote the last admin" can only be reached by demoting a
non-active admin while the active profile is a user, which rule 1
already forbids. The invariant check stays in the registry anyway as
defense in depth, like `Delete`.

### 3.2 API

Port (`IProfileRegistry`):

```csharp
// Throws InvalidOperationException if id is unknown or if the change
// would leave the registry without an admin (§2.2 invariant).
// Same role → no write.
void SetRole(string id, ProfileRole role);
```

Use case (`Application/UseCases`, next to `RenameProfile`):

```csharp
public sealed class ChangeProfileRole
{
    // ctor: IProfileRegistry, ICurrentProfile
    // Throws ProfileRoleChangeException(reason) with
    //   NotAdmin      — current profile is not admin
    //   ActiveProfile — profileId == current.Id (D1)
    //   LastAdmin     — registry refused (invariant)
    //   NotFound      — unknown id
    public void Execute(string profileId, ProfileRole newRole);
}
```

The use case is synchronous and does not take `WriteGate`: it touches
`profiles.json` only, which `ProfileRegistry` already guards with its
own lock, and no database. Placing the admin check in Application (not
only in the form) follows the `ExportService` precedent and makes it
unit-testable with fakes.

Logging: one `Information` line with the profile **id**, old and new
role. No display name (it is a person's name).

### 3.3 UI

`ProfilesManagerForm`:

- New button **Change role…** between *Change PIN…* and *Delete*
  (the form is fixed-size at 640 px; the button row needs re-layout).
- Disabled for the active profile (tooltip: switch profile first) and,
  for an admin row, when it is the last admin.
- Opens a confirmation: "Make <name> an administrator?" /
  "Make <name> a standard user?". For a promotion of a profile
  without PIN, the text adds the PIN recommendation (D2).
- On success: `Reload()`.
- `Ui.ProfilesManagerForm.Hint.ImmutableRole` is replaced by a new key
  (for example `Ui.ProfilesManagerForm.Hint.Role`) explaining that the
  role of the open profile cannot be changed; the old key is removed
  from the 5 dictionaries.
- `Ui.FirstRunWizardForm.Explanation`: drop "The role cannot be
  changed after creation" (5 dictionaries).

### 3.4 Interactions

- **Sync (B.1)**: the role stays local to `profiles.json`. A profile
  synced between two PCs can be admin on one and user on the other.
  This is correct: the role grants control over *this installation's*
  SMTP, backup and registry, which are not replicated either.
- **Export / import (C.3)**: the payload carries `Profile.Role` as
  information; import does not apply it (unchanged). No format change.
- **Automatic backup, cloud snapshots**: unaffected (they run for every
  profile regardless of role).
- **Soft security**: unchanged. `profiles.json` stays hand-editable;
  the feature only makes the supported path available in the UI.

### 3.5 Risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| UI state stale after a role change | — | — | Excluded by D1 (active profile not changeable) |
| Registry without admin | Very low | High | Invariant in `SetRole` + disabled button + use-case check; unit test |
| Accidental promotion | Low | Medium | Confirmation dialog, default button No |
| Promoted profile has no PIN | Medium | Medium | Warning in the confirmation (D2) |

---

## 4. I — Consolidated admin view

### 4.1 What it shows

A window **Tools → All profiles…** (admin only; shown only when the
registry has two or more profiles, D6). One grid, one row per medicine
of every profile (D3):

| Column | Source |
|---|---|
| Profile | `Profile.DisplayName` |
| Medicine | `MedicineListItem.Name` |
| Stock | `StockDisplay` |
| Days remaining | `DaysRemainingDisplay` |
| Run-out date | `EtaDisplay` |
| Status | `StatusDisplay` (same colors as `MainForm`) |

Above the grid, one line per profile: name, number of medicines in
Warning / Empty, "last opened <date>", and a marker when the profile
takes part in sync (its local data can lag the other devices until it
is opened) [INFERRED from sync running only in the open profile's
host].

Filter "Only medicines needing attention" (Warning + Empty), **on by
default**; inactive medicines hidden, as in `MainForm`. Sort by days
remaining ascending. A **Refresh** button re-reads everything.

Double-click on a row of another profile, or button **Open profile**:
the existing switch flow (PIN prompt of the target, then
`RestartAndExit(["--profile", id])`), after a confirmation.

### 4.2 Reading another profile safely

The requirement is: never open another profile's live database for
writing, and still show numbers that match what `MainForm` would show
if that profile were opened today.

Two facts force the design:

1. The live file of an inactive profile may be on an **older schema**
   (patches run only at that profile's boot). Querying it with the
   current EF model can fail on a missing column [INFERRED from
   `ANALYSIS.md` §8.1].
2. Its **derived consumption rows stop at the last day it was open**.
   `MedicineStock.Current` over those rows overstates the stock, and
   the run-out date is late by the number of days since then
   [INFERRED from `ConsumptionCatchUp` deriving "through yesterday"].

Pipeline per profile (reuses what `ExportService` already does):

```
BackupService.ExportProfileAsync(id, scratchDir)     ← online-backup copy
      │   (live file only read; same path as automatic backup)
      ▼
throw-away ServiceProvider on the scratch copy
  AddMedReminderPortableInfrastructure(scratchPath, pooling off)
  AddMedReminderApplication()
  + host singletons: TimeProvider, ILocalizationService
      │
      ├─ DatabaseInitializer.InitializeAsync   ← patches the COPY
      ├─ ConsumptionCatchUp.RunAsync           ← derives to yesterday on the COPY
      └─ MedicineOverviewLoader.LoadAsync      ← same rows as MainForm
      ▼
dispose provider, delete scratch directory
```

Properties:

- The live database of the inactive profile is only read, through the
  SQLite online-backup API, exactly as the nightly backup already does.
  No new writer, so the in-process `WriteGate` model and the
  single-process ownership rule hold.
- Numbers are computed by the same code as the main window
  (`MedicineOverviewLoader`, `MedicineForecast`), not a second
  calculation.
- The scratch connection must be opened with pooling disabled so the
  file can be deleted without `SqliteConnection.ClearAllPools()`,
  which would also drop the live profile's pooled connections. `CLAUDE.md`
  §7 allows closing SQLite connections only in backup/restore paths.
- `ConsumptionCatchUp.RunAsync` takes the process-wide `WriteGate`.
  It works on the copy, so holding the gate is harmless but serializes
  with the live profile's writers for the duration of the catch-up
  (milliseconds to about a second for 2-5 profiles) [UNCERTAIN: not
  measured]. The reader must not be called from inside the gate (the
  gate is not reentrant).
- The **active profile** goes through the same pipeline, for one code
  path. Its catch-up on the copy is a no-op when the live catch-up has
  run.

Where the code lives:

- Port in `Application/Abstractions`:
  `IProfileOverviewReader.ReadAsync(string profileId, CancellationToken)`
  → `ProfileOverview(ProfileId, IReadOnlyList<MedicineListItem> Items)`.
- Implementation in `MedReminder.Infrastructure` (it needs
  `IBackupService`, which lives there): `ProfileOverviewReader`,
  registered scoped.
- Query in `Application/Overview`: `ConsolidatedOverviewQuery`
  (checks `ICurrentProfile.IsAdmin`, iterates
  `IProfileRegistry.ListProfiles()`, calls the reader for each, returns
  per-profile results). A failure on one profile (missing DB file,
  corrupt copy) yields an error entry for that profile, not a failed
  window.

### 4.3 PIN-protected profiles

The view shows the medicines of profiles that have a PIN without
asking for it (decision D4, recommended). Precedent: an admin can
already export another profile's data (`ExportService`) and restore
into it, without that profile's PIN, and the PIN is documented as
friction, not security (`ANALYSIS-MULTI-USER.md` §8.2). The user guide
states this explicitly.

### 4.4 Performance

For each profile: one online-backup copy (size of the DB, typically
under a few MB) [UNCERTAIN: no field data on DB sizes], schema patches
on the copy (no-ops when already current), catch-up, and the loader
(one query per repository per medicine, as in `MainForm`). The whole
read runs off the UI thread with a progress indicator and a Cancel
button. No caching: the window reads on open and on Refresh.

### 4.5 Risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Numbers differ from those shown when the profile is opened | Medium | Medium | Same loader; catch-up on the copy; "last opened" shown; sync marker |
| Scratch file left in `%TEMP%` after a crash | Low | Low (medical data in temp) | Scratch under `%TEMP%\MedReminder-overview-<guid>`, deleted in `finally`; stale folders with that prefix removed at next open [the same risk exists today for `MedReminder-export-*`] |
| Live pooled connections dropped | — | — | Pooling off on scratch; no `ClearAllPools` |
| Old-schema copy fails the patch | Low | Low | Per-profile error entry; the live DB is untouched |
| Gate contention with the active profile | Low | Low | Short catch-up; runs only on user request |

Scratch location note: `CLAUDE.md` §5 forbids writing outside
`%LOCALAPPDATA%\MedReminder\`, while `ExportService` already uses
`Path.GetTempPath()`. The new reader should use
`%LOCALAPPDATA%\MedReminder\tmp\overview-<guid>\` to comply, and the
existing `ExportService` deviation is left for a separate change
[decision D7].

---

## 5. Architecture impact

- Domain: none.
- Application: `IProfileRegistry.SetRole`, `ChangeProfileRole`,
  `IProfileOverviewReader`, `ProfileOverview`,
  `ConsolidatedOverviewQuery`, updated comments on `ProfileRole` and
  `IProfileRegistry` (role no longer immutable).
- Infrastructure: `ProfileRegistry.SetRole`, `ProfileOverviewReader`,
  DI registration.
- Infrastructure.Portable: an overload or option on
  `AddMedReminderPortableInfrastructure` (or on `SqliteConnectionStrings`)
  to build a non-pooled connection string for the scratch copy. Must
  stay free of Windows APIs.
- UI: `ProfilesManagerForm` (button, layout, dialog),
  `ConsolidatedOverviewForm` (new), `MainForm` (menu entry and switch
  reuse).
- Schema: none. `profiles.json` format: none (`SchemaVersion` stays 1).
- Export format: none.

## 6. Localization

New keys in all five dictionaries (`strings.{en,it,fr,es,de}.json`):
role-change button, tooltips, two confirmation texts, PIN warning,
result errors (last admin, active profile); overview window title,
menu entry, column headers, filter, refresh, open profile, per-profile
summary line, sync marker, error line. Removed:
`Ui.ProfilesManagerForm.Hint.ImmutableRole`. Changed:
`Ui.FirstRunWizardForm.Explanation`.

## 7. Documentation

- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: "Roles" section (role change,
  rules), new "All profiles view" section (read-only, PIN note, data
  as of last opening for synced profiles).
- `docs/ANALYSIS.md` §5.2 (roles), §12 (feature analyses list).
- `docs/analysis/ANALYSIS-MULTI-USER.md`: note under §14a G and I and
  §16 pointing here.
- `docs/STATUS.md` §3.5, `docs/EVOLUTION.md` intro: status update.
- `CHANGE_LOG.md`.

## 8. Tests

Infrastructure (`ProfileRegistryTests`): promote, demote, same-role
no-op, demote-last-admin refused, unknown id, role persisted across
reload.

Application: `ChangeProfileRole` (not admin, active profile, last
admin, success paths) with fakes; `ConsolidatedOverviewQuery` (not
admin refused, one reader failure isolated, ordering).

Infrastructure (`ProfileOverviewReader`, needs Windows like the other
Infra tests): a profile DB built in a temp directory with a medicine
and a schedule, last derived day N days ago → read returns stock
reduced by N days of consumption and leaves the source database's
rows unchanged (compare a dump of every table, not file bytes: closing
the last connection of a WAL database may checkpoint it
[INFERRED]); an old-schema DB (missing a patched column) is read
without error; scratch directory removed after the call.

UI: none automated beyond existing patterns [INFERRED: `UI.Tests`
covers hosted services, not forms].

## 9. Decisions to confirm

| ID | Question | Recommendation |
|---|---|---|
| D1 | Can the active profile's role change? | No, switch first (mirrors §14a H) |
| D2 | Promoting a profile without PIN | Allowed, with warning |
| D3 | Overview granularity | Flat medicine grid with Profile column + per-profile summary line |
| D4 | PIN-protected profiles in the overview | Shown without PIN (admin precedent: export/restore) |
| D5 | Active profile in the overview | Included, same pipeline |
| D6 | Menu entry visibility | Admin and at least 2 profiles |
| D7 | Scratch location | `%LOCALAPPDATA%\MedReminder\tmp\` (not `%TEMP%`) |

## 10. Implementation plan

| Step | Content | Effort |
|---|---|---|
| G1 | `SetRole`, `ChangeProfileRole`, `ProfilesManagerForm` button and dialog, strings, tests | S |
| I1 | Non-pooled scratch connection, `ProfileOverviewReader`, `ConsolidatedOverviewQuery`, tests | M |
| I2 | `ConsolidatedOverviewForm`, menu entry, switch reuse, strings | M |
| D | User guides (5), ANALYSIS/STATUS/EVOLUTION, change log | S |

G1 and I1 are independent; each step is one PR and leaves the app
working. Implementation brief:
[`docs/prompt/PROMPT-MULTI-USER-ROLES-OVERVIEW.md`](../prompt/PROMPT-MULTI-USER-ROLES-OVERVIEW.md).

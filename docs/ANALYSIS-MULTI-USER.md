# ANALYSIS — Increment 15: Multi-user support (case B)

Design document, **prior** to implementation. Once approved, work
proceeds with sub-increments 15a…15e.

> **This is not a speculative analysis.** Every decision here is
> already technically motivated and delimits what will actually be
> written in code. The "Decisions still to confirm" sections at the
> end are the only remaining zones of ambiguity that require your
> input.

---

## 1. Scope

### 1.1 What "multi-user case B" means

A single physical person manages medicines for **multiple people**
from the same Windows account. Examples:

- Parent/caregiver handling therapies for 2-3 children and themselves.
- Adult managing both their own medicines and those of an elderly
  parent.
- Home caregiver looking after 2-4 patients from the same PC.

**Expected number of profiles**: 2-5 (per your input). The design
scales up to ~50 without refactoring, but the UI is optimized for
2-5.

### 1.1a Roles: admin vs user

Introduced per your input (§14 point A). Two levels:

- **admin**: manages the **global** application settings (SMTP,
  automatic backup folder and time, profile management — create /
  rename / delete / PIN); also has full access to the DB of their
  own profile like any user.
- **user**: manages only their own profile (medicines, stock,
  therapies, doctor's card, personal email recipient). Does NOT see
  the Email SMTP tab in Settings, does NOT see "Manage profiles".

**Invariable rules**:
- There must always be **at least one admin profile**. The last
  admin in the system cannot be deleted or demoted to user
  (integrity rule).
- The **first profile** created by the first-run wizard is **admin
  by default**, with no explicit choice — it is the only one that
  can create the others.
- A profile's role is **immutable after creation** (in this
  increment). Changing the role would require a "promote / demote"
  flow that adds complexity: the promoter must be admin, and if
  they demote themselves they lose the right to redo it if they
  are the only admin, and so on. Deferred to a later increment if
  the need actually emerges.

The role is **soft security**, like the PIN — a user with
filesystem access can edit `profiles.json` by hand and become
admin. The UI honors the role, the filesystem does not. Explicit
tooltip in the profile-management UI.

### 1.2 What it is NOT

- **Not multi-tenant on a shared server.** The app stays
  offline-first as in spec §1. No sync across machines.
- **Not multi-Windows-user.** For that, a separate Windows account
  is enough (already supported: `%LOCALAPPDATA%` + mutex `Local\...`
  == per-session).
- **Not a security system.** The optional PIN (§8) is friction
  against accidental changes, not protection against unauthorized
  access.
- **No audit trail** of "who modified what" — there is only one
  Windows user by definition.

### 1.3 Functional goals

- **An admin** creates / renames / removes profiles (users) from the
  UI.
- Each profile has its **own database** and its **own email
  recipient**; the other settings (SMTP server, backup folder,
  backup time, retention) are **global, admin-managed**.
- Profile choice at startup when there is more than one.
- Profile switch at runtime without manually closing the app.
- Automatic, data-lossless migration from the current single-user
  schema (`medreminder.db` in `%LOCALAPPDATA%\MedReminder\`).
- The profile migrated from V1 becomes **admin** (it is the only
  existing one).

---

## 2. Data model

### 2.1 Decision: **one SQLite DB per profile, separate files**

Alternatives evaluated:

| Approach | Pros | Cons | Chosen |
|---|---|---|---|
| **DB per profile** (separate files) | Natural isolation; trivial switch (close/open path); per-profile backup / restore out of the box; no changes to Domain / EF Core | Minimal schema duplication; connection rebuilt on profile switch | ✅ |
| `UserId` column on every table | A single DB file | Every query must filter `WHERE UserId = @current`; risk of unfiltered-query bugs = data leaking between profiles; per-profile export / import becomes non-trivial; painful DB migration; contradicts the "Dad's DB belongs to Dad" idea | ❌ |
| One DB per profile with a shared schema via `ATTACH DATABASE` | Cross-profile queries possible | No current use case requires it; gratuitous complexity | ❌ |

**Zero changes** to `MedReminder.Domain.*`, `MedReminder.Application.*`,
`MedReminder.Infrastructure.Persistence.*`. Only the **connection
string** that the composition root passes to
`AddMedReminderInfrastructure` changes.

### 2.2 Profile registry

New file `%LOCALAPPDATA%\MedReminder\profiles.json`:

```json
{
  "SchemaVersion": 1,
  "ActiveProfileId": "default",
  "Profiles": [
    {
      "Id": "default",
      "DisplayName": "Mario Rossi",
      "Role": "admin",
      "CreatedAt": "2026-09-16T09:00:00Z",
      "LastUsedAt": "2026-09-16T18:30:00Z",
      "PinHash": null,
      "PinSalt": null,
      "PinIterations": 0
    },
    {
      "Id": "3a8c…",
      "DisplayName": "Grandma",
      "Role": "user",
      "CreatedAt": "2026-09-20T14:00:00Z",
      "LastUsedAt": "2026-09-25T09:12:00Z",
      "PinHash": "base64…",
      "PinSalt": "base64…",
      "PinIterations": 100000
    }
  ]
}
```

Rules:

- `Id` is generated with `Guid.NewGuid().ToString("N")`, immutable
  for the life of the profile (used as folder name).
- `DisplayName` is editable, not used as a key.
- `Role`: `"admin"` or `"user"`. Immutable after creation (§1.1a).
  Tolerant deserialization: unknown values → `"user"` (fail-safe:
  never accidentally promote).
- `ActiveProfileId`: optional hint about the last profile loaded,
  used by auto-start (§10). Not "the" runtime active profile.
- The registry lives **outside** the profiles — it is app metadata,
  not profile-specific.
- Atomic writes: tmp + `File.Move`, as already done for
  `backup.state.json`.
- Invariant enforced on every write: **at least one profile with
  `Role == "admin"`**. `ProfileRegistry.Delete()` refuses if it
  would delete the last admin.

### 2.3 Code-side abstraction

New interface in `MedReminder.Application.Abstractions`:

```csharp
public enum ProfileRole { User, Admin }

public sealed record Profile(
    string Id,
    string DisplayName,
    ProfileRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool HasPin);

public interface IProfileRegistry
{
    IReadOnlyList<Profile> ListProfiles();
    Profile? GetById(string id);
    string? ActiveProfileIdHint { get; }

    // The role is decided at creation. The first profile in the
    // system (empty registry) is forced to admin — Create ignores
    // the role parameter in that case and returns an admin profile.
    // Afterwards, only a caller with admin role can create admins
    // (checked higher up, not here — this layer does not know the
    // "who is calling").
    Profile Create(string displayName, ProfileRole role);

    void Rename(string id, string newDisplayName);

    // Refuses with InvalidOperationException if id is the last
    // admin in the registry (invariant §2.2). With deleteData=true
    // also removes the folder <root>\profiles\<id>\.
    void Delete(string id, bool deleteData);

    void SetActiveProfileHint(string id);

    void SetPin(string id, string pin);   // pin==null → clear
    bool VerifyPin(string id, string pin);
    bool HasPin(string id);
}
```

The implementation (`ProfileRegistry`) lives in
`MedReminder.Infrastructure.Profiles`. It reads / writes
`profiles.json`. It does not know about EF Core: it only handles the
configuration file + PIN hash (§8).

### 2.4 Profile currently loaded

A second piece, distinct from the registry:

```csharp
public interface ICurrentProfile
{
    string Id { get; }
    string DisplayName { get; }
    ProfileRole Role { get; }
    bool IsAdmin => Role == ProfileRole.Admin;

    string DataDirectory { get; }             // %LOCALAPPDATA%\MedReminder\profiles\<id>\
    string DatabasePath { get; }              // <DataDirectory>\medreminder.db
    string NotificationSettingsPath { get; }  // <DataDirectory>\notifications.settings.json (per-profile recipient)
}
```

Registered as `Singleton` in the DI container once the user has
picked a profile. Every service that currently hardcodes
`AppDataPaths.GetDatabasePath()` must be updated to inject
`ICurrentProfile`.

**Global paths** (not per-profile, admin-managed):
- `smtp.settings.json`, `smtp.protected` → `%LOCALAPPDATA%\MedReminder\`
- `backup.settings.json`, `backup.state.json` → `%LOCALAPPDATA%\MedReminder\`

They stay exposed by `AppDataPaths` as static methods (invariant:
a single path per machine + Windows user). The UI that accesses
them gates on `ICurrentProfile.IsAdmin` (§12).

### 2.5 Changes to `AppDataPaths`

The current static class in `MedReminder.Infrastructure.Storage`
exposes hardcoded methods (`GetDatabasePath`, `GetCredentialsPath`,
etc.). It must be refactored:

- Methods returning **app-level** paths (log dir, SMTP DPAPI
  credentials, `smtp.settings.json`, `backup.settings.json`,
  `backup.state.json`, `profiles.json`) → **stay static** (global
  admin-managed paths).
- `GetDatabasePath()` → **removed**; replaced by
  `ICurrentProfile.DatabasePath`.
- New `NotificationSettingsPath` as a member of `ICurrentProfile`
  (the only per-profile email piece is the recipient, §7.1).

Invasive but mechanical refactor. Impact per file:
- `MedReminderDbContext` (connection string): via `ICurrentProfile`.
- `BackupService.DatabasePath`: via `ICurrentProfile`.
- `MailKitEmailNotificationService`: reads `ToAddress` from a new
  `IProfileNotificationSettings` instead of `SmtpSettings.ToAddress`
  (which no longer exists — see §7.1).

---

## 3. On-disk layout

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                    ← registry (admin-managed)
├── smtp.settings.json               ← GLOBAL SMTP (admin-managed)
├── smtp.protected                   ← GLOBAL DPAPI password
├── backup.settings.json             ← GLOBAL backup config (admin-managed)
├── backup.state.json                ← GLOBAL last backup state
├── logs\
│   ├── medreminder-20260916.log
│   └── …
└── profiles\
    ├── default\                     ← admin (example after V1→V2 migration)
    │   ├── medreminder.db
    │   ├── medreminder.db-wal
    │   ├── medreminder.db-shm
    │   └── notifications.settings.json    ← profile's "ToAddress"
    └── 3a8c…\                       ← user
        ├── medreminder.db
        └── notifications.settings.json
```

**Rationale**:

- **Global SMTP**: a single SMTP server account configured by the
  admin (one Gmail App Password). Every profile sends through the
  same server with the same sender but a different recipient (§7.1).
- **Global backup**: folder and time decided by the admin. The
  automatic daily backup backs up **every** profile DB in one shot
  (§11).
- **Per-profile notifications**: `notifications.settings.json`
  contains only `ToAddress` (who receives this profile's emails).
  It is the only thing a non-admin user can change without being
  admin.
- The `.log` files stay centralized.
- `Delete()` with `deleteData=false` only removes the entry from
  the registry; the folder stays on disk (manual recovery still
  possible). With `deleteData=true` it also removes the folder.

---

## 4. Boot flow

### 4.1 Full sequence

```
                    ┌──────────────────────────┐
                    │ Program.Main             │
                    │  (mutex, Serilog, ...)   │
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │ MigrationV1toV2          │  ← §5
                    │  if registry missing and │
                    │  medreminder.db exists   │
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │ IProfileRegistry.List()  │
                    └────────────┬─────────────┘
                                 │
             ┌───────────────────┼────────────────────┐
             │                   │                    │
     count==0│         count==1  │           count>1  │
             ▼                   ▼                    ▼
    ┌────────────────┐   ┌────────────────┐   ┌────────────────┐
    │ "Create first  │   │ Auto-select    │   │ ProfilePickerDlg
    │ profile"       │   │ the only prof. │   │ + optional PIN  │
    │ wizard         │   │                │   │                 │
    └────────┬───────┘   └────────┬───────┘   └────────┬───────┘
             │                    │                     │
             └────────────────────┴─────────────────────┘
                                  │
                    ┌─────────────▼──────────────┐
                    │ SetCurrentProfile(chosen)  │
                    │ Registry.SetActiveHint()   │
                    │ BuildHost(profile)         │
                    │ InitializeDatabase(profile)│
                    │ StartAsync + RunUi         │
                    └────────────────────────────┘
```

### 4.2 Auto-start (`--minimized`)

If the app is launched with `--minimized` (Windows auto-start) it
**does not** show the picker. It automatically selects the profile
pointed to by `ActiveProfileIdHint` in the registry (or the only
one if there is a single profile).

Reason: during Windows auto-login, showing a dialog is unpleasant.
The user can change profile from the menu once the app is open.

If the hinted profile has a PIN, it is requested (the app starts
minimized, the dialog comes to the foreground).

### 4.3 Optional `--profile <id>` flag

Command-line argument that explicitly selects a profile, bypassing
the picker. Useful for creating separate Windows shortcuts per
profile (e.g. "MedReminder — Grandma" on the desktop). The main
auto-start uses `--minimized` without `--profile` (it uses the
hint).

---

## 5. Migration from single-user

### 5.1 Detection

`MigrationV1toV2` is idempotent. It runs **only if**:

- `profiles.json` **does not exist**, AND
- `%LOCALAPPDATA%\MedReminder\medreminder.db` **exists**.

If either condition is false → skip.

### 5.2 Sequence

1. **Mandatory pre-backup.** Before moving any file, copy
   `medreminder.db` (and `.wal` / `.shm` files if present) to
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   — self-describing name, never overwritten.
2. Create `profiles\default\` (id is always the literal string
   `default` for migration; later profiles will use Guids).
3. `File.Move` on `medreminder.db`, `.db-wal`, `.db-shm` into
   `profiles\default\`.
4. `smtp.settings.json`, `smtp.protected`, `backup.settings.json`,
   `backup.state.json`: **not moved**. They were already at the app
   level in V1 (`%LOCALAPPDATA%\MedReminder\`) and remain so in V2
   — they are global settings. Nothing to do.
5. If `ToAddress` needs to be extracted from the old
   `smtp.settings.json` and moved to the per-profile
   `notifications.settings.json`:
   - Read the old JSON, if `Smtp.ToAddress` is set, write
     `profiles\default\notifications.settings.json` with
     `{ "Notifications": { "ToAddress": "<...>" } }`.
   - Remove `ToAddress` from the old JSON by rewriting it (the
     other keys stay; if they are also present in
     `appsettings.json` no problem).
6. Write `profiles.json` with a single `default` entry,
   `DisplayName = "User"` (immediately renameable from Settings),
   `Role: "admin"` (§1.1a), `ActiveProfileId = "default"`.
7. If any step from 2 to 6 fails: **rollback** — restore
   `smtp.settings.json` from the pre-backup, move the DB back,
   delete the half-created `profiles/default` folder, delete
   `profiles.json`. The user restarts at the next boot with the
   pre-migration state fully intact.

### 5.3 DPAPI

`smtp.protected` stays at the app level: same
`DataProtectionScope.CurrentUser`, nothing to touch at the crypto
level. No regression.

### 5.4 Tests

An integration test builds a fake V1 tree in `Path.GetTempPath()`,
invokes the migrator, verifies the V2 layout and that no V1 file is
left over.

---

## 6. Profile switching

### 6.1 Decision: **guided app restart**

Alternatives evaluated:

| Approach | Pros | Cons | Chosen |
|---|---|---|---|
| **Restart the exe** (as we already do after DB restore) | Simple; guarantees zero shared state; the pattern already exists (`IApplicationRestarter`) | Loss of window state and any open dialogs | ✅ |
| Hot-swap the DbContext scope | No restart | Requires stopping / restarting every hosted service, disposing the SQLite pool, rebuilding the `IHost` while keeping the WinForms message loop alive; many silent-failure points | ❌ |
| Multi-app (one window per profile) | Maximum isolation | Breaks the single-instance mutex pattern, complicates the tray | ❌ |

**Flow**:

1. User: `File → Change profile` → picker dialog.
2. Confirm → call `IProfileRegistry.SetActiveProfileHint(newId)`.
3. Call `IApplicationRestarter.RestartAndExit()`, which relaunches
   the exe. The new process reads the hint and opens the new
   profile without showing the picker.

Full reuse of the restart infrastructure already in place for
backup restore (Increment 11).

### 6.2 Auto-start conflict

If the old Windows auto-start was registered with `--minimized`,
the post-switch restart must NOT start minimized — the user has
just actively picked a profile. `IApplicationRestarter` does not
pass `--minimized`.

---

## 7. Settings — global vs per-profile

### 7.1 SMTP: global (admin) + per-profile recipient

**Split of the old `SmtpSettings`:**

- `SmtpSettings` (global in
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`, admin-managed):
  `Host`, `Port`, `UseStartTls`, `Username`, `FromAddress`,
  `FromDisplayName`, `TimeoutSeconds`. **Does not contain
  `ToAddress` anymore.**
- `NotificationSettings` (per-profile in
  `<DataDirectory>\notifications.settings.json`): `ToAddress`. New
  POCO, bound via `IOptionsMonitor<NotificationSettings>`.
- `smtp.protected` (DPAPI, global in
  `%LOCALAPPDATA%\MedReminder\`): password / App Password of
  `Username` — single for all profiles.

**Email sending flow**:
1. `MailKitEmailNotificationService` takes `SmtpSettings` (global,
   `IOptionsMonitor<SmtpSettings>`) and `NotificationSettings`
   (per-profile, `IOptionsMonitor<NotificationSettings>`).
2. Builds the email `From` from `FromAddress`, `To` from
   `ToAddress` of the active profile.
3. Authenticates with global `Username` + DPAPI password.

**Program.cs** changes:

```csharp
var appDataDir = AppDataPaths.GetAppDataDirectory();
var current = /* CurrentProfile chosen in the boot flow */;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    // Global admin-managed:
    .AddJsonFile(Path.Combine(appDataDir, "smtp.settings.json"),
                 optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(appDataDir, "backup.settings.json"),
                 optional: true, reloadOnChange: true)
    // Per-profile (recipient only):
    .AddJsonFile(current.NotificationSettingsPath,
                 optional: true, reloadOnChange: true);
```

### 7.2 Backup: global (admin)

`backup.settings.json` and `backup.state.json` stay at app level.
`AutomaticBackupHostedService` reads the global config and backs up
**every profile DB** on each successful tick (§11).

In the SettingsDialog the admin sets one folder and one time, valid
for everyone.

### 7.3 Windows auto-start

**Single** (per your input §14 point B). The
`HKCU\...\Run` entry is unique per machine + Windows user. At
login the `ActiveProfileIdHint` profile starts.

### 7.4 Admin / user gating in the SettingsDialog

The **Email SMTP** tab: visible only if `ICurrentProfile.IsAdmin`.
A non-admin user will see only the (new) "**Notifications**" entry
with a single `ToAddress` field — enough to personalize where
their emails are received without touching the shared SMTP server.

The **Backup** tab: visible only if `IsAdmin`. A non-admin user
does not see backup (nor "Run now" — the global backup already
includes their DB).

The **Auto-start** tab: visible for everyone, but for a user the
behavior is unchanged (the Windows `Run` flag is toggled — which
is per Windows account, not per profile).

---

## 8. Optional PIN

### 8.1 What it IS

- Each profile may optionally have a 4-8-digit numeric PIN.
- At startup, if the chosen profile has a PIN, it is requested
  before the IHost boots.
- On failure (3 wrong attempts): "Wrong PIN" dialog and the app
  exits (or returns to the picker if there are other profiles).

### 8.2 What it is NOT

**The PIN is friction, not security.** It lives (hashed) next to
the DB on the same disk of the Windows user. Anyone with
filesystem access can bypass it by opening the `.db` file
directly (which is NOT encrypted — it is plain SQLite).

The UI tooltip must be explicit: *"The PIN prevents accidental
switches between profiles. It does not protect the content:
anyone with access to the PC can read the database without a
PIN."*

Real alternatives:
- **SQLite encryption** (SQLCipher, .NET Encrypted): would
  require a commercial package or OSS SQLCipher; not addressed
  here.
- **BitLocker / EFS**: responsibility of the user / OS, not of
  the app.

### 8.3 Hash storage

- Algorithm: **PBKDF2** with HMAC-SHA256, 100,000 iterations.
- Salt: 16 bytes from `RandomNumberGenerator.Fill`.
- Saved base64 in the registry: `PinHash`, `PinSalt`,
  `PinIterations`.
- `IProfileRegistry.SetPin(id, pin=null)` clears the PIN.

No persistent rate limit (an attacker with registry access can
reset all state): the rate limit is only in-memory in the UI
session.

---

## 9. Multi-profile notifications

### 9.1 Scope

**Only the active profile emits notifications** (Windows toast +
email).

Rationale:
- The monitor scheduler runs inside the IHost of the active
  profile. A single process, a single IHost.
- Monitoring N profiles in parallel from the same process would
  require N EF Core contexts, N schedulings, N email channels.
  Gratuitous complexity: anyone who needs 24/7 monitoring of
  multiple profiles keeps the app open on the right profile or
  switches.

### 9.2 Fallback: "inactive-profile" notification

**Additional idea**: at boot, if profiles other than the active
one have a backup file older than 2 days, show a badge on the
`File → Change profile` menu as a visual signal. Do NOT emit
toasts / emails from inactive profiles. To be evaluated in a
follow-up increment — it does not block Increment 15.

---

## 10. Auto-start and single-instance

### 10.1 Mutex

The mutex `Local\MedReminder.SingleInstance.<guid>` stays **single
per machine + Windows user**, independent of the profile.
Rationale: two instances of the same process accessing two
different DBs would work technically, but:
- Duplicates the tray icons.
- Duplicates the hosted services.
- Confuses the user ("where am I? which profile?").

One profile at a time, period.

### 10.2 Post-switch restart

`IApplicationRestarter.RestartAndExit()` releases the mutex; the
new process acquires it with the spin already introduced
(Increment 11).

---

## 11. Automatic backup

### 11.1 GLOBAL backup covering every profile

**Substantial change** vs. Increment 11:
`AutomaticBackupHostedService` no longer backs up only the active
profile's DB — it backs up **every** DB in the system on a single
successful tick.

Each tick:
1. Reads `IProfileRegistry.ListProfiles()` to get the list.
2. For each profile, checks that `medreminder.db` exists in the
   profile folder.
3. Calls `IBackupService.ExportProfileAsync(profileId, global
   folder, ct)` for each one.
4. File naming: `medreminder-<profileId>-YYYYMMDD-HHmmss.db` to
   distinguish backups of different profiles in the same folder.
   With `profileId == "default"` it becomes
   `medreminder-default-...`.
5. Retention applied **per profile** within the same folder: the
   `PruneOldBackupsAsync` regex is extended to capture the
   `profileId`, and the "older than N days" filter is applied to
   the files of **that** `profileId` separately. The most recent
   backup of profile A does not "protect" the old backups of
   profile B.

### 11.2 Impact on `BackupService`

`IBackupService` signature changes:

```csharp
// BEFORE
Task<string> ExportAsync(string destinationDirectory, CancellationToken ct);
Task<int> PruneOldBackupsAsync(string dir, int retentionDays, CancellationToken ct);
Task ImportAsync(string sourceFilePath, CancellationToken ct);

// AFTER
Task<string> ExportProfileAsync(string profileId, string destinationDirectory, CancellationToken ct);
Task<int> PruneOldBackupsAsync(string dir, int retentionDays, CancellationToken ct);
Task ImportProfileAsync(string profileId, string sourceFilePath, CancellationToken ct);
```

`ExportProfileAsync` opens a `SqliteConnection` on the specific
profile's DB using the path
`<root>\profiles\<profileId>\medreminder.db`. It does NOT use
`ICurrentProfile` — it must be able to back up inactive profiles
too.

`ImportProfileAsync` needs `profileId` to know which DB to
replace. If the imported profile is the active one, it must still
be followed by `RestartAndExit` (SQLite lock on the current DB).

### 11.3 Restore behavior from the UI

In the `SettingsDialog` (admin only, Backup tab) the "Restore
backup…" action:
1. FileDialog to pick the .db.
2. Dropdown "Restore into profile…" with the profile list
   (default = the active one).
3. Double confirmation.
4. `ImportProfileAsync(picked)` + `RestartAndExit` if picked ==
   active, otherwise only a confirmation dialog "Done".

For a non-admin user: the entry does not appear (§7.4).

### 11.4 Benefit of a global backup

The "profile Grandma has not been opened for 3 months" case (from
the original section) **is no longer a problem**: the global
backup runs in the admin's process (or in any active profile's
process) and covers inactive profiles too. A single
`AutomaticBackupHostedService` per machine does everyone's work.

**With one caveat**: if the admin never launches the app and
only a user does, who runs the backup? Answer: **a non-admin
user's process runs the global backup too**. The "only admin
sees the Backup tab" gating is UI-only: it does not prevent the
backend service from running. The automatic backup is for
everyone, not for admins.

---

## 12. UI

### 12.1 New elements

- **ProfilePickerForm** (new). Shown at boot when there is more
  than one profile. Profile list with name + role badge
  (admin / user) + last-used date `dd/MM HH:mm`. Single "Open"
  button — profile management is inside the app.
- **PinPromptForm** (new). Small dialog with a
  `UseSystemPasswordChar = true` TextBox, in-memory attempts
  counter.
- **ProfilesManagerForm** (new, **admin only**). Opened from
  `Tools → Manage profiles…`. CRUD on profiles (create as user
  or admin, rename, delete with double confirmation, set /
  change / remove PIN). Refuses the deletion of the last admin
  (§2.2).
- **`File → Change profile…`** (**everyone**): opens
  ProfilePickerForm.
- **`Tools → Manage profiles…`** (**admin only**, hidden for
  user).
- **StatusStrip**: added label "Profile: Grandma (admin)" on the
  left. The admin badge is distinct.
- **Title bar**: `MedReminder — Grandma` (profile name appended,
  no badge).

### 12.2 Menu / toolbar gating by role

MenuStrip construction in MainForm has to be refactored to take
`ICurrentProfile` as input:

| Menu entry | admin | user |
|---|---|---|
| File → Change profile… | ✔ | ✔ |
| File → Exit | ✔ | ✔ |
| Therapy → * | ✔ | ✔ |
| Stock → * | ✔ | ✔ |
| Tools → Check now | ✔ | ✔ |
| Tools → Manage profiles… | ✔ | ✘ (hidden) |
| Tools → Settings… | ✔ (every tab) | ✔ (only Notifications + Auto-start) |
| ? → * | ✔ | ✔ |

The quick-access toolbar does not change — those are "on the
medicine" actions, available to every role.

### 12.3 First-run wizard

When the profile list is empty (clean install, no migration):

1. Dialog "Welcome! Create the first profile (administrator)."
2. Input: name (required, e.g. "Mario Rossi").
3. Explanatory text: "The first profile is the administrator. It
   will be able to create other user profiles and manage the
   global SMTP and Backup settings. The role is not editable
   after creation."
4. Optional: set a PIN now or later (recommended for admin, with
   a non-forcing textual notice).
5. Creates the profile with `Role = admin`, skips the picker,
   goes directly in.

Coordinates with §14 point E — the wizard is mandatory (you
cannot skip it without creating at least the admin profile), but
the PIN choice inside it stays skippable.

### 12.4 Creating additional profiles (admin only)

Inside ProfilesManagerForm:
1. "New profile" button.
2. Input dialog: name + radio "admin" / "user" (default user).
3. Explanatory text on the role — "a user only sees the
   medicines; cannot change SMTP / Backup".
4. The PIN can be set immediately afterwards or left empty.

### 12.5 Visual feedback

In the non-admin user's UI, add a permanent note at the bottom of
the SettingsDialog (Notifications tab):
*"To change the SMTP account or the backup configuration, please
ask the profile administrator."*

---

## 13. Risks and mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| V1→V2 migration corrupts the DB | Low | High (data loss) | Mandatory pre-backup (§5.2 step 1); rollback on error (§5.2 step 7); integration test (§5.4) |
| User deletes a profile by mistake | Medium | High | Double confirmation "Type the profile name to confirm"; "also delete data on disk" option defaults OFF (data preserved on disk even after removal from the registry) |
| Last admin deleted | Low | Very high (loss of access to global features) | `IProfileRegistry.Delete` refuses with an exception if the entry is the only admin. The UI shows the Delete button disabled with an explanatory tooltip. |
| Forgotten PIN | Medium | Low-Medium | Recovery: `PinHash` can be removed from the registry manually. Documented in USER_GUIDE. It is not security, so recovery is not a bug. |
| Admin without PIN → a user can self-promote | Medium | Medium (loss of UI gating) | The role is immutable via API. Self-promotion requires manual editing of `profiles.json`, as stated in §1.1a. The UI recommends "set a PIN for admin" in the first-run wizard. |
| Auto-start opens the wrong profile | Low | Medium | `ActiveProfileIdHint` is updated on every switch and on every clean app shutdown |
| Race condition between picker and other instances | Very low | Low | Mutex acquisition already in place + 5s spin (Increment 11) |
| Restore of a backup into the wrong profile | Medium | High (data mix between profiles) | The file name `medreminder-<profileId>-YYYYMMDD-HHmmss.db` includes the original profileId (§11.1); the restore dialog offers the destination profile with the default = the one from the filename; double confirmation. |
| Global backup runs while a user is active | Certain | Low (none; desirable) | Documented: the service is per-machine, not per-role. |

---

## 14. Confirmed decisions (after user review)

**A. SMTP: server and credentials GLOBAL, recipient per-profile.**
An admin configures the SMTP account once; each profile can
customize its own `ToAddress`. Admin / user roles introduced
(§1.1a). Impact: split `SmtpSettings` (global) +
`NotificationSettings` (per-profile, holds only `ToAddress`).

**B. Windows auto-start: single.**
Starts `ActiveProfileIdHint`.

**C. PIN on `Change profile`: only on entry.**
No PIN requested to leave the current profile.

**D. "Last used" format in the picker: `dd/MM HH:mm`.**

**E. Mandatory first-run wizard.** Not skippable, because without
a profile there is nothing to show. The first profile is **admin
by default** (§1.1a) — consistent with the admin / user model.
The PIN choice inside it stays skippable (with a "recommended
for admin" notice).

**F. Pre-migration backup: manual cleanup.** The migrator never
touches `%LOCALAPPDATA%\MedReminder\backups\pre-migration-*\`.
Documented in USER_GUIDE.

## 14a. Confirmed decisions (after user review, second round)

**G. Role immutable after creation.** No promote / demote flow in
Increment 15. If the practical need arises, it will be considered
in a later increment.

**H. The active profile cannot be deleted.** To delete their own
profile, the admin must first switch to another profile (via
`File → Change profile…`) and then delete. The
ProfilesManagerForm shows the Delete button disabled for the
active profile row, with an explanatory tooltip.

**I. One profile at a time.** The admin in their own profile
only sees their own medicines. To operate on another profile's
data they must switch profile. No cross-profile consolidated
view.

**J. SMTP change propagation: solved by the single-instance
invariant.** Two concurrent instances of the app cannot exist in
the same Windows account (mutex `Local\` + same session). No
race conflict: when the admin changes SMTP and, at a later time,
a user launches the app, the updated JSON file is read at the
next boot. `IOptionsMonitor` is not even needed for this case —
the read at boot is enough. Confirmed correct.

**K. Windows / Email notification channels: already per-medicine.**
No additional gating required: `Medicine.NotificationChannels` is
already a per-medicine flag, defined by the record creator.

## 14b. Compact recap

For quick reference, all decisions:

| ID | Question | Choice |
|----|---------|--------|
| A  | SMTP: per-profile / shared | Shared + admin / user roles |
| B  | Auto-start single / multi | Single |
| C  | PIN when leaving a profile | Only on entry |
| D  | "Last used" format | `dd/MM HH:mm` |
| E  | First-run wizard | Mandatory (creates admin) |
| F  | Pre-migration backup retention | Manual |
| G  | Role editable after creation | Immutable |
| H  | Delete active profile | No, switch first |
| I  | Admin cross-profile view | No, one at a time |
| J  | SMTP propagation between instances | N/A (single instance) |
| K  | Per-profile notification-channel gating | Not needed (already per-medicine) |

---

## 15. Implementation plan

Five sequential sub-increments, each self-contained and
committable. Every sub-increment leaves the app **working** (no
branch death-march). Changes from the original design are marked
as **[+admin]**.

### 15a — Profile registry + `ICurrentProfile` abstraction

- New types: `ProfileRole` **[+admin]**, `Profile` with Role
  **[+admin]**, `IProfileRegistry`, `ProfileRegistry`,
  `ICurrentProfile` with `IsAdmin` **[+admin]**, `CurrentProfile`.
- New `NotificationSettings` (POCO with just `ToAddress`)
  **[+admin]**.
- `AppDataPaths` refactor: `GetDatabasePath()` removed (moved to
  `ICurrentProfile`); the rest stays static.
- `SmtpSettings` refactor: `ToAddress` removed **[+admin]**.
- Registry file-write only — no `Program.cs` integration yet. The
  "at least one admin" invariant is active.
- Unit tests: create / rename / delete, atomic save, PIN set /
  verify, immutable role, delete-last-admin refused.

**Deliverable:** code, but the app is still single-user (the
registry is not used). Verification: `dotnet test`.

### 15b — V1→V2 migration

- `MigrationV1toV2` class in Infrastructure.
- `ToAddress` extraction from the old `smtp.settings.json` into
  the new `notifications.settings.json` of the `default` profile
  **[+admin]**.
- The `default` profile is created with `Role = admin`
  **[+admin]**.
- Integration test on a temporary directory.
- Still no boot-flow integration.

**Deliverable:** migrator tested but dormant.

### 15c — Boot flow + ProfilePickerForm + first-run wizard (admin only)

- `Program.Main` refactored: migrator invocation, picker,
  first-run wizard that creates an admin **[+admin]**, profile
  selection, `BuildHost(currentProfile)`.
- `ICurrentProfile` passed as `Singleton` to the DI container.
- `AddMedReminderInfrastructure` accepts `ICurrentProfile` and
  uses its `DatabasePath` for the SQLite connection string.
  Global paths (`smtp.settings.json`, `backup.settings.json`,
  `backup.state.json`) stay in `AppDataPaths` static **[+admin]**.
- Refactor `MailKitEmailNotificationService` to use
  `IOptionsMonitor<NotificationSettings>` for `To` **[+admin]**.
- `AutomaticBackupHostedService` refactored to back up **every**
  profile on a successful tick **[+admin]**.

**Deliverable:** multi-user working end-to-end, admin / user role
exists but the UI is not gated yet.

### 15d — ProfilesManagerForm + admin / user UI gating + multi-profile restore

- ProfilesManagerForm (admin only): profile CRUD with role
  radio, PIN change **[+admin]**.
- `Tools → Manage profiles…` (admin only, hidden for user)
  **[+admin]**.
- SettingsDialog: Email / Backup tabs hidden for user; new
  Notifications tab visible to everyone **[+admin]**.
- StatusStrip shows "Profile: Grandma (admin)" with role badge
  **[+admin]**.
- Backup restore from the SettingsDialog offers a "Restore into
  profile" dropdown **[+admin]** (§11.3).
- Profile switch via `IApplicationRestarter` (reuse of Increment
  11).

**Deliverable:** complete admin / user UX.

### 15e — PIN + polish + documentation

- PinPromptForm.
- 3-attempts in memory.
- Explanatory tooltips ("the PIN is friction, not security", "the
  role is immutable", "a user only sees their own profile").
- Extended `USER_GUIDE.md` with the multi-profile + admin / user
  section **[+admin]**.
- `ANALYSIS-MULTI-USER.md` marked as "implemented" at the
  bottom.

**Deliverable:** feature complete and documented.

---

## 16. Estimated effort

As broadly indicated in the original evolution plan: **L
(large)**. The introduction of the admin role (§14 point A)
shifts the estimate **upward** from the initial values:

| Increment | New files | Modified files | Effort (updated) |
|---|---|---|---|
| 15a | ~7 | ~2 (AppDataPaths + SmtpSettings) | M |
| 15b | 1 | 0 | S |
| 15c | 0 | ~13 (MailKit, backup service, program.cs) | L |
| 15d | ~4 | ~4 (SettingsDialog gating, MenuStrip gating, StatusStrip) | M/L |
| 15e | ~2 | ~3 | S |

With the split into sub-increments, every push is small and
testable. If something goes wrong in 15c (the largest), the diff
is still limited to the composition root + DI refactor + backup
service.

**Non-goals for Increment 15**: profile promote / demote (§14a
point G), consolidated admin view (§14a point I). They go into
Increment 15+ if the need emerges.

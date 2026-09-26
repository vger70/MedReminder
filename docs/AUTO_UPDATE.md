# Automatic Update — Design Proposal

Status: proposal, not implemented.
Scope: replace the passive "new version available" notice with an optional, policy-driven automatic update.

---

## 1. Current State

- `IUpdateChecker` (Application) / `GitHubUpdateChecker` (Infrastructure) query
  `https://api.github.com/repos/vger70/MedReminder/releases/latest`.
- The check runs on startup (opt-in via `UserSettings.CheckForUpdatesOnStartup`) and on demand from the About dialog.
- Nothing is downloaded or executed. The user upgrades manually from the release page.
- Distribution channels (see `docs/PACKAGING.md`):
  - `MedReminder-win-x64.zip` — official, extracted to any folder chosen by the user.
  - MSI — optional, WiX, per-user scope, no UAC.
  - MSIX — not used.

---

## 2. Goals and Non-Goals

Goals:

- An admin profile selects an update policy that applies to the whole installation.
- At most one update check per day.
- The new version is downloaded, verified and installed without manual file handling.
- A failed update never leaves the application unusable and never loses data.

Non-goals:

- Prerelease or beta channels.
- Delta patching as a hard requirement (acceptable if the chosen tool provides it).
- Updating the .NET runtime itself.

---

## 3. Update Policy

The policy is stored in the shared `user.settings.json` (it applies to every profile) and is editable only when `CurrentProfile.IsAdmin` is true.

| Policy | Daily check | Download | Install | User interaction |
|---|---|---|---|---|
| `Automatic` | Yes | Background | On application exit or next start | None; a short notice after the update |
| `Confirm` | Yes | Background, after admin approval | On application exit or next start | Prompt shown when an admin profile is active |
| `NotifyOnly` | Yes | No | No | Current behaviour: notice with release link |
| `Off` | No | No | No | None; manual check from About remains available |

Notes:

- `NotifyOnly` preserves today's behaviour and is the default after upgrade, so existing installations do not change behaviour silently.
- `Confirm`: if no admin profile is opened within 7 days of detection, non-admin profiles see the `NotifyOnly` notice as a fallback, so a pending update is never invisible indefinitely.
- `CheckForUpdatesOnStartup` is migrated into the new policy (`true` -> `NotifyOnly`, `false` -> `Off`) by an idempotent settings patch.

### Daily check

- A hosted service stores `LastUpdateCheckUtc` in the shared settings and runs the check when more than 24 hours have elapsed, at startup and then periodically while the app is running in the tray.
- A random delay (0–30 minutes) after startup avoids adding load to the application start.
- GitHub anonymous rate limit (60 requests/hour per IP) is not a concern at this frequency.

---

## 4. Installation Timing

MedReminder is a tray application that shows reminders. The updater must never interrupt it at an arbitrary time.

- The package is downloaded and verified in the background.
- The swap happens only:
  - when the user exits the application, or
  - at the next start, before the main form and the hosted services start.
- The application is never killed to apply an update. No update is applied while a reminder dialog is open.
- After the swap the application restarts with the same arguments (including `--minimized` for auto-start).

---

## 5. Technical Constraints

### 5.1 Running executable

A running executable cannot overwrite itself. A separate process must wait for the application to exit, replace the files and restart it. The process must cooperate with the single-instance mutex: it waits for the mutex to be released and never forces it.

### 5.2 Install location and write access

- ZIP: the user may extract it to any folder, including read-only or protected locations. Writing there also conflicts with the rule "never write outside `%LOCALAPPDATA%\MedReminder\`".
- MSI: replacing files installed by an MSI outside of Windows Installer breaks repair and uninstall. An MSI installation must be upgraded by running the new MSI (`msiexec /i <new>.msi /qn`, relying on `MajorUpgrade`).

Consequence: automatic update requires an install location owned by the updater. Either the distribution changes (option A) or each channel keeps its own update path (option B).

### 5.3 Authenticity of the package

Automatic download and execution turns the GitHub account and release pipeline into a distribution point for every installation. In `Automatic` mode a compromised release would be installed without user action.

Requirements:

- The package must be verified against a key that is not stored next to the package. A SHA-256 checksum published in the same release is not sufficient.
- Accepted mechanisms (at least one):
  - Authenticode signature of `MedReminder.exe` and the update package, with the expected publisher checked by the updater.
  - Detached ed25519 signature, with the public key embedded in the application.
- Download only over HTTPS from `github.com` / `objects.githubusercontent.com`.
- Verification failure aborts the update, is logged and falls back to `NotifyOnly` for that version.

Code signing is listed as a future enhancement in `docs/PACKAGING.md`. For automatic update it becomes a prerequisite.

### 5.4 Database and rollback

- Boot patches migrate the SQLite schema forward only. A rollback to an older binary after the new version has started may find an incompatible schema.
- Before applying an update, the updater triggers the existing backup mechanism for every profile database.
- The previous application version is kept until the new version has started successfully once. If the new version fails to start, the updater restores the previous version and the pre-update backup.

### 5.5 Runtime compatibility

The framework-dependent package requires a specific .NET runtime. If a release raises the required runtime, an automatic update of a framework-dependent installation would fail to start. Options:

- Publish automatic updates only for the self-contained package, or
- Include the required runtime version in the release metadata and skip the update (fall back to `NotifyOnly`) when it is missing.

### 5.6 Privacy and logging

- No profile, medicine or personal data is sent during the check or the download.
- Logs record versions, outcome and error type only.

---

## 6. Implementation Options

### Option A — Velopack (recommended)

[Velopack](https://github.com/velopack/velopack) is an open-source (MIT) installer and update framework for .NET desktop applications, successor of Squirrel.Windows.

What it provides:

- Per-user installation without UAC, in a folder owned by the updater.
- Update feed read directly from GitHub Releases.
- Full and delta packages.
- Out-of-process file swap and restart, with hooks for first run after install and update.
- Support for Authenticode signing of the packages it produces.

Integration:

- `MedReminder.Infrastructure` gains an `IUpdateInstaller` adapter over the Velopack `UpdateManager`; the Application layer keeps the policy logic and stays free of Velopack types.
- The release workflow runs `vpk pack` after `dotnet publish` and uploads the Velopack assets next to the existing ZIP.
- `Program.Main` calls the Velopack bootstrap before the single-instance check.

Costs and risks:

- Distribution changes: the Velopack `Setup.exe` becomes the recommended channel. The plain ZIP can remain as a portable, non-updating package; the MSI becomes redundant.
- Existing ZIP/MSI installations need a one-time manual migration to the Velopack installer.
- The Velopack application id must not be `MedReminder`: Velopack installs under `%LOCALAPPDATA%\<id>\`, which would collide with the runtime data folder `%LOCALAPPDATA%\MedReminder\`. Use a distinct id (for example `MedReminder.App`) and verify that uninstall never touches the data folder.
- New dependency with its own release cadence.

### Option B — Custom updater

Keep ZIP and MSI and add a small updater executable.

- Download the release asset into `%LOCALAPPDATA%\MedReminder\updates\<version>\`.
- Verify the signature (section 5.3).
- ZIP installations: the updater waits for exit, backs up the current folder, extracts the new version, restarts. Only possible when the install folder is writable; otherwise fall back to `NotifyOnly`.
- MSI installations: run the new MSI silently; Windows Installer handles upgrade and rollback.
- The application must detect which channel it was installed from.

Costs and risks:

- Two update paths to build, test and maintain.
- File swap, rollback, locked files and partial extraction must be handled in project code.
- ZIP installations in protected folders cannot be updated.

### Comparison

| Aspect | Option A (Velopack) | Option B (custom) |
|---|---|---|
| Code to maintain | Small adapter | Updater executable + two install paths |
| File swap, restart, rollback | Provided | Custom |
| Delta updates | Yes | No |
| Existing ZIP/MSI channels | Replaced (ZIP kept as portable) | Kept |
| Migration of existing users | One-time reinstall | None |
| Signing requirement | Yes | Yes |

---

## 7. Proposed Architecture

```
UI
 └─ UpdateSettingsTab (admin only)       UpdatePrompt (Confirm policy)
App
 ├─ UpdatePolicy { Automatic, Confirm, NotifyOnly, Off }
 ├─ IUpdateChecker            (existing)
 ├─ IUpdateInstaller          (new: download, verify, stage, apply-on-exit)
 └─ UpdateCoordinator         (policy + daily schedule + state machine)
Infra
 ├─ GitHubUpdateChecker       (existing)
 └─ VelopackUpdateInstaller   (option A) | CustomUpdateInstaller (option B)
UI hosting
 └─ UpdateHostedService       (daily trigger, apply on exit)
```

State machine per detected version:

```
Detected -> (policy) -> Downloading -> Verified -> Staged -> Applying -> Applied
                             |             |                     |
                             v             v                     v
                          Failed       Rejected            RolledBack
```

`Failed`, `Rejected` and `RolledBack` are logged and fall back to the `NotifyOnly` notice for that version.

---

## 8. Localization

New UI strings (policy tab, confirm prompt, post-update notice, failure notice) must be added to every `assets/localization/strings.<lang>.json` file (en, it, fr, es, de). The user guides need a section describing the policies.

---

## 9. Test Plan

- Unit: policy transitions, daily schedule, settings migration from `CheckForUpdatesOnStartup`, version comparison (existing parser).
- Infrastructure: signature verification with valid, tampered and wrongly signed packages; download with fake `HttpMessageHandler`.
- Manual, on a clean Windows VM:
  - update from N-1 to N under each policy;
  - update while a reminder is open (must be deferred);
  - update with the new version failing to start (must roll back);
  - update of a framework-dependent install with a missing runtime (must fall back);
  - uninstall leaves `%LOCALAPPDATA%\MedReminder\` intact.

---

## 10. Prerequisites and Open Decisions

1. Code signing solution for release artifacts (section 5.3).
2. Option A or B.
3. Whether the framework-dependent package participates in automatic updates.
4. Fallback period for the `Confirm` policy (proposed: 7 days).

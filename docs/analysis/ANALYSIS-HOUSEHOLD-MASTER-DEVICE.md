# ANALYSIS — Household, master device and replicated installation settings

Design document, **prior** to implementation. It extends B.1
(`ANALYSIS-B1-MOBILE-SYNC.md`) from "one sync group per profile" to
"one installation spread over several devices", with one device acting
as master.

Status on 2026-09-29: revision 9 (§18 lists the revisions). Steps H0
to H3c are implemented; H3d is in progress (§13).

Where this document and `ANALYSIS-B1-MOBILE-SYNC.md` disagree on the
topics below, this document wins once approved. §16 lists the
corrections.

Reading conventions: `[VERIFIED]` (checked against the tree at commit
`7c26328`, v2.10.0), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (not verified). Untagged statements are design
proposals.

---

## 1. Requirements (product owner, 2026-09-28)

| # | Requirement |
|---|---|
| R1 | The first device on which MedReminder is installed (PC, later a phone) creates the installation and is its **master** |
| R2 | Every later device joins the installation and synchronizes with it: profile data **and** settings |
| R3 | Only an administrator can elect another device as master |
| R4 | The newly elected master inherits every setting of the outgoing master; the inherited settings are confirmed in a wizard on the new master |
| R5 | Only the master sends email, and only if SMTP is configured. A non-master device never sends email |
| R6 | An administrator on a non-master device can change the settings; the change applies to the installation |
| R7 | A user who is not an administrator can change only the settings that belong to them |
| R8 | Later, the first installation can be a phone instead of a PC |

---

## 2. Proposed changes to the requirements

| # | Change | Why |
|---|---|---|
| C1 | **The master is a role, not a sync hub.** Devices keep synchronizing peer to peer through the user's cloud storage, as today. "Synchronize with the master" means "converge on the same state", not "exchange data through the master device" | A hub stops every device when the master is off, and a phone or a sleeping PC cannot serve others. B.1 has no backend by design (`ANALYSIS-B1-MOBILE-SYNC.md` §1.2 point 4) `[VERIFIED]` |
| C2 | **Settings live in the installation, not in the master.** They are replicated continuously to every device. The master holds no private copy; "inherit" (R4) is then automatic, and the wizard confirms only what is bound to the device (SMTP connection test, cloud backup sign-in) | With R6, an admin edits settings on any device; the master cannot be their single owner |
| C3 | **The master also owns the scheduled cloud backup** (C.3+), not only email | Two devices writing snapshots of the same profile to the same account duplicate backups and fight over retention `[INFERRED]`. Local raw database backup stays per PC |
| C4 | **The master sends email for every profile of the installation**, not only for the profile open on it | Today only the active profile notifies (`ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` §2.2) `[VERIFIED]`. With R5, a profile never opened on the master would never get email |
| C5 | **A non-master device keeps user-initiated email through the user's mail client** (`mailto:`), not through SMTP | R5 would otherwise remove the prescription request from every non-master device. `MailtoLink` exists `[VERIFIED — Application/Prescriptions/MailtoLink.cs]` |
| C6 | **Planned handover leaves a gap rather than a duplicate**: the outgoing master stops sending as soon as it learns of the election; the new master starts after its wizard. A lease stops a master that has not synchronized for 24 h | Sync is eventually consistent; two masters for a while are otherwise unavoidable (§7.4) |
| C7 | **A device may hold a subset of the profiles** (a family member's phone holds only their profile), and cannot read the others. The master holds all of them (§4.4) | Profiles are often different people; one pairing should not hand every person's health data to every device. C4 needs every profile on the master |
| C8 | **Recommend a PC as master.** A phone may be master, with a warning | Background execution on phones is at the OS's discretion; email and backup timing become unreliable `[INFERRED — iOS BGAppRefreshTask is discretionary; spike S8 not run]` |

---

## 3. Current state (v2.10.0)

| Area | Behavior today `[VERIFIED]` |
|---|---|
| Sync unit | One group per profile (`SYNC-FORMAT.md` §2); opt-in per profile (`sync.settings.json`) |
| Replicated profile settings | Display name, `ToAddress`, `CaregiverAddress`, `DoctorAddress` (`ProfileSettingChanged`, operation schema 3) |
| Not replicated | Profile registry, role, PIN, SMTP, backup, `user.settings.json`, text size, tokens (B.1 §4.6, §15) |
| Email sender | Every device with SMTP configured; no designated mail device in code (B.1 §8.4 designed only) |
| Email deduplication | `NotificationCycle.ShouldNotify` checks the latest `NotificationEvent` of the medicine for the current stock epoch; `NotificationEvent` is device-local |
| Consequence | Two synced devices with SMTP both send the same low-stock email; a device that starts sending after another has sent re-sends for the current epoch `[INFERRED from the two rows above]` |
| Notifications scope | Only the profile open in the process |
| Automatic backup | Every profile of the registry, local raw copy and encrypted cloud snapshot, on each device where it is enabled (`UI/Hosting/AutomaticBackupHostedService.cs`) |
| Settings gating | Email, Startup (auto-start) and the automatic-backup part of Backup are admin-only; General (language, reference country, update check, text size) and Notifications (recipients) are open to every profile (`UI/Forms/SettingsDialog.cs`) |
| Single instance | Mutex `Local\MedReminder.SingleInstance.<guid>`, per Windows session (`ANALYSIS.md` §7) |
| Roles | `profiles.json`, UI-gated; no mutator for an existing profile's role; not a security boundary (`ANALYSIS.md` §10) |
| Join by passphrase | Tries every group of the storage, joins the first that opens (`SyncDialog.cs`) |
| Old apps and group listing | `ListGroupsAsync` keeps paths ending in `/group.json` |
| Sync engine | `ApplyRemoteOperations` is bound to the profile repositories (medicines, stock, intakes…) |

---

## 4. Model

### 4.1 Concepts

- **Household**: one installation spread over devices. It has an id, a
  household key, a list of profiles (each with its sync group), the
  installation settings, the device list and the master designation.
- **Household group**: a sync group of its own, holding household state
  only, never medicine data. Same envelope, cryptography, segments,
  images, pairing and lifecycle as a profile group (`SYNC-FORMAT.md`).
- **Profile group**: unchanged: one per profile, holds the profile's
  data and profile settings.
- **Device**: an installation of the app. Holds the household and a
  subset of the profiles (C7).
- **Master**: the single device that sends email and runs the
  scheduled cloud backup (R5, C3).

A lone device with no storage configured is a household of one: it is
the master, and nothing changes for users who never enable sync.

### 4.2 Topology

Unchanged from B.1: every device reads and writes the storage; no
device serves another (C1). The master matters only for side effects
that must happen once (email, cloud backup).

### 4.3 State classification and permissions

| State | Scope | Edited by | Effective on |
|---|---|---|---|
| SMTP transport | Household | Admin, any device | Master only |
| SMTP password | Household (secret, §5.4) | Admin, any device | Master only |
| Scheduled cloud backup: policy, storage account | Household | Admin, any device | Master only (C3) |
| Local raw database backup: enabled, folder, time, retention | Device | Admin on that device (as today) | That device |
| Reference country | Household | Admin (change: today any profile, D-14) | Every device |
| Language | Device | Any user on the device (as today) | That device |
| Update check on start | Device | Any user on the PC (as today) | That PC |
| Auto-start | Device | Admin on that PC (as today) | That PC |
| Profiles: list, removal | Household | Admin | Every device |
| Role of a profile | Household | Admin (the last admin cannot be demoted) | Every device |
| PIN of a profile | Household (hash) | Admin for any profile (Manage profiles); a user for their own (Settings) (as today) | Every device |
| Display name of a profile | Profile group (today) and household (§4.4) | Admin, from Manage profiles (as today) | Every device |
| Recipients (`To`, caregiver, doctor) | Profile group (today) | Each profile for itself (as today) | Master (email) |
| Medicine data | Profile group (today) | The profile's user; an admin | Every device holding it |
| Text size | Device × profile | The profile's user | That device |
| Device notifications (toast, dose reminder) per kind | Device | Any user on the device | That device |
| Profiles held by a device | Household | Admin | That device |
| Devices: pair, remove; join an installation | Household | Admin (change: today Tools → Sync… is open to every profile) | Every device |
| Master election | Household | Admin | Every device |
| Storage account sign-in, tokens, DPAPI blobs, paths | Device | — | Never replicated |

"Edited by" is enforced by the app on every device, as today (§8).
Rows marked "as today" keep the current gating; the others are new.
`backup.settings.json` holds both local and cloud fields today
`[VERIFIED — BackupSettings]`: the projection (§5.3) writes only the
cloud fields and never touches the local ones.

### 4.4 Profile subsets per device

A device holds only the profiles an admin granted to it. For a profile
not granted, the device:

- has no copy of its data and does not download its group;
- cannot decrypt its group: it never receives the profile key;
- shows it only by name and role, for an admin to manage it;
- raises no notification for it.

The barrier holds against the users of that device. It does not hold
against an admin, who can grant any profile to any device, nor against
anyone who knows the household passphrase (point 4), consistent with
§8. It does not hold against someone who extracts the keys from a
device that holds the profile; that is the threat model of B.1 §6.3.

Mechanism:

1. **Device key pair.** Each device creates a key pair when it joins
   the household and publishes the public key in its device record
   (encrypted with the household key). The private key stays in DPAPI
   or `SecureStorage`.
2. **Grant.** Giving profile P to device D: a device that holds P's
   key wraps it for D's public key and records `ProfileKeyGranted`.
   Only D can unwrap it.
3. **Revocation.** `ProfileKeyRevoked`, then a rotation of P's group
   key, granted again to the devices that keep P. D keeps what it had
   already downloaded and cannot read what is written afterwards (same
   limit as B.1 §6.2).
4. **Recovery key.** A recovery key pair, distinct from the household
   key: the public key is household state (`RecoveryKeyPublished`), the
   private key is wrapped with the household passphrase
   (`recovery.<v>.wrap`, §5.1). Every profile key is also recorded
   wrapped for the recovery public key (`ProfileKeyEscrowed`), so any
   device can escrow a new profile group without the passphrase
   (revision 7). Devices do not store the recovery private key. Whoever types the household passphrase can
   therefore open every profile, which is what an admin needs on a new
   device when no other device is at hand (§6.3 page 5), and what
   recovers the data when every device is lost. Without it, losing the
   devices that hold a profile would lose the profile. The household
   passphrase is therefore an **admin secret** (D-13).
5. **Master.** The master holds every profile (C4): an election grants
   the missing profiles to the target (§7.2).

Residual risk: every device holding the household key can write any
household operation and any device record, because a record is
authenticated with the shared household key only. A technically
capable user of a household device could publish a forged public key
for another device and receive the grants made for it afterwards; it
cannot read grants already made. The barrier therefore holds against
users acting through the app, not against a technical insider, the
same limit as roles (§8). Having the admin confirm a fingerprint of
the new device's public key at join would narrow it; left out of
scope.

Algorithm: ECDH on P-256 with HKDF-SHA256 and AES-GCM for the wrap.
`ECDiffieHellman` is available in .NET on Windows `[UNCERTAIN —
training knowledge, not exercised in this repository]`; on Android and
iOS `[UNCERTAIN — add to spike S1]`.

Profile display names are replicated in the household state as well as
in the profile group, so a device can list profiles it cannot read.
Name and role are the only profile information visible to every device
of the household.

---

## 5. Household group

### 5.1 Layout

```
<householdId>/
  household.json                cleartext: { "format": "MedReminder.Household", "formatVersion": 1, "householdId": "…" }
  key.<keyVersion>.wrap         household key wrapped with the household passphrase
  recovery.<keyVersion>.wrap    recovery key wrapped with the household passphrase (§4.4)
  genesis/<generation>.mrg      household image
  checkpoints/…, ops/…, devices/…, pairing/…   as SYNC-FORMAT.md §2
```

`household.json` replaces `group.json`, so apps up to 2.10, whose
`ListGroupsAsync` keeps `/group.json` paths only `[VERIFIED]`, never
offer the household as a profile group to join. The envelope and
header keep `formatVersion` 1.

### 5.2 Operations (household catalogue, schema version 1)

| Type | Payload | Merge |
|---|---|---|
| `ProfileRegistered` | `profileId`, `groupId`, `displayName`, `role`, `createdAt` | new profile; `displayName` LWW, mirrored from the profile group (§4.4) |
| `ProfileKeyGranted` | `deviceId`, `groupId`, `keyVersion`, `wrappedKey` (for the device's public key) | latest key version per device and group wins |
| `ProfileKeyRevoked` | `deviceId`, `groupId` | wins over earlier grants of that key version |
| `ProfileKeyEscrowed` | `groupId`, `keyVersion`, `wrappedKey` (for the recovery key) | latest key version wins |
| `ProfileRoleChanged` | `profileId`, `role` | LWW; refused on apply if it would leave no admin (§7.5 rule for concurrency) |
| `ProfilePinChanged` | `profileId`, `hash`, `salt`, `iterations` or `null` | LWW |
| `ProfileRemoved` | `profileId` | tombstone, wins |
| `HouseholdSettingChanged` | `setting`, `value` | LWW per setting |
| `MasterElected` | `electionId`, `deviceId`, `electedBy` (profile id), `kind` (`Planned`, `Takeover`) | LWW register `Master` |
| `MasterActivated` | `electionId` | valid only for the current election |

`HouseholdSettingChanged.setting` values: the `SmtpSettings` fields,
`SmtpPassword`, the cloud-backup fields of `BackupSettings` except the
local folder fields, `ReferenceCountry`. The operations are sealed in
segments with the household key, so the SMTP password never appears in
clear on the storage. Profile keys are additionally wrapped per device
(§4.4), so the household key alone does not open any profile.

### 5.3 Local store and projection

- `%LOCALAPPDATA%\MedReminder\household\household.db` (SQLite):
  operation log, registers, applied vectors. `household.settings.json`:
  household id, device id, generation, key version, storage.
  `household.protected`: the household key (DPAPI; `SecureStorage` on
  mobile).
- The existing files stay the local copies read by today's code:
  `smtp.settings.json`, `smtp.protected`, `backup.settings.json`,
  `user.settings.json` (reference country), `profiles.json` (roles, PIN
  hashes). After every apply, a projection writes the winners into
  them, the pattern of `ProfileSettingsProjection` `[VERIFIED]`.
  Consumers do not change.
- Every write of these settings goes through an Application use case
  that records the household operation; `UiWritePathGuardTests` is
  extended to forbid direct writes from the UI, as for P8
  `[VERIFIED — the test exists]`.

### 5.4 Secrets

The SMTP password leaves DPAPI protection: in storage it is inside a
segment encrypted with the household key (E2E); on each device it is
re-protected with DPAPI or `SecureStorage`. This is what makes R4 and
R6 possible for the password; D-2 confirms it. Logs never contain it
(`CLAUDE.md` §7).

### 5.5 Engine

The segment pipeline (publish, list, causal apply, checkpoints,
compaction, generations, rotation) is reused. The apply step and the
image store are profile-specific today `[VERIFIED —
ApplyRemoteOperations depends on the profile repositories]`; they
become a port with two implementations (profile, household)
`[INFERRED — size of the refactor not measured]`.

---

## 6. Setup wizard: new installation or join an existing one

### 6.1 Entry points

- **First start** (empty profile registry). Today the first-run wizard
  creates the first profile as admin, with no other choice
  (`UI/Forms/FirstRunWizardForm.cs`) `[VERIFIED]`. It gains a first
  page: **Set up a new installation** (today's flow; the device creates
  the household and is master, R1) or **Join an existing
  installation** (this section).
- **Later**, on a device already set up: Tools → Sync… → Join an
  existing installation. Same wizard, plus the page of §6.4 for the
  local profiles.
- **Phone**: the same wizard is the onboarding of B.1 §9.1 item 1.

### 6.2 Preparation on a device of the household

An admin opens Devices → Add a device on any device of the household
and:

1. selects the profiles the new device will hold (none preselected);
2. gets a pairing code (`mrpair2`, QR and text, 10 minutes, only while
   the window is open, as `SYNC-FORMAT.md` §4.4).

As with `mrpair1`, the code holds a random secret, not a key; the
offer file `pairing/<deviceId>.mrp`, encrypted with that secret, holds
the household key and the keys of the selected profiles, and is
deleted when the window closes (`SYNC-FORMAT.md` §4.4) `[VERIFIED —
format of version 1]`. The new device has no public key yet when the
code is made, so it records the grants for itself from the offer
(`ProfileKeyGranted` for its own public key). Creating the code is the
admin's approval: the new device then needs no admin PIN. Without a
device at hand, the household passphrase replaces the code (§6.3
page 5).

### 6.3 Pages of the join wizard

| # | Page | Content | Checks and errors |
|---|---|---|---|
| 1 | Language | Language of this device (D-8) | — |
| 2 | Choice | New installation / Join an existing installation | — |
| 3 | Storage | OneDrive, Google Drive, shared folder (PC only); sign-in in the browser | No household in the storage → "No MedReminder installation found in this storage" |
| 4 | Key | Pairing code (text; phone: camera; PC: webcam after H6) or household passphrase. The passphrase is tried on every household of the storage; if it opens more than one, the wizard lists them by household name | Code expired or window closed; wrong passphrase; household written by a newer app → "Update MedReminder to join this installation" |
| 5 | Admin approval | Only on the passphrase path. The passphrase opened the recovery key (§4.4); choose an admin profile, enter its PIN, then select the profiles for this device; the device grants them to itself from the escrow | Wrong PIN; no PIN on the admin profile → warning, continue |
| 6 | This device | Device name (default: computer name); notifications on this device per kind (low stock, dose reminder; D4 of B.1); start with Windows (PC) | — |
| 7 | Summary | Household name, storage account, profiles to download, current master and its last-seen time; the note "This device will not send email; the master does". Option **Make this device the master after joining** | — |
| 8 | Join | Progress per profile: download, build, first sync | A profile that fails is shown with Retry; the others stay joined. Cancel before this page leaves nothing on the device |
| 9 | Done | Restart; profile picker with the joined profiles | If page 7 option was set: the handover wizard (§7.2) opens after the restart |

What the wizard writes, in order, under `%LOCALAPPDATA%\MedReminder\`:
household store and key (§5.3), device key pair (§4.4), device
record, one profile directory per granted profile (database from the
group image, `sync.settings.json`), then the projection of the
household settings into the local files. The files are written to a
staging folder and moved at the end, so an interrupted wizard leaves
the device as it was `[INFERRED — the swap pattern of
ProfileDatabaseSwap applied to a whole setup]`.

The wizard waits until its own device record is listed before it
reports success (`STATUS.md` §3.1, known sync limit) `[VERIFIED — limit
documented]`; joining N groups at once multiplies that exposure.

### 6.4 Device already set up

When a device that already has profiles joins a household, an extra page
lists each local profile with two choices:

- **Replace**: the local profile is dropped; its database is kept as
  `medreminder.db.bak-*`, as the single-group join does today
  `[VERIFIED — SYNC-TWO-PC-CHECKLIST.md step 4]`.
- **Add to the installation**: the profile becomes a new household
  profile with a new group (as enabling sync does today).

A local profile already synced with a group of the household (a
v2.8–v2.10 profile group adopted by the household, §11) is matched by
its group id and kept: no choice, no download.

Its local installation settings (SMTP, backup, roles) are replaced by
the household's; the summary page lists what changes. A device that is
already in another household must leave it first.

### 6.5 Localization and logs

Every new UI string goes to the five `strings.<lang>.json` files
(`CLAUDE.md` §6). The wizard logs steps and counters only: no code,
passphrase, PIN, account e-mail, profile name or key.

---

## 7. Master role

### 7.1 Creation

The device that creates the household records `MasterElected` +
`MasterActivated` for itself (R1).

### 7.2 Planned handover

1. Admin on any device: Devices → select the target → Make master.
   `MasterElected(kind = Planned)`.
2. The outgoing master stops sending email and running cloud backup as
   soon as it applies the election (C6).
3. The target, on applying an election naming itself, shows the
   **handover wizard** to an admin (R4):
   - summary of inherited settings (SMTP, cloud backup policy,
     reference country), editable;
   - device-bound steps: SMTP connection test; cloud-backup storage
     sign-in on this device; the cloud-backup passphrase, typed again
     (D-18: it is not a household setting);
   - the profiles missing on this device are granted by the admin
     (key wrapped for this device, §4.4) and joined (C4, C7);
   - confirm → `MasterActivated`.
4. Activation follows the rule of §7.4. The wizard shows what it
   waits for.

Between election and activation no device sends email: a gap, stated
on every device ("No active master"). An admin cancels a pending
election by electing the previous master again; that device confirms
with one click, since nothing bound to it changed.

### 7.3 Takeover

Master lost or broken: admin elects a device with `kind = Takeover`.
Same wizard and same activation rule (§7.4); in practice the lease
decides, since the old master does not answer. If the old master
comes back, it applies the election and stays silent. `kind` only
changes the wording of the wizard and the log.

### 7.4 Lease and fencing

A master sends email and runs cloud backup only while its last
successful household sync run (listing and applying the household
group) is less than 24 h old (the lease, configurable). An isolated
master (storage unreachable, SMTP reachable) therefore stops within
24 h. Without the lease, a master that cannot sync but can send would
keep sending next to the new one `[INFERRED]`.

Activation rule for an elected device, planned or takeover: activate
when either

- the outgoing master's device record shows an applied vector that
  covers the election (it has applied it and stopped), or
- the outgoing master's `lastSeen` (written after each of its sync
  runs) is older than lease + 1 h. The margin absorbs clock skew
  between the two devices; a larger skew is reported by the HLC clock
  warning (B.1 §4.1) `[INFERRED — margin not measured]`.

A master that keeps syncing but has not yet listed the election keeps
renewing its `lastSeen`, so the second condition waits; it soon lists
the election and the first one holds.

### 7.5 Concurrency

- Two elections made concurrently: the one with the higher HLC wins on
  every device (LWW register). A device activated under the losing
  election stops when it applies the winner.
- Two role changes that together would leave no admin: the later one
  by HLC is ignored on apply, and the admin who made it is told on the
  next sync `[UNCERTAIN — needs a deterministic rule tested against
  every order of arrival]`.

### 7.6 Email on the master

- **Replicated email deduplication**: after a successful email the
  master records `EmailNotificationSent(medicineId, epochFactId,
  sentAt)` in the profile group (profile operation schema version 4).
  `ShouldNotify` for the email channel consults it; toasts keep the
  device-local `NotificationEvent`, which today records every channel
  attempted in one row `[VERIFIED — MedicationMonitor]` and must be
  split per channel. The epoch fact id is derived from replicated
  facts, so every device computes the same one `[INFERRED — ledger
  derivation is deterministic, B.1 P9]`. A new master then does not
  resend what the old one sent.
- **All profiles** (C4): the master evaluates every profile it holds,
  not only the open one. Needs, for a profile not open: schema patches
  (`DatabaseInitializer` runs for the current profile only) and the
  ledger catch-up (`ConsumptionCatchUp`) `[VERIFIED — both limits in
  ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md §1]`. Reading every profile
  from one process has a precedent: the automatic backup copies every
  profile's database `[VERIFIED — AutomaticBackupHostedService]`.
  Writing derived rows and notification events into a profile that is
  not open is new and must go through the in-process database gate
  (`CLAUDE.md` §7) `[INFERRED]`.
- **Non-master**: `IEmailNotificationService` is gated on "this device
  is the active master"; the SMTP tab stays editable by an admin (R6)
  and shows where mail is sent from. The connection test runs only on
  the master. Prescription requests use `mailto:` (C5).

---

## 8. Roles, PIN and enforcement

- Roles and PIN hashes become household state (§5.2). Changing a role
  (item G of `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`) is part of this
  work, not a separate item: with elections by admins only, a
  household must be able to name a new admin.
- Enforcement stays in the app, on every device. Any device holding
  the household key can write any operation; role and PIN remain
  access conveniences, not a security boundary (`ANALYSIS.md` §10)
  `[VERIFIED]`. Signing operations per admin would change that; out of
  scope.
- Replicating the PIN hash is required: otherwise an admin profile
  arrives on a new device without a PIN and anyone at that device is
  an admin. Correction of revision 1, which proposed not to replicate
  it. Consequence: a short numeric PIN hashed with PBKDF2 (100 000
  iterations, `ANALYSIS.md` §5.2) `[VERIFIED]` can be brute-forced
  offline by anyone holding the household key `[INFERRED]`. This does
  not lower today's protection, since the hash already sits in a plain
  `profiles.json`, but the user guide must keep saying that the PIN is
  not a security measure.
- Phones gain roles and PIN (B.1 §9.1 lists an app lock only).

---

## 9. Device removal

An admin removes device D:

1. New household key, new household passphrase, new recovery key
   (the removed device holds the old household key; B.1 §6.2 explains
   why the passphrase changes).
2. Rotation of the key of every profile group D held (B.1 Phase 4c,
   per group); profiles D never held are not rotated.
3. The new profile keys are granted to the remaining devices that hold
   each profile (`ProfileKeyGranted`) and escrowed with the new
   recovery key.
4. The remaining devices receive the new household key. Option A
   (as B.1 today): each asks for the new passphrase or a pairing code.
   Option B (proposed): the removing device writes the new household
   key wrapped for each remaining device's public key, in a file
   outside the household envelope, so the others continue without
   typing anything `[INFERRED — new file kind; the forged-key risk of
   §4.4 applies]`. D-15.
5. The profile-group wraps of the new keys (`key.<n>.wrap`) use a
   random passphrase never shown; recovery goes through the recovery
   key (§4.4).

Removing the master is a removal plus a takeover (§7.3).

---

## 10. First installation on a phone (R8)

- Creating a household on a phone needs a cloud storage account: the
  phone has no folder transport (B.1 §5.8) `[VERIFIED]`.
- The phone is master (R1). SMTP on a phone arrives with MailKit on
  mobile (B.1 Phase 7). Until then the phone is a master "without
  email": when a PC joins, the app proposes to make the PC master (C8).
- Argon2id for the household passphrase runs on the phone (spike S2
  not run) `[VERIFIED — STATUS.md §3.1]`.
- Reverse pairing (phone shows the code, PC joins): the PC accepts the
  text code `[VERIFIED]`. The webcam decoder reads `CODE_39`, `EAN_13`,
  `DATA_MATRIX` only (`UI/Camera/FrameBarcodeDecoder.cs`)
  `[VERIFIED]`; adding `QR_CODE` would let the PC scan the phone
  `[INFERRED — ZXing.Net supports QR]`.

---

## 11. Migration from per-profile groups (v2.8–v2.10)

- On upgrade, each installation becomes a household of one, master of
  itself, with its current settings as the household genesis. No
  storage change until an admin publishes the household.
- Publishing the household adopts the existing profile groups
  (`ProfileRegistered` with the existing `groupId`, `ProfileKeyGranted`
  for itself, `ProfileKeyEscrowed`; the admin sets the household
  passphrase at this point) and records `HouseholdLinked(householdId)` in each adopted profile
  group (profile operation schema version 5).
- Another device of those groups, on applying `HouseholdLinked`, is
  asked to join the household (pairing code or household passphrase)
  instead of publishing its own. Two households published concurrently
  for the same group: the earliest `HouseholdLinked` by HLC wins; the
  other device joins it and shows which of its settings are replaced.
- Joining a single profile group without a household stays available
  until the next major version, for devices not yet upgraded.

---

## 12. Compatibility

- Apps up to 2.10 never see the household (§5.1).
- Profile operation schema versions 4 (`EmailNotificationSent`) and 5
  (`HouseholdLinked`): an older app in the same profile group stops at
  the first such operation and asks for an update (R7 of B.1)
  `[VERIFIED — SYNC-FORMAT.md §5.1]`. Release notes must say that
  every device of a group must be updated.
- `mrpair1` codes keep working for single-group joins; `mrpair2` names
  a household.

---

## 13. Phases

Decision: **implement in steps**. Each step leaves the app consistent;
later steps depend on earlier ones.

Branching (product owner, 2026-09-28):

- Integration branch `feature/master-slave`, created from `main` at
  `7c26328`. Every step branch starts from it and every step PR
  targets it, never `main`.
- `main` is merged into `feature/master-slave` (merge commit, no
  rebase: the branch is shared) whenever `main` receives a release or
  a sync fix, so the final merge stays small. Conflicts in
  `CHANGE_LOG.md` are resolved by keeping both sides' entries.
- Only after H5 (D-16), one PR merges `feature/master-slave` into
  `main`. Exception: H0 targets `main` and reaches the integration
  branch through the next merge of `main` (D-17).
- Releases from `main` in the meantime ship none of this work. A
  preview build of the integration branch, if needed, follows
  `docs/PACKAGING.md` with a pre-release version.

| Step | Content | Depends on | Effort `[INFERRED — not measured]` |
|---|---|---|---|
| H0 | Join waits for its own device record; join by passphrase asks which group when more than one opens; user guide: "configure SMTP on one device only" until H4 | — | 2–4 d |
| H1 | `EmailNotificationSent` in profile groups; email dedup on replicated facts (profile schema 4) | — | 4–6 d |
| H2 | Household local store, projection, use cases for every installation setting, permission matrix (§4.3), role change and PIN as household state; single device, no storage | — | 10–15 d |
| H3 | Household group on storage: create, pairing `mrpair2`, setup wizard (§6), profile subsets with per-device key grants and recovery escrow (§4.4), adoption of existing groups (§11), engine port (§5.5) | H1, H2 | 25–38 d |
| H4 | Master: election, handover wizard, takeover, lease, email and cloud backup gated on master, all-profile monitoring, `mailto:` on non-master | H3 | 15–25 d |
| H5 | Device removal at household level (§9) | H3 | 8–12 d |
| H6 | Mobile: household creation and join on the phone, roles and PIN (B.1 Phase 5); phone as master with email (B.1 Phase 7); QR decode on the PC webcam | H3, H4, B.1 Phase 5 | inside B.1 Phases 5 and 7, plus 5–8 d |

Step H3 is split into four PRs, each merged into
`feature/master-slave` (plan of 2026-09-29):

| Sub-step | Content |
|---|---|
| H3a | Household group on a storage: creation with the household passphrase, a dedicated engine (publish, pull in causal order, apply, device records) reusing the envelope and header of `SYNC-FORMAT.md`, the household image as the full operation log (no checkpoints: the log stays small, §5.5), projection of remote changes into `profiles.json` and the settings files, the SMTP password carried in clear only inside the encrypted segment |
| H3b | Device key pairs, `ProfileKeyGranted` / `Revoked` / `Escrowed`, recovery key; adoption of existing profile groups (grant to self, escrow) |
| H3c | `mrpair2` offers with the profile selection; the join-installation use case (household, then each granted profile group, staged); `HouseholdLinked` in adopted profile groups (profile operation schema 5) to detect two households claiming one group (§11) |
| H3d | UI, in two PRs. H3d-1: Tools → Installation… (publish, devices, Add a device, join an existing installation from a device already set up), household sync in the background service, Tools → Sync… admin-only. H3d-2: first-run choice and join wizard (§6.1), which needs a start without a profile |

H0 and H1 can ship before any household code. H1 reduces, not
removes, duplicate email in today's two-PC setups: two devices can
still send before either sees the other's record; only the master
(H4) removes it. Desktop total H0–H5: about 64–100
developer-days.

---

## 14. Tests

- Domain / Application: household registers (LWW, tombstones, last
  admin rule under every arrival order); election and activation state
  machine with a fake `TimeProvider` (planned, takeover, concurrent
  elections, lease expiry); email dedup across devices with
  `EmailNotificationSent`.
- Application: projection writes only changed files and never tokens
  or paths; permission matrix per role; join of N profiles with one
  failing; a device without a grant cannot open a profile group, and
  after a revocation cannot read what is written next; an interrupted
  setup wizard leaves the device unchanged.
- Convergence: the S9 prototype approach (random operation orders over
  several devices) extended to household operations.
- Manual: `SYNC-TWO-PC-CHECKLIST.md` gains a household section (create,
  join wizard by pairing code and by passphrase, join from a device
  already set up (replace / add), add device with a profile subset, admin edit on non-master, planned
  handover with wizard, takeover with lease, no duplicate email, no
  email from a non-master).
- Recovery: every device lost, a new device with the household
  passphrase recovers every profile; a device joined by pairing code
  cannot open a profile it was not granted.
- Lease: activation with clocks skewed by less and by more than the
  margin.
- Logs: no household key, recovery key, pairing code, SMTP password, PIN hash,
  profile name, address or medicine data (`CLAUDE.md` §7).

---

## 15. Decisions to confirm

| # | Decision | Proposal |
|---|---|---|
| D-1 | Master as a role, not a sync hub (C1) | Yes |
| D-2 | SMTP password replicated, E2E encrypted (§5.4) | Yes: needed for R4 and R6 |
| D-3 | PIN hash replicated (§8) | Yes |
| D-4 | Scheduled cloud backup only on the master (C3) | Yes |
| D-5 | Gap rather than duplicate at handover; lease 24 h (C6, §7.4) | Yes |
| D-6 | Profile subset per device, enforced by per-device key grants; the master holds every profile (C7, §4.4) | Yes |
| D-7 | Phone as master allowed, PC recommended (C8) | Yes |
| D-8 | Language per device (§4.3) | Yes |
| D-9 | Prescription request from a non-master through `mailto:` only (C5); a replicated outbox sent by the master is a later option | Yes |
| D-10 | Single-group join without household kept until the next major version (§11) | Yes |
| D-11 | Profile names visible to every device of the household (§4.4) | Yes: needed to manage a profile a device does not hold |
| D-12 | Profiles for a new device chosen by the admin when creating the pairing code; no admin PIN on the new device on that path (§6.2) | Yes |
| D-13 | Recovery key escrow: the household passphrase opens every profile and is an admin secret (§4.4) | Yes: otherwise losing the devices loses the data |
| D-14 | Reference country becomes household-wide and admin-only (§4.3) | Yes |
| D-15 | After a device removal, remaining devices receive the new household key without typing it (§9 option B) | Yes, after H5 tests |
| D-16 | Last step before merging `feature/master-slave` into `main` | **Decided 2026-09-28**: H5 (desktop complete); H6 follows with B.1 Phases 5 and 7 |
| D-17 | H0 (join fixes, no format change) merged into `main` directly, then `main` into `feature/master-slave` | **Decided 2026-09-28**: yes |
| D-18 | Cloud-backup passphrase: household secret or asked in the handover wizard | **Decided 2026-09-29**: asked in the handover wizard (§7.2); never replicated |

---

## 16. Corrections to other documents (after approval)

- `ANALYSIS-B1-MOBILE-SYNC.md` §1.3, §4.2 last row, §4.6, §15: registry,
  role, PIN, SMTP and cloud-backup settings are replicated through the
  household group.
- `ANALYSIS-B1-MOBILE-SYNC.md` §8.4: the designated mail device is
  replaced by the master (§7).
- `ANALYSIS-B1-MOBILE-SYNC.md` §9.1: roles and PIN on mobile.
- `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`: item G is absorbed by H2.
- `SYNC-FORMAT.md`: household layout and catalogue; profile operation
  schema version 4.
- `ANALYSIS-MULTI-USER.md` §9.1: the master notifies for every
  profile.
- `SYNC-TWO-PC-CHECKLIST.md`: household section (§14).
- `docs/USER_GUIDE.<lang>.md` (five languages): setup wizard, master,
  household passphrase as an admin secret.
- `CLAUDE.md` §5: `household\` directory in the runtime data.
- `EVOLUTION.md`, `STATUS.md`: new item and ranking.

---

## 17. Sources

- `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §1.2, §1.3, §4.2, §4.6,
  §5.8, §6, §8.4, §9.1, §15.
- `docs/SYNC-FORMAT.md` §2, §4.4, §5.1, §7, §8.
- `docs/STATUS.md` §3.1.
- `docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` §1, §2.2.
- `docs/ANALYSIS.md` §7, §10.
- Code: `Domain/Calculations/NotificationCycle.cs`,
  `Application/Monitoring/MedicationMonitor.cs`,
  `Application/Sync/ApplyRemoteOperations.cs`,
  `Application/Sync/ProfileSettingsProjection.cs`,
  `Application/Sync/SyncGroupUseCases.cs`,
  `Application/Prescriptions/MailtoLink.cs`,
  `UI/Forms/SyncDialog.cs`, `UI/Forms/SettingsDialog.cs`,
  `UI/Forms/FirstRunWizardForm.cs`,
  `UI/Hosting/AutomaticBackupHostedService.cs`,
  `UI/Camera/FrameBarcodeDecoder.cs`.

---

## 18. Revisions

| Rev. | Change |
|---|---|
| 1 | `ANALYSIS-DEVICE-PROVISIONING.md`: one-time settings seed by a multi-group pairing code |
| 2 | Replaced by this document: household group, master role, continuous replication (product owner requirements §1) |
| 3 | Setup wizard (§6); per-device profile keys (§4.4), because keys in the household state made the profile subset ineffective |
| 4 | Review against the code: recovery key escrow (without it, losing every device lost the profiles, and the passphrase path of the wizard could not open any profile); offer file carries the profile keys (the new device has no public key when the code is made); one activation rule with clock margin; permission matrix aligned with today's gating (auto-start admin-only, update check open); forged-key residual risk; PIN hash brute force; H1 reduces duplicates, does not remove them |
| 5 | Branching policy (§13): integration branch `feature/master-slave`; decisions D-16, D-17 |
| 6 | D-18 (cloud-backup passphrase asked in the handover wizard); H3 split into H3a–H3d (§13) |
| 7 | Recovery key as a key pair (§4.4 point 4), so escrow needs no passphrase; `HouseholdLinked` moved from H3b to H3c |
| 8 | `HouseholdLinked` is profile schema version 5, not 4 (§11, §12). H3c as built: the household join replaces the local household store and is not staged, only the profiles are (§6.3); profile groups are read from the household's storage |
| 9 | H3d split into H3d-1 and H3d-2 (§13). Until H4, the email settings are household settings, so every device of an installation can send email; the H1 deduplication limits repeats (user guides say so) |

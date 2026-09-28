# ANALYSIS — Device provisioning from an existing sync group

Design document, **prior** to implementation. It studies one scenario
on top of B.1 (`ANALYSIS-B1-MOBILE-SYNC.md`):

> MedReminder is installed and configured on a PC. An administrator
> installs the app on a second PC (later on Android or iOS) and joins
> an existing sync group. The goal is to replicate the first
> installation, settings included. Later, the first installation may
> also happen on a phone instead of a PC.

Status on 2026-09-28: analysis only. Nothing here is planned until the
decisions in §9 are confirmed.

Reading conventions: `[VERIFIED]` (checked against the tree at commit
`7c26328`, v2.10.0), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (not verified). Untagged statements are design
proposals.

---

## 1. The premise, restated

"Synchronize everything, settings included" cannot be delivered by
extending today's sync, and should not be:

- The unit of sync is the **profile**: one group per profile
  (`SYNC-FORMAT.md` §2) `[VERIFIED]`.
- The profile registry, PIN, role, SMTP and backup settings are
  explicit non-goals of B.1 (`ANALYSIS-B1-MOBILE-SYNC.md` §1.3, §4.6,
  §15) `[VERIFIED]`.
- "Everything" mixes four kinds of state with different lifetimes:

| Kind | Examples | Right policy |
|---|---|---|
| Profile data | medicines, facts, intakes, counts | Continuous sync (shipped) |
| Profile preferences | display name, recipients | Continuous sync, last writer wins |
| Installation configuration | SMTP, backup policy, language, profiles and roles | Copied **once** when a device is provisioned |
| Device state | paths, OAuth tokens, DPAPI blobs, notification events, auto-start | Never leaves the device |

The scenario is therefore split in two: **provisioning** (make a new
device look like the first installation, once) and **sync** (keep
profile data and profile preferences converged, continuously). This
document is about the first; it changes the second only where noted.

---

## 2. Current behavior on a second PC (v2.10.0)

1. First start: the first-run wizard creates an admin profile.
2. Tools → Sync… → Join a group… (passphrase) or Join with a pairing
   code…. The current profile's database is replaced by an image of
   the group; a `medreminder.db.bak-*` is kept; the app restarts
   (`SYNC-TWO-PC-CHECKLIST.md` step 4) `[VERIFIED]`.
3. Replicated: medicine data and every replicated fact; the profile
   display name and the `ToAddress`, `CaregiverAddress`,
   `DoctorAddress` recipients (`ProfileSettingChanged`, operation
   schema version 3; `ProfileSetting.All` in
   `Domain/Sync/SyncOperationBodies.cs`) `[VERIFIED]`.
4. Not replicated `[VERIFIED]`:

| State | Storage | What the administrator does on the second PC |
|---|---|---|
| Other profiles | `profiles.json` | Create each profile, then join its group with its own passphrase or pairing code |
| Role, PIN | `profiles.json` | Set again; the role of an existing profile cannot be changed (`ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` §1) |
| SMTP transport and password | `smtp.settings.json`, `smtp.protected` (DPAPI) | Configure again |
| Backup settings | `backup.settings.json` | Configure again |
| Language, reference country, update check | `user.settings.json` (`UserSettings`) | Configure again |
| Text size | `profiles\<id>\ui.settings.json` | Configure again |
| Cloud account tokens | `onedrive.protected`, `googledrive.protected` | Sign in again (correct: tokens are device secrets) |
| Sync device name, device id | `profiles\<id>\sync.settings.json` | Per device by design |

---

## 3. Findings

### 3.1 Duplicate email (blocking for any SMTP replication)

The designated mail device (`ANALYSIS-B1-MOBILE-SYNC.md` §8.4) is
designed but not implemented: no code refers to it `[VERIFIED — no
match for "designated" or "MailDevice" under src/]`. `MedicationMonitor`
deduplicates through `NotificationEvent`, which is device-local and not
replicated (§4.2 there) `[VERIFIED]`. Two synced desktops that both
have SMTP configured therefore both send the low-stock and caregiver
email for the same medicine `[INFERRED]`. Today this needs a manual
SMTP setup on the second PC; replicating SMTP would make it automatic.
The designation must ship before, or together with, any SMTP seed.

### 3.2 One join per profile

There is no installation-level object that lists the groups. With N
profiles the administrator creates N profiles and runs N joins, with N
passphrases or N pairing codes `[VERIFIED — Tools → Sync… acts on the
current profile]`. `ListGroupsAsync` returns group ids only; the
profile names are encrypted in the operation log and never appear in
`group.json` or in paths `[VERIFIED]`.

### 3.3 Join by passphrase picks the first group that opens

On a storage holding several groups, `SyncDialog` tries the passphrase
on each group and joins the first one it opens `[VERIFIED —
SyncDialog.cs, "the first that opens is joined"]`. If two profiles use
the same sync passphrase, a join can land on the wrong profile's
group. The confirmation names only the local profile, not the group
`[INFERRED]`. Provisioning by pairing code avoids the ambiguity; the
passphrase path should either forbid reuse at group creation or ask
which group when more than one opens.

### 3.4 Join while the listing lags

A device that joins while the provider listing lags, and another
device compacts, ends in `RebuildRequired` (`STATUS.md` §3.1, known
sync limit) `[VERIFIED]`. Provisioning N groups in one go multiplies
the exposure. The fix named there (the join waits until its own device
record is listed) is a prerequisite.

### 3.5 Role is not a security boundary

Any device holding a group key can write operations to the group. Role
and PIN are access conveniences (`ANALYSIS.md` §10) `[VERIFIED]`.
Replicating the role is acceptable on that basis only, and the user
guide must keep stating it.

### 3.6 Household privacy

Profiles are often different people. A mechanism that hands every
profile to a new device with one pairing puts several people's health
data behind one secret. Provisioning must let the administrator pick
the profiles, with none preselected beyond the current one.

---

## 4. Setting classification

| Setting | Policy | Notes |
|---|---|---|
| Profile data, display name, recipients | Continuous sync | Shipped |
| Per-profile notification defaults (channels for new medicines, low-stock time) | Continuous sync, new `ProfileSetting` values | Only settings that exist on every platform |
| Text size | Device-local | A phone and a PC need different values |
| Language, reference country, update check | Seed | Then local |
| SMTP host, port, STARTTLS, user, from, timeout | Seed, opt-in | Only after §3.1 |
| SMTP password | Seed, separate opt-in | Re-encrypted as in `.mrz` export (`ExportedProtectedSecret`) |
| Backup policy (enabled, time, retention) | Seed, opt-in | Directory, cloud folder and cloud account excluded: machine or device specific |
| Profile list, display names, roles | Seed | From the provisioning manifest (§5.1) |
| PIN hash | Decision D-c | A PBKDF2 hash leaving the machine, even encrypted, is a new exposure |
| Paths, tokens, DPAPI blobs, auto-start, device name, notification and dose-reminder events, `sync.settings.json` | Never | |

Adding a `ProfileSetting` value keeps the `ProfileSettingChanged` type.
`ProfileSettingsProjection` iterates `ProfileSetting.All`, so an older
app would record the register and not project it `[VERIFIED for the
projection; UNCERTAIN whether decoding validates the setting name]`.
The schema-version rule of `SYNC-FORMAT.md` §8 must be settled for
this case before the first new setting ships.

The seed has a ready format: `ExportedShared` in
`Application/Export/ExportPayload.cs` already carries `UserSettings`,
`BackupSettings`, `SmtpSettings` and the SMTP password re-encrypted
with the archive key, each opt-in `[VERIFIED]`. Reusing it keeps one
definition of "installation settings" for export and provisioning.

---

## 5. Options

### 5.1 Option A — multi-group pairing with a seed (proposed)

A device of the installation offers a **provisioning code**, an
extension of the Phase 4c pairing code (`SYNC-FORMAT.md` §4.4):

- Code `mrpair2.<groupId>.<deviceId>.<provider>.<secret>`; the group
  named is the one whose storage holds the offer file.
- Offer file `pairing/<deviceId>.mrp`, `formatVersion` 2, same
  envelope, key and associated data as version 1. Plaintext:

```json
{
  "expiresAt": "…",
  "profiles": [
    { "groupId": "…", "keyVersion": 2, "key": "…", "provider": "OneDrive",
      "displayName": "…", "role": "admin" }
  ],
  "seed": { "userSettings": { }, "backupSettings": { }, "smtpSettings": { },
            "smtpPasswordEncrypted": null }
}
```

- The profiles listed must share one storage account: the joining
  device signs in once. Groups on another account or folder are
  offered one by one, as today.
- The joining device, after one confirmation that lists the profiles
  by name: creates each profile locally (creation keeps forcing admin
  on an empty registry; the manifest role applies to the others),
  joins each group in sequence, applies the seed, restarts once.
- A failure on one group leaves the others joined and reports which
  one failed; the administrator can retry it with a single-group code.

Effect on sync: none. Segments, images, operation catalogue and
merge rules are unchanged. Older apps ignore `pairing/`
`[VERIFIED — SYNC-FORMAT.md §7]`; an older app given an `mrpair2`
code rejects the prefix `[INFERRED]`.

Limit, on purpose: after provisioning, installation settings diverge
between devices. A later SMTP change on the first PC does not reach
the second.

Estimate: 8–12 developer-days, excluding §3.1 and §3.4 `[INFERRED —
not measured; it reuses the Phase 4c pairing code paths]`.

### 5.2 Option B — household group (continuous)

A further sync group holding no medicines, only installation state:
`ProfileRegistered`, `InstallationSettingChanged`, and
`ProfileKeyShared` (each profile group key wrapped with the household
key). Operation schema version 4. A profile created on one device
appears on the others.

Costs:

- Every key rotation of a profile group must rewrap its key in the
  household group; a removal needs a rotation at both levels.
- The household key becomes a master key for every profile's data.
- Last-writer-wins on SMTP across devices surprises users (a change
  made on a phone reconfigures the PC's mail).
- Mobile has no backup and no auto-start: part of the replicated
  state has no meaning there.

Estimate: several times Option A `[INFERRED]`. Justified only by a
need that A does not cover (profiles created later on one device
appearing on the others without a new pairing).

### 5.3 Recommendation

Option A first. Option B only if that need is confirmed after A ships.

---

## 6. First installation on a phone

What already holds `[VERIFIED]`:

- `SyncGenesis`, `JoinSyncGroup`, `SyncKeys`, the file codec and the
  pairing code live in `MedReminder.Application` (`net10.0`); the
  image is a SQLite file. A phone can create a group with the same
  code paths.
- The desktop reaches OneDrive and Google Drive through the provider
  API, not a synced folder (`ANALYSIS-B1-MOBILE-SYNC.md` §5.8), so a
  group created on a phone is reachable from a PC.

What is missing or open:

1. **Cloud transports only.** The phone has no local-folder transport;
   an installation born on a phone cannot use a shared folder.
2. **No administrator on mobile.** The mobile screens (§9.1 there)
   have "Profiles on this device" with no role or PIN. Backup is not
   applicable on mobile; SMTP arrives only in Phase 7 and only on the
   designated mail device. A phone-first installation produces a seed
   without SMTP and backup; the first PC that joins configures them
   locally. Decision D-d: whether the phone may edit installation
   settings it does not use.
3. **Reverse pairing.** The phone shows the code; the PC joins by
   typing it (text form exists) `[VERIFIED]`. The webcam decoder reads
   `CODE_39`, `EAN_13` and `DATA_MATRIX` only
   (`UI/Camera/FrameBarcodeDecoder.cs`) `[VERIFIED]`; adding
   `QR_CODE` would let a PC with a webcam scan the phone's code
   `[INFERRED — ZXing.Net supports QR; not tested here]`.
4. **Spikes still open** (`STATUS.md` §3.1): S2 (Argon2id at 64 MiB on
   a low-end phone, which a phone creating a group by passphrase
   pays), S1, S3, the Android halves of S6 and S7, the mobile OAuth
   registrations (P14).
5. **Role across devices.** With Option A the role travels in the
   manifest at provisioning time. The role still cannot be changed
   afterwards; `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` item G covers
   that and should be decided first if a phone-first installation
   must later promote a PC profile.

---

## 7. Proposed sequence

| Step | Content | Depends on |
|---|---|---|
| 1 | Designated mail device (B.1 §8.4), brought forward from Phase 7 | — |
| 2 | Join waits until its own device record is listed (§3.4) | — |
| 3 | Group choice or passphrase-reuse guard on join (§3.3) | — |
| 4 | New `ProfileSetting` values for per-profile notification defaults; settle the schema-version rule (§4) | — |
| 5 | Option A: `mrpair2` code, offer file version 2, seed from `ExportedShared`, profile selection, opt-in SMTP and password | 1, 2 |
| 6 | Mobile onboarding "create or join" with the Option A code (B.1 Phase 5) | 5, D-d |
| 7 | Optional: QR decoding in the webcam scanner for reverse pairing | 5 |

Steps 1–4 are independent of each other and also fix today's two-PC
setups.

---

## 8. Tests and checks

- Application: offer file round trip (version 1 and 2), expiry,
  wrong secret, a profile list spanning a group the reader cannot open.
- Application: provisioning with one group failing leaves the others
  joined and reports the failed one.
- Application: seed application never writes paths, tokens or a
  password without its opt-in.
- Manual: `SYNC-TWO-PC-CHECKLIST.md` gains a provisioning section
  (three profiles, one code, SMTP opt-in on and off, no duplicate
  email once the mail device is designated).
- Logs: no pairing code, key, passphrase, SMTP password, profile name
  or address (`CLAUDE.md` §7).

---

## 9. Decisions to confirm

| # | Decision | Options | Proposal |
|---|---|---|---|
| D-a | Provisioning model | A (one-time seed); B (household group) | A |
| D-b | SMTP password in the seed | Opt-in; never (always typed again) | Opt-in, off by default |
| D-c | PIN hash in the seed | Yes; no (PIN set again on each device) | No |
| D-d | Installation settings editable on mobile | Yes; no | No until Phase 7 |
| D-e | Mail device designation brought forward | Yes; keep in Phase 7 | Yes (prerequisite of Option A) |

---

## 10. Sources

- `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §1.3, §4.2, §4.6, §5.5,
  §5.8, §6.1, §8.4, §9.1, §15.
- `docs/SYNC-FORMAT.md` §2, §4.4, §6, §7, §8.
- `docs/SYNC-TWO-PC-CHECKLIST.md`.
- `docs/STATUS.md` §3.1.
- `docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` §1.
- Code: `Application/Sync/ProfileSettingsProjection.cs`,
  `Application/Sync/SyncGroupUseCases.cs`,
  `Application/Monitoring/MedicationMonitor.cs`,
  `Application/Export/ExportPayload.cs`,
  `Domain/Sync/SyncOperationBodies.cs`, `UI/Forms/SyncDialog.cs`,
  `UI/Camera/FrameBarcodeDecoder.cs`.

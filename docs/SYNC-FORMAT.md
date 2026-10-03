# MedReminder — Sync Storage Format

This document is the **public contract** for the files MedReminder
writes to a sync folder when several devices share a profile. It
describes the on-disk format only. The design rationale (merge rules,
security model, phases) is in
[`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md).

Format version: **1**. Operation catalogue: schema version **10**.
Database image: schema version **7**.

---

## 1. Principles

- Every device keeps its own database. Devices exchange **operations**
  (what a user recorded), never database files in use.
- Every file has **one writer**: the device named in its path, or the
  device that created the group for `group.json`. No file is ever
  written by two devices, so a sync client cannot produce conflict
  copies.
- Everything except `group.json` and the key wrap is **encrypted**
  with the group key. File names contain random ids and counters only.
- A file is complete or absent: writers use a temporary name starting
  with `.` and rename it when done. Readers ignore names starting with
  `.`. Through a provider API, small files are written in one request
  and larger ones under a temporary name then renamed, because an
  upload in progress is visible under its final name.

---

## 2. Layout

One folder per sync group (one group per profile), directly under the
sync root chosen by the user:

```
<groupId>/
  group.json                                  cleartext, written once
  key.<keyVersion>.wrap                       group key, wrapped with the passphrase
  genesis/<generation>.mrg                    database image a generation starts from
  checkpoints/<generation>/<deviceId>-<n>.mrc database image and applied vector
  ops/<generation>/<deviceId>/<seq>.mrs       operation segments
  devices/<deviceId>.mrd                      device record
  pairing/<deviceId>.mrp                      pairing offer, while one is shown
```

The sync root is the folder chosen by the user, or, for a provider
reached through its API, the `sync/` folder of the app's folder in the
user's account (OneDrive: `/Apps/<app>/sync/`). The layout below that
root is the same.

Google Drive has file ids, not paths, and allows two files with one
name. There the files lie flat in the app data folder (`appDataFolder`):
each file's name is its relative path below (for example
`<groupId>/ops/1/<deviceId>/7.mrs`), and it carries the public property
`mrsync` = `1`. Files without that property are not sync files. When
two files have the same name, readers use the one created first (then
the smaller file id); the writer of the other one deletes it.

- `groupId`, `deviceId`: GUIDs, 32 lowercase hexadecimal digits (`N`
  format).
- `generation`, `keyVersion`, `seq`, `n`: decimal integers starting at
  1.
- The current key version is the highest `key.<keyVersion>.wrap` that
  has a generation sealed with it (a wrap without one is left by an
  interrupted rotation and is ignored).
- A generation has one key version: the `keyVersion` in the header of
  its genesis. The current generation is the highest genesis sealed
  with the current key version. A newer genesis sealed with an older
  key version is ignored (§7). Files of a generation sealed with
  another key version are ignored.
- Segments of one device are numbered `1, 2, 3, …` with no reuse. A
  missing number below the highest one means the segment was deleted
  after a checkpoint, or has not been synced by the storage client yet.

---

## 3. `group.json`

UTF-8 JSON, camelCase:

```json
{ "format": "MedReminder.Sync", "formatVersion": 1, "groupId": "…" }
```

A reader refuses a `formatVersion` it does not know.

---

## 4. Cryptography

### 4.1 Group key and key wrap

The group key is 32 random bytes, generated when sync is enabled on
the first device. `key.<keyVersion>.wrap` is UTF-8 JSON:

| Field | Meaning |
|---|---|
| `formatVersion` | 1 |
| `groupId`, `keyVersion` | as in the path |
| `iterations`, `memoryKiB`, `parallelism` | Argon2id parameters (default 3, 65536, 1) |
| `salt` | 16 random bytes, base64 |
| `nonce`, `tag`, `ciphertext` | AES-256-GCM of the group key, base64 |

Wrapping key: Argon2id over the UTF-8 sync passphrase with `salt` and
the listed parameters, 32 bytes. AES-GCM associated data: the UTF-8
string `MedReminder.Sync.Key|<groupId N format>|<keyVersion>`.

The sync passphrase is separate from the backup passphrase. A wrong
passphrase fails the AES-GCM tag check.

### 4.2 Encrypted file envelope (see also §4.4)

Segments, checkpoints, genesis images and device records share one
binary envelope:

| Bytes | Content |
|---|---|
| 4 | ASCII `MRS1` |
| 4 | header length `L`, unsigned 32-bit little endian |
| `L` | header, UTF-8 JSON (§4.3) |
| 12 | AES-GCM nonce, random per file |
| 16 | AES-GCM tag |
| rest | AES-256-GCM ciphertext of the gzip-compressed content |

Key: the group key of the header's `keyVersion`. Associated data: the
header bytes exactly as stored. A file whose header was changed, or
that was copied under another device's folder or another number,
fails authentication; readers also check that the header matches the
path.

### 4.3 Header

| Field | Meaning |
|---|---|
| `kind` | `segment`, `checkpoint`, `genesis`, `device` |
| `formatVersion` | 1 |
| `groupId`, `generation`, `deviceId` | as in the path (for `genesis`: the device that wrote it) |
| `seq` | segment number or checkpoint number; 0 otherwise |
| `keyVersion` | group key used |
| `contentVersion` | operation schema version (segments), image schema version (images), 1 (device records) |
| `vector` | applied vector `{ deviceId: seq }`; checkpoints and genesis only |

The header is cleartext: ids and counters only.

---

### 4.4 Pairing code and pairing file

A device of the group can offer the group key to a new device without
the passphrase. It shows a **pairing code** (as a QR code, and as text
for a PC), valid for 10 minutes and only while its window is open:

```
mrpair1.<groupId>.<deviceId>.<provider>.<secret>
```

| Part | Meaning |
|---|---|
| `mrpair1` | format of the code |
| `groupId`, `deviceId` | N format; `deviceId` is the device showing the code |
| `provider` | `folder`, `OneDrive` or `GoogleDrive`: where the joining device looks for the group |
| `secret` | 32 random bytes, base64url without padding |

The code does not hold the group key. It names
`pairing/<deviceId>.mrp`, written by the showing device (one offer per
device, a new offer replaces the file) and deleted when the offer
ends. UTF-8 JSON:

| Field | Meaning |
|---|---|
| `formatVersion` | 1 |
| `groupId`, `deviceId` | as in the path |
| `nonce`, `tag`, `ciphertext` | AES-256-GCM, key: the `secret` of the code |

Associated data: the UTF-8 string
`MedReminder.Sync.Pairing|<groupId N format>|<deviceId N format>`. The
plaintext is JSON `{ "keyVersion", "key" (base64), "expiresAt" }`. A
reader refuses the file after `expiresAt`, and when it is absent. A
code photographed during the offer opens nothing once the file is
gone.

## 5. Content

All JSON is UTF-8 and camelCase. Enums are written by member name.
`DateOnly` is `yyyy-MM-dd`, `TimeOnly` `HH:mm:ss`, `DateTimeOffset`
ISO 8601 with offset, decimals are JSON numbers.

### 5.1 Segment

```json
{
  "dependencies": { "<deviceId>": 12 },
  "operations": [
    {
      "id": "…", "physicalMs": 1790000000000, "counter": 0, "deviceId": "…",
      "type": "StockEntryRecorded", "schemaVersion": 1,
      "medicineId": "…", "entityId": "…", "payload": "{…}"
    }
  ]
}
```

- `dependencies`: the writer's applied vector when it sealed the
  segment. A reader applies the segment only after it has applied, for
  each listed device, the segments up to that number, and after this
  device's previous segment.
- `operations`: in the writer's hybrid-clock order. `(physicalMs,
  counter, deviceId)` is the hybrid logical clock timestamp; timestamps
  are totally ordered by physical time, then counter, then `deviceId`
  compared as its 32-digit lowercase hex text.
- `payload`: the operation as JSON text, by `type`, per §6.

An operation of an unknown `type` or `schemaVersion` stops the reader
at that segment; nothing after it is applied until the app is updated.
Each operation is written with the lowest schema version that carries
it (§6), so an older app stops only at an operation it cannot read.
The segment header's `contentVersion` is the writer's highest schema
version.

### 5.2 Device record

```json
{
  "deviceId": "…", "name": "…", "platform": "desktop", "appVersion": "…",
  "publishedSeq": 7, "applied": { "<deviceId>": 12 }, "lastSeen": "…"
}
```

Written by its device when it takes part (group creation, join, new
generation) and after every sync run. `applied` is what this device
has applied from each other device.

### 5.3 Genesis and checkpoint images

Content, before compression:

| Bytes | Content |
|---|---|
| 4 | length `M` of the metadata, unsigned 32-bit little endian |
| `M` | `{ "snapshotSchema": 1 }` |
| rest | a SQLite 3 database file |

The database is the profile database of the writing device (MedReminder
schema; `docs/ANALYSIS.md` §4.1) without what is not replicated:
derived stock movements (`Origin` 3), notification, dose-reminder,
prescription-reminder, deadline-reminder and shortage-notice events, hint conflicts (kinds 4 to 6), sync progress (`SyncPeers`) and
the reference catalogue. It keeps the facts, the frozen (`Legacy`)
movements and the cutoff, the register versions (`SyncFieldVersions`),
the tombstones (`FactRetractions`), the low-stock emails sent by any
device (`SentEmailNotifications`), the prescriptions (`Prescriptions`),
the administrative deadlines (`Deadlines`),
the register conflicts and the operation log (`SyncOperations`).

Image schema versions: 1, the original image; 2, the operation log may
hold `MedicineDeleted`, so a medicine can be absent from the image
while later operations for it exist (they are skipped, §6); 3, the
image holds `SentEmailNotifications`, which an older app would drop;
4, `SentEmailNotifications` carry `Stage`, which an older app would
drop; 5, the image holds `Prescriptions`, which an older app would drop;
6, the image holds `Deadlines`, which an older app would drop; 7, slots
carry `IsAsNeeded` and intakes `IsExtra`, which an older app would drop
(it would consume as-needed slots every day and read extra intakes as
scheduled ones).

A device joins from the newest checkpoint whose `vector` covers every
device folder's first remaining segment (`vector[d] >= first − 1`), or
from the genesis when no segment was deleted. It then applies the
segments after that vector. A reader refuses an image whose
`snapshotSchema` is higher than its own.

---

## 6. Operation catalogue (schema versions 1 to 10)

One operation per user fact or per changed register; derived values
(consumption, count corrections, stock epoch, the current schedule on
the medicine) are never operations. Every payload has `medicineId`;
it is the empty GUID (`00000000-0000-0000-0000-000000000000`) on a
profile-level operation.
Ids are those of the rows written, so replaying an operation changes
nothing.

| `type` | Payload fields | Merge rule |
|---|---|---|
| `MedicineCreated` | `startDate`, `createdAt`, `fields[]` (`field`, `value`) | new medicine; every field versioned at the operation's timestamp |
| `MedicineFieldChanged` | `field`, `value`, `baseVersion` | last writer wins per field |
| `MedicineStartChanged` (version 10) | `startDate` | last writer wins (register `StartDate` of the medicine), no conflict entry; the schedule row and slot set that make the plan follow the new date are their own `ScheduleRowRecorded` and `SlotSetRecorded` |
| `MedicineActivityChanged` | `changeId`, `day`, `active`, `recordedAt` | dated fact; the current value is the latest by timestamp |
| `ScheduleRowRecorded` | `rowId`, `effectiveFrom`, `dosePerAdministration`, `administrationsPerDay`, `scheduleKind`, `schedulePayload`, `recordedAt`, `baseVersion` | fact; the latest row of a date wins |
| `SlotSetRecorded` (version 9 when a slot has `isAsNeeded` true) | `setId`, `effectiveFrom`, `recordedAt`, `slots[]` (`slotId`, `dose`, `time`, `timingLabel`, `order`, `isAsNeeded` (from version 9; absent = false), `presetId` (display only, any version; absent = null)), `baseVersion` | fact; the latest set in force applies; an as-needed slot is never consumed automatically |
| `StockEntryRecorded` | `movementId`, `kind`, `quantityDelta`, `occurredAt`, `notes` | fact |
| `IntakeRecorded` (version 9 when `isExtra` is true) | `intakeId`, `day`, `status`, `quantity`, `scheduledAt`, `actualAt`, `notes`, `recordedAt`, `isExtra` (from version 9; absent = false) | fact; an extra intake books its quantity and leaves the day's automatic consumption in place |
| `StockCountRecorded` | `countId`, `countDay`, `countedQuantity`, `takenToday`, `thresholdAtCount`, `recordedAt`, `notes`, stored outcome | fact; its outcome is evaluated again on the facts recorded before it |
| `SuspensionRecorded` | `suspensionId`, `startDate`, `endDate`, `reason`, `recordedAt` | fact |
| `SuspensionEndChanged` | `suspensionId`, `endDate` | last writer wins |
| `FactRetracted` | `retractionId`, `kind`, `factId`, `recordedAt` | the fact is removed whatever the order of arrival |
| `MedicineDeleted` (version 2) | `recordedAt` | the medicine and every row that refers to it are removed; any operation for the medicine, before or after it in any order, is logged and not applied |
| `ProfileSettingChanged` (version 3) | `setting`, `value` | last writer wins per setting, no conflict entry; profile-level (`medicineId` empty) |
| `EmailNotificationSent` (version 4; version 6 for `stage` 2) | `notificationId`, `stockEpoch`, `epochFactId`, `sentAt`, `stage` (from version 6; absent = 1) | fact; a low-stock email sent for that stock epoch of the medicine (the epoch is `epochFactId` when set, else `stockEpoch`) at that warning stage: the receiving device does not send it again at that stage or an earlier one |
| `PrescriptionChanged` (version 7) | `prescriptionId`, `requestedOn`, `issuedOn`, `code`, `packages`, `validUntil`, `collectedOn` (dates `null` when not known), `deleted`, `recordedAt` | the whole state of one prescription, written when it is recorded, changed or deleted; last writer wins per prescription (register `Prescription` of the prescription id, its value the payload), no conflict entry; when the winner has `deleted` true the prescription is removed, and a later write brings it back |
| `DeadlineChanged` (version 8) | `deadlineId`, `kind` (`TherapeuticPlan`, `ExemptionRenewal`, `CheckUp`, `Other`), `label`, `dueOn`, `leadDays`, `repeatMonths` (`null` for a one-off deadline), `channels` (`None`, `Email`, `Windows`, `Both`), `doneOn`, `deleted`, `recordedAt` | the whole state of one administrative deadline, with the rules of `PrescriptionChanged` (register `Deadline` of the deadline id); `medicineId` is empty for a deadline of the profile |
| `HouseholdLinked` (version 5) | `householdId`, `linkedAt` | fact; the household that adopted the group (§9); the earliest by HLC wins; profile-level (`medicineId` empty) |

Every type is schema version 1 except `MedicineDeleted`, version 2,
`ProfileSettingChanged`, version 3, `EmailNotificationSent`, version 4,
`HouseholdLinked`, version 5, `PrescriptionChanged`, version 7,
`DeadlineChanged`, version 8, and `MedicineStartChanged`, version 10. A
second-stage `EmailNotificationSent` (the second low-stock warning, sent
at half of the medicine's warning threshold) is written with version 6, so only that operation stops an
older app; a first-stage one keeps version 4 and its `stage` field,
which an older app ignores, is 1.

Profile settings (`ProfileSettingChanged.setting`): `DisplayName` (the
profile's name, never empty), `ToAddress`, `CaregiverAddress`,
`DoctorAddress` (notification recipients, `""` for none),
`CaregiverEmails` (the kinds of email copied to the caregiver: `""` for
every kind, `None`, or `LowStock`, `DoseReminder`, `Prescription`,
`Deadline`, `Shortage` separated by commas), `CaregiverDigest` (`""` or
`Off`, or `Weekly`) and `CaregiverDigestSentOn` (`yyyy-MM-dd` of the
last weekly summary sent by any device, so the others do not send it
again). An app that does not know a setting keeps its versions and
does not project it, so these three need no schema version. They are
registers of the profile in the image's `SyncFieldVersions`
(`MedicineId` and `EntityId` empty, register `Profile.<setting>`); the
device that writes a genesis records the values it holds as genesis
versions when the profile has none. Each device copies the winning
values into its own profile name and notification settings.

Medicine fields (`MedicineFieldChanged.field`): `Name`,
`ActiveIngredient`, `Package`, `Unit`, `ThresholdDays`, `DoctorName`,
`Notes`, `NotificationChannels` (integer flags), `RemindOnDose`
(`true`/`false`), `NationalCode`, `AtcCode`,
`LinkedReferenceMedicineId`, `EndDate`. Values are text, `null` for no
value.

`baseVersion` (`physicalMs`, `counter`, `deviceId`, or `null`): the
version of the register the writer held. Two writes are concurrent when
the later one's `baseVersion` is older than the earlier one.

---

## 7. Lifecycle

- **Create**: the first device writes `group.json`, `key.1.wrap`,
  `genesis/1.mrg` and its device record.
- **Sync run**: publish the device's new operations as the next
  segment; apply the other devices' segments in causal order; rewrite
  the device record; every 5 000 operations write a checkpoint (the
  device keeps its newest one only); delete the device's own segments
  that a checkpoint covers and every device seen in the last 90 days
  has applied.
- **Join**: obtain the key with the passphrase (the wrap of the
  current key version) or a pairing code (§4.4), build a new database
  from an image of the current generation (§5.3), write the device
  record, then sync.
- **New generation**: after an import or a restore on a synced device,
  that device writes `genesis/<generation + 1>.mrg`. The other devices
  publish their pending operations of the old generation, stop, and
  rebuild from the new genesis; those operations are not applied.
- Files of an old generation are not read again.
- **Key rotation** (to remove a device, or after the passphrase
  leaked): the rotating device writes `key.<v + 1>.wrap` with a new
  passphrase, then `genesis/<generation + 1>.mrg` sealed with key
  `v + 1`. A removed device is simply not given the new key. Any other
  device that finds a newer genesis sealed with a key version above
  its own publishes nothing more (its old key may be held by the
  removed device) until it obtains the new key, by the new passphrase
  or a pairing code. It then builds a new database from the new
  generation and publishes again, in that generation, its own
  operations of the old generation that the new genesis does not hold
  (same ids, same timestamps). Operations for a medicine absent from
  the new generation are not carried over.
- A newer genesis sealed with an **older** key version than the
  reader's is ignored: a removed device still holds the older key.

Key rotation and pairing files keep format version 1: the envelope, the
header and the existing files are unchanged, and older readers ignore
`pairing/`. An app without key rotation (MedReminder 2.8.x and earlier)
cannot follow a rotation: it sees a newer generation it cannot open and
must be updated before it can take the new key.

---

## 8. Evolution discipline

- A new operation type or payload field raises the operation schema
  version; an older app stops at such an operation (§5.1).
- A schema change that adds replicated data raises the image schema
  version.
- A change to the envelope, the layout or the header raises
  `formatVersion`.

---

## 9. Household group

A household (the installation spread over several devices, household
feature, `docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md`) is a group
of its own next to the profile groups, under the same sync root.

### 9.1 Layout

```
<householdId>/
  household.json                               cleartext, written once
  key.<keyVersion>.wrap                        household key, wrapped with the household passphrase (§4.1)
  recovery.<keyVersion>.wrap                   recovery private key, wrapped with the household passphrase
  genesis/<generation>.mrg                     household image
  ops/<generation>/<deviceId>/<seq>.mrs        household segments
  devices/<deviceId>.mrd                       device record (§5.2)
  pairing/<deviceId>.mrp                       pairing offer (§9.5), while a code is shown
```

`household.json` is `{ "format": "MedReminder.Household",
"formatVersion": 1, "householdId": "…" }`. There is no `group.json`: a
reader that lists `group.json` files never takes a household for a
profile group. Key wrap, envelope, header and device record are those
of §4 and §5.2, with the household id in the place of the group id.
There are no checkpoints.

### 9.2 Content

Segments and the genesis hold the same JSON:

```json
{
  "dependencies": { "<deviceId>": 3 },
  "operations": [
    {
      "id": "…", "physicalMs": 1790000000000, "counter": 0, "deviceId": "…",
      "type": "ProfileRoleChanged", "schemaVersion": 1,
      "profileId": "default", "payload": "{…}"
    }
  ]
}
```

The genesis holds every operation of the household when it was
published, with empty dependencies. Header `contentVersion` is 1.

### 9.3 Operation catalogue (household schema version 1)

| `type` | Payload fields | Merge rule |
|---|---|---|
| `ProfileRegistered` | `displayName`, `role`, `createdAt` | the profile exists from then on; name and role are registers |
| `ProfileRenamed` | `displayName` | last writer wins |
| `ProfileRoleChanged` | `role` (`admin`, `user`) | last writer wins |
| `ProfilePinChanged` | `hash`, `salt`, `iterations` (PBKDF2-HMAC-SHA256, base64; `null`, `null`, `0` when cleared) | last writer wins |
| `ProfileRemoved` | — | tombstone: the profile never comes back |
| `HouseholdSettingChanged` | `setting`, `value` | last writer wins per setting; `profileId` empty |
| `DeviceKeyPublished` | `deviceId`, `publicKey` | last writer wins; `profileId` is `device:<deviceId>` |
| `RecoveryKeyPublished` | `keyVersion`, `publicKey` | last writer wins; `profileId` is `recovery` |
| `ProfileKeyGranted` | `deviceId`, `groupId`, `keyVersion`, `wrappedKey` | last writer wins per profile and device |
| `ProfileKeyRevoked` | `deviceId` | clears the grant of that device |
| `ProfileKeyEscrowed` | `groupId`, `keyVersion`, `wrappedKey` | last writer wins per profile |
| `MasterElected` | `electionId`, `deviceId`, `electedBy`, `kind` (`Creation`, `Planned`, `Takeover`) | last writer wins; `profileId` is `master` |
| `MasterActivated` | `electionId`, `deviceId` | last writer wins; counts only for the current election |
| `MasterReleased` | `electionId` | last writer wins; the outgoing master stopped for that election |
| `DeviceRemoved` | `deviceId` | the device's public key no longer counts; `profileId` is `device:<deviceId>` |

A removal changes the household key: `key.<v+1>.wrap` and
`recovery.<v+1>.wrap` with the new household passphrase, then the
genesis of a new generation, sealed with the new key, holding the whole
log. A device holding an older key publishes nothing more and takes the
new key with the new passphrase or a code; its own operations the new
genesis lacks are published again in the new generation, where receivers
skip those they have by id.

Settings: `Smtp.Host`, `Smtp.Port`, `Smtp.UseStartTls`,
`Smtp.Username`, `Smtp.FromAddress`, `Smtp.FromDisplayName`,
`Smtp.TimeoutSeconds`, `Smtp.Password`, `CloudBackup.Enabled`,
`CloudBackup.Retention`, `CloudBackup.Provider`,
`CloudBackup.AccountId`, `ReferenceCountry`. Values are invariant text
(`true` / `false`, decimal integers, the provider by name). Every
payload also carries `profileId`.

`Smtp.Password` is the only secret: it is in clear inside the encrypted
segment and nowhere else; each device keeps it protected with its own
credential protector.

### 9.4 Keys

Public keys are the SubjectPublicKeyInfo of an ECDH P-256 key, base64.
A device publishes its own; the recovery public key is published with
the household, its private key (PKCS#8) is in `recovery.<v>.wrap`: the
key wrap of §4.1 with associated data
`MedReminder.Sync.Recovery|<householdId N format>|<v>`.

A wrapped key is `1.<ephemeral public key>.<nonce>.<tag>.<ciphertext>`,
each part base64url without padding: a fresh ephemeral P-256 key,
ECDH with the recipient's public key, HKDF-SHA256 (salt: the ephemeral
public key, info: the purpose) to a 32-byte key, AES-256-GCM with the
purpose as associated data. Purposes:

| Use | Purpose |
|---|---|
| Grant to a device | `MedReminder.Household.Grant|<groupId N>|<keyVersion>|<deviceId N>` |
| Escrow for the recovery key | `MedReminder.Household.Escrow|<groupId N>|<keyVersion>` |

The group key of a profile is thus readable by the devices it was
granted to, and by whoever types the household passphrase.

### 9.5 Household pairing

A household pairing code has the parts of `mrpair1` (§4.4) with the
household id in place of the group id:

```
mrpair2.<householdId N>.<deviceId N>.<provider>.<secret>
```

The offer `<householdId>/pairing/<deviceId>.mrp` has the cleartext
fields of a group pairing file (`formatVersion`, `householdId`,
`deviceId`, `nonce`, `tag`, `ciphertext`). The ciphertext holds the
household key version and key, the profiles offered (`profileId`,
`groupId`, `keyVersion`, `key`) and the expiry, encrypted with the
secret of the code (AES-256-GCM, associated data
`MedReminder.Household.Pairing|<householdId N>|<deviceId N>`). Only an
admin writes an offer; it lasts 10 minutes and is deleted when the
window closes. The joining device records `ProfileKeyGranted` for its
own public key for each profile offered, so it keeps them after the
offer ends. A reader of `mrpair1` codes refuses an `mrpair2` code.

A profile brought by an installation join is built from its profile
group in the storage of the household: an installation keeps its
profile groups and its household in one storage.

# MedReminder — Sync Storage Format

This document is the **public contract** for the files MedReminder
writes to a sync folder when several devices share a profile. It
describes the on-disk format only. The design rationale (merge rules,
security model, phases) is in
[`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md).

Format version: **1**. Operation catalogue: schema version **1**.
Database image: schema version **1**.

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
```

The sync root is the folder chosen by the user, or, for a provider
reached through its API, the `sync/` folder of the app's folder in the
user's account (OneDrive: `/Apps/<app>/sync/`). The layout below that
root is the same.

- `groupId`, `deviceId`: GUIDs, 32 lowercase hexadecimal digits (`N`
  format).
- `generation`, `keyVersion`, `seq`, `n`: decimal integers starting at
  1.
- The current generation is the highest `genesis/<generation>.mrg`.
  The current key version is the highest `key.<keyVersion>.wrap`.
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

### 4.2 Encrypted file envelope

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
derived stock movements (`Origin` 3), notification and dose-reminder
events, hint conflicts (kinds 4 to 6), sync progress (`SyncPeers`) and
the reference catalogue. It keeps the facts, the frozen (`Legacy`)
movements and the cutoff, the register versions (`SyncFieldVersions`),
the tombstones (`FactRetractions`), the register conflicts and the
operation log (`SyncOperations`).

A device joins from the newest checkpoint whose `vector` covers every
device folder's first remaining segment (`vector[d] >= first − 1`), or
from the genesis when no segment was deleted. It then applies the
segments after that vector. A reader refuses an image whose
`snapshotSchema` is higher than its own.

---

## 6. Operation catalogue (schema version 1)

One operation per user fact or per changed register; derived values
(consumption, count corrections, stock epoch, the current schedule on
the medicine) are never operations. Every payload has `medicineId`.
Ids are those of the rows written, so replaying an operation changes
nothing.

| `type` | Payload fields | Merge rule |
|---|---|---|
| `MedicineCreated` | `startDate`, `createdAt`, `fields[]` (`field`, `value`) | new medicine; every field versioned at the operation's timestamp |
| `MedicineFieldChanged` | `field`, `value`, `baseVersion` | last writer wins per field |
| `MedicineActivityChanged` | `changeId`, `day`, `active`, `recordedAt` | dated fact; the current value is the latest by timestamp |
| `ScheduleRowRecorded` | `rowId`, `effectiveFrom`, `dosePerAdministration`, `administrationsPerDay`, `scheduleKind`, `schedulePayload`, `recordedAt`, `baseVersion` | fact; the latest row of a date wins |
| `SlotSetRecorded` | `setId`, `effectiveFrom`, `recordedAt`, `slots[]` (`slotId`, `dose`, `time`, `timingLabel`, `order`), `baseVersion` | fact; the latest set in force applies |
| `StockEntryRecorded` | `movementId`, `kind`, `quantityDelta`, `occurredAt`, `notes` | fact |
| `IntakeRecorded` | `intakeId`, `day`, `status`, `quantity`, `scheduledAt`, `actualAt`, `notes`, `recordedAt` | fact |
| `StockCountRecorded` | `countId`, `countDay`, `countedQuantity`, `takenToday`, `thresholdAtCount`, `recordedAt`, `notes`, stored outcome | fact; its outcome is evaluated again on the facts recorded before it |
| `SuspensionRecorded` | `suspensionId`, `startDate`, `endDate`, `reason`, `recordedAt` | fact |
| `SuspensionEndChanged` | `suspensionId`, `endDate` | last writer wins |
| `FactRetracted` | `retractionId`, `kind`, `factId`, `recordedAt` | the fact is removed whatever the order of arrival |

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
- **Join**: unwrap the key with the passphrase, build a new database
  from an image (§5.3), write the device record, then sync.
- **New generation**: after an import or a restore on a synced device,
  that device writes `genesis/<generation + 1>.mrg`. The other devices
  publish their pending operations of the old generation, stop, and
  rebuild from the new genesis; those operations are not applied.
- Files of an old generation are not read again.

Not in format version 1: key rotation and device revocation (a new
`keyVersion`), planned with QR pairing.

---

## 8. Evolution discipline

- A new operation type or payload field raises the operation schema
  version; an older app stops at such an operation (§5.1).
- A schema change that adds replicated data raises the image schema
  version.
- A change to the envelope, the layout or the header raises
  `formatVersion`.

# MedReminder — Export Archive Format

This document is the **public contract** for the MedReminder encrypted
export archive (the `.mrz` file produced by **Settings → Backup →
Export all data**). It is complete enough for a third party to write an
independent decrypter and reader without the application, so a user can
migrate their data away without lock-in.

The feature is described in
[`docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md`](analysis/ANALYSIS-C3-EXPORT-IMPORT.md).
This file documents the on-disk format only; it is not the design
rationale.

---

## 1. Container

An archive is a plain **ZIP** file. The default extension is `.mrz`, but
the extension carries no meaning — the format is identified by the
`manifest.json` entry, not by the file name.

A valid archive contains exactly two entries:

```
export.mrz  (ZIP container)
├── manifest.json     cleartext metadata (schema, KDF, cipher, hash)
└── payload.enc       AES-GCM ciphertext of payload.json
```

Any other entry in the ZIP is ignored on import. The nonce and the
authentication tag are **not** separate files — they travel inside
`manifest.json` (§2).

`manifest.json` is cleartext by design: it must be inspectable without
the passphrase, and it contains no medical data. `payload.enc` is the
raw AES-GCM ciphertext of the UTF-8 bytes of `payload.json` (§3), with
no length prefix and no appended tag.

---

## 2. `manifest.json`

UTF-8 JSON, camelCase property names. Example:

```json
{
  "format": "medreminder-export",
  "formatVersion": 1,
  "appVersion": "2.3.2",
  "createdAtUtc": "2026-09-24T14:03:22+00:00",
  "scope": "profile",
  "profileId": "3f2a…",
  "kdf": {
    "algorithm": "Argon2id",
    "iterations": 3,
    "memoryKiB": 65536,
    "parallelism": 1,
    "saltBase64": "…16 bytes, base64…"
  },
  "cipher": {
    "algorithm": "AES-GCM",
    "keyBits": 256,
    "nonceBase64": "…12 bytes, base64…",
    "tagBase64": "…16 bytes, base64…"
  },
  "payload": {
    "sha256Base64": "…32 bytes, base64…",
    "sizeBytes": 12345
  },
  "includes": {
    "smtpCredential": false,
    "smtpSettings": false,
    "userSettings": true,
    "backupSettings": true
  }
}
```

Field by field:

| Field | Type | Meaning |
|---|---|---|
| `format` | string | Always `"medreminder-export"`. A reader must reject anything else. |
| `formatVersion` | int | Archive envelope version. A reader of version *N* accepts archives with `formatVersion <= N` and rejects newer ones. Current: `1`. |
| `appVersion` | string | MedReminder version that produced the archive. Informational only. |
| `createdAtUtc` | string | Export instant, ISO-8601, UTC. |
| `scope` | string | `"profile"` (single profile). `"all-profiles"` is reserved for a future version and is not produced by the current build. |
| `profileId` | string | The exported profile's id. Present when `scope == "profile"`. |
| `kdf.algorithm` | string | Always `"Argon2id"`. |
| `kdf.iterations` | int | Argon2id time cost (passes). |
| `kdf.memoryKiB` | int | Argon2id memory cost, in KiB. |
| `kdf.parallelism` | int | Argon2id degree of parallelism (lanes). |
| `kdf.saltBase64` | string | 16-byte random salt, base64. |
| `cipher.algorithm` | string | Always `"AES-GCM"`. |
| `cipher.keyBits` | int | Always `256`. |
| `cipher.nonceBase64` | string | 12-byte random nonce, base64. |
| `cipher.tagBase64` | string | 16-byte GCM authentication tag, base64. |
| `payload.sha256Base64` | string | SHA-256 of the **decrypted** `payload.json` bytes, base64. |
| `payload.sizeBytes` | int | Length in bytes of the decrypted `payload.json`. |
| `includes.smtpCredential` | bool | The archive carries the re-encrypted SMTP password. |
| `includes.smtpSettings` | bool | The archive carries SMTP transport settings. |
| `includes.userSettings` | bool | The archive carries user preferences. |
| `includes.backupSettings` | bool | The archive carries backup preferences. |
| `source` | string, optional | `"automatic"` when the archive was produced by the C.3+ automatic cloud-folder backup. Absent (or `"user"`) when the archive was produced by the user-triggered **Export all data** dialog. Additive since C.3+: readers that ignore it lose nothing. |
| `device` | object, optional | Present on automatic cloud-folder snapshots only. See below. |
| `device.hostNameSha256` | string | SHA-256 of the source machine's plain host name, hex-encoded, lower-case. Never the plain name. Lets a restore dialog group snapshots by originating machine without disclosing it. |
| `device.profileId` | string | Convenience mirror of the top-level `profileId`, so a folder-listing UI can group snapshots by profile without decrypting anything. |

The KDF and cipher parameters live in the manifest, not in the reader,
so the reader honours what the archive declares. A future build may
strengthen the defaults without breaking older archives.

The `source` and `device` fields are additive: an older reader
ignores unknown keys and still imports the archive normally. The
current build sets both only on the automatic scheduled cloud-folder
snapshot; a manually-triggered export from the Settings dialog omits
them.

---

## 3. `payload.json`

UTF-8 JSON, camelCase, ISO-8601 timestamps, no BOM. This is the
plaintext that `payload.enc` decrypts to. Its integrity is verified
against `manifest.payload.sha256Base64` after decryption.

```json
{
  "schemaVersion": 1,
  "profile": { "id": "…", "displayName": "…", "role": "Admin", "createdAt": "…" },
  "medicines": [ … ],
  "stockMovements": [ … ],
  "medicationScheduleHistory": [ … ],
  "medicationAdministrationSlots": [ … ],
  "medicationSuspensions": [ … ],
  "medicationIntakes": [ … ],
  "notificationEvents": [ … ],
  "doseReminderEvents": [ … ],
  "notificationSettings": { … },
  "shared": { … }
}
```

- **`schemaVersion`** (int) tracks the entity model, independently of
  the manifest's `formatVersion`. A reader accepts
  `schemaVersion <= currentSchemaVersion` (current: `1`) and rejects
  newer. An **older** `schemaVersion` imports fine: fields absent from
  the older archive take their defaults.
- **`profile`** is a descriptive header (not restored as a database
  row): `id`, `displayName`, `role` (`"User"` / `"Admin"`), `createdAt`.
- **`notificationSettings`** is this profile's per-profile recipient
  configuration; it travels with the profile.
- **`shared`** holds the opt-in non-database settings (§3.10).

Enumerations are serialized by **name** (e.g. `"Both"`, `"InitialLoad"`),
not by number. `Guid` values are the standard 8-4-4-4-12 hex string.
`DateOnly` is `"yyyy-MM-dd"`; `TimeOnly` is `"HH:mm:ss"`;
`DateTimeOffset` is ISO-8601. Decimal values are serialized as JSON
numbers.

### 3.1 `medicines[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `name` | string | |
| `activeIngredient` | string? | |
| `package` | string? | |
| `unit` | string | |
| `dosePerAdministration` | decimal | |
| `administrationsPerDay` | int | |
| `startDate` | DateOnly | |
| `endDate` | DateOnly? | |
| `thresholdDays` | int | |
| `doctorName` | string? | |
| `notes` | string? | |
| `isActive` | bool | |
| `stockEpoch` | int | |
| `notificationChannels` | string | `None` / `Email` / `Windows` / `Both` |
| `createdAt` | DateTimeOffset | |
| `updatedAt` | DateTimeOffset | |
| `remindOnDose` | bool | |
| `nationalCode` | string? | catalogue link (null when unlinked) |
| `atcCode` | string? | 7-char ATC code (null when unlinked) |
| `linkedReferenceMedicineId` | Guid? | |

### 3.2 `stockMovements[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `occurredAt` | DateTimeOffset | |
| `kind` | string | `InitialLoad` / `NewPackage` / `ManualAdd` / `Consumption` / `PositiveCorrection` / `NegativeCorrection` |
| `quantityDelta` | decimal | |
| `stockEpoch` | int | |
| `notes` | string? | |

### 3.3 `medicationScheduleHistory[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `effectiveFrom` | DateOnly | |
| `dosePerAdministration` | decimal | |
| `administrationsPerDay` | int | |
| `scheduleKind` | string | `FixedDaily` / `Weekly` / `Cyclic` / `Tapering` / `Prn` / `SteppedTapering` |
| `schedulePayload` | string? | JSON payload for non-FixedDaily kinds |

### 3.4 `medicationAdministrationSlots[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `dose` | decimal | |
| `time` | TimeOnly? | |
| `timingLabel` | string? | |
| `order` | int | |

### 3.5 `medicationSuspensions[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `startDate` | DateOnly | |
| `endDate` | DateOnly? | null = open suspension |
| `reason` | string? | |

### 3.6 `medicationIntakes[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `day` | DateOnly | local calendar day |
| `scheduledAt` | DateTimeOffset? | |
| `actualAt` | DateTimeOffset? | |
| `quantity` | decimal | |
| `status` | string | `Taken` / `Skipped` / `Cancelled` / `ManualCorrection` |
| `notes` | string? | |

### 3.7 `notificationEvents[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `stockEpoch` | int | |
| `triggeredAt` | DateTimeOffset | |
| `channel` | string | `None` / `Email` / `Windows` / `Both` |
| `daysRemainingAtSend` | int | |
| `success` | bool | |
| `errorMessage` | string? | |

### 3.8 `doseReminderEvents[]`

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `medicineId` | Guid | parent medicine |
| `slotKey` | string | slot id or `HH:mm` fallback |
| `localDate` | DateOnly | local calendar day |
| `firedAt` | DateTimeOffset | |
| `channel` | string | `None` / `Email` / `Windows` / `Both` |

### 3.9 `notificationSettings`

| Field | Type | Notes |
|---|---|---|
| `toAddress` | string | primary recipient (may be empty) |
| `caregiverAddress` | string | optional secondary recipient (may be empty) |

### 3.10 `shared`

Every property is present only when the user opted the corresponding
item into the export; otherwise it is omitted.

- `userSettings` — `{ language, referenceCountry, checkForUpdatesOnStartup }`
- `backupSettings` — `{ enabled, directory, preferredTime, retentionDays }`
- `smtpSettings` — `{ host, port, useStartTls, username, fromAddress, fromDisplayName, timeoutSeconds }` (never contains the password)
- `smtpPasswordEncrypted` — the SMTP password re-encrypted with the
  archive key (§4), as `{ nonceBase64, tagBase64, ciphertextBase64 }`.
  Present only when the user opted the password in.

---

## 4. Cryptography

### 4.1 Key derivation (Argon2id)

```
key = Argon2id(
    password    = UTF-8(passphrase),
    salt        = base64_decode(manifest.kdf.saltBase64),
    iterations  = manifest.kdf.iterations,
    memoryKiB   = manifest.kdf.memoryKiB,
    parallelism = manifest.kdf.parallelism,
    outputLen   = 32 bytes)
```

Current defaults: `iterations = 3`, `memoryKiB = 65536` (64 MiB),
`parallelism = 1`, salt = 16 random bytes. The derived key is 32 bytes
(AES-256).

### 4.2 Payload cipher (AES-256-GCM)

```
payload.json = AES-GCM-Decrypt(
    key        = key,
    nonce      = base64_decode(manifest.cipher.nonceBase64),   // 12 bytes
    ciphertext = bytes of the payload.enc ZIP entry,
    tag        = base64_decode(manifest.cipher.tagBase64))      // 16 bytes
```

No additional authenticated data (AAD) is used. A GCM tag mismatch means
either a wrong passphrase or a tampered archive — the two are
indistinguishable by design, and the application reports both as
"the passphrase does not match this file".

After a successful decrypt, verify `SHA-256(payload.json)` against
`manifest.payload.sha256Base64`; a mismatch means a corrupt archive.

### 4.3 SMTP password (`shared.smtpPasswordEncrypted`)

The SMTP password, when included, is encrypted with the **same archive
key** under its own random nonce:

```
password = UTF-8-decode(AES-GCM-Decrypt(
    key        = key,
    nonce      = base64_decode(smtpPasswordEncrypted.nonceBase64),
    ciphertext = base64_decode(smtpPasswordEncrypted.ciphertextBase64),
    tag        = base64_decode(smtpPasswordEncrypted.tagBase64)))
```

The application re-wraps this password with Windows DPAPI on the target
machine at import time. The archive never contains a raw DPAPI blob (a
DPAPI blob is bound to one Windows account and would be useless on
another machine).

---

## 5. Evolution discipline

- `formatVersion` (manifest) and `schemaVersion` (payload) are additive
  and move independently.
- A reader of version *N* MUST accept archives of versions `<= N` and
  MUST refuse versions `> N` with a clear error.
- New fields are added as optional; readers of an older archive take
  defaults for fields that did not exist yet.
- Existing field names, types and enum names are stable within a major
  format version.

---

## 6. Decrypting with off-the-shelf tools

The archive is a plain ZIP plus standard Argon2id and AES-256-GCM, so it
can be read without MedReminder. The sketch below (Python, using
`argon2-cffi` and `cryptography`) reproduces the full decryption:

```python
import base64, json, zipfile
from argon2.low_level import hash_secret_raw, Type
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

def decrypt_mrz(path, passphrase):
    with zipfile.ZipFile(path) as z:
        manifest = json.loads(z.read("manifest.json"))
        ciphertext = z.read("payload.enc")

    if manifest["format"] != "medreminder-export":
        raise ValueError("not a MedReminder export")

    kdf = manifest["kdf"]
    key = hash_secret_raw(
        secret=passphrase.encode("utf-8"),
        salt=base64.b64decode(kdf["saltBase64"]),
        time_cost=kdf["iterations"],
        memory_cost=kdf["memoryKiB"],
        parallelism=kdf["parallelism"],
        hash_len=32,
        type=Type.ID,           # Argon2id
    )

    cipher = manifest["cipher"]
    nonce = base64.b64decode(cipher["nonceBase64"])
    tag = base64.b64decode(cipher["tagBase64"])
    # AES-GCM in the cryptography library expects ciphertext || tag.
    plaintext = AESGCM(key).decrypt(nonce, ciphertext + tag, None)

    payload = json.loads(plaintext)
    # Optional: verify the hash.
    import hashlib
    assert base64.b64encode(hashlib.sha256(plaintext).digest()).decode() \
        == manifest["payload"]["sha256Base64"]
    return payload

if __name__ == "__main__":
    import sys, getpass
    data = decrypt_mrz(sys.argv[1], getpass.getpass("Passphrase: "))
    print(json.dumps(data, indent=2, ensure_ascii=False))
```

Notes:

- MedReminder stores the GCM tag separately (in the manifest). Libraries
  that expect `ciphertext || tag` — like the example above — must
  append the tag before decrypting.
- The salt, nonce, tag and all KDF parameters come from the manifest;
  nothing is hard-coded in the reader.
- There is **no passphrase recovery**. A lost passphrase means the
  archive cannot be read, by anyone, ever.

# Data Protection Impact Assessment (DPIA)
## MedReminder — Vital-parameter tracking feature (D.1)

**Reference regulation**: EU General Data Protection Regulation
2016/679 (GDPR), Article 35 — Data Protection Impact Assessment.

**Prepared by**: vger70 (controller / developer).
**Date**: 7 October 2026.
**Version**: 1.0 — initial draft, pending DPO/legal review before feature release.
**Related documents**:
- `docs/analysis/ANALYSIS-D1-VITAL-TRACKING.md` (technical design)
- `PRIVACY.md` (current privacy policy)
- Legal-compliance guide — Guida alla Conformità Legale e Privacy (Scenario A: Non-MDR)

---

## Part 1 — Is a DPIA required?

GDPR Art. 35(1) requires a DPIA when processing "is likely to result in
a high risk to the rights and freedoms of natural persons". Art. 35(3)b
specifies that large-scale processing of **special-category data (Art. 9)**
always triggers the obligation.

**Assessment**: vital-parameter readings (blood pressure, heart rate,
blood glucose, body weight, SpO₂, body temperature) are **health data**
under Art. 4(15) and therefore special-category data under Art. 9(1).
The DPIA is **mandatory** regardless of scale, because the data category
alone satisfies Art. 35(3)b.

---

## Part 2 — Description of the processing

### 2.1 Controller

| Field | Value |
|---|---|
| Name / handle | vger70 |
| Contact | info@medreminder26.org |
| Role | Controller (the developer processes no data; all processing is by the user on their own device — see §2.5) |

### 2.2 Purpose and legal basis

| Purpose | Legal basis |
|---|---|
| Storing a user's self-entered vital-parameter readings on their own device for personal reference and historical review | Art. 9.2.a GDPR — **explicit consent** of the data subject, collected via the in-app consent screen (double opt-in, non-pre-selected checkboxes) at first use of the Vitals section |

There is **no secondary purpose**. The data is not used for analytics,
advertising, research, or any purpose other than displaying the user's
own history back to them.

### 2.3 Categories of data and data subjects

| Category | Description | GDPR classification |
|---|---|---|
| Vital-parameter readings | Type (e.g. blood pressure), numeric value(s), user-chosen timestamp, optional free-text note | **Special-category health data — Art. 9(1)** |
| Profile context | Internal profile ID (a UUID, not a real-world identifier) | Personal data — Art. 4(1) |

Data subjects: the individual users of MedReminder, who are the same
persons who enter their own data. No data concerning third parties is
collected by this feature.

### 2.4 Data flows

```
User (manual entry)
        │
        ▼
In-app input form
        │
        ▼
Use case: LogVitalReading
        │
        ▼
VitalReadingRepository
        │
        ▼
MedReminderDbContext ──► medreminder.db (SQLite)
                          %LOCALAPPDATA%\MedReminder\profiles\<id>\
                          (Desktop)
                          /data/data/…/MedReminder/databases/
                          (Android — app-private sandbox)
        │
        ├──[if sync enabled]──► Sync engine (B.1)
        │                         end-to-end encrypted payload
        │                         ──► user's OneDrive or Google Drive
        │                         ──► peer device (Desktop or Android)
        │
        └──[if cloud backup]──► .mrz archive (AES-GCM encrypted)
                                 ──► user's OneDrive or Google Drive
```

**No data is sent to the developer's infrastructure at any point.**
The developer operates no server, no telemetry endpoint, no analytics
pipeline. The "controller" role is nominal: the actual processing takes
place on the user's device, on the user's behalf, making this closer to
the Art. 2.2.c household-activity exemption — but Art. 9 compliance is
maintained regardless.

### 2.5 Recipients

| Recipient | What they receive | Basis |
|---|---|---|
| The user themselves | All data they entered, in plaintext, in the app | Implicit — the user is the data subject |
| Peer device (sync, optional) | Vital readings, end-to-end encrypted with a key the user holds; the cloud provider (OneDrive/Google Drive) sees only ciphertext | User's decision to enable sync; GDPR Art. 6.1.a and 9.2.a consent |
| Cloud provider (OneDrive or Google Drive, optional) | Encrypted `.mrz` archive; no plaintext health data | User's decision to enable cloud backup; data processed under the provider's own DPA |

No data processor agreement is required with Microsoft or Google for
the cloud storage: MedReminder stores only encrypted blobs whose key
never leaves the user's device; the providers are storage utilities
without access to the content.

### 2.6 Retention

The data is retained on the user's device until:

- The user deletes individual readings.
- The user deletes all vitals data (Settings → Privacy).
- The user deletes their profile.
- The user uninstalls MedReminder and manually removes
  `%LOCALAPPDATA%\MedReminder\` (Desktop) or clears app data (Android).

There is no automated expiry. For a personal diary application with no
server component, manual retention under the user's full control satisfies
the **storage-limitation principle** (Art. 5.1.e): the user is both
controller-in-practice and data subject, and decides what is retained.

### 2.7 Automated decision-making

**None.** The application does not perform automated decision-making or
profiling in the sense of Art. 22. The chart is a visual representation
of data the user entered; no algorithm interprets values, flags
anomalies or influences medical treatment.

---

## Part 3 — Necessity and proportionality

### 3.1 Is the processing necessary for the stated purpose?

Yes. To show the user a chronological history of their vital parameters,
the application must store those readings. There is no less-intrusive
alternative that would satisfy the purpose.

### 3.2 Data minimization (Art. 5.1.c)

| Data element | Justification |
|---|---|
| Type key + unit | Required to label the chart axes and group readings |
| Primary value (and secondary for BP) | The reading itself; without it the feature does not exist |
| Timestamp (`RecordedAt`) | Required to place the reading on the time axis |
| Note (optional, max 500 chars) | User-initiated; the field is optional and the app stores it only when the user provides it |
| `CreatedAt` (internal) | Required for CRDT sync conflict resolution; not exposed in the UI |

No data element beyond this list is collected. No real-world identifier
(name, national ID, date of birth, address) is collected by the Vitals
feature. The profile UUID used as context is already present in the
existing database for medicine tracking.

### 3.3 User control

The user has full control over their data:

- **Access**: all readings are visible in the Vitals section.
- **Correction**: readings can be edited after the fact.
- **Deletion**: per-reading, per-type, or bulk (all vitals).
- **Portability (Art. 20)**: export as CSV (plain text, structured)
  and as part of the `.mrz` archive (machine-readable JSON).
- **Consent withdrawal**: the user may revoke consent from
  Settings → Privacy; the Vitals section is locked but data is
  retained until the user explicitly deletes it (consent withdrawal
  does not automatically erase data, consistent with Art. 7.3 read in
  light of Art. 17.1.b — the user must separately exercise the right
  to erasure).

---

## Part 4 — Risk assessment

### 4.1 Risk identification

| # | Risk | Likelihood | Severity | Overall |
|---|---|---|---|---|
| R1 | Unauthorised access by another person who obtains physical access to an unlocked device | Medium | High | **High** |
| R2 | Data loss due to device failure without backup | Medium | Medium | **Medium** |
| R3 | Unauthorised access via sync channel (MITM, compromised cloud account) | Low | High | **Medium** |
| R4 | Unintended disclosure via app log files | Low | High | **Medium** |
| R5 | Data retained beyond the user's expectation (no automated expiry) | Low | Medium | **Low** |
| R6 | Re-identification via combination of readings and other profile data | Low | Low | **Low** |
| R7 | Developer/third-party access to health data | Very low | High | **Low** |

### 4.2 Risk analysis

**R1 — Physical access to unlocked device (Desktop)**

The Desktop SQLite database was unencrypted at rest `[VERIFIED —
PRIVACY.md §3]`. Anyone with access to the Windows user account could
read `medreminder.db`, including the new `VitalReadings` table.

**Decision DA7 (product owner, 2026-10-07)**: SQLCipher database
encryption is a **D.1 prerequisite**. The database will be encrypted
with AES-256 before D.1 ships to users. A one-time migration path
re-encrypts existing databases on first open after the update. The
encryption key is stored via DPAPI (`ICredentialProtector`), which
ties it to the Windows user account.

*Residual risk after DA7*: **Low** — the database is encrypted at
rest; physical access to the machine does not yield plaintext health
data without the Windows account credentials.

**R1 — Physical access to unlocked device (Android)**

The Android database is protected by SQLCipher with a key in the
Android Keystore, within the app-private internal storage sandbox
`[INFERRED from ANALYSIS-B1-ANDROID-PLAN.md §4.7]`. Additionally,
app lock via `BiometricPrompt` is implemented at M1 `[VERIFIED in B.1
plan]`. The residual risk is low.

**R2 — Data loss**

MedReminder already offers encrypted `.mrz` export and, optionally,
encrypted cloud backup `[VERIFIED]`. Vital readings are included in
both (§4.3 of ANALYSIS-D1-VITAL-TRACKING.md). The backup reminder (no
backup in 30 days, sync off) also covers vital data.

*Residual risk*: **low**.

**R3 — Sync-channel interception**

The sync protocol uses end-to-end AES-GCM encryption with an
Argon2id-derived key `[VERIFIED — B.1 §18, spike S1]`. The cloud
provider sees only ciphertext. A MITM attack would require compromising
both the TLS layer and the AES-GCM envelope.

*Residual risk*: **low**.

**R4 — Log disclosure**

MedReminder already prohibits logging medical notes and passwords
`[VERIFIED — CLAUDE.md §7]`. The same prohibition is extended
explicitly to `VitalReading.Value`, `SecondaryValue` and `Note` fields.
A build-time lint rule enforces this.

*Residual risk*: **low** after the lint rule is in place.

**R5 — Retention**

No automated expiry exists. The user decides when to delete data. For a
local diary with no server side, this is compliant with
storage-limitation (Art. 5.1.e). The consent screen informs the user
that data is retained until explicitly deleted.

*Residual risk*: **low**.

**R6 — Re-identification**

The database contains no real-world identifier. The profile UUID is
device-local. Combining readings with medicine data could in theory
narrow the user's identity, but since both are on the same device and
under the same user's control, this is not a risk in the threat model
of a local-only app.

*Residual risk*: **negligible**.

**R7 — Developer access**

The developer operates no server. No telemetry, analytics or crash-
reporting SDK is integrated `[VERIFIED — PRIVACY.md §1]`. The
developer has no access to user data under any circumstances.

*Residual risk*: **negligible**.

---

## Part 5 — Measures adopted

### 5.1 Technical measures

| Measure | Status | Notes |
|---|---|---|
| Local-only storage by default | Implemented | No server component |
| End-to-end encrypted sync (AES-GCM + Argon2id) | Implemented (B.1) | Vital readings included in sync payload |
| Encrypted cloud backup (AES-GCM) | Implemented (C.3+) | Vital readings included in `.mrz` archive |
| No third-party analytics or tracking SDK | Implemented | PRIVACY.md §1 `[VERIFIED]` |
| Log policy: no health data in logs | Implemented + extended for vitals | Build-time lint rule added |
| Android: app-sandbox + SQLCipher + BiometricPrompt | Planned (B.1 M1) | Covers Android database and app lock |
| Desktop: SQLCipher database encryption | **Required in D.1** (DA7, 2026-10-07) | AES-256 at rest; key via DPAPI; one-time migration of existing databases; closes R1 Desktop |
| Data portability: CSV and `.mrz` export | Extended for vitals in D.1 | Satisfies GDPR Art. 20 |
| Explicit double opt-in consent screen | New in D.1 | Non-pre-selected checkboxes; medical disclaimer |
| Consent revocation UI | New in D.1 | Settings → Privacy |
| Per-reading and bulk deletion | New in D.1 | Satisfies GDPR Art. 17 |

### 5.2 Organisational measures

| Measure | Status |
|---|---|
| Medical disclaimer (4 paragraphs) displayed at first use and in Settings / Info | New in D.1 |
| Privacy Policy updated to include vitals data category, flows and retention | Required before release; not yet done |
| DPIA recorded and kept as documentation (this document) | This document |
| Consent timestamp stored per profile | New in D.1 |

### 5.3 Follow-on measures

**Decision DA7 (product owner, 2026-10-07)**: Desktop SQLCipher
encryption is no longer a follow-on recommendation — it is a D.1
prerequisite (§5.1 table above). No residual follow-on measures remain
from the original risk R1 assessment. The only outstanding
organisational measure before release is the Privacy Policy update
(§5.2), which is tracked as a release condition in §7.1.

---

## Part 6 — Consultation

### 6.1 Data subject consultation

MedReminder is a B2C consumer application. Individual consultation with
data subjects before the DPIA is completed is not practicable. However:

- The feature is opt-in: users who do not open the Vitals section are
  not affected.
- The consent screen provides full transparency before any data is
  collected.
- User feedback can be submitted via the project issue tracker
  (https://github.com/vger70/MedReminder/issues).

### 6.2 DPO / legal review

The developer (vger70) acts as sole controller. A formal DPO has not
been designated (not required under Art. 37 for this scale of
processing). However, **a review of this DPIA by a legal professional
or privacy consultant is strongly recommended before the feature is
released**, given the Art. 9 special-category data involved.

---

## Part 7 — Conclusion and approval

### 7.1 Residual risk summary

Reflects decisions DA1–DA8 confirmed by the product owner on 2026-10-07.

| Risk | Residual level | Note |
|---|---|---|
| R1 (physical access — Desktop) | **Low** | SQLCipher required in D.1 (DA7); closes R1 before vital data reaches production |
| R1 (physical access — Android) | Low | App-sandbox + SQLCipher + BiometricPrompt (B.1 M1) |
| R2 (data loss) | Low | `.mrz` export and optional cloud backup cover vitals |
| R3 (sync interception) | Low | AES-GCM end-to-end encryption (B.1) |
| R4 (log disclosure) | Low | Build-time lint rule prohibits logging `VitalReading` fields |
| R5 (retention) | Low | Manual retention under full user control; disclosed in consent screen |
| R6 (re-identification) | Negligible | No real-world identifier in the database |
| R7 (developer access) | Negligible | Developer operates no server; no telemetry |

The overall residual risk is **low and acceptable**.

### 7.2 Decision

Processing may proceed conditionally on **two items** being completed
before D.1 ships to users:

1. Medical disclaimer displayed at first use of the Vitals section
   (D.1 design §5.3) — implemented in D.1.
2. Privacy Policy (`PRIVACY.md` and four translations) updated to
   include the vitals data category, flows and retention — implemented
   in the same D.1 PR (§8.8 of the design document).

Desktop database encryption (SQLCipher) was elevated to a D.1
prerequisite by decision DA7 (2026-10-07); it is no longer a
conditional item — it must be implemented before D.1 compiles for
release.

| Field | Value |
|---|---|
| DPIA initial draft | 7 October 2026 |
| Decisions DA1–DA8 recorded | 7 October 2026 |
| Decision | Proceed — all conditions addressed in D.1 |
| Review date | Before D.1 release, or within 12 months if release is delayed |
| Author | vger70 |

---

*This DPIA must be updated whenever the processing described in
`ANALYSIS-D1-VITAL-TRACKING.md` changes materially — in particular if
sensor integration, clinical thresholds, alerting or a server component
is added.*

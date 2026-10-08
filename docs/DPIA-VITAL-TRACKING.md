# Data Protection Impact Assessment / Privacy Risk Assessment
## MedReminder — Vital-parameter recording feature (D.1)

**Reference regulation:** Regulation (EU) 2016/679 (GDPR), especially
Articles 2, 4, 5, 9 and 35.

**Prepared by:** MedReminder developer, for product accountability and
privacy-by-design review. This document does **not** by itself determine the
legal controller role for every MedReminder processing activity.

**Date:** 7 October 2026  
**Version:** 2.0 — revised after the D.1 architecture decision  
**Related documents:**
- `docs/analysis/ANALYSIS-D1-VITAL-TRACKING.md`
- `docs/analysis/ANALYSIS-B1-ANDROID-PLAN.md`
- `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
- `PRIVACY.md`

> **Placement (2026-10-08):** D.1 is evolution V, evaluated only after the
> store release of the Android and iOS apps
> (`docs/analysis/ANALYSIS-B1-ANDROID-PLAN.md` §4.3a). Android release 1
> (milestones M0–M5) processes no vital data and is not covered by this
> assessment. Review and update this assessment when evolution V is
> scheduled, including iOS, which it does not yet cover.

> **Important:** this document is a product privacy/risk assessment, not legal
> advice. The architecture deliberately prevents the developer from receiving
> D.1 vital data. A material change to that boundary requires this assessment
> to be reopened.

---

## Part 1 — Does GDPR require a DPIA for this exact processing?

GDPR Article 35(1) requires a DPIA where processing is likely to result in a
high risk to the rights and freedoms of natural persons. Article 35(3)(b)
expressly refers to **large-scale** processing of special-category data; it
does not say that any processing of health data, regardless of scale,
automatically requires a DPIA. citeturn0search10

D.1 is designed as a local-only diary. The developer does not receive the
vital readings, does not operate a vital-data server, and does not perform
remote analytics or profiling. Accordingly, this document does **not** claim
that Article 35(3)(b) automatically makes a DPIA mandatory merely because
vital readings are health data.

Nevertheless, the project maintains this assessment voluntarily as a
privacy-by-design and accountability measure because health data is sensitive,
D.1 creates a new data store, and exported CSV/PDF files can create additional
user-controlled copies. If a concrete deployment model introduces a GDPR
controller processing activity at scale or another high-risk processing
condition, the legal requirement must be reassessed before that processing
starts.

---

## Part 2 — Processing description and responsibility boundary

### 2.1 Responsibility matrix

| Activity | Where data exists | Developer receives it? | D.1 developer role |
|---|---|---:|---|
| Manual entry into `vitals.db` | User's Desktop/Android device | No | No controller role established for this local activity |
| Local chart/list display | User's device | No | No |
| CSV export | User-selected local file | No, unless user separately sends it | No |
| CSV import | User-selected local file → `vitals.db` | No | No |
| PDF export | User-selected local file | No | No |
| B.1 sync | **Not applicable to vitals** | No | Prohibited by D.1 architecture |
| Cloud backup | **Not applicable to vitals** | No | Prohibited by D.1 architecture |
| Developer account/billing/support processing | Separate MedReminder systems | Potentially | Separate GDPR analysis; outside this D.1 boundary |

The developer may have controller responsibilities for other MedReminder
processing, such as account, subscription, support or non-vital cloud
services. Those responsibilities must not be inferred to include local vital
data merely because the developer publishes the application.

### 2.2 Purpose

The purpose of D.1 is limited to enabling the user to record, store, review,
export and manually re-import their vital-parameter history for personal
reference.

There is no D.1 purpose for:

- analytics;
- advertising;
- profiling;
- research;
- clinical decision support;
- diagnosis or prognosis;
- treatment or dose recommendations;
- remote physiological monitoring.

### 2.3 Data categories

| Category | Description |
|---|---|
| Vital readings | Vital type, numeric value(s), unit, user-selected date/time |
| Optional note | Free text up to 500 characters |
| Technical timestamps | `CreatedAt`, `UpdatedAt` |
| Profile context | Profile-local UUID/context required to isolate the selected profile |

Vital readings can constitute health data under GDPR Article 4(15) and special-
category data under Article 9 when GDPR applies.

D.1 intentionally does not require name, national identifier, address,
location, diagnosis or sensor identifier.

### 2.4 Data subjects

The primary intended data subject is the person whose personal diary is being
recorded. The application must not assume that the person holding the device
and the person described by the readings are always the same individual; if a
caregiver records another person's health data, the applicable GDPR analysis
can differ. This is another reason the product must not present the household
exemption as an automatic legal conclusion.

### 2.5 Data flows

```text
                         ┌──────────────────────────────┐
                         │        User / device         │
                         └──────────────┬───────────────┘
                                        │ manual entry
                                        ▼
                              ┌───────────────────┐
                              │   Vitals UI       │
                              └─────────┬─────────┘
                                        ▼
                              ┌───────────────────┐
                              │ Vitals application│
                              │      layer        │
                              └─────────┬─────────┘
                                        ▼
                              ┌───────────────────┐
                              │    vitals.db      │
                              │ encrypted SQLite  │
                              └─────────┬─────────┘
                                        │
                    ┌───────────────────┼───────────────────┐
                    │                   │                   │
                    ▼                   ▼                   ▼
               CSV export          PDF export         local display
                    │                   │
                    ▼                   ▼
             user-selected       user-selected
             local destination   local destination

         X NO B.1 SYNC
         X NO .mrz inclusion
         X NO CLOUD BACKUP
         X NO DEVELOPER SERVER
```

The separation from `medreminder.db` is deliberate. No cross-database foreign
key exists. Vital data is not passed to the B.1 operation log or archive
writer.

### 2.6 Recipients

For D.1 there are no remote recipients. The only destination is the user's
local device or a local file destination explicitly selected by the user.

If the user manually sends a CSV/PDF to another person or service, that is a
separate user-initiated disclosure outside the D.1 application's automatic
data flow.

### 2.7 Retention

There is no developer-controlled server retention period for D.1 because the
developer does not receive the data. The application retains local readings
until the user deletes them or removes the local application/profile data.

Exported CSV/PDF files are independent copies and are not automatically
removed when the user deletes the in-app readings.

### 2.8 Automated decision-making

None. D.1 performs no profiling and makes no decisions with legal or similarly
significant effects. Charts are descriptive and do not classify readings as
normal/abnormal or recommend action.

---

## Part 3 — Necessity, proportionality and safeguards

### 3.1 Necessity

The minimum functionality required for the stated purpose is manual entry,
local persistence, list/chart display, deletion and user-controlled export.
Remote storage is not necessary, so it is deliberately excluded.

### 3.2 Data minimization

The feature stores only measurement values, units, dates/times and optional
notes required for the diary. No clinical interpretation or unrelated
identifier is collected.

A separate database reduces accidental disclosure through the normal
MedReminder sync/archive paths and provides a stronger technical boundary than
merely documenting an exclusion.

### 3.3 User control

The user can:

- view readings;
- edit readings;
- delete individual readings;
- delete all local vital data;
- export complete history to CSV;
- export selected history to PDF;
- manually import CSV using merge + deduplication.

No subscription state may silently delete vital data.

### 3.4 Data portability

CSV is the canonical machine-readable D.1 transfer format. It is independent
of `.mrz` and independent of cloud backup. This supports user control and,
where Article 20 applies to the relevant processing, facilitates structured
data portability. The product must not claim that Article 20 automatically
applies to every local-only household activity.

### 3.5 Security by separation

`medreminder.db` and `vitals.db` use separate encryption keys. D.1 does not
reuse the B.1 sync key as a database key and does not place the vital database
inside the `.mrz` archive.

---

## Part 4 — Risk assessment

### 4.1 Risk matrix

| # | Risk | Likelihood | Severity | Initial | Residual |
|---|---|---|---|---|---|
| R1 | Physical/local unauthorized access to an unlocked or compromised device | Medium | High | High | Low |
| R2 | Accidental inclusion of vital data in `.mrz` or cloud backup | Medium | High | High | Low |
| R3 | Accidental inclusion in B.1 sync operations | Low | High | Medium | Low |
| R4 | Disclosure through CSV/PDF exported by the user | Medium | High | High | Medium |
| R5 | Health data written to logs/diagnostics | Low | High | Medium | Low |
| R6 | Malicious or malformed CSV import | Low | Medium | Medium | Low |
| R7 | Wrong-profile import or accidental mixing of records | Low | High | Medium | Low |
| R8 | Excessive retention of local records | Low | Medium | Low | Low |
| R9 | Future feature drift into clinical/MDR processing | Medium | High | High | Low* |
| R10 | Developer/third-party access to vital data | Very low | High | Medium | Low |

`*` Low only while the current architecture and release gates remain in force.

### 4.2 R1 — local unauthorized access

The principal control is encrypted SQLite at rest plus OS/app access controls.
Desktop uses a DPAPI-protected key; Android uses Android Keystore-backed key
protection. The databases are separate, so compromise of the medicine DB key
does not automatically expose `vitals.db`.

Residual risk: **Low**, subject to implementation and platform security.

### 4.3 R2 — accidental archive/cloud inclusion

The strongest mitigation is architectural: the archive/backup layer does not
have access to `vitals.db` as an input. Automated tests must assert that an
`.mrz` archive contains no vital entities or database bytes.

Residual risk: **Low** after the boundary tests pass.

### 4.4 R3 — accidental synchronization

No `SyncOperation` type exists for vitals. `VitalReading` and `VitalType` are
not in the B.1 sync model, and no sync repository is injected into the Vitals
application layer.

Residual risk: **Low** after compile-time/integration tests and code review.

### 4.5 R4 — exported-file disclosure

CSV and PDF are deliberately user-accessible and may be plaintext files. The
application should show a concise warning that exported files are outside
MedReminder's database protection once saved elsewhere.

Residual risk: **Medium** because the user can intentionally copy the file to
an unprotected location. This is accepted because export is a core user-
controlled function and there is no automatic remote transfer.

### 4.6 R5 — logs and diagnostics

No value, note, CSV row or PDF content may be logged. Import diagnostics must
use row numbers/error categories rather than echoing health data.

Residual risk: **Low** after automated log tests.

### 4.7 R6 — malicious/malformed import

CSV import is parsed and validated before commit, runs in a transaction, and
uses stable IDs for deduplication. Invalid rows are rejected without inserting
partial invalid data.

Residual risk: **Low**.

### 4.8 R7 — wrong-profile import

The import flow displays the destination profile and requires explicit user
confirmation before committing. A CSV never silently switches the active
profile.

Residual risk: **Low**.

### 4.9 R8 — retention

There is no automatic deletion, but deletion is user-controlled and there is
no server-side copy. This is proportionate to a personal diary; the UI must
make bulk deletion discoverable without making accidental deletion easy.

Residual risk: **Low**.

### 4.10 R9 — regulatory/intended-purpose drift

The product can become materially different if future releases add clinical
thresholds, alerts, sensor ingestion, diagnostic analysis, therapy advice,
remote monitoring, AI interpretation or medical claims. MDCG Rule 11 is based
on intended purpose and the significance of information used for healthcare
decisions; software intended to monitor physiological processes or support
diagnostic/therapeutic decisions can fall within MDSW classification. citeturn0search34

Residual risk: **Low only with mandatory change control**. A future feature
cannot be treated as a routine extension of D.1 without reassessment.

### 4.11 R10 — developer/third-party access

The D.1 architecture provides no automated path for vital data to reach the
developer or a cloud provider. Any manual disclosure by the user is outside
the automatic D.1 flow.

Residual risk: **Low**, provided the architecture remains unchanged.

---

## Part 5 — Technical and organisational measures

### 5.1 Technical measures

| Measure | Status / release gate |
|---|---|
| Separate `vitals.db` | Mandatory |
| Separate encryption key | Mandatory |
| SQLCipher/equivalent at rest | Mandatory |
| Android Keystore-backed key protection | Mandatory on Android |
| No vital `SyncOperation` | Mandatory |
| No vital `.mrz` content | Mandatory |
| No vital cloud backup | Mandatory |
| CSV merge + deduplication by stable ID | Mandatory |
| PDF export-only | Mandatory |
| Log/diagnostic prohibition | Mandatory |
| Transactional CSV import | Mandatory |
| Explicit profile confirmation before import | Mandatory |
| User deletion controls | Mandatory |

### 5.2 Organisational measures

- Privacy Policy updated before release.
- Product/store/website copy reviewed for medical claims.
- D.1 design and this assessment reviewed when intended purpose changes.
- Legal/privacy review performed before adding any remote vital-data flow.
- Release checklist includes tests proving `.mrz`, sync and cloud exclusion.

### 5.3 No processor dependency for D.1 vital data

Because D.1 sends no vital data to a cloud provider, there is no D.1 processor
relationship created by OneDrive/Google Drive for vital data. Those providers
may have separate roles for other MedReminder data and must be assessed under
the general privacy architecture; that analysis is outside this document.

---

## Part 6 — Consultation and review

### 6.1 Data subject consultation

Individual consultation is not required as a prerequisite to this internal
privacy-risk assessment. The feature is user initiated and local-only.
User-facing privacy information and the medical disclaimer must be available
before first use.

### 6.2 Legal/DPO review

A formal DPO is not automatically required merely because the product has a
local diary feature. Whether a DPO is required depends on the organisation's
full processing activities and the criteria in GDPR Article 37.

Given the sensitivity of health data and the regulatory boundary, a legal or
privacy review is recommended before release and is mandatory before any
material expansion of D.1 into remote, analytical or clinical processing.

---

## Part 7 — Conclusion and approval

### 7.1 Residual risk summary

The current architecture materially reduces privacy risk by keeping vital
data local and physically separate from the replicated MedReminder data.
The highest residual risk is user-controlled export to an unprotected local
file; this is transparent and user initiated.

Overall residual risk for the **developer's D.1 automated processing** is
low because there is no developer-side vital-data processing path. The local
device risk remains dependent on OS/device security and implementation quality.

### 7.2 Release conditions

D.1 must not ship until:

1. `vitals.db` is separate on Desktop and Android.
2. Separate database keys are implemented and tested.
3. `.mrz` contains no vital data.
4. Cloud backup contains no vital data.
5. Sync contains no vital operations.
6. CSV import is transactional and merge+deduplication by ID is tested.
7. PDF is export-only.
8. Logs and diagnostics cannot contain vital values or notes.
9. Privacy Policy accurately describes the local-only architecture.
10. Product/store/website claims are consistent with the intended-purpose
    boundary and have passed the required regulatory/privacy review.
11. This assessment is reviewed again before any material architecture or
    feature change.

### 7.3 Review triggers

Reopen this document before implementing any of the following:

- synchronization of vital data;
- cloud backup of vital data;
- remote sharing or caregiver access to vital data;
- analytics, profiling or AI on vital data;
- reference ranges or abnormality detection;
- alerts based on vital values;
- sensor/BLE acquisition;
- diagnosis, prognosis or treatment recommendations;
- any medical or clinical claim about D.1.

---

*Assessment status: revised 7 October 2026. The assessment is based on the
local-only D.1 architecture confirmed by the product owner: separate database,
no sync/cloud for vitals, manual CSV merge+deduplication, and PDF export-only.
Placement note added 8 October 2026: D.1 is evolution V, after the store
release of the Android and iOS apps.*

# ANALYSIS — A3: Caregiver notifications

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/caregiver-notifications` (per
`CLAUDE.md` §5). Corresponds to `EVOLUTION.md` §3.3 (Group A, item
A3). Follows the structure of `ANALYSIS-A5-DOSE-TIME-REMINDER.md`
and `ANALYSIS-A6-DONATION-SUPPORT.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree),
`[VERIFIED against ANALYSIS.md]` (specified there, not necessarily
still implemented verbatim — a tree check applies at implementation
time), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (hypothesis pending confirmation).

---

## 1. Scope

### 1.1 Problem

The multi-user work of Increment 15 landed a per-profile
`NotificationSettings.ToAddress` (see
`src/MedReminder.Application/Abstractions/NotificationSettings.cs`
and `src/MedReminder.Infrastructure/Profiles/CurrentProfile.cs`)
`[VERIFIED]`. That solves "one PC, many patients — each profile
sends to its own inbox". It does **not** solve the equally common
case where the real end-user is elderly and the actual reorder is
done by a family member or a paid caregiver whose inbox must also
receive the notification.

There is today no path that copies a low-stock or reorder
notification to a second recipient.

### 1.2 Goal

Add, **per profile**, a single optional secondary recipient
(`CaregiverAddress`) that receives the same email notifications the
primary recipient (`ToAddress`) already receives. Reuses the
existing MailKit transport (`ANALYSIS.md` §1.1 item 10,
`MailKitEmailNotificationService.cs`) `[VERIFIED]`. The feature
*extends* an existing path; it introduces **no new transport**, no
new schema, and no new hosted service.

### 1.3 What A3 is NOT

- **No third recipient.** A single caregiver address is enough for
  the documented use case (`EVOLUTION.md` §3.3). Distribution lists
  are a mail-server concern; putting one address in
  `CaregiverAddress` and letting the receiving MTA fan it out is
  strictly better than reinventing that inside the app.
- **No per-event opt-in in the first cut.** `EVOLUTION.md` §3.3
  lists "independent per-event opt-in (low stock yes, generic
  reminder no)" as an *optional refinement*, not a requirement.
  The first cut sends the caregiver **exactly** what the primary
  recipient receives — no more, no less. Reopen only if users ask.
- **No caregiver-only toast.** Toasts are local to the Windows
  session and cannot reach a remote inbox; A3 is strictly an
  email-channel extension. The medicine's `NotificationChannels`
  still gate whether an email is sent at all — no email to the
  primary means no email to the caregiver.
- **No dose-time reminder fan-out in the first cut.** A5's
  `DoseReminderHostedService` is a punctual, per-slot, per-day
  signal (`ANALYSIS-A5` §4). Copying every dose-time email to a
  caregiver is a defensible but distinct product decision (spam
  risk, expectations of confidentiality); it is **out of scope**
  for the first cut and listed as an open decision in §11.
- **No SMS / push / webhook.** MailKit only. Any other channel is
  a new item, not this one.
- **No medical detail beyond what the primary already accepts.**
  The email body stays within the existing non-clinical envelope
  (`CLAUDE.md` §6, `ANALYSIS.md` §2.11). A3 does not change what
  the email contains; it only changes who receives it.

---

## 2. Preconditions — what already exists

- **Per-profile notification settings.** `NotificationSettings` is
  a POCO in `MedReminder.Application.Abstractions` with a single
  `ToAddress` field, bound from
  `<profile-dir>\notifications.settings.json` and consumed via
  `IOptionsMonitor<NotificationSettings>` `[VERIFIED]`. A3 adds
  one sibling field to this POCO and one sibling JSON key.
- **MailKit adapter.** `MailKitEmailNotificationService` opens a
  connection per send, builds a `MimeMessage` from
  `SmtpSettings` + `NotificationSettings` + `EmailMessage`, and
  sends `[VERIFIED]`. Adding a second recipient is a MIME-header
  change (`mime.To.Add` or `mime.Cc.Add`), not a transport change.
- **Per-medicine channel flags.** `Medicine.NotificationChannels`
  is a `[Flags]` enum (`Email`, `Windows`) already respected by
  the monitor (`ANALYSIS.md` §2.9,
  `MedicationMonitor.RunAsync`) `[VERIFIED]`. A3 does **not**
  change this: the caregiver only receives an email when the
  medicine's `NotificationChannels` already includes `Email`.
- **Failure isolation.** The monitor already dispatches Windows
  and Email in isolation (`MedicationMonitor.cs`: "an email
  failure does NOT prevent the toast and vice versa") `[VERIFIED]`.
  A3 inherits this posture — a caregiver-address failure must
  not prevent delivery to the primary and vice versa (§4.3).
- **Per-profile isolation.** Only the **active** profile emits
  notifications (`ANALYSIS-MULTI-USER.md` §9.1). The caregiver
  address is per-profile and therefore inherits this: one
  profile's caregiver is not another profile's caregiver.
- **Settings UI.** A `SettingsDialog` already writes
  `notifications.settings.json` (see
  `src/MedReminder.UI/Forms/SettingsDialog.cs`, the "per-profile
  notifications.settings.json" note) `[VERIFIED]`. A3 extends
  that dialog with one text field and one save path.
- **Localization pipeline.** Five dictionaries under
  `assets/localization/` (`en`, `it`, `fr`, `es`, `de`) with
  parity enforced by `DictionaryParityTests` (`ANALYSIS-A1` §6).
  New keys must be added to every dictionary or the build fails.

---

## 3. Data model

### 3.1 New field `CaregiverAddress`

A single additive string on `NotificationSettings`, default empty,
next to `ToAddress`.

```csharp
// In MedReminder.Application.Abstractions.NotificationSettings
public string CaregiverAddress { get; set; } = string.Empty;
```

Empty (or whitespace-only) → **no caregiver configured** → the
send path behaves exactly as today. This is the safe default and
matches the "do not enable by default" constraint
(`EVOLUTION.md` §3.3).

### 3.2 No SQLite schema change

The caregiver address lives in `notifications.settings.json`, not
in the SQLite database. `notifications.settings.json` is a
per-profile JSON file already read as `NotificationSettings`
`[VERIFIED]`. Adding a new key is a **backwards-compatible JSON
change**: old files that lack the key deserialize with the
default (`""`) and behave exactly as before.

There is therefore **no** `DatabaseInitializer` patch to write
and no `EnsureCreated`/`ALTER TABLE` concern (`CLAUDE.md` §9).

### 3.3 `notifications.settings.json` shape after A3

```json
{
  "Notifications": {
    "ToAddress": "user@example.com",
    "CaregiverAddress": "caregiver@example.com"
  }
}
```

An older file without `CaregiverAddress` loads with
`CaregiverAddress = ""` and produces the pre-A3 behaviour.
No migration is required.

---

## 4. Runtime

### 4.1 Where the fan-out happens — one decision to make

Two implementation surfaces are viable. This analysis chooses
**Option A**; Option B is documented so the trade-off is explicit.

| Option | Fan-out point | Trade-off | Verdict |
|---|---|---|---|
| **A — MailKit adapter fans out** | `MailKitEmailNotificationService.BuildMimeMessage` adds the second recipient (as `To` alongside the primary, or as `Cc` — see §4.2) | One MIME message, one SMTP round-trip; adapter owns the "primary + caregiver" mapping; monitor / caller sees a single `SendAsync`. Adapter must handle partial failures for a single-recipient MIME — SMTP semantics are already all-or-nothing per message, so this is not a new failure mode | ✅ Recommended |
| B — Monitor sends twice | `MedicationMonitor.DispatchEmailAsync` calls `_email.SendAsync` a second time with a different recipient | Two SMTP round-trips, doubled connection cost, doubled failure surface; forces `IEmailNotificationService` to expose "send to X specifically", widening the port | ❌ Rejected |

Option A keeps the port (`IEmailNotificationService.SendAsync`)
unchanged, keeps the monitor unchanged, and localizes the
"there is a second recipient" concern in exactly the place that
already reads `NotificationSettings.ToAddress`. The monitor's
current failure-isolation posture (Windows vs. Email as a whole)
is preserved as-is: the email is either delivered to both
recipients or to neither, which is the standard SMTP guarantee.

### 4.2 Header choice — `To` vs. `Cc` vs. `Bcc`

Three MIME options; the decision affects visibility.

| Header | Visibility | Fit for A3 |
|---|---|---|
| Two `To` recipients | Both addresses are visible to both recipients | ✅ Recommended. The primary user has explicitly configured the caregiver — mutual visibility is expected and honest. |
| `Cc` on the caregiver | Both addresses still visible to both recipients | Equivalent effect for consumer mail; distinction is conventional, not enforced. Not worth the cognitive overhead of explaining "why Cc". |
| `Bcc` on the caregiver | Caregiver address hidden from the primary | Rejected. Bcc for a recipient the primary user configured themselves is misleading and complicates spam-filter behaviour. |

**Decision.** Use two `To` recipients. Both addresses are visible;
that matches the user's mental model ("I told the app to also
send it to Alice") and avoids surprises when a caregiver replies
to all. `[INFERRED]`

### 4.3 Failure isolation and validation

- **Empty caregiver.** `string.IsNullOrWhiteSpace(CaregiverAddress)`
  → the MIME message has one `To` (the primary). No behavioural
  change from pre-A3.
- **Malformed caregiver address.** `MailboxAddress.Parse` throws.
  A3 must **not** let a bad caregiver value block delivery to the
  primary. Two acceptable shapes; the analysis picks (b):
  - (a) validate at save time in the settings dialog only.
    Risk: a hand-edited JSON file bypasses the check.
  - (b) validate at save time AND catch the parse failure in the
    adapter, logging a warning and falling back to primary-only.
    `[INFERRED — matches the isolation posture already used for
    the Windows/Email split]`
- **SMTP failure on the message.** Same as today — the monitor's
  `try/catch` around `_email.SendAsync` records a warning and
  the run continues. No new failure mode.

### 4.4 Interaction with A5

A5's `DoseReminderHostedService` also emits emails when the
medicine has `NotificationChannels.Email` set. If the caregiver
fan-out lives in `MailKitEmailNotificationService` (§4.1
Option A), **every email path** (low-stock, dose-time, any
future one) inherits the caregiver copy automatically.

That is likely the **wrong** default for dose-time reminders: a
caregiver does not necessarily need a "take your 08:00 tablet"
message. Two ways out; the analysis defers the choice to §11 as
an open decision:

- **Simple**: dose-time emails also go to the caregiver. Users
  who do not want that either disable dose-time email per
  medicine (already possible) or leave `CaregiverAddress` empty.
- **Refined**: `EmailMessage` gains a `Kind`
  (`LowStock` / `DoseReminder`) and the adapter routes only
  `LowStock` to the caregiver by default. Adds one enum + one
  branch, no schema change.

Neither option requires a data-model change; both are code-only.

### 4.5 Not a hosted service

A3 adds **no** new `IHostedService`. The existing
`MedicationMonitorHostedService` (30-min tick) and, once A5
lands, `DoseReminderHostedService` (1-min tick) drive email
production; A3 only extends the recipient list on the resulting
message.

---

## 5. UI

### 5.1 The field

Extend the "Notifications" section of `SettingsDialog` (already
the writer of `notifications.settings.json`) with:

- A labelled `TextBox _caregiverAddress` under the existing
  `ToAddress` field.
- A short helper label immediately below, warning that anything
  sent to the primary is **also** sent to the caregiver, and that
  the caregiver's inbox will receive medical-relevant subject
  lines (medicine names). This is the "explicit user choice" copy
  called out in `EVOLUTION.md` §3.3.

### 5.2 Validation

At save time, if `_caregiverAddress.Text` is non-empty:

- Trim.
- Reject with an inline error if it does not parse as a
  well-formed mailbox (`MimeKit.MailboxAddress.TryParse`).
- Reject if it equals `ToAddress` (case-insensitive) — a
  self-copy is a misconfiguration, not a feature.

Save proceeds only when the field is empty (unconfigured, allowed)
or valid.

### 5.3 No caregiver-only toggles in the first cut

Per §1.3, no per-event opt-in and no separate channel checkboxes
in the first cut. The caregiver field is a single input; the
mental model is "who else should be copied on the same emails".

### 5.4 Discoverability

The field lives in the same dialog and same section as
`ToAddress`. No new menu entry, no startup prompt, no wizard.
The user finds it exactly where they already configure the
primary recipient.

---

## 6. Channels

Reuse `MailKitEmailNotificationService` unchanged from the
transport perspective. The **only** code change inside the
adapter is the recipient list construction in `BuildMimeMessage`:

```csharp
mime.To.Add(MailboxAddress.Parse(notifications.ToAddress));
if (!string.IsNullOrWhiteSpace(notifications.CaregiverAddress))
{
    try
    {
        mime.To.Add(MailboxAddress.Parse(notifications.CaregiverAddress));
    }
    catch (ParseException ex)
    {
        _log.LogWarning(ex,
            "Caregiver address '{Caregiver}' is malformed; sending to primary only.",
            notifications.CaregiverAddress);
    }
}
```

The log message respects `CLAUDE.md` §9 — no password, no medical
detail, only the malformed address (which is by definition
user-configured and low-sensitivity).

Toast (`IWindowsNotificationService`) is untouched: it is local to
the Windows session and has no notion of "recipient".

---

## 7. Localization

New keys added to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`) per
`CLAUDE.md` §8. `DictionaryParityTests` fails the build on any
missing key (`ANALYSIS-A1` §6). Proposed keys (final names to be
aligned with existing conventions):

- `Ui.SettingsDialog.Notifications.CaregiverAddress.Label` — field
  label, e.g. `"Caregiver e-mail (optional)"`.
- `Ui.SettingsDialog.Notifications.CaregiverAddress.Help` — helper
  copy under the field, e.g. `"When set, the same low-stock
  e-mails are also sent to this address. Leave empty to disable."`.
- `Ui.SettingsDialog.Notifications.CaregiverAddress.Invalid` —
  validation error, e.g. `"Enter a valid e-mail address."`.
- `Ui.SettingsDialog.Notifications.CaregiverAddress.SameAsPrimary`
  — validation error, e.g. `"The caregiver address cannot be the
  same as the primary address."`.

Following the A1 / A5 precedent, the four non-English
dictionaries may ship with `TODO(<lang>): <english fallback>`
placeholders; Italian wording requires maintainer sign-off.

Shipped user guides (`USER_GUIDE.*.md`) get a short "Caregiver
notifications" section documenting the opt-in nature (empty by
default), the "same email, two recipients" semantics, and the
mutual visibility (both recipients see each other's address).
English ships with the PR; the four localized guides may follow
in a follow-up, mirroring A1 / A5.

---

## 8. Tests

### 8.1 `MedReminder.Application.Tests`

Because the fan-out lives in the infrastructure adapter, the
application-side test surface is small:

- **NotificationSettings deserialization.** A JSON string with
  and without `CaregiverAddress` binds correctly (empty when
  absent).
- **Monitor unchanged.** The existing monitor tests continue to
  pass without modification — a regression signal that A3 did
  not leak into `MedicationMonitor`.

### 8.2 `MedReminder.Infrastructure.Tests`

Drive `MailKitEmailNotificationService` with a fake SMTP client
(the existing test pattern from Increment 7 hardening — locate
the current mock and extend it).

- **Primary only.** Empty `CaregiverAddress` → MIME message has
  one `To`; recipient equals `ToAddress`.
- **Primary + caregiver.** Valid `CaregiverAddress` → MIME
  message has two `To` recipients; order is deterministic
  (primary first).
- **Malformed caregiver.** `CaregiverAddress = "not-an-email"` →
  MIME message has one `To` (the primary); a warning is logged;
  the send does not throw.
- **Self-copy misconfiguration.** `CaregiverAddress == ToAddress`
  is normally caught at save time (§5.2), but if it slips through
  a hand-edited file, the adapter still delivers a single-`To`
  message (dedup the duplicate) rather than a two-recipient
  message with the same address twice.

### 8.3 UI tests / smoke checks

- **Save round-trip.** Setting a valid `CaregiverAddress` in the
  dialog and reloading it produces the same value.
- **Reject invalid input.** A malformed value at save time
  surfaces the inline error and does not touch the JSON file.
- **Reject self-copy.** `CaregiverAddress` equal to `ToAddress`
  surfaces the inline error.

---

## 9. Retro-compatibility

- **On-disk.** Existing `notifications.settings.json` files that
  lack `CaregiverAddress` deserialize with the default (`""`).
  No migration pass required.
- **Behaviour.** With `CaregiverAddress` defaulting to empty for
  every existing profile, an upgraded install behaves
  **identically** until the user opts in per profile.
- **Revert-safety.** A later build that reverts A3 ignores the
  extra JSON key; the JSON file is still valid. Same posture as
  A5 (`ANALYSIS-A5` §9).
- **Per-profile.** Because the field lives in the per-profile
  `notifications.settings.json`, one profile's caregiver is not
  another profile's caregiver (`ANALYSIS-MULTI-USER.md` §9.1).

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Silent leak of medical-relevant information to a wrong inbox | High (privacy) | Empty by default; explicit UI copy at save time; malformed-address fallback logs a warning and delivers to primary only (§4.3) |
| Two-recipient message misclassified as spam by receiving MTAs | Low | Standard SMTP practice; two `To` recipients are ordinary consumer mail (§4.2). No product-level mitigation needed |
| Caregiver fan-out unintentionally applies to A5 dose-time emails | Medium (product) | Documented as open decision in §11; the two ways out are both code-only, no schema change |
| User confuses `ToAddress` and `CaregiverAddress` | Low | Field label and helper copy are explicit; self-copy validation at save time (§5.2) prevents the most common mistake |
| Adapter throws on `MailboxAddress.Parse` and blocks the send | Medium | Wrap the parse of `CaregiverAddress` in try/catch (§6); the primary parse stays authoritative — a primary that fails to parse still fails the send, as today |
| Bad caregiver value hand-edited into the JSON file | Low | Adapter defensive parse (§6) turns it into a warning log + primary-only send, not a crash |
| Caregiver email cadence perceived as spam | Low | Cadence equals the primary's — the primary already accepted it. Refined per-event opt-in remains as an optional later refinement (§1.3) |

---

## 11. Decisions still to confirm

1. **A5 fan-out.** Should A5's dose-time emails also be copied to
   the caregiver? Two options are documented in §4.4; the analysis
   defaults to the **Refined** option (caregiver receives only
   low-stock / reorder emails, not dose-time), which requires an
   `EmailMessage.Kind` discriminator. Confirm — or accept the
   **Simple** default (caregiver gets everything).
2. **Field header.** Two `To` recipients (§4.2 recommended) vs.
   `Cc` on the caregiver. Recommended path is two `To`; confirm
   before implementation. `[UNCERTAIN — user-preference call]`
3. **Localized user guides.** English section ships with the PR;
   the four localized guides may follow, mirroring A1 / A5. Confirm
   this is acceptable.
4. **Per-event opt-in (deferred).** `EVOLUTION.md` §3.3 lists an
   optional per-event opt-in. Confirmed **out** of the first cut
   in §1.3; reopen only on user demand.

---

## 12. Implementation plan

One PR on `feature/caregiver-notifications`. Per `CLAUDE.md` §5,
the PR is opened **after the first commit**, and a
`CHANGE_LOG.md` entry is prepended when the PR opens. Indicative
commit order:

1. **Application.** Add `CaregiverAddress` to
   `NotificationSettings`. No behavioural change yet.
2. **Infrastructure.** Extend `MailKitEmailNotificationService.
   BuildMimeMessage` to append the caregiver recipient (§6);
   add unit tests (§8.2).
3. **UI (`SettingsDialog`).** Add the field, helper, and
   validation (§5); wire the write path to
   `notifications.settings.json`.
4. **Localization.** Keys in all five dictionaries (§7). Italian
   awaits sign-off per §7.
5. **A5 interaction (open decision).** If §11 item 1 chooses the
   Refined route, add `EmailMessage.Kind` and route only
   `LowStock` to the caregiver by default. Otherwise no change.
6. **`CHANGE_LOG.md`** entry when the PR opens; short "Caregiver
   notifications" section in `docs/USER_GUIDE.en.md`.

Run `dotnet build` and `dotnet test` before every commit that
touches source (`CLAUDE.md` §8).

**Effort.** ~1 developer-week including UI, MIME plumbing, tests,
five-language localization and user-guide updates
(`EVOLUTION.md` §3.3). `[INFERRED]`

---

## Change log for this document

- 2026-09-21 — initial draft (pre-implementation). Derived from
  `EVOLUTION.md` §3.3 and cross-checked against
  `NotificationSettings`, `MailKitEmailNotificationService`,
  `CurrentProfile` and `SettingsDialog`. Fan-out placed in the
  MailKit adapter (§4.1 Option A); recipient header chosen as
  two `To` (§4.2); A5 dose-time interaction flagged as an open
  decision (§4.4, §11 item 1).

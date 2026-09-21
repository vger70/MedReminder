# Implementation prompt — A3: Caregiver notifications

This file is the self-contained briefing for the Claude Code session
that will implement feature A3. Read it completely before touching
any source file.

---

## 0. What you are about to implement

**Feature A3** adds a per-profile secondary email recipient
(`CaregiverAddress`) to MedReminder. When set, every email that
today reaches the primary recipient (`ToAddress`) is also
delivered to the caregiver in the **same** MIME message. The
transport, the message body and the trigger cadence do not
change — only the recipient list widens by one address.

The authoritative design lives in two documents — read them before
coding:

- **`docs/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md`** — the approved
  design. Sections §3–§12 describe the data model, runtime
  algorithm, UI, localization, and implementation order. Where
  this prompt and the analysis document disagree, **the analysis
  document wins**.
- **`docs/EVOLUTION.md` §3.3** — the original sketch, retained for
  context. The analysis document supersedes it where they differ.

Also read before making any architectural change:

- **`CLAUDE.md`** — mandatory rules covering language policy,
  branch naming, PR workflow, localization, and the things to
  never do.
- **`docs/ANALYSIS.md`** — the base architecture (SMTP client
  choice §1.1 item 10, per-profile settings, notification dispatch
  in `MedicationMonitor`).
- **`docs/ANALYSIS-MULTI-USER.md`** §7.1 — the per-profile
  `NotificationSettings` split A3 extends.

---

## 1. Hard boundaries — do NOT cross these

A3 is scoped strictly to **one extra recipient on the same email**.
The following are **out of scope by design** (`ANALYSIS-A3` §1.3):

- No third recipient. One caregiver address; a distribution list
  is a mail-server concern, not a MedReminder concern.
- No per-event opt-in in the first cut. Caregiver receives what
  the primary receives.
- No caregiver-only toast — toasts are local to the Windows
  session; a remote inbox cannot receive one.
- No SMS / push / webhook / new transport. MailKit only.
- No change to the email body. The medical envelope is unchanged.
- No `SmtpClient` from `System.Net.Mail` — MailKit remains the
  only supported SMTP client (`CLAUDE.md` §9).

If a reviewer asks for any of these, decline and reference
`ANALYSIS-A3` §1.3.

---

## 2. Preconditions — verify against the tree first

Before writing any code, confirm the following in the actual
source:

1. `MedReminder.Application.Abstractions.NotificationSettings`
   is a POCO with a single string `ToAddress` and a
   `SectionName = "Notifications"` constant.
2. `MailKitEmailNotificationService.BuildMimeMessage` reads
   `notifications.ToAddress` and builds a single-`To` `MimeMessage`.
3. `SettingsDialog` writes `notifications.settings.json` into the
   active profile's data directory (search for
   `notifications.settings.json` and follow the writer).
4. `CurrentProfile.NotificationSettingsPath` resolves to
   `<profile-dir>\notifications.settings.json`.
5. `DictionaryParityTests` (or its current equivalent) enforces
   parity across `assets/localization/strings.<lang>.json` for
   the five shipped languages.

Document what you find (brief inline notes in the commit message
are fine); update the analysis document if any fact is wrong.

---

## 3. Branch and PR

Per `CLAUDE.md` §5:

- Branch name: **`feature/caregiver-notifications`**, based on
  `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

---

## 4. Implementation order

Follow `ANALYSIS-A3` §12. Each step must leave `dotnet build` and
`dotnet test` green before the next commit.

### Step 1 — Application: `CaregiverAddress` field

File:
`src/MedReminder.Application/Abstractions/NotificationSettings.cs`.

Add one field next to `ToAddress`:

```csharp
public string CaregiverAddress { get; set; } = string.Empty;
```

No behavioural change yet — the field is unread by anyone until
Step 2 lands. Unit test in `MedReminder.Application.Tests`:
deserialize a JSON payload with and without the key, assert the
default is empty when the key is absent.

### Step 2 — Infrastructure: fan-out in the MailKit adapter

File:
`src/MedReminder.Infrastructure/Email/MailKitEmailNotificationService.cs`.

Extend `BuildMimeMessage` to append the caregiver recipient when
`notifications.CaregiverAddress` is non-empty, using a **second
`To` header** (not `Cc`, not `Bcc` — `ANALYSIS-A3` §4.2):

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
            "Caregiver address is malformed; sending to primary only.");
    }
}
```

**Do not** log the caregiver address itself unless it is already
present in the exception message — the log stays free of PII
beyond what the log framework is already emitting for the
primary.

Also handle the **self-copy** case defensively: if
`CaregiverAddress` equals `ToAddress` (case-insensitive) after
trim, skip the second `Add` so the MIME message is not built with
the same address twice. Save-time UI validation (§Step 3) is the
primary defence; this is the belt-and-braces for hand-edited JSON.

**Infrastructure tests** (extend the existing MailKit test double
— find the current fake SMTP client and reuse it):

- Empty `CaregiverAddress` → one `To`.
- Valid `CaregiverAddress` → two `To`, primary first.
- Malformed `CaregiverAddress` (e.g. `"not-an-email"`) → one `To`
  (primary), warning logged, no throw.
- `CaregiverAddress == ToAddress` (hand-edited path) → one `To`,
  no duplicate.

### Step 3 — UI: field in `SettingsDialog`

File: `src/MedReminder.UI/Forms/SettingsDialog.cs`.

In the section that already writes `notifications.settings.json`
(search the file for `notifications.settings.json`):

- Add a `TextBox _caregiverAddress` under the existing
  `ToAddress` field.
- Add a `Label _caregiverAddressHelp` below the textbox with a
  short helper copy (`ANALYSIS-A3` §5.1).
- Wire the read path to fill the textbox from the loaded
  `NotificationSettings.CaregiverAddress`.
- Wire the write path to persist the trimmed value into the JSON
  file next to `ToAddress`.
- Validation at save time (`ANALYSIS-A3` §5.2):
  - Empty → allowed (unconfigured).
  - Non-empty must parse via `MimeKit.MailboxAddress.TryParse`.
    On failure: inline error using the localization key
    `Ui.SettingsDialog.Notifications.CaregiverAddress.Invalid`
    and abort the save.
  - Non-empty must not equal `ToAddress` (case-insensitive after
    trim). On failure: inline error using
    `Ui.SettingsDialog.Notifications.CaregiverAddress.SameAsPrimary`
    and abort the save.

### Step 4 — Localization

Add four keys to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`). Missing
keys fail the build via `DictionaryParityTests`.

Keys (`ANALYSIS-A3` §7):

| Key | English value |
|-----|---------------|
| `Ui.SettingsDialog.Notifications.CaregiverAddress.Label` | `"Caregiver e-mail (optional)"` |
| `Ui.SettingsDialog.Notifications.CaregiverAddress.Help` | `"When set, the same low-stock e-mails are also sent to this address. Leave empty to disable."` |
| `Ui.SettingsDialog.Notifications.CaregiverAddress.Invalid` | `"Enter a valid e-mail address."` |
| `Ui.SettingsDialog.Notifications.CaregiverAddress.SameAsPrimary` | `"The caregiver address cannot be the same as the primary address."` |

For the four non-English dictionaries, follow the A1 / A5
precedent: ship with `"TODO(<lang>): <english fallback>"`
placeholders and finalize wording after the form is inspected.
Italian wording requires maintainer sign-off before it lands.

### Step 5 — A5 interaction (conditional)

`ANALYSIS-A3` §11 item 1 is an open decision. Default assumption
for this PR:

- **If the product owner has confirmed the Refined route**
  (caregiver receives low-stock only, not dose-time), add an
  `EmailMessage.Kind` (`LowStock` | `DoseReminder`) discriminator
  and skip the caregiver `To.Add` in the adapter when
  `Kind == DoseReminder`. Cover this with an infrastructure test.
- **Otherwise**, ship the Simple route (caregiver receives all
  emails). No code change beyond Step 2.

Ask before implementing this step if the decision is not
recorded in `ANALYSIS-A3` §11 by the time you reach it.

### Step 6 — User guide and CHANGE_LOG.md

- **`CHANGE_LOG.md`**: prepend an entry when the PR opens (the PR
  number is filled in after creation). Follow the format at the
  top of that file.
- **`docs/USER_GUIDE.en.md`**: add a short "Caregiver
  notifications" section covering: how to enable, the "same email
  to both recipients" semantics, the mutual visibility of the
  addresses, and the fact that leaving the field empty disables
  it. The four localized guides may follow in a follow-up.

---

## 5. Decided items — not open for re-debate

These items in `ANALYSIS-A3` §11 are decided by the analysis and
must not be re-opened without the product owner's explicit
sign-off:

- **Field header** (`ANALYSIS-A3` §4.2). Two `To` recipients, not
  `Cc`, not `Bcc`.
- **Fan-out surface** (`ANALYSIS-A3` §4.1). The MailKit adapter
  does the fan-out. Do **not** duplicate the send in
  `MedicationMonitor`.
- **No new schema.** The caregiver address lives in
  `notifications.settings.json` only. No `ALTER TABLE`, no
  `EnsureCreated`, no `DatabaseInitializer` patch.
- **Per-event opt-in** — out of the first cut. Do not add
  per-event checkboxes even if a review comment asks for one.

---

## 6. Still open — settle at implementation time

- **A5 interaction** (`ANALYSIS-A3` §11 item 1). See Step 5 above.
  Confirm the Refined vs. Simple choice before that step.
- **Localized user guides** (`ANALYSIS-A3` §11 item 3). Ship
  English with the PR; the four localized guides may follow.

---

## 7. Conventions and constraints (from `CLAUDE.md`)

- All identifiers, comments, log messages, exception messages,
  XML docs, and commit messages must be in **English**. Italian
  is used only in chat with the user and in the localized
  `strings.it.json` and `USER_GUIDE.it.md`.
- Run `dotnet build` and `dotnet test` before every commit that
  touches source.
- No `EnsureCreated()` — but A3 introduces no schema change, so
  the point does not arise.
- No `SmtpClient` from `System.Net.Mail` — MailKit only.
- No plaintext passwords or PII to logs.
- When adding a UI string, add the key to **every** localization
  dictionary.
- Keep Domain free of Windows-specific APIs and EF Core
  references.

---

## 8. Acceptance criteria

The PR is ready to merge when:

1. `dotnet build MedReminder.sln -c Release` is green.
2. `dotnet test MedReminder.sln -c Release` is green, including
   the new tests in Steps 1, 2 and 3.
3. All four localization keys are present in all five
   dictionaries (`DictionaryParityTests` passes).
4. `MailKitEmailNotificationService` produces a single-`To`
   message when `CaregiverAddress` is empty and a two-`To`
   message when it is set, with primary first.
5. `SettingsDialog` rejects a malformed caregiver address and a
   caregiver address equal to the primary at save time.
6. A pre-A3 `notifications.settings.json` file still loads and
   the app behaves as before (no `CaregiverAddress` key required).
7. `CHANGE_LOG.md` has a new entry for this PR.
8. `docs/USER_GUIDE.en.md` has a "Caregiver notifications"
   section.

---

*Generated 2026-09-21. Authoritative source:
`ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md`.*

# Implementation prompt — Prescription request draft for the doctor

Briefing for the Claude Code session that implements proposal 3.4 of
`docs/notes/EVOLUTION-PROPOSALS.md` (ranking #4). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

When a medicine is running low, let the user ask the GP for a new
prescription in a few clicks. The app prepares a short, pre-filled
message; the user reviews it and chooses how to deliver it: copy to
clipboard, open in the default mail client, or send through the app's
configured SMTP account.

This closes the loop of the app's core purpose (stock and prescription
reminders) at low cost. Target effort: 2–3 days.

## 2. Context to read first

- `CLAUDE.md` — especially §5 (runtime data), §7 (MailKit only, no
  health data or PII in logs).
- `docs/notes/EVOLUTION-PROPOSALS.md` §3.4.
- `docs/EXPORT-FORMAT.md` §3.9 (`notificationSettings`) and §5
  (evolution discipline).
- `src/MedReminder.Domain/Medicines/Medicine.cs` — `Name`, `Package`,
  `NationalCode` (AIC in Italy), `DoctorName`.
- `src/MedReminder.Application/Abstractions/NotificationSettings.cs` —
  per-profile settings, already holding `ToAddress` and
  `CaregiverAddress`.
- `src/MedReminder.Application/Abstractions/IEmailNotificationService.cs`,
  `src/MedReminder.Application/Notifications/EmailMessage.cs`,
  `src/MedReminder.Application/Notifications/NotificationTexts.cs` —
  existing email port and localized text pattern.
- `src/MedReminder.UI/Forms/MainForm.cs` — medicine actions and menus.
- `src/MedReminder.UI/Forms/SettingsDialog.cs` — where the caregiver
  address is edited.

## 3. Design

- **Doctor address.** New optional per-profile field. The natural home is
  `NotificationSettings` next to `CaregiverAddress`, which is already
  per profile and already exported. Validate it like the caregiver
  address. `Medicine.DoctorName` stays as is and is used in the greeting
  when present.
- **Message text.** Built in the Application layer by a pure, localized
  builder (follow `NotificationTexts`): subject and body with medicine
  name, package, national code when present, and profile display name.
  No dosage, notes or other clinical detail beyond what identifies the
  product. Language follows the UI language.
- **Dialog.** A `PrescriptionRequestDialog` shows the editable subject and
  body, the recipient, and three actions:
  - *Copy* — clipboard.
  - *Open in mail client* — `mailto:` via the shell. Check the length:
    long bodies can exceed what mail clients accept; fall back to copy
    with a message if needed.
  - *Send* — through `IEmailNotificationService` (MailKit), enabled only
    when SMTP is configured and a doctor address is set, with an explicit
    confirmation.
- **Entry points.** A medicine context-menu / toolbar action in MainForm,
  available for any medicine; optionally a link from the low-stock
  notification if it fits the existing notification activation path
  without new plumbing.

Decision to surface to the user: one doctor address per profile (simple)
versus per medicine (specialists prescribe different medicines). Recommend
per profile for this iteration unless the code makes per medicine trivial.

## 4. Hard rules

- Sending is always an explicit user action. No automatic or scheduled
  sending, no sending from a notification without opening the dialog.
- The message contains health data and a personal name: never log the
  subject, body or recipient. Log only the outcome (sent / failed) and the
  exception type.
- No `System.Net.Mail.SmtpClient`.
- The doctor address is personal data: include it in export/import as an
  additive, optional field in `notificationSettings`, update
  `docs/EXPORT-FORMAT.md` per its §5, and keep older archives importable.

## 5. Out of scope

- Electronic prescription systems, pharmacy integrations, attachments.
- Tracking whether a request was answered.

## 6. Constraints and workflow

- New UI strings and message templates in all five
  `assets/localization/strings.<lang>.json`; parity test must pass.
- Document the feature in all five `docs/USER_GUIDE.<lang>.md`.
- Ask before creating the branch; proposed name
  `feature/prescription-request`. Open the PR after the first commit,
  prepend a `CHANGE_LOG.md` entry, ask the user to run build and tests
  before committing source.

## 7. Verification

- Unit tests for the text builder: all fields, missing optional fields
  (no national code, no doctor name), each supported language renders
  without missing keys.
- Export/import round-trip test including the doctor address, and import
  of an archive without the field.
- Test that the send path does not write subject/body/recipient to the
  logger (use a capturing logger).
- Manual check of copy, `mailto:` and SMTP send with a test account.

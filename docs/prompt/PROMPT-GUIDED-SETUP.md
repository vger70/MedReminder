# Implementation prompt — Guided setup after the first start

Briefing for the Claude Code session that implements item M2 of the
evolution plan prepared on 2026-10-04 (competitor benchmark and
cost/benefit plan, kept outside this repository). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Take a new user from an empty main window to a profile that will
actually warn them: the first medicines entered, a warning lead time
chosen, and, when wanted, an email address for the user and for the
person who assists them. All of it in one short, skippable sequence that
reuses the dialogs the app already has.

Today `FirstRunWizardForm` asks only for the profile name and an
optional PIN, then the main window opens with an empty list. The user
must find Add medicine, the warning threshold (default 7 days in
`MedicineEditDialog`), the per-medicine channel (default Windows), the
notification addresses in Settings and, for email, the SMTP account. The
benchmark found that the most rated mobile competitors open with a very
short first run; MedReminder's breadth is its strength and its
learning curve **[INFERRED]**.

Target effort: 5–8 days **[INFERRED]**.

## 2. Context to read first

- `CLAUDE.md` — §5 (per-profile `ui.settings.json`), §6 (strings in all
  five dictionaries), §7.
- `src/MedReminder.UI/Program.cs` — boot flow: `FirstRunWizardForm`,
  `RunFirstRunJoin`, `ApplySystemLanguageOnFirstRun`.
- `src/MedReminder.UI/Forms/FirstRunWizardForm.cs` — current first run
  (admin profile, optional PIN, "Join an existing installation…").
- `src/MedReminder.UI/Forms/MedicineEditDialog.cs` — Create mode,
  catalogue autocomplete, "Scan barcode", Simple / Advanced schedule,
  threshold and channel fields.
- `src/MedReminder.UI/Forms/MainForm.cs` — summary cards
  (`Ui.MainForm.Summary.*`), Add medicine, Help menu.
- `src/MedReminder.UI/Forms/SettingsDialog*.cs` — notification addresses,
  caregiver copies and weekly summary, Email (SMTP, admin only).
- `src/MedReminder.Application/UseCases/ProfileSettingsUseCases.cs`
  (`SaveProfileSettings`: `ToAddress`, `CaregiverAddress`,
  `CaregiverEmails`, `CaregiverDigest`);
  `UseCases/AddMedicine.cs`, `UseCases/UpdateMedicine.cs`.
- `src/MedReminder.Infrastructure.Portable/Settings/
  ProfileUiSettingsFile.cs` and `Application/Abstractions/TextSize.cs`,
  `AppearanceMode.cs` — device-local per-profile UI settings.
- `src/MedReminder.UI/Forms/DialogLayout.cs`, `MedReminderFormBase.cs`
  — the dialog template, inline errors, DPI and text-size scaling.
- `docs/ANALYSIS.md` — household and master device (who sends email),
  roles.

## 3. Design

### 3.1 When it appears

- Right after `FirstRunWizardForm` creates a new installation (not after
  a join, which brings existing profiles), once the main window is
  shown.
- For any profile with no medicines, from a "Start guided setup" link
  in the empty main list (an empty state, if the list has none yet:
  one line of text and two links, "Add a medicine" and "Start guided
  setup").
- From Help → Guided setup, at any time.
- Dismissing it ("Not now" or closing) sets a device-local flag
  `GuidedSetupDismissed` in the profile's `ui.settings.json`, so it does
  not open by itself again for that profile; the empty-state link and
  the Help item stay. Nothing is replicated.

### 3.2 Steps

One window, `GuidedSetupForm`, with Back / Next / Not now and a step
indicator; each step can be skipped. Keep the step logic in a plain
class (`GuidedSetupFlow`) that the form drives, so it is testable
without WinForms.

1. **Who the medicines are for.** "For me" or "For someone I look
   after". The answer changes only the wording of the next steps and
   pre-selects step 4; the profile name from the first-run wizard stays
   editable here. Creating more profiles stays in Manage profiles; this
   step ends with one line pointing there.
2. **First medicines.** "Add a medicine" opens the existing
   `MedicineEditDialog` in Create mode, with catalogue autocomplete and
   "Scan barcode" when available. The step lists the medicines added
   (name, days left) and allows adding more. No second medicine form.
3. **When to warn.** Lead time with three presets and a custom value
   (for example 7, 10 or 14 days; 7 stays the default) and channel
   (Windows, Email, both). Applied through `UpdateMedicine` to the
   medicines added in step 2, and stored as the seed of new medicines in
   `ui.settings.json` (`NewMedicineThresholdDays`,
   `NewMedicineChannels`) that `MedicineEditDialog` reads in Create
   mode. Device-local, so no sync or export change.
4. **Email (shown when step 3 includes email, or step 1 was "someone I
   look after").** The user's address and the caregiver's address,
   which kinds of email the caregiver receives and the weekly summary,
   saved through `SaveProfileSettings`. If no SMTP account is
   configured: an administrator gets "Set up the email account", which
   opens Settings on the Email section and returns to the step; a
   standard user gets a line saying an administrator has to set it up.
   On a device that is not the master, say that emails are sent by the
   master device.
5. **Summary.** What the app will now do, in plain sentences ("You will
   be warned 7 days before Ramipril runs out, on Windows and by email").
   Below, three optional next steps as links, each opening the existing
   window: dose-time reminders, backup, use on another PC (Sync /
   Installation, administrators only). Finish closes the window.

### 3.3 Interface rules

- Same look as the other dialogs (`DialogLayout`, theme, high
  contrast); honours the profile text size; every control reachable by
  keyboard with a visible focus; Enter = Next, Esc = Not now.
- Short sentences, no medical advice, no dose suggestions: the wizard
  only collects what the user types (not a medical device,
  `docs/EVOLUTION.md` §9.2).
- No feature is hidden or removed from the main window by this item. A
  simplified main view for new users is out of scope; mention it in the
  PR as a possible follow-up.

## 4. Constraints

- No schema change, no new replicated data, no export change: the only
  new values are device-local UI settings. If the implementation finds
  a reason to replicate a value, stop and ask.
- Profile addresses and caregiver settings go through the existing use
  case; do not write `notifications.settings.json` directly.
- No address or medicine name in logs; log only "Guided setup
  completed" or "dismissed" with the step reached.
- New UI strings in all five `assets/localization/strings.<lang>.json`;
  the five `docs/USER_GUIDE.<lang>.md` get a short "Guided setup"
  section and an updated first-start section.
- Keep the single-instance mutex and the boot order in `Program.cs`
  intact; the wizard opens after the main window, never before the
  profile is open.

## 5. Workflow

- Ask before creating the branch; proposed name
  `feature/guided-setup`. Open the PR after the first commit, prepend a
  `CHANGE_LOG.md` entry, ask the user to run `dotnet build` and
  `dotnet test` before committing source code.
- Suggested commit order: `GuidedSetupFlow` with tests; UI settings
  fields and the seed in `MedicineEditDialog`; `GuidedSetupForm`;
  empty state and Help item; boot hook after a new installation;
  strings and user guides.

## 6. Verification

- `GuidedSetupFlow` tests: step order and skips; step 4 shown or not
  depending on steps 1 and 3; admin versus standard user and SMTP
  configured or not; dismissed flag set on "Not now" and on close.
- Seed test: a new medicine created after step 3 starts with the chosen
  lead time and channel; existing medicines not added in step 2 are
  untouched.
- Manual checks in the running app: a fresh installation in Italian at
  150 % scaling with Large text; "For someone I look after" with a
  caregiver address and no SMTP account (administrator and standard
  user); a join of an existing installation does not open the wizard;
  Help → Guided setup on a profile that already has medicines; dark
  mode and a high-contrast theme.

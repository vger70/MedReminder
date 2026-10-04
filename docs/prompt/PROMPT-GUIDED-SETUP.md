# Implementation prompt — Guided setup after the first start

Briefing for the Claude Code session that implements item M2 of the
evolution plan prepared on 2026-10-04 (not the "M2" catalogue
autocomplete cited in `MedicineEditDialog.cs`; competitor benchmark and
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
  (`Ui.MainForm.Summary.*`), Therapy → New medicine
  (`Ui.MainForm.Menu.Therapy.NewMedicine`), the `?` menu
  (`Ui.MainForm.Menu.Help`), the SMTP check
  (`IOptionsMonitor<SmtpSettings>.CurrentValue.IsConfigured`).
- `src/MedReminder.UI/Forms/SettingsDialog*.cs` — notification addresses,
  caregiver copies and weekly summary, Email (SMTP, admin only). The
  dialog always opens on its first section (`SelectSection(0)`).
- `src/MedReminder.Application/UseCases/ProfileSettingsUseCases.cs`:
  `UpdateNotificationSettings.ExecuteAsync(toAddress, caregiverAddress,
  doctorAddress, …, caregiverEmails, caregiverDigest)` and
  `RenameProfile`; `UseCases/AddMedicine.cs`, `UseCases/UpdateMedicine.cs`.
- `src/MedReminder.Application/Abstractions/IMasterRole.cs` — which
  device sends email (every device while no master is elected).
- `src/MedReminder.Infrastructure.Portable/Settings/
  ProfileUiSettingsFile.cs` and `Application/Abstractions/TextSize.cs`,
  `AppearanceMode.cs` — device-local per-profile UI settings.
- `src/MedReminder.UI/Forms/DialogLayout.cs`, `MedReminderFormBase.cs`
  — the dialog template, inline errors, DPI and text-size scaling.
- `docs/ANALYSIS.md` — household and master device (who sends email),
  roles.

## 3. Design

### 3.1 When it appears

- By itself, once the main window is shown, for any profile that has
  no medicines (inactive ones included) and whose `ui.settings.json`
  has no `GuidedSetupShown` flag. This covers the new installation
  created by `FirstRunWizardForm` without passing any signal from
  `Program.cs`; a join brings existing profiles, which open it only if
  they have no medicines and the flag is not set on this device.
- For any profile with no medicines, from a "Start guided setup" link
  in the empty main list (an empty state, if the list has none yet:
  one line of text and two links, "Add a medicine" and "Start guided
  setup").
- From the `?` menu → Guided setup, at any time.
- Finishing, dismissing ("Not now", Esc) or closing it sets the
  device-local flag `GuidedSetupShown` in the profile's
  `ui.settings.json`, so it does not open by itself again for that
  profile on this device; the empty-state link and the menu item stay.
  The flag is not replicated.

### 3.2 Steps

One window, `GuidedSetupForm`, with Back / Next / Not now and a step
indicator; each step can be skipped. Keep the step logic in a plain
class (`GuidedSetupFlow`) that the form drives, so it is testable
without WinForms. The flow receives what it depends on as plain
inputs (administrator or not, SMTP configured or not, this device
sends email or not, from `IMasterRole.SendsEmailAsync`), because
`SmtpSettings` lives in the Windows-only Infrastructure project.

1. **Who the medicines are for.** "For me" or "For someone I look
   after". The answer changes the wording of the next steps, which
   address field the user's own address goes to in step 4, and
   pre-selects step 4; the profile name from the first-run wizard stays
   editable here and a change goes through `RenameProfile`. Creating
   more profiles stays in Tools → Manage profiles; this step ends with
   one line pointing there.
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
   Email alone is offered only together with a warning: if the chosen
   channels do not include Windows and, when the wizard ends, the
   profile has no recipient address or no SMTP account is configured,
   the summary says that these medicines will not warn anyone and
   offers "Also warn on Windows", which adds the Windows channel to the
   step 2 medicines and to the seed.
4. **Email (shown when step 3 includes email, or step 1 was "someone I
   look after").** Two address fields, which kinds of email the
   caregiver receives and the weekly summary. "For me": the user's
   address is `ToAddress`, the optional second field is the person who
   assists them (`CaregiverAddress`). "For someone I look after": the
   user is the caregiver, so their address is `CaregiverAddress` and
   the optional second field is the address of the person looked after
   (`ToAddress`). Saved through `UpdateNotificationSettings.ExecuteAsync`;
   `doctorAddress` is required by that method and is not edited here,
   so read the current value from `IProfileSettingsStore` and pass it
   back unchanged, otherwise it is cleared on every device of the
   profile. If no SMTP account is configured: an administrator gets
   "Set up the email account", which opens `SettingsDialog` on the
   Email section (add an optional initial-section parameter; today it
   always opens on the first one) and, when it closes, the step reads
   `IOptionsMonitor<SmtpSettings>.CurrentValue.IsConfigured` again; a
   standard user gets a line saying an administrator has to set it up.
   On a device that does not send email (`IMasterRole`), say that
   emails are sent by the master device.
5. **Summary.** What the app will now do, in plain sentences ("You will
   be warned 7 days before Ramipril runs out, on Windows and by email").
   Below, three optional next steps as links, each opening the existing
   window: dose-time reminders, backup, use on another PC (Sync /
   Installation, administrators only). Finish closes the window.

### 3.3 Interface rules

- Same look as the other dialogs (`DialogLayout`, theme, high
  contrast); honours the profile text size; every control reachable by
  keyboard with a visible focus; Enter = Next unless the focused
  control is a button, link or list that handles Enter itself;
  Esc = Not now.
- Short sentences, no medical advice, no dose suggestions: the wizard
  only collects what the user types (not a medical device,
  `docs/EVOLUTION.md` §9.2).
- No feature is hidden or removed from the main window by this item. A
  simplified main view for new users is out of scope; mention it in the
  PR as a possible follow-up.

## 4. Constraints

- No schema change, no new replicated value, no export change: the only
  new values are device-local UI settings (`GuidedSetupShown`,
  `NewMedicineThresholdDays`, `NewMedicineChannels`). If the
  implementation finds a reason to replicate a new value, stop and ask.
- Existing replicated values are written only through their existing
  use cases, and their replication is intended: medicine threshold and
  channels through `UpdateMedicine`, addresses and caregiver settings
  through `UpdateNotificationSettings`, the profile name through
  `RenameProfile`. Do not write `notifications.settings.json` or the
  profile registry directly.
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
  empty state and `?` menu item; automatic opening for a profile with
  no medicines once the main window is shown;
  strings and user guides.

## 6. Verification

- `GuidedSetupFlow` tests: step order and skips; step 4 shown or not
  depending on steps 1 and 3; admin versus standard user, SMTP
  configured or not, sending device or not; the user's address mapped
  to `ToAddress` for "For me" and to `CaregiverAddress` for "For
  someone I look after"; the email-only warning raised when no
  recipient or no SMTP account; shown flag set on Finish, "Not now"
  and close; automatic opening only for a profile with no medicines
  and no flag.
- Notification settings test: saving from step 4 keeps the existing
  doctor address.
- Seed test: a new medicine created after step 3 starts with the chosen
  lead time and channel; existing medicines not added in step 2 are
  untouched; `ProfileUiSettingsFile` round-trips the new values and
  reads a missing or damaged value as the default.
- Manual checks in the running app: a fresh installation in Italian at
  150 % scaling, then Large text set in Settings and the wizard
  reopened from the `?` menu; "For someone I look after" with a
  caregiver address and no SMTP account (administrator and standard
  user); a join of an existing installation does not open the wizard
  for profiles that have medicines; `?` → Guided setup on a profile
  that already has medicines; dark
  mode and a high-contrast theme.

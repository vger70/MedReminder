# Implementation prompt — Accessible "large text" mode

Briefing for the Claude Code session that implements proposal 3.2 of
`docs/notes/EVOLUTION-PROPOSALS.md` (ranking #2). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Make the WinForms UI comfortable for elderly and low-vision users, the
main audience for chronic-therapy management. A user who struggles to
read the grid or hit small buttons should be able to fix that from
Settings, once, and have every window follow.

Success means: at the larger size every form is fully readable, no
control is clipped or overlaps another, every action stays reachable
with the keyboard, and the app still looks correct at the default size.

## 2. Context to read first

- `CLAUDE.md` — language policy, localization, PR workflow, constraints.
- `docs/notes/EVOLUTION-PROPOSALS.md` §3.2 — the proposal and its notes.
- `docs/ANALYSIS.md` — UI layer structure and settings files.
- `src/MedReminder.UI/app.manifest` — declares `PerMonitorV2` DPI awareness.
- `src/MedReminder.UI/Program.cs` — `ApplicationConfiguration.Initialize()`.
- `src/MedReminder.UI/Forms/MedReminderFormBase.cs` — base class of every
  form; its comment already anticipates shared font conventions.
- `src/MedReminder.Application/Abstractions/UserSettings.cs` and
  `src/MedReminder.UI/Forms/SettingsDialog.cs` — where UI preferences live.
- `src/MedReminder.Application/Abstractions/IApplicationRestarter.cs` —
  existing restart path (used by language change).

## 3. What the tree looks like today

Verify these before designing; correct this list if any fact is wrong.

- Around 40 explicit `new Font(...)` calls and around 50 explicit
  `Location = new Point(...)` assignments exist under `src/MedReminder.UI`.
  Fixed positions and fixed fonts are the main obstacle to scaling.
- Some dialogs already use `AutoSize` and layout panels; others are
  positioned by hand.
- No code reads `SystemInformation.HighContrast` or reacts to
  `SystemEvents.UserPreferenceChanged`.

## 4. Approach

Work in this order, and stop to report after step 1 if the findings
change the scope materially.

1. **Audit before building.** Run the app at 100 %, 150 % and 200 %
   Windows display scaling and with a Windows high-contrast theme.
   Record per form what breaks (clipping, overlap, hard-coded colours
   that disappear in high contrast). Part of the need may be met by
   honouring system settings correctly; fix that first, because it helps
   users who never open Settings.
2. **Make layouts scale.** Replace hand-placed coordinates with
   `TableLayoutPanel` / `FlowLayoutPanel` and `AutoSize` where a form
   cannot scale otherwise. Derive fonts from the form font instead of
   constructing fixed families and sizes. Keep the change form by form
   so each commit is reviewable.
3. **Add the setting.** A text-size preference (for example Normal /
   Large / Extra large) stored in `UserSettings`, applied centrally —
   `Application.SetDefaultFont` before the first window is created is the
   natural place, with `MedReminderFormBase` for anything the default font
   does not reach. If the change needs a restart, reuse
   `IApplicationRestarter` and the same confirmation pattern the language
   switch uses. Larger size should also enlarge grid row height and
   button minimum size.
4. **Contrast.** Prefer system colours (`SystemColors`) so Windows
   high-contrast themes work, over shipping a custom high-contrast
   palette. Replace hard-coded colours that break under high contrast.

Decision to surface to the user before step 3: whether the preference is
shared across profiles (`user.settings.json`, simplest) or per profile
(an elderly person and a caregiver sharing the PC may want different
sizes). Recommend one with the tradeoff; do not decide silently.

## 5. Out of scope

- "Simplified navigation" from the original draft: note concrete ideas
  found during the audit in the PR description, but do not redesign
  navigation in this change.
- Themes, dark mode, custom colour palettes.
- Any Domain or Infrastructure change beyond persisting the setting.

## 6. Constraints

- Every new UI string goes into all five
  `assets/localization/strings.<lang>.json` files; the dictionary parity
  test must pass.
- Update the text-size section in all five `docs/USER_GUIDE.<lang>.md`
  guides (each in its own language).
- Code, comments, commits and PR text in English.
- Keep the look at default size unchanged unless the audit shows a
  defect.

## 7. Workflow

- Ask before creating the branch; proposed name
  `feature/large-text-mode`. Base: `main`.
- Open the PR after the first commit and prepend an entry to
  `CHANGE_LOG.md`.
- Ask the user to run `dotnet build MedReminder.sln -c Release` and
  `dotnet test MedReminder.sln -c Release` before each commit that
  touches source.
- Commit messages: imperative, explain why.

## 8. Verification

- Unit test: the setting round-trips through `user.settings.json`, and an
  unknown or missing value falls back to Normal.
- Manual check, documented in the PR with the list of forms covered:
  each form at Normal and Extra large, at 100 % and 200 % scaling, and
  under a high-contrast theme. Use the `run` skill to launch the app and
  take screenshots where helpful.
- Keyboard-only pass through MainForm and the most used dialogs.

# ANALYSIS — UI modernization (restyling and UX redesign in WinForms)

Design document, **prior** to implementation. Phase 0 of the UI
modernization: review of the current WinForms UI, target design
system, layout proposals and phased plan. No code changes.

Status on 2026-09-30: revision 5. Scope agreed with the product owner:
options A (restyling) and B (UX redesign) of the preliminary estimate;
a framework migration (WinUI 3, WPF, Avalonia) is out of scope.

Reading conventions: `[VERIFIED]` (checked against the tree at commit
`64db3a0`, branch `feature/master-slave`), `[INFERRED]` (deduction from
verified facts), `[UNCERTAIN]` (not verified). Untagged statements are
design proposals.

Method: static review of `src/MedReminder.UI`. The review ran in a
Linux container, so no screenshot of the running app was taken; the
screenshot baseline is the first task of phase 1 (§8, step 1.0) and
must run on Windows.

---

## 1. Goals and non-goals

| # | Goal |
|---|---|
| G1 | One visual language across all windows: palette, typography, spacing, icons |
| G2 | Light and dark mode, following Windows by default, overridable per profile |
| G3 | Readability for the core audience (elderly users, caregivers): the large-text mode, display scaling and high-contrast themes keep working |
| G4 | The main window answers "what needs attention today" at a glance |
| G5 | Settings and dialogs share one layout, one button bar, one validation style |
| G6 | Each phase ships on its own; the app is releasable after every phase |

Non-goals: framework migration; new features; changes to Domain,
Application or Infrastructure; changes to the printed therapy card
layout (it stays black on white, §5.6).

---

## 2. Inventory [VERIFIED]

| Item | Count / value |
|---|---|
| C# files in `MedReminder.UI` | 65 (20,254 lines) |
| Files under `Forms/` | 38 (35 windows, base class, pickers) |
| Largest windows | `MainForm.cs` 1,929 lines, `SettingsDialog.cs` 1,990, `MedicineEditDialog.cs` 1,084, `HouseholdDialog.cs` 842, `SyncDialog.cs` 800, `ProfilesManagerForm.cs` 752 |
| Designer files | 0: every control is built in code |
| Layout containers | 33 `TableLayoutPanel`, 74 `FlowLayoutPanel`, 9 `Panel`, 3 `TabControl`, 1 `GroupBox` |
| Absolute positioning | 2 files (`BarcodeScanDialog`, `MedicineAutocompleteBox`) |
| `ForeColor`/`BackColor` assignments | 83 |
| `new Font(...)` | 36 |
| Hard-coded `Color.FromArgb` | 24, of which 12 in `MainForm.cs`, 10 in `Controls/TimelinePainter.cs` |
| `MessageBox.Show` / `TaskDialog` | 115 / 26 |
| Windows with `AcceptButton` | 21 |
| Windows with a right-to-left button bar | 25 |
| `FormBorderStyle` | 27 `FixedDialog`, 2 `Sizable` |
| Owner-drawn controls | `TherapyTimelineChart`, `TimelineLegendEntry`, `MedicineAutocompleteBox` (dropdown), `SchedulePanel` |
| Icons | Segoe MDL2 Assets glyphs rendered to bitmaps (`UiExtensions/Mdl2Glyph.cs`), cached by glyph, size and colour |
| Theming | `UiExtensions/UiColors.cs`: four text colours, high-contrast fallback; no dark mode |
| Scaling | `MedReminderFormBase.ScaleLayout`: forms are written at 96 DPI and scaled by display DPI × profile text size |
| UI tests | `MedReminder.UI.Tests`: hosted services and a few controls; no visual tests |

The layout is almost entirely panel-based. [INFERRED] A restyling does
not require rewriting layouts; it requires replacing the styling calls
and a small set of shared builders.

---

## 3. Findings

Severity: **H** visible inconsistency or accessibility risk,
**M** inconsistency a user notices on comparison, **L** code hygiene
that blocks theming.

| # | Sev | Finding | Where |
|---|---|---|---|
| F1 | H | Two base fonts. 17 windows set Segoe UI 9.75 pt; 19 inherit the WinForms default (Segoe UI 9 pt). Moving from the main window to Settings, the medicine editor or the PIN prompt shrinks the text | `Font = new Font("Segoe UI", 9.75F)` in `MainForm`, `SyncDialog`, `HouseholdDialog`...; absent in `SettingsDialog`, `MedicineEditDialog`, `PinPromptForm`, `ProfilePickerForm`, `FirstRunWizardForm`... |
| F2 | H | No theme layer. Colours live in the forms: status palette and row tints in `MainForm.cs:34-49`, error banner in `MainForm.cs:493-502`, timeline palette in `TimelinePainter.cs:35-47`. A dark mode would need edits in every file | see §2 |
| F3 | H | The main-window grid encodes status twice: a full-row tint (`WarningColor`, `EmptyColor`, `SuspendedColor`) and a coloured status cell. With several rows in warning the grid turns yellow/red as a whole and the selection highlight competes with the tint | `MainForm.cs:34-49`, `MainForm.cs:1117-1135` |
| F4 | M | No summary. The grid is the only view; the user scans every row to find medicines running out or prescriptions to request | `MainForm.BuildLayout` |
| F5 | M | Toolbar with seven text+icon buttons at 24 px, `RenderMode.System`: dated look, crowded at 720 px width and in German | `MainForm.cs:534-568` |
| F6 | M | `SettingsDialog` uses a `TabControl` with up to five tabs, some of them long (General, Notifications, Backup); a tab's content scrolls inside a fixed 840×620 window. One 1,990-line class builds all tabs | `SettingsDialog.cs:198-229` |
| F7 | M | Dialog buttons vary: fixed `Width = 100, Height = 32` in some windows, `AutoSize` in others; 21 of 35 windows set `AcceptButton` | `SettingsDialog.cs:231`, others |
| F8 | M | Spacing has no scale: 15 distinct `Padding`/`Margin` values, e.g. `(12)`, `(12,8,12,8)`, `(16)`, `(4,8,4,4)`, `(3,3,3,8)`, `(0,6,8,6)` | all forms |
| F9 | M | Validation errors reach the user through `MessageBox` after the click; no inline messages next to the field (`ErrorProvider` not used) | 115 `MessageBox.Show` |
| F10 | M | Mixed confirmation styles: `MessageBox` (115) and `TaskDialog` (26) | all forms |
| F11 | L | `Mdl2Glyph` renders with `SystemColors.ControlText` by default; the cache keys on colour, so a theme switch needs re-rendering and re-assignment of every image | `Mdl2Glyph.cs` |
| F12 | L | Section headings are ad-hoc bold labels (`new Font(Font, FontStyle.Bold)`, 14 pt title in `AboutDialog`, 10.5 pt in `HandoverWizardForm`): no heading scale | 8 occurrences |
| F13 | L | Segoe MDL2 Assets is the Windows 10 icon font; Windows 11 ships Segoe Fluent Icons with the same code points and a lighter stroke [UNCERTAIN: full code-point parity for the glyphs used to be checked in step 1.3] | `Mdl2Glyph.cs` |

What already works and must be preserved:

- High-contrast fallback in `UiColors` and in the error banner.
- DPI and text-size scaling in `MedReminderFormBase` (EVOLUTION-PROPOSALS §3.2).
- Keyboard access: `MainMenuStrip` shortcuts, `AcceptButton` where set.
- The status colours in the grid already pass AA contrast on their tints [INFERRED from the values in `MainForm.cs:42-49`; to be measured in step 1.1].

---

## 4. Target design system

### 4.1 Tokens

One static class, `UiExtensions/UiTheme.cs`, replaces `UiColors` (kept
as a thin facade during the migration). Three palettes: Light, Dark,
High contrast. High contrast maps every token to a `SystemColors`
value, as `UiColors` does today.

Contrast ratios measured with the WCAG 2.x relative-luminance formula;
every text pair is at least 4.5:1 (AA, normal text).

| Token | Light | Dark | Ratio L / D | High contrast |
|---|---|---|---|---|
| `Background` (window) | `#F3F3F3` | `#202020` | — | `Control` |
| `Surface` (grid, cards, fields) | `#FFFFFF` | `#323232` | — | `Window` |
| `Border` | `#E0E0E0` | `#464646` | — | `WindowFrame` |
| `Hover` (menus, secondary buttons) | `#EAEAEA` | `#3D3D3D` | 14.3 / 10.9 with `Text` | `Highlight` |
| `Text` on `Surface` | `#1B1B1B` | `#FFFFFF` | 17.2 / 12.8 | `WindowText` |
| `TextSecondary` on `Surface` | `#5C5C5C` | `#C5C5C5` | 6.7 / 7.4 | `GrayText` |
| `Accent` (primary button, links, focus) | `#0F6CBD` | `#62ABF5` | 5.4 / 5.3 on surface | `Highlight` |
| `AccentHover` | `#115EA3` | `#7DB9F7` | 6.7 / 10.2 with `OnAccent` | `Highlight` |
| `OnAccent` (text on accent) | `#FFFFFF` | `#000000` | 5.4 / 8.7 | `HighlightText` |
| `Selection` (grid row) | `#CFE4FA` | `#0E4775` | 13.2 / 9.7 with `Text` | `Highlight` |
| `Ok` fg / bg | `#0E700E` / `#DFF6DD` | `#9FD89F` / `#1E3A1E` | 5.5 / 7.6 | `WindowText` / `Window` |
| `Warning` fg / bg | `#835B00` / `#FFF4CE` | `#F4D38A` / `#3D300E` | 5.5 / 8.9 | same |
| `Danger` fg / bg | `#B10E1C` / `#FDE7E9` | `#F1BBBC` / `#4A2426` | 6.0 / 8.0 | same |
| `Neutral` (suspended) fg / bg | `#484644` / `#EDEBE9` | `#C8C8C8` / `#404040` | 7.9 / 6.2 | same |

The palette follows the neutral greys and the blue accent of the
Windows 11 look; it is not a copy of an official token set. The dark
`Background` and `Surface` equal the colours WinForms itself uses in
dark mode for `Control` (`#202020`) and `Window` (`#323232`)
[VERIFIED: `KnownColorTable.AlternateSystemColors`, .NET 10.0.12], so
stock and themed controls share the same greys. `UiThemeTests` checks
every text pair of both palettes against 4.5:1.

### 4.2 Typography

| Role | Font | Size (pt at text size Normal) |
|---|---|---|
| Body, controls | Segoe UI Variable Text, fallback Segoe UI | 10 |
| Secondary, captions | same | 9 |
| Section heading | Segoe UI Variable Display Semibold, fallback Segoe UI Semibold | 12 |
| Window title (in-content) | same | 16 |
| Codes (pairing, report preview) | Cascadia Mono, fallback Consolas | 10 |

`UiTheme.Fonts` creates each font once per text size. The base size
moves from the two current values (9 and 9.75 pt, F1) to 10 pt; the
text-size factor of `MedReminderFormBase` applies on top. [INFERRED]
The move to 10 pt enlarges the windows that inherit 9 pt by about 11 %;
their minimum sizes are re-checked in the baseline run.

Segoe UI Variable ships with Windows 11 only; on Windows 10 the
fallback applies. Font availability is checked once through
`InstalledFontCollection`.

### 4.3 Spacing and shape

- Scale in pixels at 96 DPI: 4, 8, 12, 16, 24, 32. `UiTheme.Space.*`
  constants; every `Padding`/`Margin` uses them (F8).
- Window content padding 16; between groups 24; label to field 8;
  between fields 12.
- Button height 32, minimum width 96, `AutoSize` above that; primary
  button filled with `Accent`, others outlined with `Border`.
- Corner radius: WinForms controls stay square; owner-drawn elements
  (status pills, summary cards) use a 4 px radius.

### 4.4 Dark mode

`Application.SetColorMode` is a supported API in .NET 10 (it required
suppressing `WFO5001` in .NET 9)
([What's new in WinForms for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100));
the .NET 10.0.12 reference assembly carries no `WFO5001` marker
[VERIFIED].

Behaviour of `System.Windows.Forms` 10.0.12 [VERIFIED, decompiled]:

- `SetColorMode` can be called at any time. It switches
  `SystemColors` to the dark set and broadcasts a colour change; it
  does not rebuild colours a form assigned explicitly.
- `SystemColorMode.System` follows the Windows "app mode" only on
  Windows 11; on Windows 10 it resolves to light.
  `SystemColorMode.Dark` is honoured on Windows 10 too.
- Under a high-contrast theme `IsDarkModeEnabled` is always false.
- In the dark set, `Highlight` `#2864B4` with `HighlightText` `#000000`
  reads at 3.6:1, below AA; themed grids use the `Selection` token
  instead. Stock list views keep it (step 2 checks them).
Open issues in dotnet/winforms report rendering defects for some
controls in dark mode, e.g. #13723 (buttons), #13636, #13901 (dropdown
focus contrast). Mitigation: primary and secondary buttons are drawn
by the theme (§4.3); the baseline run lists every remaining defect.

Setting: **Appearance** = System (default), Light, Dark, in Settings →
General. Scope: device × profile, stored in the profile's
`ui.settings.json` next to the text size; not replicated by the
household sync, same rule as the text size
(ANALYSIS-HOUSEHOLD-MASTER-DEVICE §3). The first-run wizard follows
Windows; the profile picker takes the appearance of the profile used
last, the PIN prompt that of the profile being opened. A change
applies after restart, which the app already offers for the language
and the text size (D5): colours assigned when a
window is built do not follow a live `SetColorMode` call.

When Windows runs a high-contrast theme, the High contrast palette
wins over the Appearance setting.

### 4.5 Icons

Keep `Mdl2Glyph` as the renderer; select Segoe Fluent Icons when
installed, Segoe MDL2 Assets otherwise (F13). Icons take their colour
from `UiTheme.Text` or `UiTheme.Accent`. The appearance changes only at
restart (D5), so the glyph cache never holds images of another theme
(F11 closed). The font switch moves to step 2: code-point parity of
the glyphs in `Mdl2Glyph.Glyphs` must be checked on a Windows 11
machine first.

---

## 5. Layout proposals

Mockups, light and dark: HTML page at
https://claude.ai/artifact/2uFqZT6oVJHqohxf9nXhmm (private, shared on
request). They show intent, not pixel values.

### 5.1 Main window

```
┌──────────────────────────────────────────────────────────────┐
│ File  Therapy  Stock  Tools  Help               (menu kept)  │
├──────────┬───────────────────────────────────────────────────┤
│ Medicines│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐  │
│ Timeline │  │ 1       │ │ 2       │ │ 1       │ │ 7       │  │
│ Reports  │  │ Empty   │ │ Warning │ │Suspended│ │ All     │  │
│ Household│  └─────────┘ └─────────┘ └─────────┘ └─────────┘  │
│          │  [+ New medicine] [Register intake]   [Search…]   │
│          │  ┌─────────────────────────────────────────────┐  │
│          │  │ Medicine   Stock  Daily  Days  Run-out Status│ │
│          │  │ ...                                  (pill)  │ │
│          │  └─────────────────────────────────────────────┘  │
├──────────┴───────────────────────────────────────────────────┤
│ Profile: Mario · Ready · Last check 09:12                    │
└──────────────────────────────────────────────────────────────┘
```

- Navigation pane on the left, collapsible to icons below 900 px
  width (D3). "Medicines" is the only in-window page (the grid). The
  other entries (timeline, therapy report, prescription request,
  installation, settings) open the existing windows, as the menus do
  today; they never show a selected state and carry an "opens a
  window" glyph. Turning them into in-window pages is out of scope:
  every window would become a hosted control with its own OK/Cancel,
  sizing and scaling rules, and modeless pages would allow editing a
  medicine while another page shows stale data.
- Summary cards: counts from the list the grid already loads
  (`MedicineOverviewLoader.LoadAsync`, grouped by status); a click
  filters the grid. No new use case. Cards that need other data (next
  dose, prescriptions to request) are out of scope for phase B.
- Grid: no full-row tint; status in a pill (tinted background,
  coloured text, 4 px radius); row height 36 px; horizontal separators
  only; alternating rows off (F3).
- Toolbar reduced to New medicine, Register intake and a search box
  (D4, F5). The five other actions stay reachable [VERIFIED]:
  Edit through row double-click (`MainForm.cs:1103`), F2, the Therapy
  menu and the new context menu; Check now through Tools (Ctrl+R);
  Therapy report (Ctrl+P), Timeline (Ctrl+T) and Request prescription
  through the navigation pane and the Therapy menu. Keyboard shortcuts
  do not change.
- New grid context menu (the grid has none today [VERIFIED]): Edit,
  Register intake, Add package, Adjust stock, Change dose/frequency,
  Deactivate, History. It reuses the menu commands.
- Search box: filters the grid by medicine name while typing; one or
  two new UI keys.
- Error banner restyled with `Danger` tokens.
- Menu bar kept: keyboard shortcuts and the user guide rely on it.

### 5.2 Settings

- Left list of sections (General, Email, Notifications, Startup,
  Backup, Appearance under General), right scrollable panel (F6).
- Admin-only sections stay hidden for non-admin profiles, as today.
- Each section becomes a `UserControl` under `Forms/Settings/`;
  `SettingsDialog` keeps navigation, load and save. Pure refactor, no
  behaviour change.
- Window resizable, minimum 760×520.

### 5.3 Dialog template

`Forms/DialogLayout.cs`: builder used by every dialog.

```
┌ Title ────────────────────────────────────────┐
│  Heading (12 pt semibold)                     │
│  Short explanation (secondary text)           │
│                                               │
│  Label        [field.....................]    │
│  Label        [field......]  ⓘ inline error   │
│                                               │
├───────────────────────────────────────────────┤
│                         [Cancel]  [ Save ]    │
└───────────────────────────────────────────────┘
```

- Button bar right-aligned, primary button last and filled, always
  `AcceptButton`/`CancelButton` (F7).
- Field errors shown inline under the field (label with `Danger`
  colour); `MessageBox` only for errors not tied to a field (F9).
- Confirmations through `TaskDialog` (already used in 26 places), with
  localized buttons (F10).

### 5.4 Wizards

`FirstRunWizardForm` and `HandoverWizardForm` use the dialog template
plus a step indicator ("Step 2 of 4").

### 5.5 Owner-drawn controls

`TimelinePainter` already groups its colours in one record (Light
only). Add a Dark and a High-contrast instance and pick by theme.
`SchedulePanel`, `TimelineLegendEntry` and the autocomplete dropdown
read `UiTheme`.

### 5.6 Printing

`TherapyCardPrintDocument` keeps its own fonts and black-on-white
output whatever the theme.

---

## 6. Localization

New keys (all five `strings.<lang>.json`): Appearance setting and
values, summary card titles, navigation entries, step indicator, a few
inline validation messages. Estimate 30 to 50 keys. The user guides
(`USER_GUIDE.{en,it,fr,es,de}.md`) describe the main window, the
toolbar and the Settings tabs; the affected sections are rewritten in
phase B.

German is the length stress case for navigation entries, card titles
and button captions.

---

## 6b. Implementation status

| Step | State | Where |
|---|---|---|
| 1.1 Tokens, fonts, spacing | Done | `UiExtensions/UiTheme.cs`; `UiColors` is a facade over it |
| 1.2 Appearance setting | Done | `AppearanceMode` (Application), `ProfileUiSettingsFile`, `Program.ApplyAppearance`, Settings → General, 5 new keys per language |
| 1.3 Themed controls | Done | `UiThemeApplier` (buttons, grids) from `MedReminderFormBase.OnLoad`; `UiToolStripRenderer` as `ToolStripManager.Renderer` |
| Palette migration pulled forward | Done | Main grid status and error banner, timeline dark palette, autocomplete badge: without it the dark mode would show light tints under light text |
| 1.0 Screenshot baseline | Done | 12 captures by the product owner (§6c) |
| 2 Controls, typography, icons | Done for the main window, Settings and the medicine editor (PR #145) | Baseline fixes S1, S3, S4, L1–L3, L5, L6 (label alignment), P1; one 10 pt base font set by `MedReminderFormBase`; icons drawn at the scaled size, Segoe Fluent Icons when installed. The 32 px buttons and the spacing scale in the other dialogs move to step 5 |
| 3 Main window | Done (PR #146) | `Controls/NavigationPane.cs` (D3), `Controls/SummaryCard.cs`, `MedicineListFilter` (Application, unit-tested), toolbar with New medicine, Register intake and search (D4, Ctrl+F), grid context menu, status pill, 36 px rows; main-window section of the five user guides |
| 4 Settings | Done (PR #147) | Section list (`NavigationPane`) on the left, one section shown at a time with a heading, Ctrl+Tab / Ctrl+PageDown between sections; window resizable, minimum 760×520; each section in its own partial file `Forms/SettingsDialog.<Section>.cs` instead of a `UserControl` (see below); Notifications line in the five user guides |
| 5a Dialog template | Done, pending Windows check | `Forms/DialogLayout.cs` (form table, button bar with the primary button last, 88×32 buttons, `GrowToContent`, inline errors) applied to 12 small dialogs; baseline L6 fixed (`MedicineAutocompleteBox` height); `LogicalToDeviceUnits` removed from the PIN prompt and wizards (scaled twice with `ScaleLayout`). Larger dialogs, wizard step indicator and confirmations follow in 5b/5c |

The summary cards count the rows the grid can show before the card and
search filters (active medicines, plus inactive ones when shown), so a
card always shows how many rows a click on it lists. A second click on
the active card clears the filter; opening a medicine from the timeline
clears a filter that would hide it.

Settings sections are partial files of `SettingsDialog`, not the
`UserControl` per section planned in §5.2. The sections share about
twenty injected services and cross-section state (the cloud warning
reads the backup fields, the General save prompts the restart shared
with the text size), which a `UserControl` split would have to pass
around; the partial files move the code without changing it, so the
refactor carries no behaviour change. The dialog frame and the section
list stay in `SettingsDialog.cs`.

## 6c. Screenshot baseline (step 1.0)

Captured on Windows 11 by the product owner on 2026-09-30, build of
PR #144: main window, Settings (General, Notifications, Backup) and
the medicine editor, at 100 % + Normal + Light + EN and at Large +
Dark + DE, the main window also at 150 % display scaling. Findings:

| # | Finding | Where | Step 2 action |
|---|---|---|---|
| S1 | Drop-down lists keep a white face in dark mode | Settings, medicine editor | Flat style with palette colours (`UiThemeApplier.StyleComboBox`) |
| S2 | Date pickers keep a white field in dark mode | Medicine editor | None: WinForms does not recolour the field; revisit if a replacement is wanted |
| S3 | Text boxes mix a light single border and no border in dark mode | Medicine editor, Notifications | One 3D border style and palette colours (`StyleTextBox`) |
| S4 | Dose-slot list draws light grid lines in dark mode | Medicine editor | Grid lines off, palette colours (`StyleListView`) |
| S5 | Tab headers stay light grey in dark mode | Settings | None: tabs are replaced in step 4 |
| L1 | "Benachrichtigungskanäle" split mid-word by a fixed 160 px label column | Medicine editor | Label column sized to the longest label |
| L2 | Wrapped German column headers cut ("Verbleibende Tage", "Aufgebraucht am") | Main window | Header height sized to the captions |
| L3 | Help texts touch the next label | Settings → General | 16 px above each group |
| I1 | Toolbar and menu glyphs stay at 24/16 px while text grows with Large and 150 % | Main window | Glyphs rendered at the scaled size |
| L5 | At 150 % + Large the General tab is taller than the window and its top-down flow wraps the last note into a second column | Settings → General | Top-down flows in Settings scroll instead of wrapping |
| L6 | Labels centred on rows taller than their field: Name and Active ingredient rows in the medicine editor at 150 % + Large (the rows are about twice the field height), wrapped labels in Settings → Notifications | Medicine editor, Settings | Labels aligned to the top of the row; the extra row height in the medicine editor is still open |
| P1 | Profile picker light for a profile set to Dark: it followed Windows because no profile was known yet | Profile picker | Picker takes the last used profile's appearance, PIN prompt the chosen profile's |

Display scaling itself works: the main window at 150 % + Large is
1.86 times the 100 % + Normal capture (1.5 × 1.25 = 1.875 expected).

## 7. Risks

| Risk | Mitigation |
|---|---|
| Dark-mode defects in stock controls (§4.4) | Theme-drawn buttons; list defects in the baseline; fall back to Light for a control that cannot be fixed |
| Layout regressions at 150–200 % scaling or Large text | Manual matrix (§9) at the end of every phase |
| Merge conflicts with ongoing work on `feature/master-slave` (household windows) | Phase 1 touches shared files only (`UiTheme`, base form); per-window changes go in small PRs |
| Moving to 10 pt base font clips labels in fixed-size dialogs | Baseline screenshots before/after; `AutoSize` on labels |
| Summary counts disagree with the grid | Cards computed from the same list the grid binds |

---

## 8. Plan and estimates

Estimates in person-days for one senior developer familiar with
WinForms; they include the manual verification matrix (§9).

| Step | Content | Days |
|---|---|---|
| **1.0** | Baseline: screenshots of every window at 100 % and 150 %, Light, Normal and Large text, EN and DE (Windows) | 0.5 |
| **1.1** | `UiTheme` tokens, fonts, spacing; `UiColors` becomes a facade | 1 |
| **1.2** | Appearance setting, `SetColorMode`, persistence in `ui.settings.json`, keys in five languages | 1 |
| **1.3** | Themed buttons, `ToolStripRenderer`, grid style helper, `Mdl2Glyph` font selection and cache reset | 1.5 |
| **2** | Apply to every window: 83 colour and 36 font assignments, spacing scale, owner-drawn controls | 4–6 |
| | **Total A** | **8–10** |
| **3** | Main window: navigation pane, summary cards, grid restyle, reduced toolbar | 5–7 |
| **4** | Settings: section list, one `UserControl` per section | 3–5 |
| **5** | `DialogLayout` builder, inline validation, `TaskDialog` confirmations, wizards step indicator; applied to ~30 dialogs | 4–6 |
| **6** | User guides (five languages), `docs/ANALYSIS.md`, change log | 1–2 |
| | **Total B (includes A)** | **21–30** |

Phase 0 (this document and the mockups): 1–2 days, done.

Each step is one PR into `feature/master-slave` (or into `main` once
the household work merges), except step 2, split by window group
(main and editors; sync and household; import/export and backup;
remaining dialogs).

---

## 9. Verification matrix (every phase)

- Display scaling 100 %, 150 %, 200 %.
- Text size Normal and Large.
- Appearance Light, Dark; Windows high-contrast theme (Aquatic or
  Desert).
- Languages EN and DE (length), IT spot check.
- Windows 10 22H2 and Windows 11 (font fallback, icon font).
- Keyboard only: Tab order, Enter/Escape on every dialog.

---

## 10. Decisions

Accepted by the product owner on 2026-09-30 as proposed.

| # | Decision | Outcome |
|---|---|---|
| D1 | Appearance scope | Device × profile, like text size (§4.4) |
| D2 | Base font size | 10 pt (§4.2) |
| D3 | Navigation pane entries open windows or in-window pages | Windows in phase B; pages out of scope |
| D4 | Toolbar | Two primary actions + search (§5.1) |
| D5 | Live theme switch or restart | Restart, unless step 1.2 shows live switching is cheap |

---

## 11. Revisions

| Rev | Date | Change |
|---|---|---|
| 1 | 2026-09-30 | First version: inventory, findings, design system, layouts, plan |
| 2 | 2026-09-30 | D1–D5 accepted; §5.1 details the navigation pane and the toolbar |
| 3 | 2026-09-30 | Phase 1 implemented (§6b); dark tokens aligned with WinForms' dark system colours; §4.4 verified against .NET 10.0.12 |
| 4 | 2026-09-30 | Screenshot baseline (§6c); step 2 started |
| 5 | 2026-09-30 | Step 2 closed for the reviewed windows; step 3 implemented |

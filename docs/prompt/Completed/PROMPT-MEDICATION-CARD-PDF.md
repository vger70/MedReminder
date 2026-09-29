# Implementation prompt — Printable medication card (PDF)

Briefing for the Claude Code session that implements proposal 4.4 of
`docs/notes/EVOLUTION-PROPOSALS.md` (ranking #9). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Give the user a compact, well laid-out list of active medicines and daily
dosages to hand to a GP, emergency room or pharmacist, printable on paper
and savable as PDF.

## 2. What already exists

This proposal extends an existing feature; it is not a new one. Verify:

- `src/MedReminder.Application/Reporting/TherapyReport.cs` builds a
  localized plain-text therapy card (active medicines, slots, start/end,
  doctor, notes, disclaimer).
- `src/MedReminder.UI/Forms/TherapyReportDialog.cs` shows it, saves it as
  `.txt`, and prints it with preview via `PrintDocument`.

The gap is presentation and format: plain monospace text, no table
layout, no direct PDF output.

## 3. Context to read first

- `CLAUDE.md`, `docs/notes/EVOLUTION-PROPOSALS.md` §4.4.
- The two files above and their tests.
- `THIRD-PARTY-NOTICES.md` and `docs/PACKAGING.md` (dependency and size
  impact on both publish profiles).

## 4. Design

- **Separate content from layout.** Refactor `TherapyReport` so it
  produces a structured model (header, profile, date, rows per medicine
  with dosage/slots, period, doctor, optional notes, disclaimer). The
  existing text output becomes one renderer of that model, so existing
  behaviour and tests keep working.
- **Printed layout.** A second renderer draws a proper table with
  `System.Drawing.Printing`, paginating rows and repeating the header,
  sized for A4 and Letter.
- **PDF output.** Two options; evaluate and recommend one before
  implementing:
  1. No new dependency: print the same `PrintDocument` to the built-in
     "Microsoft Print to PDF" printer, preselected by a "Save as PDF"
     button with the file name set programmatically. Check that it works
     on Windows 10 and 11 and fails cleanly when that printer is removed.
  2. A PDF library. Only permissive licences (MIT, Apache-2.0, BSD);
     check the licence of the exact version, including revenue-based
     "community" licences, which do not fit. Add it to
     `THIRD-PARTY-NOTICES.md` and report the publish size increase.
  Ask the user before adding any package.
- **Content choices.** Active medicines only by default. Notes are free
  text that may be private: make including them an explicit option,
  default off. Include the "not a medical device" disclaimer.

## 5. QR code

The original draft proposed a QR code carrying the therapy as
"anonymised JSON". A medicine list is health data, and a printed QR is
readable by anyone. Do not implement it in this change; mention it in the
PR as a possible opt-in follow-up.

## 6. Constraints

- The PDF is written only where the user chooses via `SaveFileDialog`,
  as the existing `.txt` save does. No automatic copies under
  `%LOCALAPPDATA%\MedReminder\`.
- Never log report content, medicine names or the chosen file path.
- New UI strings in all five `assets/localization/strings.<lang>.json`;
  existing `Reports.Therapy.*` keys are reused where possible. Update the
  five `docs/USER_GUIDE.<lang>.md`.

## 7. Workflow

- Ask before creating the branch; proposed name
  `feature/medication-card-pdf`. Open the PR after the first commit,
  prepend a `CHANGE_LOG.md` entry, ask the user to run build and tests
  before committing source.

## 8. Verification

- Existing `TherapyReport` tests pass unchanged (text renderer).
- New tests on the structured model: no active medicines, many medicines
  (pagination input), missing optional fields, notes option on and off,
  each supported language.
- Manual check: print preview, a real PDF opened in a viewer, A4 and
  Letter, a long list spanning two pages. Attach a sample PDF made from
  test data (no real personal data) to the PR.

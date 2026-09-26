# ANALYSIS — A2: Medicine barcode scan (webcam and USB HID scanner)

Design document, **prior** to implementation. Corresponds to
`EVOLUTION.md` §3.2 (Group A, item A2). Covers both desktop input
variants:

- **Variant W — webcam**: frames from the PC camera, decoded in-process
  by ZXing.Net.
- **Variant H — USB HID scanner**: a handheld scanner that decodes the
  symbol in hardware and delivers the payload to Windows as keyboard
  input ("keyboard wedge").

Both variants feed the same parser and the same catalogue lookup.
Two user flows are defined: **(a)** add a new medicine by scanning its
package, and **(b)** restock an existing medicine by scanning its
package. Delivery is phased (§1.6): HID scanner first, webcam second,
flow (b) last and only on explicit product-owner request.
Follows the structure of `ANALYSIS-A1-REGIMENS.md`. Previously named
`ANALYSIS-A2-BARCODE-WEBCAM.md` (webcam only); §14 lists what changed.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification: `[VERIFIED]` (checked against the current
tree, or against a publicly traceable primary source), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (hypothesis pending
confirmation).

---

## 1. Scope

### 1.1 Problem

Adding a medicine today is a keyboard-only workflow: the user opens
`MedicineEditDialog`, types the commercial name or the active
ingredient, and — when the catalogue feature is enabled — picks a row
from the autocomplete dropdown. Picking a row fills name, ingredient,
package and unit, and caches the reference linkage (national code,
ATC, reference id) in private fields. `[VERIFIED]` —
`src/MedReminder.UI/Forms/MedicineEditDialog.cs`,
`OnReferenceSelected`.

The dialog has **no input field for the national code**. The code is
only ever set as a side effect of picking a catalogue row.
`[VERIFIED]` — same file; `_linkedNationalCode` is private state, not
a control.

For elderly users, and for anyone adding a medicine while holding
the physical package, typing and disambiguating the commercial name
is the largest single source of data-entry error observed by the
maintainer.

### 1.2 Barcodes on packages sold in Italy

The primary target is Italy, where the shipped AIFA catalogue is keyed
by AIC (Autorizzazione all'Immissione in Commercio, 9 digits).

- **Code 32 (Italian Pharmacode)** — 1D symbology, a Code 39
  derivative that encodes the 9-digit AIC as 6 base-32 characters
  (digits plus consonants; vowels excluded). This is the
  long-standing AIC barcode on Italian medicine packs. `[VERIFIED]` —
  AIFA, *The Italian drug traceability system*; Code 32 symbology
  references (BWIPP wiki, Seagull Scientific).
- **Bollino farmaceutico** — the state-issued security sticker. Carries
  the AIC and a pack serial, including as a DataMatrix ECC-200.
  `[VERIFIED]` — AIFA traceability document. Being phased out.
- **GS1 DataMatrix (EU FMD unique identifier)** — 2D symbology under
  Delegated Regulation (EU) 2016/161. Encodes product code (AI `01`),
  serial (AI `21`), batch (AI `10`), expiry (AI `17`). Italy had a
  derogation: the DataMatrix transition started on 9 February 2025,
  bollino and DataMatrix coexist during a stabilization period ending
  8 February 2027, and only EU-FMD serialization is allowed from
  9 February 2027. `[VERIFIED]` — Legislative Decree 10/2025, as
  reported by NMVO Italia and industry sources.
- **EAN-13** — generic retail GTIN. Present on some OTC and
  parapharmacy products. It is **not** an AIC carrier. `[INFERRED]`
  from the Code 32 facts above; see §14 for the correction.

Design consequences:

1. **Code 32 is the most reliable AIC source until at least 2027.** A2
   must decode it, in both variants.
2. The DataMatrix product code (AI `01`) is not guaranteed to embed
   the AIC in a documented, algorithmic way. `[UNCERTAIN]` — no
   verified public mapping rule for all AIC ranges. The parser does
   not derive the AIC from it (§2.3).

### 1.3 Goal

Add a "Scan barcode" button to `MedicineEditDialog` that opens a scan
dialog. The dialog accepts input from a USB HID scanner (variant H) or,
on request, from the webcam (variant W). Whatever the source, the
payload goes through one parser, which extracts the national code
(AIC in Italy) or a GTIN. The dialog then looks the code up in the
local catalogue and populates the fields exactly as a manual
autocomplete pick does.

Preserve the keyboard-only path as the primary input method: scanning
is an *accelerator*, never a precondition.

This is flow (a). Flow (b), restock by scan, is specified in §5C and
belongs to a later phase (§1.6).

### 1.4 Variant comparison

| Aspect                      | W — webcam                                | H — USB HID scanner (keyboard wedge)              |
|-----------------------------|-------------------------------------------|---------------------------------------------------|
| Hardware                    | Built-in or USB webcam                    | Handheld scanner (2D imager for DataMatrix)       |
| Decoding                    | In-process, ZXing.Net                     | In the scanner firmware                           |
| New Infrastructure code     | `WindowsCameraCaptureService`             | None                                              |
| New NuGet dependency        | `ZXing.Net` (+ binding, §6.1)             | None                                              |
| Windows privacy surface     | Camera permission, camera indicator       | None (the scanner is a keyboard)                  |
| Device detection            | Yes (`ProbeAsync`)                        | Not possible (indistinguishable from a keyboard)  |
| Decode reliability          | Depends on webcam optics (§10.2)          | High, but depends on scanner configuration (§5B)  |
| Symbology reported          | Yes (ZXing `BarcodeFormat`)               | No; inferred from the payload shape               |
| GS1 `<GS>` separator        | Preserved in `Result.Text`                | Often lost or remapped (§5B.3)                    |
| Keyboard layout sensitivity | None                                      | Yes (§5B.4)                                       |
| Effort                      | ~6–7 days on top of the shared core       | ~1.5 days on top of the shared core               |

`[INFERRED]` for the effort split; §10.1 has the full table.

**Default input mode: H (scanner).** The dialog opens ready for
scanner input and does not touch the camera. The webcam starts only
when the user clicks "Use webcam". Rationale: no camera handle, no
privacy prompt, no indicator LED unless the user asks for it;
`EVOLUTION.md` §3.2 already names handheld scanners as the safer
desktop default. See §12.6.

### 1.6 Flows and delivery phases

**DECIDED 2026-09-26** by the product owner.

| Phase | Content                                                                 | Flow | Precondition                               |
|-------|-------------------------------------------------------------------------|------|--------------------------------------------|
| 1     | Shared core (parser, `BarcodeScanDialog`, catalogue lookup) + variant H  | (a)  | None                                       |
| 2     | Variant W (webcam panel, `ICameraCaptureService`, ZXing.Net)             | (a)  | Phase 1 merged                             |
| 3     | Flow (b) — restock by scan (§5C)                                         | (b)  | Flow (a) complete (phases 1 and 2 merged) **and** explicit product-owner request |

Rationale for H before W:

- The shared core is about half of the total effort. Validating it
  with decoded, deterministic input (variant H) isolates parser and
  lookup defects from camera-optics effects.
- Phase 1 is useful without a scanner: the scanner input box also
  accepts an AIC typed by hand from the package (§4.2), a path that
  does not exist today.
- All open technical risks sit in variant W (§5A.2 TFM, §6.1 binding
  package, 1D decode quality on fixed-focus webcams, §10.2). Keeping
  them out of phase 1 means they cannot block it.

Phase 3 is **not** scheduled. It starts only when the product owner
asks for it after flow (a) is complete. Until then no code for flow
(b) is written; §5C records the design so that it is not re-derived.

Each phase is a separate PR.

### 1.7 What A2 is NOT

- **Not authenticity verification.** MedReminder does not query the
  EU Hub / NMVS / Italian NMVO to confirm the FMD serial is
  unique-and-active. That interaction requires OBP (On-Boarding
  Partner) credentials and is a pharmacy-side workflow, out of
  product scope per `CLAUDE.md` §1.
- **Not batch/expiry extraction into stock.** The parser MAY expose
  AI `10` (batch) and AI `17` (expiry) if trivially available, but
  no phase stores them: `StockMovement` has no expiry or batch field
  (§5C.2). Adding them is a separate feature with a schema patch
  (§12.2), not part of A2 — not even of flow (b).
- **Not a HID POS or serial-port driver integration.** Variant H uses
  the scanner in its keyboard-wedge mode only. The Windows
  `PointOfService` API, Raw Input device filtering and virtual COM
  port mode are evaluated and rejected for this slice in §5B.6.
- **Not a mobile-camera pipeline.** The mobile companion
  (`EVOLUTION.md` §7) is a separate track and does not share this
  document's implementation.
- **Not a medical-device feature.** No dose safety checks, no
  interaction warnings, no adherence claims. `CLAUDE.md` §1 and
  `EVOLUTION.md` §9.2 remain binding.

---

## 2. Barcode payload semantics

### 2.1 GS1 DataMatrix — layout

A GS1 DataMatrix payload begins with the FNC1 codeword. Decoders
surface it either as the AIM symbology identifier `]d2` before the
payload or not at all; FNC1 used as a separator between elements is
surfaced as ASCII `0x1D` (GS). `[VERIFIED]` — GS1 General
Specifications.

Typical FMD payload (`<GS>` = 0x1D after each variable-length element
that is not last):

```
01<14 digits GTIN>21<serial><GS>17<YYMMDD>10<batch>
```

Element extraction rules used by the parser:

| AI  | Meaning                | Length         |
|-----|------------------------|----------------|
| 01  | GTIN / product code    | 14 (fixed)     |
| 17  | Expiry (YYMMDD)        | 6  (fixed)     |
| 10  | Batch / lot            | up to 20 (var) |
| 21  | Serial number          | up to 20 (var) |

Fixed-length AIs (01, 17) are parsed by offset. Variable-length AIs
(10, 21) run until the next `<GS>` or end of payload.

**Separator-free payloads.** Variant H frequently delivers the payload
without `<GS>` (§5B.3). When no separator is present and a
variable-length AI is not the last element, its end cannot be
determined unambiguously. Rule: the parser still extracts AI `01`
when it is the first element (fixed length, offset 0), and sets
`Batch`, `Serial` and `Expiry` to `null` rather than guessing. A2
needs only the product code, so this loses nothing in scope.

### 2.2 Code 32 — layout

- Symbology: Code 39 character set restricted to `0-9` and the 22
  consonants `B C D F G H J K L M N P Q R S T U V W X Y Z`, 6
  characters. `[VERIFIED]` — Code 32 references in §1.2.
- Value: the 6 characters are a base-32 number; converted to decimal
  and left-padded, they give the 9-digit AIC, whose last digit is a
  check digit. The human-readable text printed under the bar is `A`
  followed by the 9 digits. `[VERIFIED]` for the base-32 encoding;
  `[UNCERTAIN]` for the exact check-digit algorithm, to be verified
  against the BWIPP "Italian Pharmacode" specification during
  implementation and covered by tests (§9.1).
- A webcam decoder reports it as plain Code 39 with the raw 6-char
  text. ZXing.Net has no Code 32 converter. `[INFERRED]` from the
  open ZXing Code 32 feature request (zxing-js issue #308);
  `[UNCERTAIN]` for the .NET port, to be checked against the package
  version selected in §6.1.
- A HID scanner, depending on its configuration, emits either the raw
  6 characters or the converted `A` + 9 digits (many models also
  allow 9 digits without the `A`). `[INFERRED]` from vendor
  configuration guides; model-dependent.

The parser accepts all three forms (§3.3).

### 2.3 GTIN → AIC mapping (Italy)

**The parser does not derive the AIC from a GTIN.** No verified public
algorithm covers every AIC range (§1.2). A payload that yields only a
GTIN is looked up as-is (§3.6); for the Italian catalogue this will
normally return nothing, and the dialog tells the user so. When a
verified mapping becomes available, it belongs in the catalogue query
layer, not in the parser.

### 2.4 EAN-13 — layout

12 payload digits + 1 GS1 mod-10 check digit. The parser validates the
check digit and exposes the value as a GTIN (left-padded to 14 digits).
It does not extract a national code from it.

---

## 3. New abstractions (shared by both variants)

### 3.1 `RawBarcode` — `MedReminder.Domain/Catalogue/RawBarcode.cs`

Pure value object. Immutable. No dependencies.

```csharp
public readonly record struct RawBarcode(BarcodeSymbology Symbology, string Payload);

public enum BarcodeSymbology : int
{
    Unknown    = 0,   // variant H: the scanner does not report it
    Ean13      = 1,
    DataMatrix = 2,
    Code39     = 3,   // carries Code 32 (Italian Pharmacode)
}
```

Constructed by the capture layer (variant W) or by the scan dialog
from keyboard input (variant H); consumed by the parser. A
`RawBarcode` handed to the parser always has a non-empty `Payload`.

### 3.2 `BarcodeContent` — `MedReminder.Domain/Catalogue/BarcodeContent.cs`

The structured result of parsing a `RawBarcode`. Extraction is
best-effort: unknown or malformed payloads land as
`BarcodeContent.Unrecognized`.

```csharp
public sealed record BarcodeContent(
    string? Gtin,           // 14 digits or null
    string? NationalCode,   // AIC (9 digits) in Italy, otherwise null
    string? Batch,          // AI 10 if unambiguous, else null
    DateOnly? Expiry,       // AI 17 if unambiguous, else null
    string? Serial)         // AI 21 if unambiguous, else null (never used in A2)
{
    public static readonly BarcodeContent Unrecognized =
        new(null, null, null, null, null);

    public bool HasLookupKey => NationalCode is not null || Gtin is not null;
}
```

### 3.3 `IBarcodeParser` — `MedReminder.Application/Catalogue/IBarcodeParser.cs`

Pure interface, no Windows types.

```csharp
public interface IBarcodeParser
{
    BarcodeContent Parse(RawBarcode raw);
}
```

The implementation lives in `MedReminder.Application` (pure C#, the
project targets `net10.0`). Before any rule runs, the payload is
normalized: trim leading/trailing whitespace and CR/LF (scanner
suffixes), map the configured `<GS>` substitute (§5B.3) back to
`0x1D`. Rules:

1. **DataMatrix.** If `Symbology == DataMatrix`, or `Symbology ==
   Unknown` and the payload starts with `]d2` or with `01` followed by
   14 digits: strip the AIM prefix, then walk the AI table from §2.1.
   Unknown AIs stop the walk conservatively. Apply the separator-free
   rule of §2.1.
2. **Code 32.** If `Symbology == Code39`, or `Symbology == Unknown`
   and the payload matches one of:
   - exactly 6 characters from the Code 32 alphabet → base-32 decode;
   - `A` + 9 digits → strip the `A`;
   - exactly 9 digits (Unknown only) → take as-is;

   then validate the check digit and expose the 9 digits as
   `NationalCode`. Failed check digit → `Unrecognized`.
3. **EAN-13.** If `Symbology == Ean13`, or `Symbology == Unknown` and
   the payload is 13 digits: validate GS1 mod-10, expose as `Gtin`
   (left-padded to 14). No `NationalCode`.
4. Rules are tried in the order 1 → 2 → 3 for `Unknown`; the first
   match wins. The shapes do not overlap (length and alphabet
   differ). `[INFERRED]`
5. Never throw on malformed input. Return `Unrecognized`. Log the
   symbology, the length and the matched rule, never the payload
   (FMD serial numbers are pseudonymous identifiers of a physical
   box — data minimization).

Unit-tested in isolation (§9.1).

### 3.4 Catalogue lookup seam

`LinkMedicineToReferenceUseCase` is **not** reused: it requires an
existing `medicineId` and persists immediately, while the dialog in
Create mode has no medicine yet and persists only on OK. `[VERIFIED]`
— `src/MedReminder.Application/Catalogue/LinkMedicineToReferenceUseCase.cs`.

Instead the dialog reuses what it already receives:
`CatalogueAutocompleteContext.LookupByNationalCode`, backed by
`IReferenceCatalogueQueryService.GetByNationalCodeAsync`. A hit is fed
into the existing `OnReferenceSelected` code path (extracted into a
private `ApplyReference(ReferenceMedicine)` method), so a scan and a
manual autocomplete pick produce identical dialog state. `[VERIFIED
against]` `src/MedReminder.UI/Forms/MedicineEditDialog.cs`.

Lookup key order: `NationalCode` first; if null, `Gtin`. Country: the
profile's catalogue country (§12.4).

---

## 4. UI integration (shared)

### 4.1 Placement

In `MedicineEditDialog`, next to the "Commercial name" autocomplete
row, add a small button labelled by resource key
`MedicineEdit.ScanBarcode`. Visible only when the catalogue context is
non-null (same condition as autocomplete). `[VERIFIED against]`
`CatalogueAutocompleteContext` in `MedicineEditDialog.cs`.

Phases 1–2 (flow a): not present in the `MainForm` toolbar. Phase 3
adds a separate `MainForm` entry point for flow (b) (§5C.3, §12.1).

### 4.2 `BarcodeScanDialog`

New form `BarcodeScanDialog : MedReminderFormBase`, modal, resizable,
minimum size 480×360. It returns a `BarcodeContent` with
`HasLookupKey == true`, or `DialogResult.Cancel`.

Layout:

- Header: `MedicineEdit.ScanBarcode.Prompt`.
- **Scanner panel (variant H, default)**: an instruction label
  (`MedicineEdit.ScanBarcode.ScannerHint`) and a single-line input
  box that has focus when the dialog opens. The box also accepts
  manual typing of an AIC — a keyboard-only user gets a direct "enter
  code" path for free. Details in §5B.
- **Webcam panel (variant W)**: hidden until the user clicks
  `MedicineEdit.ScanBarcode.UseWebcam`. Contains the preview
  `PictureBox`, the framing overlay and the status label described in
  §5A. A "Use scanner" button switches back and releases the camera.
- Footer buttons: "Cancel" only. **`AcceptButton` is not set**
  (§5B.2).

On a successful parse from either source:

1. Play `SystemSounds.Asterisk`. No custom asset.
2. Close with `DialogResult.OK`.
3. `MedicineEditDialog` looks the code up (§3.4).
4. Hit → `ApplyReference(row)`; the dialog now looks exactly as after
   a manual autocomplete pick.
5. Miss → a message (`MedicineEdit.ScanBarcode.NotInCatalogue`)
   showing the code read, in a selectable text box so the user can
   copy it, and stating that the details must be entered manually.
   No field is modified and no linkage is cached. There is no
   national-code field to pre-fill (§1.1).

A payload that parses to `Unrecognized` does **not** close the
dialog: the scanner box is cleared and the status line shows
`MedicineEdit.ScanBarcode.Unrecognized`; the webcam keeps scanning.

Webcam timeout: 30 s without a decoded frame stops the camera and
shows "No barcode detected" (`Capture:ScanTimeoutSeconds`). The
scanner panel has no timeout: it holds no resource.

### 4.3 Error surfaces (webcam only)

Variant H has no error states of its own: a keyboard-wedge scanner
cannot be probed (§5B.1). The webcam panel handles the terminal states
from `CameraAvailability`:

| Availability          | UI behavior                                                                                          |
|-----------------------|------------------------------------------------------------------------------------------------------|
| `Available`           | Preview starts. See §5A.                                                                             |
| `NoDeviceFound`       | Panel shows `MedicineEdit.ScanBarcode.NoDevice` and a "Use scanner" button.                          |
| `PermissionDenied`    | Panel shows `MedicineEdit.ScanBarcode.PermissionDenied` with an "Open Privacy Settings" button that shells `ms-settings:privacy-webcam`. |
| `InitializationFailed`| Panel shows `MedicineEdit.ScanBarcode.InitializationFailed` with a "Copy log location" button that copies `%LOCALAPPDATA%\MedReminder\logs\` to the clipboard. |

In every state the scanner panel stays usable. No stack traces. All
four states are logged (`Warning` for the first three, `Error` for the
fourth) via Serilog, never with PII or medical detail (`CLAUDE.md`
§7).

---

## 5A. Variant W — camera capture layer

### 5A.1 Port — `MedReminder.Application/Abstractions/ICameraCaptureService.cs`

Hides `MediaCapture`, `SoftwareBitmap` and ZXing types from the UI.

```csharp
public interface ICameraCaptureService
{
    Task<CameraAvailability> ProbeAsync(CancellationToken ct);

    Task<RawBarcode?> ScanAsync(
        IProgress<CameraFrameStatus>? progress,
        CancellationToken ct);
}

public enum CameraAvailability : int
{
    Available            = 0,
    NoDeviceFound        = 1,
    PermissionDenied     = 2,   // Windows privacy panel disallows it
    InitializationFailed = 3,   // driver / hardware fault
}

public sealed record CameraFrameStatus(
    int FramesProcessed,
    bool DecoderActive);
```

Change from the first draft: `ScanAsync` returns the `RawBarcode`,
not a `BarcodeContent`. Parsing moves to the dialog, so both variants
go through the same `IBarcodeParser` call and the service stays a
pure acquisition adapter. The dialog calls `ScanAsync` in a loop until
a result parses to `HasLookupKey`, the timeout fires, or the user
cancels. Cancellation returns `null`.

### 5A.2 API surface choice

`Windows.Media.Capture.MediaCapture` (WinRT) over DirectShow.
MediaCapture is the API Microsoft actively supports, respects the
Windows privacy panel (§7), and its `MediaFrameReader` avoids the
XAML-in-WinForms interop problem. `[VERIFIED]` — reachable from
`net10.0-windows10.0.19041.0`, which the UI project already targets
(`src/MedReminder.UI/MedReminder.UI.csproj`).

`[UNCERTAIN]` — the Infrastructure project targets `net10.0-windows`
without a Windows SDK version, so the WinRT projections are not
available there today. Two options, to settle at implementation time:
raise the Infrastructure TFM to `net10.0-windows10.0.19041.0`, or
place `WindowsCameraCaptureService` in the UI project behind the same
port. The port is unaffected either way.

### 5A.3 Frame pipeline

1. Enumerate video devices via `MediaFrameSourceGroup.FindAllAsync`;
   pick the first source of kind `Color`. Log group names only, never
   device ids.
2. Initialize `MediaCapture` with `SharingMode = ExclusiveControl`,
   `MemoryPreference = Cpu`, `StreamingCaptureMode = Video`.
3. Create a `MediaFrameReader` bound to the picked source.
4. On `FrameArrived`, convert the `SoftwareBitmap` to a `Bitmap` via
   `CopyToBuffer` (no XAML `SoftwareBitmapSource`).
5. Feed every third frame to ZXing.Net (§6).
6. First decoded result with a mapped symbology (§6.3) completes
   `ScanAsync`.
7. Report a `CameraFrameStatus` via `IProgress<T>` on the UI thread.

### 5A.4 Disposal

The service is a singleton holding a `SemaphoreSlim` that serializes
scan requests; each `ScanAsync` owns a scoped session. On completion,
cancellation, or a switch back to scanner mode:

1. `MediaFrameReader.StopAsync` awaited.
2. `MediaCapture.Dispose()`.
3. Semaphore released.

The camera indicator must go off within one second of the webcam
panel closing — mandatory user-trust property, verified manually
(§9.5).

---

## 5B. Variant H — USB HID scanner (keyboard wedge)

### 5B.1 Operating model

Most USB handheld scanners enumerate as a standard HID keyboard by
default. They decode the symbol in firmware and "type" the payload as
a burst of keystrokes, normally followed by a configurable suffix
(Enter by default on most models). `[INFERRED]` — common factory
default across vendors; model-dependent.

Consequences:

- The app cannot detect whether a scanner is attached: it is
  indistinguishable from a keyboard. No `ProbeAsync`, no device list.
- Keystrokes go to whichever control has focus. The only reliable
  capture point is a control the app has deliberately focused for
  that purpose.
- No Windows permission applies; nothing to log at device level.
- **Hardware requirement.** Reading GS1 DataMatrix needs a 2D imager.
  A 1D (laser/linear CCD) scanner reads Code 32 and EAN-13 only. For
  Italy until February 2027, a 1D scanner with Code 32 enabled is
  sufficient (§1.2). The user guide states this.

### 5B.2 Capture in `BarcodeScanDialog`

The scanner input box (§4.2) is a `TextBox` subclass,
`ScannerInputBox`, in `MedReminder.UI/Controls/`:

- `AcceptsReturn = false`, `AcceptsTab = false`, `MaxLength = 256`.
- Overrides `ProcessCmdKey` / `OnKeyPress` to buffer characters
  itself, **including control characters** that a plain `TextBox`
  drops. In particular `0x1D` (GS), which a wedge scanner may emit as
  the Ctrl+] chord. `[UNCERTAIN]` — whether WinForms delivers `0x1D`
  through `KeyPress` for that chord on every keyboard layout; verified
  during implementation, and the substitute mechanism of §5B.3 covers
  the failure case.
- **Terminator**: Enter or Tab ends the payload and raises
  `PayloadCompleted(string)`. The dialog wraps it in
  `RawBarcode(BarcodeSymbology.Unknown, payload)` and parses.
- **Idle fallback**: if no terminator arrives, a payload is also
  completed after `Capture:HidIdleCompleteMilliseconds` (default 300)
  without keystrokes, provided it matches one of the parser shapes.
  This covers scanners configured with no suffix. Manual typing is
  slower than this interval between characters only in the final
  pause, and a partial manual entry does not match a shape, so it is
  not submitted prematurely. `[INFERRED]`
- `BarcodeScanDialog` sets **no `AcceptButton`**, so the scanner's
  Enter suffix cannot trigger a default button.

No inter-keystroke timing is used to tell scanner input from human
typing: inside a dedicated box both are legitimate, and the parser
decides.

### 5B.3 GS1 group separator

The `<GS>` (0x1D) between variable-length elements has no printable
key. Depending on the scanner, a wedge delivers it as Ctrl+], drops
it, or replaces it with a configured character. `[INFERRED]` — vendor
configuration guides; model-dependent.

Handling:

1. `0x1D` received through `ScannerInputBox` is kept as-is.
2. `Capture:HidGroupSeparatorSubstitute` (default empty = disabled)
   names one printable character that the parser maps back to `0x1D`
   before parsing (§3.3). A user who configures the scanner to emit,
   e.g., `~` for GS sets the same character here. Not exposed in the
   Settings UI in this slice (§12.7).
3. If neither applies, the separator-free rule of §2.1 still yields
   the GTIN, which is all A2 needs.

### 5B.4 Keyboard layout mismatch

A wedge scanner sends key scan codes for the keyboard layout it is
configured for (commonly US), and Windows translates them with the
host's active layout. `[INFERRED]` — how HID keyboards work.

- Digits and uppercase consonants map identically on US, IT, ES and
  UK layouts. Code 32, `A` + 9 digits and all-digit payloads are
  unaffected there. `[INFERRED]`
- **French AZERTY**: the digit row produces `&é"'(-è_çà` unshifted;
  a US-configured scanner delivers symbols instead of digits.
  **German QWERTZ**: `Y` and `Z` are swapped, which affects Code 32
  payloads that contain those letters. `[INFERRED]`

Mitigation, in order:

1. User guide: configure the scanner for the host layout (all
   mainstream scanners support country keyboard selection), or use
   its "emulate numeric keypad" / Alt-code mode if available.
   `[INFERRED]`
2. The parser rejects the garbled payload as `Unrecognized`
   (checksums fail), so a mismatch never links a wrong medicine.
3. No automatic layout remapping in this slice (§12.8).

### 5B.5 Scanning outside the scan dialog

If the user scans while `MedicineEditDialog` has focus, the payload
lands in the focused control (typically the commercial-name box) and
the Enter suffix triggers the dialog's `AcceptButton` (OK). `[VERIFIED
against]` `MedicineEditDialog.cs`, `AcceptButton = okButton`. OK runs
the normal validation, so no half-filled record is saved silently,
but the result is confusing.

Proposed handling in this slice: none beyond documentation. The user
guide says "click Scan barcode first, then scan". Intercepting
scanner bursts globally is out of scope (§5B.6, §12.9).

### 5B.5b Relation to the first draft

The first draft stated that HID scanners "already work with the
current UI (they type into the focused textbox); no code is
required". That is wrong: the dialog has no national-code field
(§1.1), so a scanned AIC typed into the name box triggers a
name-prefix search that matches nothing, and Enter submits the form.
Variant H does require code — the small amount described above.

### 5B.6 Alternatives evaluated and rejected for this slice

| Alternative | Why rejected |
|---|---|
| **HID POS mode** via `Windows.Devices.PointOfService.BarcodeScanner` | Gives the real symbology and the raw payload with `<GS>`, and is layout-independent. But the scanner must be switched to HID POS mode, and the API documentation requires the `pointOfService` device capability in an app package manifest. MedReminder ships as an unpackaged ZIP (`docs/PACKAGING.md`). `[VERIFIED]` capability requirement — Microsoft Learn; `[UNCERTAIN]` whether it works unpackaged at all. Revisit if MSIX becomes the primary channel. |
| **Raw Input** (`RegisterRawInputDevices`, `WM_INPUT`) to identify the scanner device and capture globally | Would solve §5B.5 and allow scanning without opening the dialog. Needs P/Invoke, per-device selection UI, and scan-code-to-character translation. Disproportionate for the benefit. |
| **Virtual COM port** (USB CDC) mode | Lossless payload, but needs `System.IO.Ports`, port selection UI and a non-default scanner mode. Low value over wedge for this audience. |
| **Global keyboard hook** | Intrusive, easily flagged by security software, and captures keystrokes outside the app. Not acceptable. |

---

## 5C. Flow (b) — restock by scan (Phase 3, deferred)

Design record only. Implemented after flow (a) is complete and only on
explicit product-owner request (§1.6).

### 5C.1 Goal

The user holds a new package of a medicine already in the profile,
scans it, and lands in the restock dialog with the medicine identified
and the fields pre-filled as far as the data allows.

### 5C.2 What the scan can and cannot fill

`StockAdjustmentDialog` has three inputs: operation kind, quantity,
notes. `StockMovement` has no expiry or batch field. `[VERIFIED]` —
`src/MedReminder.UI/Forms/StockAdjustmentDialog.cs`,
`src/MedReminder.Domain/Stock/StockMovement.cs`.

| Field          | Source                                                                 |
|----------------|------------------------------------------------------------------------|
| Medicine       | Scan → `NationalCode` → medicine in the active profile with the same `Medicine.NationalCode` |
| Operation kind | Fixed to `NewPackage`                                                  |
| Quantity       | Quantity of the most recent `NewPackage` movement of that medicine, editable; empty if none |
| Notes          | Left empty                                                             |
| Expiry / batch | Not stored anywhere today → not filled (§12.2)                          |

Why the quantity comes from history and not from the catalogue:
`ReferenceMedicine` has no numeric units-per-package field; the pack
size is only inside the free-text `Dosage` description (AIFA
DESCRIZIONE). Extracting it would be a fragile text heuristic.
`[VERIFIED]` — `src/MedReminder.Domain/Catalogue/ReferenceMedicine.cs`.
The AIC identifies a specific pack, so the last `NewPackage` quantity
for the same medicine is normally the right value. `[INFERRED]`

No schema change. The history query uses the existing
`IStockMovementRepository.ListForMedicineAsync`, or a dedicated
`GetLastNewPackageQuantityAsync` if the list proves too large.
`[VERIFIED]` — `src/MedReminder.Application/Abstractions/IStockMovementRepository.cs`.

### 5C.3 Entry point

A `MainForm` action "Restock from barcode", independent of the row
selection. Today restocking starts from a selected row
(`MainForm.ShowStockDialogAsync`); scanning after the user has already
picked the row would only confirm what they know. The value of the
scan is identifying the medicine. `[VERIFIED against]`
`src/MedReminder.UI/Forms/MainForm.cs`.

Flow:

1. The action opens `BarcodeScanDialog` (same dialog, same input
   modes as flow a).
2. The parsed `NationalCode` is matched against the medicines of the
   active profile.
3. Outcomes:
   - **One match** → `StockAdjustmentDialog` opens for that medicine,
     kind `NewPackage`, quantity pre-filled per §5C.2. The user
     confirms with OK; persistence goes through the existing
     `AddStock` use case.
   - **Several matches** (same product used in more than one therapy)
     → the user picks one from a short list, then as above.
   - **No match** → message offering (i) to link an existing,
     unlinked medicine to this code, or (ii) to create a new medicine,
     which opens flow (a) pre-filled from the scan.
   - **Only a GTIN, no national code** → treated as no match; the
     message explains that the package code is not in the catalogue.
4. Suspended or ended medicines are included in the match (restocking
   them is legitimate); archived ones are not. `[UNCERTAIN]` — to be
   aligned with the medicine lifecycle states present at phase 3 time.

### 5C.4 New types

- Application: `FindMedicinesByNationalCodeUseCase` (or a repository
  method `ListByNationalCodeAsync` on `IMedicineRepository`, which today
  has no such lookup `[VERIFIED]`), and the last-quantity query.
- UI: a `StockAdjustmentDialog` constructor overload taking an initial
  quantity; the "Restock from barcode" action; the disambiguation
  list.
- No change to `BarcodeScanDialog`, the parser or the capture layer.

### 5C.5 Tests

- Application: match by national code (none / one / several),
  last-`NewPackage` quantity (none, one, several movements, ignores
  other kinds).
- Manual: one, several, and no matching medicine; unlinked medicine
  linked from the "no match" path; quantity edited before OK.

### 5C.6 Effort

`[INFERRED]`: ~4–5 developer-days, including localization and user
guide updates in five languages.

---

## 6. Barcode decoding — `ZXing.Net` integration (variant W)

### 6.1 Package

- NuGet: `ZXing.Net` (Apache 2.0). `[VERIFIED]` license.
- Companion binding for `Bitmap` → `LuminanceSource` under
  `net10.0-windows`. `[UNCERTAIN]` on the exact package; validated
  against current NuGet metadata at implementation time.

No commercial libraries. IronBarcode / Dynamsoft ruled out on
licensing grounds — the project is Apache 2.0. `[VERIFIED against
LICENSE]`.

### 6.2 Configuration

```csharp
var reader = new BarcodeReader
{
    AutoRotate = true,
    Options = new DecodingOptions
    {
        TryHarder      = true,
        TryInverted    = false,
        PossibleFormats = new[]
        {
            BarcodeFormat.CODE_39,
            BarcodeFormat.EAN_13,
            BarcodeFormat.DATA_MATRIX,
        },
    },
};
```

`CODE_39` is new versus the first draft: it is how Code 32 is decoded
(§2.2). Restricting `PossibleFormats` cuts decode time and avoids
false positives (e.g. a marketing QR code on the packaging).

### 6.3 Symbology mapping

- `BarcodeFormat.DATA_MATRIX` → `BarcodeSymbology.DataMatrix`.
- `BarcodeFormat.CODE_39`     → `BarcodeSymbology.Code39`.
- `BarcodeFormat.EAN_13`      → `BarcodeSymbology.Ean13`.
- anything else               → discard; keep scanning.

`Result.Text` is the raw payload. For DataMatrix, ZXing does not
inject the `]d2` prefix by default; the parser handles both cases.

---

## 7. Windows privacy panel (variant W only)

### 7.1 Behavior

Since Windows 10 1903 the "Camera access" toggle in **Settings →
Privacy & security → Camera** also governs classic desktop apps ("Let
desktop apps access your camera"). `MediaCapture` initialization on a
denied device throws `UnauthorizedAccessException`. `[VERIFIED]` — MS
documentation, WinRT contract.

### 7.2 UX

No runtime OS prompt for unpackaged desktop apps. The webcam panel
renders `PermissionDenied` (§4.3) with a button that launches
`ms-settings:privacy-webcam`. The scanner panel stays usable
meanwhile.

### 7.3 No MSIX-specific work

The primary channel is the self-contained ZIP (`docs/PACKAGING.md`).
The `webcam` (and, for HID POS, `pointOfService`) manifest
capabilities are future work if MSIX becomes primary.

### 7.4 Camera-in-use indicator

Windows shows a camera indicator while a process holds a camera
handle. The app must not attempt to suppress it; §5A.4 releases the
handle promptly. Variant H never triggers it.

---

## 8. Data model, persistence, schema

### 8.1 Domain

Two new value objects (`RawBarcode`, `BarcodeContent`) and one enum
(`BarcodeSymbology`) in `MedReminder.Domain/Catalogue/`. No mutable
state, no schedule interaction.

### 8.2 Application

`IBarcodeParser` + implementation, `ICameraCaptureService` port. No
new use case (§3.4).

### 8.3 Infrastructure / UI

- Variant W: `WindowsCameraCaptureService` (location per §5A.2).
- Variant H: `ScannerInputBox` control in the UI project. No
  Infrastructure code.
- Shared: `BarcodeScanDialog`, `ApplyReference` extraction in
  `MedicineEditDialog`.

No EF model changes, no migration.

### 8.4 Persistence patches

**None.** No columns added, `DatabaseInitializer` untouched. The
`CLAUDE.md` §7 rule on `EnsureCreated()` is not triggered.

### 8.5 Configuration

`appsettings.json` gains:

```jsonc
"Capture": {
  "ScanTimeoutSeconds": 30,             // §4.2, webcam only
  "MaxDecodeFps": 10,                   // §5A.3 throttle
  "PreviewMaxWidthPixels": 640,         // §5A.3 initialization hint
  "HidIdleCompleteMilliseconds": 300,   // §5B.2
  "HidGroupSeparatorSubstitute": ""     // §5B.3, empty = disabled
}
```

Loaded via the existing `Microsoft.Extensions.Configuration.Json`
pipeline.

If §12.6 is confirmed with "remember last mode", `UserSettings` gains
`BarcodeInputMode` (`"Scanner"` | `"Webcam"`, default `"Scanner"`) in
`user.settings.json`, which export/import already covers
(`ExportOptions`). `[VERIFIED against]`
`src/MedReminder.Application/Abstractions/UserSettings.cs`.

### 8.6 Localization

New keys, in all five `assets/localization/strings.<lang>.json`
(`CLAUDE.md` §6): `MedicineEdit.ScanBarcode`,
`.Prompt`, `.ScannerHint`, `.UseWebcam`, `.UseScanner`,
`.Unrecognized`, `.NotInCatalogue`, `.NoBarcodeDetected`,
`.NoDevice`, `.PermissionDenied`, `.InitializationFailed`,
`.OpenPrivacySettings`, `.CopyLogLocation`. Final key names follow
the dictionaries' existing `Ui.<Form>.<Key>` convention.

---

## 9. Tests

### 9.1 Unit — parser (`MedReminder.Application.Tests`)

Cross-platform (`net10.0`), runs on Linux CI.

- **GS1 DataMatrix**:
  - Payload with `<GS>` separators, with and without `]d2`.
  - Separator-free payload: GTIN extracted, `Batch`/`Serial`/`Expiry`
    null.
  - Substitute character mapped back to `<GS>` (§5B.3).
  - Malformed (missing AI 01, truncated, non-digit in AI 01) →
    `Unrecognized`.
- **Code 32**:
  - Raw 6-char base-32 form → correct AIC, for a set of real AICs
    with published human-readable codes.
  - `A` + 9 digits and bare 9 digits → same AIC.
  - Vowel in the 6-char form, wrong check digit → `Unrecognized`.
- **EAN-13**: valid → `Gtin` padded to 14, `NationalCode` null;
  wrong check digit, wrong length → `Unrecognized`.
- **Wedge artifacts**: trailing CR/LF/Tab stripped; AZERTY-garbled
  digit row (`&é"'(-è_çà`) → `Unrecognized` (§5B.4).
- **Symbology hint absent**: every positive case above also passes
  with `BarcodeSymbology.Unknown`.
- **Robustness**: the fuzz corpus (§9.3) never throws.

### 9.2 Unit — wedge buffering (variant H)

The buffering logic of `ScannerInputBox` is kept in a small pure class
(`WedgeBuffer`: append char, terminator, idle tick → completed
payload) in `MedReminder.Application`, unit-tested cross-platform:
Enter / Tab terminators, idle completion only on a parser-shape match,
`0x1D` retained, `MaxLength` enforced. The WinForms wiring
(`ProcessCmdKey`, focus, no `AcceptButton`) goes in
`MedReminder.UI.Tests` (Windows-only, `[VERIFIED]` — the project
exists and targets `net10.0-windows10.0.19041.0`) where it can be
driven without real key events, otherwise in the manual checklist
(§9.5).

### 9.3 Fuzz corpus

256 random payloads (lengths 0..64, including control characters) in
`tests/fixtures/barcode-fuzz/`, replayed in an xUnit theory. Objective:
zero exceptions. Regenerated by a one-off script, not at CI time.

### 9.4 Unit — Infrastructure (variant W, Windows-only)

- `ProbeAsync` on a machine with no camera → `NoDeviceFound`.
- After `ScanAsync` cancellation no `MediaCapture` instance is
  retained (`WeakReference` + forced GC).

### 9.5 Manual acceptance checklist

Kept in `docs/` as a maintainer checklist, not in the end-user guide.

Variant H (1D scanner and 2D imager, if available):

1. Three real Italian packs, Code 32 → autofill, for each scanner
   output form (raw 6 chars, `A` + 9 digits).
2. A pack with FMD DataMatrix (2D imager) → GTIN read; expect the
   "not in catalogue" message unless the catalogue resolves it.
3. Scanner with no suffix → idle completion works.
4. Scanner configured US, Windows layout French → `Unrecognized`, no
   wrong link.
5. Scan while `MedicineEditDialog` has focus → behavior as documented
   in §5B.5, no silent save.
6. Manual typing of an AIC in the scanner box → autofill.

Variant W:

7. Same three packs via webcam → autofill.
8. Cover the camera → timeout after 30 s with the right message.
9. Disable desktop camera access → `PermissionDenied`, deep link works,
   scanner panel still usable.
10. Unplug the webcam mid-scan → graceful stop; `InitializationFailed`
    or `NoDeviceFound` on the next attempt.
11. Camera indicator off within 1 s of switching back to scanner mode
    or closing the dialog.

---

## 10. Effort, risks, non-obvious costs

### 10.1 Effort estimate

`[INFERRED]`, single developer familiar with the codebase:

| Layer                                                           | Days  |
|-----------------------------------------------------------------|-------|
| Shared: `RawBarcode`, `BarcodeContent`, parser (incl. Code 32) + tests | 2.5 |
| Shared: `BarcodeScanDialog` shell, `ApplyReference`, lookup wiring | 1.5 |
| Variant H: `ScannerInputBox` / `WedgeBuffer` + tests            | 1.5   |
| Variant W: `ICameraCaptureService` + WinRT/ZXing adapter        | 3–4   |
| Variant W: webcam panel, error states                           | 1     |
| DI wire-up                                                      | 0.5   |
| Localization (5 dictionaries)                                   | 1     |
| User guide sections (5 languages), scanner setup notes          | 1.5   |
| Manual acceptance (§9.5) + fixes                                | 1.5–2 |
| `CHANGE_LOG.md`, release notes                                  | 0.5   |

**Total for flow (a): 15–17 developer-days**, split by phase (§1.6):

| Phase | Content                         | Days   |
|-------|---------------------------------|--------|
| 1     | Shared core + variant H         | ~9–10  |
| 2     | Variant W                       | ~6–7   |
| 3     | Flow (b), on request (§5C.6)    | ~4–5   |

Phase 1 is a shippable slice on its own: it delivers the full Italian
use case with a cheap 1D scanner or by typing the AIC, with no new
dependency and no privacy surface. Phase 2 plugs into the dialog
without touching the parser or the dialog contract. Phase 3 touches
neither.

### 10.2 Risks

1. **Webcam DataMatrix / Code 32 decode on cheap laptop webcams.**
   Untested until acceptance. Mitigations: throttle, `TryHarder`,
   framing overlay, timeout knob. Variant H is the fallback.
2. **Scanner configuration diversity (variant H).** Suffix, GS
   handling, Code 32 conversion and keyboard layout are all
   per-model settings. Mitigation: the parser accepts every known
   form; checksums prevent wrong links; the user guide lists the
   settings to check. Residual risk: support questions.
3. **GTIN-only scans in Italy.** Post-2027 packs may carry only the
   FMD DataMatrix. If the catalogue cannot resolve its product code
   to an AIC, scans degrade to "not in catalogue". `[UNCERTAIN]` —
   depends on whether Code 32 remains printed after February 2027 and
   on a verified GTIN/NTIN → AIC rule. Revisit before that date.
4. **ZXing Code 32 support.** Handled by decoding Code 39 and
   converting in the parser; no dependency on a ZXing Code 32 feature.
5. **Attack surface of the parser.** Accepts arbitrary strings from
   untrusted physical input (camera or keyboard). Managed C#, no
   `unsafe`, no P/Invoke, bounded loops, `MaxLength` on the input
   box. Fuzz corpus as guardrail.
6. **Privacy panel URI change.** `ms-settings:privacy-webcam` has been
   stable since Windows 10 1809; the dialog degrades gracefully.
7. **Self-contained ZIP size.** ZXing.Net is pure managed, order of
   1 MB. Negligible.

### 10.3 Costs paid elsewhere

- Every new key in **all five** dictionaries (`CLAUDE.md` §6).
- Manual acceptance needs physical hardware (webcam, 1D and ideally 2D
  scanner) and real packs. CI cannot cover it.

---

## 11. Non-goals — restated

1. FMD authenticity check (NMVS/EMVS query).
2. Storing batch/expiry in stock (no phase, §12.2). Flow (b) restocks
   quantity only (§5C).
3. HID POS, Raw Input, virtual COM port or global-hook scanner
   integrations (§5B.6).
4. Scanning outside `BarcodeScanDialog` (§5B.5).
5. Automatic keyboard-layout remapping of wedge input.
6. Mobile-camera pipeline (`EVOLUTION.md` §7).
7. Any medical-device claim, dose safety check, or interaction
   alert (`CLAUDE.md` §1).
8. Custom sound assets; system sounds only.
9. Barcode generation. Read-only.

---

## 12. Decisions still to confirm

### 12.1 Placement of the scan button

Flow (a): single button in `MedicineEditDialog`, next to the
commercial-name field. **Proposed default: `MedicineEditDialog` only.**

Flow (b), phase 3: a `MainForm` action "Restock from barcode" (§5C.3).
Toolbar button vs menu entry to decide at phase 3 time.

### 12.2 Expiry / batch capture

- **A**: expose AI `10` / AI `17` in `BarcodeContent`, unused in A2,
  including flow (b).
- **B**: add expiry/batch to `StockMovement` and fill them in flow
  (b). Needs a boot schema patch, export/import format changes, UI,
  and works only with DataMatrix (2D scanner or webcam). Separate
  feature.

**Proposed default: A.**

### 12.3 User guide coverage

**Proposed default:** a "Scanning a barcode" section in all five
`USER_GUIDE.*.md` covering both variants: 1D vs 2D scanner, the
scanner settings to check (suffix, Code 32 output, keyboard layout),
"click Scan barcode first", webcam permission.

### 12.4 Reference-catalogue country restriction

- **A**: look up only in the profile's catalogue country; cross-country
  scans are "not found".
- **B**: on a miss, retry once across all supported countries.

**Proposed default: A.**

### 12.5 Failure telemetry

`Warning` on `NoDeviceFound` / `PermissionDenied` / timeout /
`Unrecognized`; `Error` on `InitializationFailed`. Fields: input
variant, symbology, length, matched parser rule, duration. **Never**
the payload.

### 12.6 Default input mode

- **A**: always open in scanner mode; webcam on explicit click
  (proposed).
- **B**: remember the last mode in `user.settings.json` (§8.5).
- **C**: start both at once, first result wins. Rejected by default:
  it turns the camera on for users who only own a scanner.

**Proposed default: A.** B is a cheap follow-up if users ask.

### 12.7 GS substitute in the Settings UI

**Proposed default:** `appsettings.json` only in this slice (§5B.3);
the separator-free rule already covers A2's needs.

### 12.8 Keyboard-layout remapping

**Proposed default:** none; documentation plus checksum rejection
(§5B.4). Reopen if French-speaking users report it.

### 12.9 Scan capture while `MedicineEditDialog` has focus

- **A**: document only (proposed).
- **B**: detect a scanner burst in the name box (inter-key interval
  under ~30 ms, shape match) and reroute it to the scan flow. Adds a
  heuristic that can misfire on fast typists or paste.

**Proposed default: A.**

### 12.10 Delivery order

**DECIDED 2026-09-26.** Phase 1 = shared core + variant H; phase 2 =
variant W; phase 3 = flow (b), only after flow (a) is complete and on
explicit product-owner request. One PR per phase. See §1.6.

---

## 13. Sources

- AIFA, *The Italian drug traceability system* (Code 32 / AIC barcode,
  bollino DataMatrix) — https://www.aifa.gov.it/documents/20142/1267542/Traceability2020_WEB.pdf
- NMVO Italia, Legislative Decree on serialization —
  https://www.nmvoitalia.it/en/2025/02/10/decreto-legislativo-serializzazione/
- BWIPP wiki, Italian Pharmacode —
  https://github.com/bwipp/postscriptbarcode/wiki/Italian-Pharmacode
- zxing-js issue #308, Code 32 support request —
  https://github.com/zxing-js/library/issues/308
- Microsoft Learn, `Windows.Devices.PointOfService` namespace and
  "Getting started with Point of Service" —
  https://learn.microsoft.com/en-us/uwp/api/windows.devices.pointofservice
- Commission Delegated Regulation (EU) 2016/161; GS1 General
  Specifications.

---

## 14. Change log for this document

- 2026-09-19 — initial draft (pre-implementation), as
  `ANALYSIS-A2-BARCODE-WEBCAM.md`. Webcam variant only; HID scanner
  out of scope.
- 2026-09-25 — renamed to `ANALYSIS-A2-BARCODE-SCAN.md` and extended
  to the USB HID-scanner variant (§1.4, §5B, variant-specific tests
  and checklist, §12.6–§12.10). Corrections to the first draft,
  checked against the tree and public sources:
  - Italian packs carry the AIC as **Code 32** (Code 39 derivative),
    not inside an EAN-13. The EAN-13 → AIC `Substring(1, 9)` rule is
    dropped; EAN-13 now yields a GTIN only. `CODE_39` added to the
    ZXing formats and `Code39` to `BarcodeSymbology`.
  - The FMD DataMatrix has been mandatory in Italy only since
    9 February 2025 (stabilization to 8 February 2027), not since
    2019.
  - `MedicineEditDialog` has no national-code textbox; the "not
    found" path now shows the code in a message instead.
  - `LinkMedicineToReferenceUseCase` cannot serve Create mode (needs
    a persisted medicine); the dialog uses the existing
    `LookupByNationalCode` delegate and the `OnReferenceSelected` path.
  - "HID scanners work today with no code" was wrong (§5B.5b).
  - `ICameraCaptureService.ScanAsync` returns `RawBarcode`; parsing
    is shared by both variants in the dialog.
  - Infrastructure TFM lacks the Windows SDK version needed for WinRT
    projections; flagged in §5A.2.
- 2026-09-26 — product-owner decisions recorded. New §1.6: delivery
  phases HID (1) → webcam (2) → flow (b) (3, deferred, on explicit
  request after flow a is complete); §12.10 marked DECIDED. New §5C:
  flow (b) restock by scan — medicine match by national code, kind
  `NewPackage`, quantity from the last `NewPackage` movement, no
  expiry/batch (no field in `StockMovement`). §1.7 (was §1.5), §4.1,
  §10.1, §11, §12.1, §12.2 aligned.

# ANALYSIS — A2: Medicine barcode scan via webcam

Design document, **prior** to implementation. Corresponds to
`EVOLUTION.md` §3.2 (Group A, item A2), *webcam variant*. The USB
HID-scanner variant sketched in `EVOLUTION.md` is explicitly out of
scope for this document — see §12.3. Follows the structure of
`ANALYSIS-A1-REGIMENS.md`.

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
`MedicineEditDialog`, types the commercial name (with autocomplete
against the reference catalogue when the country profile is
supported) and — optionally — the national code (AIC in Italy).
`[VERIFIED]` — `src/MedReminder.UI/Forms/MedicineEditDialog.cs`.

For elderly users, and for anyone adding a medicine while holding
the physical package, this friction is the largest single source of
data-entry error observed by the maintainer.

Every box of medicine sold in the EU carries at least one barcode
that uniquely identifies the product:

- **GS1 DataMatrix** — 2D symbology, mandatory since 9 February 2019
  under Delegated Regulation (EU) 2016/161 (falsified-medicines
  safeguards). Encodes at minimum GTIN (AI `01`), serial number
  (AI `21`), batch (AI `10`), expiry (AI `17`). `[VERIFIED]` — EU
  Falsified Medicines Directive 2011/62/EU and Delegated Regulation
  2016/161.
- **EAN-13** — 1D symbology, historically the human-readable
  barcode on Italian packaging. In Italy, the AIC (Autorizzazione
  all'Immissione in Commercio, 9-digit AIFA identifier) is embedded
  in a 13-digit GS1 GTIN-13. `[VERIFIED]` — AIFA public
  documentation; still routinely present on boxes alongside the
  DataMatrix. `[INFERRED]` for "still present on every box"; not
  formally mandated, but universally observed.

### 1.2 Goal

Add a "Scan barcode" button to `MedicineEditDialog` that opens a
webcam preview, decodes GS1 DataMatrix and EAN-13, extracts the
national code (AIC in Italy, GTIN in the general case), and reuses
the existing `LinkMedicineToReferenceUseCase` to populate the
medicine record from the reference catalogue. `[VERIFIED against]`
`src/MedReminder.Application/Catalogue/LinkMedicineToReferenceUseCase.cs`.

Preserve the keyboard-only path as the primary input method: the
webcam is an *accelerator*, never a precondition.

### 1.3 What A2 is NOT

- **Not authenticity verification.** MedReminder does not query the
  EU Hub / NMVS / Italian NMVO to confirm the FMD serial is
  unique-and-active. That interaction requires OBP (On-Boarding
  Partner) credentials and is a pharmacy-side workflow, out of
  product scope per `CLAUDE.md` §1.
- **Not batch/expiry extraction into stock.** The parser MAY expose
  AI `10` (batch) and AI `17` (expiry) if trivially available, but
  the initial slice writes only the national code / GTIN. Stock
  seeding from the barcode is a follow-up (§12.2), not part of A2.
- **Not a USB HID scanner integration.** Handheld HID scanners already
  work with the current UI (they type into the focused textbox); no
  code is required. Explicitly scoping this out — see §12.3.
- **Not a mobile-camera pipeline.** The MAUI companion (`EVOLUTION.md`
  §6) is a separate track and does not share this document's
  implementation.
- **Not a medical-device feature.** No dose safety checks, no
  interaction warnings, no adherence claims. `CLAUDE.md` §1 and
  `EVOLUTION.md` §8.2 remain binding.

---

## 2. Barcode payload semantics

### 2.1 GS1 DataMatrix — layout

A GS1 DataMatrix payload begins with the FNC1 codeword, which
barcode decoders surface either as the raw ASCII character
`0x1D` (GS) between variable-length elements, or as the AIM
Identifier prefix `]d2` before the whole payload. `[VERIFIED]` —
ISO/IEC 15417 & GS1 General Specifications §7.8.

Typical FMD payload (fields packed, no separators between
fixed-length AIs, `<GS>` = 0x1D between the two variable-length
tails):

```
01<14 digits GTIN>21<serial>17<YYMMDD>10<batch>
]d201095012345678903211234567890123<GS>1725123110ABCD1234
```

Element extraction rules used by the parser:

| AI  | Meaning                | Length         |
|-----|------------------------|----------------|
| 01  | GTIN                   | 14 (fixed)     |
| 17  | Expiry (YYMMDD)        | 6  (fixed)     |
| 10  | Batch / lot            | up to 20 (var) |
| 21  | Serial number          | up to 20 (var) |

Fixed-length AIs (01, 17) are parsed by offset. Variable-length AIs
(10, 21) run until the next `<GS>` (`0x1D`) or end of payload.

### 2.2 EAN-13 — layout

- 12 payload digits + 1 checksum digit.
- Italian pharma EAN-13: leading `A` (typically `0`), followed by
  the 9-digit AIC, followed by a country/subrange filler and the
  GS1 mod-10 check digit. `[INFERRED]` from AIFA public examples;
  the concrete extraction rule the parser will implement is defined
  in §3.3.

### 2.3 GTIN → AIC mapping (Italy)

The GTIN embedded in the DataMatrix (AI `01`, 14 digits) starts
`0300` for many Italian human-medicinal products, with the AIC
sitting inside the next digits. `[UNCERTAIN]` for the exact
byte-level rule across all AIC ranges — deterministic mapping via a
public algorithm is not documented for every historical range.

Design consequence: **the parser does not attempt to derive AIC from
GTIN algorithmically.** Instead it hands the raw GTIN to the query
service; if that returns nothing, it also tries the EAN-13 fallback
strategy (§3.3). This keeps the parser stateless and pushes lookup
policy into `IReferenceCatalogueQueryService`, which is the layer
that already understands per-country codes. `[VERIFIED against]`
`src/MedReminder.Application/Catalogue/IReferenceCatalogueQueryService.cs`.

---

## 3. New abstractions

### 3.1 `RawBarcode` — `MedReminder.Domain/Catalogue/RawBarcode.cs`

Pure value object. Immutable. No dependencies.

```csharp
public readonly record struct RawBarcode(BarcodeSymbology Symbology, string Payload);

public enum BarcodeSymbology : int
{
    Unknown    = 0,
    Ean13      = 1,
    DataMatrix = 2,
}
```

Constructed by the capture layer; consumed by the parser. Never
leaves the boundary between Infrastructure and Application without a
non-null `Payload` of at least one character.

### 3.2 `BarcodeContent` — `MedReminder.Domain/Catalogue/BarcodeContent.cs`

The structured result of parsing a `RawBarcode`. Extraction is
best-effort: unknown or malformed payloads land as
`BarcodeContent.Unrecognized`.

```csharp
public sealed record BarcodeContent(
    string? Gtin,           // 14 digits or null
    string? NationalCode,   // AIC-9 in Italy, otherwise null
    string? Batch,          // AI 10 if present, else null
    DateOnly? Expiry,       // AI 17 if present, else null
    string? Serial)         // AI 21 if present, else null (never used in A2)
{
    public static readonly BarcodeContent Unrecognized =
        new(null, null, null, null, null);
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

Concrete implementation lives in `MedReminder.Application` (not
Infrastructure — it is pure C# and platform-independent, and the
Application project already targets `net10.0`). Rules:

1. If `Symbology == DataMatrix` and payload starts with `01` or the
   AIM prefix `]d2`, strip the AIM prefix, then walk the AI table
   from §2.1 to extract GTIN / batch / expiry / serial. Unknown AIs
   are skipped conservatively (variable-length ones stop at
   `<GS>`).
2. If `Symbology == Ean13`, and the payload is 13 digits, and the
   check digit passes GS1 mod-10, extract the 9-digit AIC as
   `payload.Substring(1, 9)` and expose it as `NationalCode`.
   `[INFERRED]` — matches the AIFA public examples; will be
   validated against a corpus during implementation.
3. If `Symbology == Unknown`, run rule 1 first (heuristics: `]d2`
   prefix or the presence of `01` followed by 14 digits), then
   rule 2 (13 digits, mod-10 valid), then return `Unrecognized`.
4. Never throw on malformed input. Return `Unrecognized`. Log the
   symbology and length, never the payload (payloads may contain
   FMD serial numbers, which are pseudonymous identifiers of a
   physical box — treat as data minimization, not as a hard
   requirement).

Unit-tested in isolation (§9.1).

### 3.4 `ICameraCaptureService` — `MedReminder.Application/Abstractions/ICameraCaptureService.cs`

The port through which the UI asks Infrastructure to acquire and
decode a barcode. Deliberately hides `MediaCapture`, `SoftwareBitmap`,
ZXing types.

```csharp
public interface ICameraCaptureService
{
    Task<CameraAvailability> ProbeAsync(CancellationToken ct);

    Task<BarcodeContent> ScanAsync(
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

`ScanAsync` returns the *first* recognized `BarcodeContent`
whose parse produced at least a `Gtin` or a `NationalCode`. On
`ct` cancellation it returns `BarcodeContent.Unrecognized`.

### 3.5 `WindowsCameraCaptureService` — Infrastructure

Lives in `MedReminder.Infrastructure/Capture/`. Wraps:

- `Windows.Media.Capture.MediaCapture` for camera enumeration and
  frame acquisition. `[VERIFIED]` — WinRT API surface, reachable
  from `net10.0-windows10.0.19041.0` via CsWinRT projections. The
  UI project already targets that TFM. `[VERIFIED against]`
  `src/MedReminder.UI/MedReminder.UI.csproj`.
- `MediaFrameReader` for a pull-based frame loop at ~10 fps
  (`Windows.Media.Capture.Frames` namespace).
- **ZXing.Net** (NuGet `ZXing.Net`, Apache 2.0) for barcode
  decoding of individual frames. `[VERIFIED]` license and package
  availability; the concrete decoder-init call surface will be
  finalized against the NuGet version at implementation time.

Registered in `InfrastructureServiceCollectionExtensions.cs`
against `ICameraCaptureService`.

### 3.6 UseCase seam

No new use case class. The dialog reuses the existing
`LinkMedicineToReferenceUseCase` once the parser produced a
`NationalCode` (or a `Gtin` used as a fallback code). The
consequence: only two new types cross the Application boundary
(`IBarcodeParser`, `ICameraCaptureService`) — everything else is
existing code.

---

## 4. UI integration

### 4.1 Placement

In `MedicineEditDialog`, next to the "Commercial name" autocomplete
row, add a small square button `[⌾]` labelled by resource key
`MedicineEdit.ScanBarcode`. Enabled only when
`CountryProfile.Country` is in the catalogue-supported set (same
condition as autocomplete), `[VERIFIED against]`
`src/MedReminder.UI/Forms/MedicineEditDialog.cs` `CatalogueAutocompleteContext`.

Not present in `MainForm` toolbar. Rationale: barcode scan makes
sense only in a create-or-edit context bound to a single medicine;
adding it to the main window would require a follow-up modal
anyway.

### 4.2 Scan dialog

New form `BarcodeScanDialog : MedReminderFormBase`, modal, resizable,
minimum size 480×360. Contents:

- Header: `MedicineEdit.ScanBarcode.Prompt`.
- Preview area: a `Panel` that hosts the live frames as a
  `PictureBox` filled with the current `SoftwareBitmap` converted
  to `Bitmap`. `[INFERRED]` — MediaCapture preview XAML control is
  not usable inside a WinForms host without heavy interop; a pulled
  `MediaFrameReader` loop into a `PictureBox` is the smaller and
  well-trodden path.
- Overlay: a horizontal band drawn on top of the frame indicating
  the target framing area (visual only; the decoder scans the whole
  frame).
- Status label: bound to `CameraFrameStatus` from
  `ScanAsync`'s `IProgress<T>` — shows "Searching…" or
  "Decoded".
- Footer buttons: "Cancel" (only).

On successful decode:
1. Play a short system sound (`SystemSounds.Asterisk.Play()`), no
   custom asset.
2. Close the form with `DialogResult.OK`.
3. `MedicineEditDialog` receives the `BarcodeContent`, hands the
   `NationalCode` (or `Gtin` as fallback) to
   `LinkMedicineToReferenceUseCase.ExecuteAsync` via the existing
   catalogue path.
4. On `LinkResult.Linked` the dialog fields repopulate exactly as
   they do today when the user selects a row from the autocomplete
   dropdown.
5. On `LinkResult.ReferenceNotFound` the dialog fills only the
   `NationalCode` textbox with the raw code and shows a warning
   toast: "Barcode read, but no matching entry in the catalogue.
   Enter the medicine details manually."

Timeout: 30 seconds of scanning without a decoded frame closes the
dialog with a "No barcode detected" message. Configurable in
`appsettings.json` under `Capture:ScanTimeoutSeconds`
(default 30).

### 4.3 Error surfaces

The dialog handles four terminal states from
`CameraAvailability`:

| Availability          | UI behavior                                                                                          |
|-----------------------|------------------------------------------------------------------------------------------------------|
| `Available`           | Preview starts. See §4.2.                                                                            |
| `NoDeviceFound`       | Modal shows `MedicineEdit.ScanBarcode.NoDevice`. Cancel-only. Not an error dialog — a plain message. |
| `PermissionDenied`    | Modal shows `MedicineEdit.ScanBarcode.PermissionDenied` with a "Open Privacy Settings" button that shells `ms-settings:privacy-webcam`. |
| `InitializationFailed`| Modal shows `MedicineEdit.ScanBarcode.InitializationFailed` with a "Copy log location" button that copies `%LOCALAPPDATA%\MedReminder\logs\` to the clipboard. |

No stack traces. All four states are logged (level `Warning` for
the first three, `Error` for the fourth) via Serilog, per
`CLAUDE.md` §6 (never log PII or medical detail).

---

## 5. Camera capture layer — implementation notes

### 5.1 API surface choice

`Windows.Media.Capture.MediaCapture` (WinRT) over DirectShow. Both
technically work; MediaCapture is the API Microsoft actively
supports, is aware of the Windows privacy panel (§7), and has a
frame-reader model that avoids the XAML-in-WinForms interop
problem when combined with `MediaFrameReader`. `[VERIFIED]` — MS
docs list DirectShow as legacy for consumer camera capture.

DirectShowLib is the fallback if a maintainer decision later drops
the `net10.0-windows10.0.19041.0` TFM — not the case today.

### 5.2 Frame pipeline

1. Enumerate video devices via
   `MediaFrameSourceGroup.FindAllAsync`; pick the first source of
   kind `Color` with subtype `NV12` or `MJPG`. Log all detected
   groups (name only, never the device id).
2. Initialize `MediaCapture` with
   `MediaCaptureInitializationSettings { SourceGroup = …,
   SharingMode = ExclusiveControl, MemoryPreference = Cpu,
   StreamingCaptureMode = Video }`.
3. Create a `MediaFrameReader` bound to the picked source.
4. Subscribe to `FrameArrived`; each arrival hands a
   `SoftwareBitmap`. Convert to a `Bitmap` (via a small helper
   using `SoftwareBitmap.CopyToBuffer` and `BitmapData`; **no**
   dependency on `Windows.Graphics.Imaging.SoftwareBitmapSource`,
   which is XAML-tied).
5. Feed the `Bitmap` to ZXing.Net's `BarcodeReader`, hint set to
   `{ BarcodeFormat.EAN_13, BarcodeFormat.DATA_MATRIX }`, tryHarder
   `true`, tryInverted `false`.
6. First non-null result → raise the `ScanAsync` completion via
   the `TaskCompletionSource` owned by the service.
7. Regardless of result, dispatch a `CameraFrameStatus` to the
   `IProgress<T>` on the UI thread.

Throttling: only every third arrived frame is fed to ZXing. Empirical
tuning knob (§10 risks).

### 5.3 Disposal

`WindowsCameraCaptureService` holds `MediaCapture`,
`MediaFrameReader`, and one `SemaphoreSlim` to serialize
concurrent scan requests. `ScanAsync` uses a scoped disposable
scan session; the service instance is registered singleton.

On `ScanAsync` cancellation or completion:
1. `FrameReader.StopAsync` awaited.
2. `MediaCapture.Dispose()`.
3. Semaphore released.

The camera indicator LED must go off within one second of the
dialog closing — mandatory user-trust property. Verified manually
during acceptance testing.

---

## 6. Barcode decoding — `ZXing.Net` integration

### 6.1 Package

- NuGet: `ZXing.Net` (Apache 2.0). `[VERIFIED]` license.
- Companion: `ZXing.Net.Bindings.Windows.Compatibility` for
  `Bitmap` → `LuminanceSource` conversion, when the primary
  package does not already ship a `BitmapLuminanceSource` under
  the target framework. Package selection to be validated at
  implementation time against the current NuGet metadata.
  `[UNCERTAIN]` on exact binding package for `net10.0-windows`.

No commercial libraries. IronBarcode / Dynamsoft ruled out on
licensing grounds — the project is Apache 2.0 (`LICENSE`,
`CLAUDE.md` §1). `[VERIFIED against LICENSE]`.

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
            BarcodeFormat.EAN_13,
            BarcodeFormat.DATA_MATRIX,
        },
    },
};
```

Restricting `PossibleFormats` cuts decode time significantly and
avoids false positives (e.g. an internal QR code on the packaging
carrying marketing content).

### 6.3 Symbology mapping

`BarcodeReader.Decode` returns a `Result` whose `BarcodeFormat` we
map into `BarcodeSymbology`:

- `BarcodeFormat.DATA_MATRIX` → `BarcodeSymbology.DataMatrix`.
- `BarcodeFormat.EAN_13`      → `BarcodeSymbology.Ean13`.
- anything else               → discard; keep scanning.

`Result.Text` is the raw payload. For DataMatrix, ZXing does **not**
inject the `]d2` AIM prefix by default; the parser (§3.3) handles
both cases (with and without prefix).

---

## 7. Windows privacy panel

### 7.1 Behavior

Since Windows 10 build 1903 (April 2019) the OS enforces the
"Camera access" toggle in **Settings → Privacy & security → Camera**
also for classic desktop apps, exposed as "Let desktop apps access
your camera". `[VERIFIED]` — MS documentation. `MediaCapture`
initialization on a denied device throws
`UnauthorizedAccessException`. `[VERIFIED]` — WinRT contract.

### 7.2 UX

No runtime OS prompt for non-MSIX desktop apps; the user must
change the flag in Settings. `BarcodeScanDialog` renders the
`PermissionDenied` state (§4.3) with a button that launches
`ms-settings:privacy-webcam` via `Process.Start`. The user comes
back and re-clicks Scan.

### 7.3 No MSIX-specific work

The primary distribution channel is the self-contained ZIP
(`docs/PACKAGING.md`). Declaring the `webcam` capability in the
MSIX manifest would enable the packaged-app prompt flow — noted
for future work if MSIX becomes the primary channel, not required
here.

### 7.4 Camera-in-use indicator

Windows 11 24H2 shows an on-screen camera indicator when a
process holds a camera handle. This is a system feature, not an
in-app one — the app must not attempt to suppress it. Verified as
part of §5.3 (release the handle promptly).

---

## 8. Data model, persistence, schema

### 8.1 Domain

Two new types (`RawBarcode`, `BarcodeContent`) in
`MedReminder.Domain/Catalogue/`. Both are value objects. No new
mutable state, no schedule interaction, no impact on
`MedicationScheduleHistory`.

### 8.2 Application

Two new interfaces (`IBarcodeParser`, `ICameraCaptureService`) in
the locations named in §3.3 / §3.4. One concrete parser
implementation (pure C#) alongside the interface. No new use case.

### 8.3 Infrastructure

One concrete `WindowsCameraCaptureService` in
`MedReminder.Infrastructure/Capture/`. No EF model changes, no
migration.

### 8.4 Persistence patches

**None.** A2 does not add columns and does not read from tables
that A1 changed. `DatabaseInitializer` is untouched.
`[VERIFIED against `CLAUDE.md` §9 "never use `EnsureCreated()`"]`
— this rule is not triggered because no schema change is proposed.

### 8.5 Configuration

`appsettings.json` gains:

```jsonc
"Capture": {
  "ScanTimeoutSeconds": 30,       // §4.2
  "MaxDecodeFps": 10,             // §5.2 throttle
  "PreviewMaxWidthPixels": 640    // §5.2 initialization hint
}
```

Loaded via the existing `Microsoft.Extensions.Configuration.Json`
pipeline. `[VERIFIED against]`
`src/MedReminder.UI/MedReminder.UI.csproj`.

---

## 9. Tests

### 9.1 Unit — Domain / Application (`MedReminder.Application.Tests`)

Cross-platform (`net10.0`), runs on Linux CI.

- **GS1 DataMatrix**: table-driven cases covering the FMD payload
  shape from §2.1, including:
  - Fixed-length-only payloads.
  - Payloads with a `<GS>` separator between AI 10 and AI 21.
  - Payloads with the `]d2` AIM prefix.
  - Malformed payloads (missing GTIN, truncated variable field,
    non-digit inside AI 01) → `Unrecognized`.
- **EAN-13**:
  - Valid Italian pharma EAN-13 → correct 9-digit AIC.
  - Wrong mod-10 check digit → `Unrecognized`.
  - Non-13-digit input → `Unrecognized`.
- **Symbology hint absent**: `Symbology.Unknown` with a valid
  DataMatrix payload → recognized DataMatrix.
- **No PII in exceptions**: assert the parser never throws with an
  input from the fuzz corpus (§9.3).

### 9.2 Unit — Infrastructure (`MedReminder.Infrastructure.Tests`)

Windows-only, matches the existing infra-tests posture
(`CLAUDE.md` §4). Scope kept narrow:

- `ProbeAsync` on a machine with no camera returns
  `CameraAvailability.NoDeviceFound`.
- Disposal test: after `ScanAsync` cancellation, no `MediaCapture`
  instance is retained. Verified via `WeakReference` and forced
  GC.

No integration test drives ZXing against a real camera on CI — the
CI runner has no webcam. Manual acceptance testing covers that
path (§9.4).

### 9.3 Fuzz corpus

A small fixed corpus of random `byte[]` payloads (256 samples,
lengths 0..64) is checked into `tests/fixtures/barcode-fuzz/` and
replayed against `IBarcodeParser` in a xUnit theory. Objective:
zero exceptions, all outcomes `Unrecognized`. Regenerated by a
one-off script (not run at CI time).

### 9.4 Manual acceptance checklist

Documented in `docs/USER_GUIDE.en.md` under a new "Testing the
barcode scanner" appendix section (not visible to end users; used
by the maintainer at release time):

1. Print or hold three real Italian medicine boxes with distinct
   AICs. Scan each; expect autofill.
2. Cover the camera; expect timeout after 30 s with the correct
   message.
3. Disable "Let desktop apps access your camera" in Windows
   Settings; expect `PermissionDenied` state, and the settings
   deep-link to work.
4. Yank the USB webcam mid-scan; expect graceful cancellation and
   the "InitializationFailed" state on the next attempt.
5. Confirm the Windows 11 24H2 camera indicator turns off within
   1 s of closing the dialog.

---

## 10. Effort, risks, non-obvious costs

### 10.1 Effort estimate

`[INFERRED]`, single developer familiar with the codebase, no
parallel work:

| Layer                                            | Days |
|--------------------------------------------------|------|
| `RawBarcode`, `BarcodeContent`, `IBarcodeParser` + unit tests | 2 |
| `ICameraCaptureService` + WinRT/ZXing adapter    | 3–4  |
| `BarcodeScanDialog` + integration into `MedicineEditDialog` | 2 |
| Wire-up in DI (`InfrastructureServiceCollectionExtensions`, UI DI) | 0.5 |
| Localization (5 dictionaries, new keys)          | 1    |
| User guide sections (5 languages)                | 1    |
| Manual acceptance (§9.4) + fixes                 | 1–2  |
| `CHANGE_LOG.md` entry, release notes             | 0.5  |

**Total: 11–13 developer-days.** In the same ballpark as
`EVOLUTION.md` §3.2 ("1–2 weeks for the desktop path"), and
consistent with the A1 shape.

### 10.2 Risks

1. **DataMatrix decode reliability on cheap laptop webcams.**
   Untested until acceptance. Mitigations: frame throttle
   (§5.2), `TryHarder`, framing overlay, longer timeout knob.
   No worse-case guarantee — if the empirical rate is bad,
   `EVOLUTION.md` §3.2's "handheld USB scanner as HID keyboard"
   remains as a supported alternative *at the OS level*, requires
   no code change, and the maintainer's install guide can call it
   out.
2. **ZXing DataMatrix parser edge cases.** Real FMD payloads can
   include Latin-1 characters inside the batch/serial. Parser
   already treats them as opaque strings; not a decoding-time
   concern. `[INFERRED]`
3. **Windows privacy panel changes.** MS could change the settings
   URI in a future release. `ms-settings:privacy-webcam` has been
   stable since Windows 10 1809 `[VERIFIED]`; the risk is low but
   the dialog degrades gracefully — the button becomes a no-op and
   the user still sees the instructional text.
4. **Attack surface of a barcode parser.** `IBarcodeParser` accepts
   arbitrary byte strings from an untrusted physical input. Kept
   in managed C# with no `unsafe`, no P/Invoke, no allocation-
   unbounded loops. The fuzz corpus (§9.3) is the primary
   guardrail.
5. **Self-contained ZIP size.** ZXing.Net is pure managed, ~1 MB.
   Negligible. `[VERIFIED]` — order of magnitude confirmed against
   NuGet metadata.
6. **CsWinRT projection stability under `net10.0`.** The UI TFM
   already exercises WinRT (`Microsoft.Toolkit.Uwp.Notifications`
   for toast). Adding `Windows.Media.Capture` uses the same
   projection pipeline. `[VERIFIED against]`
   `src/MedReminder.UI/MedReminder.UI.csproj`.

### 10.3 Costs paid elsewhere

- New localization keys — every new key must land in **all five**
  dictionaries per `CLAUDE.md` §8. Non-negotiable.
- Manual acceptance requires a physical webcam and a physical
  medicine box. CI cannot cover this.

---

## 11. Non-goals — restated

Explicit list for cross-reference during PR review:

1. FMD authenticity check (NMVS/EMVS query).
2. Stock seeding from batch/expiry.
3. HID scanner integration (works today at the OS level; no code).
4. Mobile-camera pipeline (belongs to `EVOLUTION.md` §6).
5. Any medical-device claim, dose safety check, or interaction
   alert (`CLAUDE.md` §1, `EVOLUTION.md` §8.2).
6. Custom sound assets; system sounds only.
7. Barcode *generation* (encoder). Read-only.

---

## 12. Decisions still to confirm

### 12.1 Placement of the scan button

Proposal: single button in `MedicineEditDialog`, next to the
"Commercial name" field. Alternative: also expose it as a toolbar
action on `MainForm` that opens a create dialog pre-populated from
the scan. **Proposed default: create-dialog only.** Confirm.

### 12.2 Expiry / batch capture — deferred?

The parser already extracts AI `10` and AI `17` (cheap). Two
options:

- **A**: expose them in `BarcodeContent` but do not use them in
  A2 (current proposal). Zero UX churn, keeps the door open.
- **B**: also seed a stock movement with `ExpiryDate = AI17`.
  Requires a UX choice (auto-select quantity? default to 1 unit?)
  and touches `AddStock`. Bigger scope. Deferred by default.

**Proposed default: option A.** Confirm.

### 12.3 HID scanner path — explicitly documented?

The user's Q3 answer scoped this document to webcam only. Should
`docs/USER_GUIDE.*` still call out "an HID USB scanner already
works with the current UI"? **Proposed default: yes, one
sentence in the "Adding a medicine" section.** Confirm.

### 12.4 Reference-catalogue country restriction

Today the catalogue is queried per `CountryProfile.Country`. If
the user scans an Italian AIC while their profile is set to
`FR`, the query returns nothing. Two options:

- **A**: silently follow the profile country; treat cross-country
  scans as "not found" (proposed).
- **B**: after a "not found" on the profile country, retry once
  across all supported countries.

**Proposed default: option A**, on grounds of predictability and
minimal surprise. Reopen if user reports show it hurts.

### 12.5 Failure telemetry

The service logs `Warning` on `NoDeviceFound` /
`PermissionDenied` / timeout, and `Error` on
`InitializationFailed`. Log fields: symbology, length,
duration. **Never** the payload. Confirm this is the wanted
posture.

---

## 13. Change log for this document

- 2026-09-19 — initial draft (pre-implementation). Webcam variant
  of `EVOLUTION.md` §3.2. HID scanner variant explicitly out of
  scope. Numbering follows `EVOLUTION.md`'s A2, not a new A4.

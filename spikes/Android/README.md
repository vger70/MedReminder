# B.1 Android spikes S1–S4

Throw-away tool for the Android spikes of B.1 Phase 0
(`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §13 Phase 0, §18). They are
the go / no-go risks for MAUI before Phase 5 (`docs/STATUS.md` §4).
Like S6 and S7, this code lives on a draft PR and is **not merged**.
Only the results go into §18 of the analysis.

| Spike | Question | Where |
|---|---|---|
| S1 | Does AES-GCM work on Android, and does the phone decrypt a desktop archive? (P12) | App: *S1 AES-GCM*, *S1b Decrypt a desktop archive…* |
| S2 | What does Argon2id with `Argon2Params.Default` cost on a low-end phone? | App: *S2 Argon2id cost* |
| S3 | Do EF Core SQLite and the production persistence work with Release trimming / AOT? (P13) | App: *S3 EF Core SQLite*, in the Release APK |
| S4 | Does `StripReleaseDebugArtifacts` break or pass a Release Android build? (D11) | `run-s4.ps1` |

Out of scope here:
- S5 (notifications) and S8 (background sync) need hours of
  observation and reboots, so they get a later tool.
- The Android halves of S6 and S7 need the Android OAuth clients (P14),
  and run with Phase 5.

The app references the production `MedReminder.Domain`,
`MedReminder.Application` and `MedReminder.Infrastructure.Portable`
projects through `AddMedReminderPortableInfrastructure`. The spikes
therefore measure the code a Phase 5 client would run. The app
inherits `Directory.Build.props` unchanged.

**Status (2026-10-04, aligned with `main` at v2.16.0):** the app builds,
and the S1–S3 logic passes off-device. What remains is the run on real
devices. See [Pre-check without a device](#pre-check-without-a-device).

## Prerequisites

- Windows 10/11 with the .NET 10 SDK, the same SDK used for the
  desktop build.
- The MAUI Android workload:
  `dotnet workload install maui-android`.
- The Android SDK and a JDK. Visual Studio installs both. Without Visual
  Studio, from `spikes/Android/MedReminder.MobileSpikes`:
  `dotnet build -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=True`.
- `adb` on `PATH` (Android SDK `platform-tools`).
- Devices with USB debugging on:
  - one on Android 14 or later;
  - one at the proposed floor of D13 (Android 8.0, API 26), if
    available;
  - one low-end phone (3–4 GB RAM or less) for S2.
- For S1b: a `.mrz` archive exported by the desktop from a **test
  profile** with synthetic data, and its passphrase.

## Procedure

Run from `spikes/Android/`.

1. **Debug baseline.** Build and start the app in Debug:
   `dotnet build MedReminder.MobileSpikes -t:Run -f net10.0-android`.
   Tap *Run S1, S2, S3*, then *S1b* with the test archive, then *Share
   report*. Save the JSON as
   `results/<device>-debug.json`.
2. **S4 and the Release build.** `pwsh ./run-s4.ps1 -Install`. The
   script publishes Release, writes `results/s4-<timestamp>-default.md`,
   installs the APK and starts it. In the app, tap *Run S1, S2, S3* and
   *S1b*, then save the report as
   `results/<device>-release.json`. Fill in the last section of the S4
   report.
3. **S3 with full trimming.** `pwsh ./run-s4.ps1 -TrimMode full -Install`,
   then tap *S3 EF Core SQLite* and save the report as
   `results/<device>-release-trim-full.json`.
4. **S2 on the low-end phone.** Repeat step 2 on that phone, or at
   least *S2 Argon2id cost*.

The header of the app shows how the running APK was built:
configuration, `PublishTrimmed`, `TrimMode`, `RunAOTCompilation` and
the JSON reflection switch. The report repeats this under
`environment.build.*`.

## Acceptance

From §13 Phase 0. The Phase 0 exit needs S1 and S3 to pass or to
have accepted mitigations.

| Spike | Pass when |
|---|---|
| S1 | `AesGcm.IsSupported` is true. The AES-GCM and Argon2id known answers equal the references. A tampered tag is rejected. The desktop archive decrypts, in Debug and Release |
| S2 | No out-of-memory. The worst of three derivations takes under 5 s on the low-end phone. The key equals the reference |
| S3 | Every S3 check passes in the Release build with the default settings. The full-trimming run is recorded; a failure there is a finding, not a blocker, as long as the default passes |
| S4 | The publish succeeds with `Directory.Build.props` unchanged. A signed APK is produced, installs, starts, and passes S1 and S3. Otherwise the exclusion goes to the product owner (D11) |

What to look for in S3 and S4, given the pre-check below:

- **Reflection JSON.** `ArchiveReader` (manifest, payload) and the sync
  codec use reflection-based `System.Text.Json`. The .NET for Android
  Release default sets `JsonSerializerIsReflectionEnabledByDefault=true`
  (pre-check), so with the default settings the check *Reflection-based
  System.Text.Json* is expected to pass. Under full trimming the
  serializer is kept, but the members of the serialized types may be
  trimmed (IL2026 warnings on `ArchiveReader`, `JsonSyncSettingsStore`
  and `ScheduleCodec`). S1b and the JSON check in the
  `-TrimMode full` APK show whether that happens. The mitigation would
  be source-generated `JsonSerializerContext`s in the portable code.
- **EF Core under trimming.** EF Core builds its model with reflection.
  A failure in *DatabaseInitializer on a new database* in Release,
  but not in Debug, is the P13 risk itself. Under full trimming EF Core,
  EF Core Relational, EF Core Sqlite and SQLitePCLRaw report trim
  warnings (IL2104), and so do the entity configurations and the
  `MedReminderDbContext` constructor (IL2026).

## Pre-check without a device

Run on 2026-10-04 on Linux, on the merge of this branch with `main`
(v2.16.0). Toolchain:
- .NET SDK 10.0.112 (Ubuntu package);
- workloads `android` 36.1.69 and `maui-android` 10.0.110;
- OpenJDK 21.

The Android SDK download is blocked in that environment. In its place
the build used:
- the Robolectric `android-all` 16 jar (API 36) from Maven Central, as
  `android.jar`;
- `zipalign` and `apksigner` from the Ubuntu packages.

The APKs built there were not installed on a device. Build on Windows
with the real Android SDK for the device runs.

| Check | Result |
|---|---|
| Debug build | Succeeds, no warnings |
| Release publish, default settings | Succeeds, no warnings. Defaults: `PublishTrimmed=true`, `TrimMode=partial`, `RunAOTCompilation=true`, `AndroidEnableProfiledAot=true`, `JsonSerializerIsReflectionEnabledByDefault=true`. Signed APK 41.6 MB |
| `StripReleaseDebugArtifacts` on that publish | Runs unchanged, deletes 2 candidates (`MedReminder.MobileSpikes.pdb`, `.xml`) from the output folder. No `*.pdb` / `*.xml` left in the output or publish folders. The APK is produced after the strip |
| Release publish, `-p:SpikeTrimMode=full` | Succeeds with 54 IL2026 and 8 IL2104 trim warnings (EF Core, SQLitePCLRaw, reflection JSON). Signed APK 36.3 MB |
| S1–S3 logic on CoreCLR (linux-x64), production cipher and persistence | Every check passes. AES-GCM and Argon2id known answers equal the references. Argon2id default about 0.4–0.5 s on the build machine. 32 tables created. `journal_mode` WAL, SQLite 3.53.3 |

The earlier note that `StripReleaseDebugArtifacts` might run before the
publish folder is filled did not show up in this build: nothing was
left to strip. It still has to be confirmed on Windows, where the
target was designed.

## Privacy

The report contains device model, Android version, ABI, RAM, build
settings and results. It contains no account, no device identifier,
and no passphrase. S1b reports format facts and row counts only. S3
writes one synthetic medicine to a scratch database in the app folder.

Commit only the JSON reports and the `s4-*.md` files. The build logs
(`s4-*-build.log`) contain local paths.

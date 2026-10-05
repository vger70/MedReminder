# B.1 Android spikes S1–S5, S8

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
| S5 | Do local notifications planned with `AlarmManager` fire on time in the background, swiped away, force-stopped, after a reboot, under Doze, and with exact alarms denied? (§8.1, §8.2) | App: *S5 …* buttons, [S5 and S8](#s5-and-s8) |
| S8 | How often does WorkManager run 15-minute periodic work, with and without battery saver? (§7.4) | App: *S8 …* buttons, [S5 and S8](#s5-and-s8) |

Out of scope here:
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
  Studio, install them with the `InstallAndroidDependencies` target. It
  needs absolute target folders: without `AndroidSdkDirectory` it fails
  with MSB4044 (`AndroidSdkPath`). It installs the JDK only when
  `JavaSdkDirectory` is given. From `spikes/Android/MedReminder.MobileSpikes`:

  ```powershell
  dotnet build -t:InstallAndroidDependencies -f net10.0-android `
    -p:AndroidSdkDirectory=C:\Android\sdk `
    -p:JavaSdkDirectory=C:\Android\jdk `
    -p:AcceptAndroidSDKLicenses=True
  ```

  Every later build must find the same folders. MSBuild reads
  environment variables as properties, so set them once for the
  session (or with `setx` for good), and put `adb` on `PATH`:

  ```powershell
  $env:AndroidSdkDirectory = 'C:\Android\sdk'
  $env:JavaSdkDirectory = 'C:\Android\jdk'
  $env:PATH += ';C:\Android\sdk\platform-tools'
  ```

- Devices with USB debugging on:
  - one on Android 14 or later;
  - one at the proposed floor of D13 (Android 8.0, API 26), if
    available;
  - one low-end phone (3–4 GB RAM or less) for S2.
- For S1b: a `.mrz` archive exported by the desktop from a **test
  profile** with synthetic data, and its passphrase.

## Procedure

Run from `spikes/Android/`.

1. **Debug baseline.** Connect the phone and authorize USB debugging;
   `adb devices` must list it as `device` (not `unauthorized` or
   `offline`), or the run stops with XA0010 after a successful build.
   Build and start the app in Debug:
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

## Device results (2026-10-05)

motorola edge 50 neo, Android 16 (API 36), arm64-v8a, 7.4 GB RAM;
.NET SDK 10.0.401 on Windows, runtime .NET 10.0.12. Reports in
`results/`. The desktop archive for S1b was exported by MedReminder
2.16.0 (payload schema 2); the reports hold counts only.

| Spike | Debug | Release, default (`TrimMode=partial`, profiled AOT) | Release, `TrimMode=full` |
|---|---|---|---|
| S1 | Pass; S1b decrypts the desktop archive | Pass; S1b decrypts it in 1.2 s | Primitives pass; **S1b fails**: `JsonSerializerIsReflectionDisabled` |
| S2 | 7.05–7.09 s, Fail; runs in the interpreter (`isDynamicCodeCompiled=False`), not representative | **0.88–0.90 s, Pass**; key equals the reference, peak working set 334 MiB | 0.89–0.91 s, Pass |
| S3 | Pass | **Pass**: every check | **Fail**: every EF Core check throws `MissingMethodException` (`EntryCurrentValueComparer<Guid>` constructor trimmed); the JSON check fails as S1b |
| S4 | — | **Pass**: publish exit 0 with `Directory.Build.props` unchanged, 2 files stripped and none left, APK installs, starts and passes S1 and S3 | Builds and starts; S1b and S3 fail as above |

Reading:

- With the .NET for Android Release defaults, S1, S3 and S4 pass on
  this phone: no exclusion from `StripReleaseDebugArtifacts` is needed
  (D11), and EF Core SQLite works with the default trimming and AOT
  (P13).
- Full trimming is not usable as is. It disables reflection-based JSON
  (`JsonSerializerIsReflectionEnabledByDefault=false`, unlike the
  default) and removes EF Core members created by reflection. Using it
  would need source-generated JSON contexts in the portable code and
  EF Core trimming support; the default settings do not need either.
  The earlier note that full trimming keeps the serializer was wrong.
- S2 passes on a mid-range phone; see below for the low-end phone.

### Two more phones, Release default

| Phone | Android | RAM | S1 / S1b | S2 per derivation | S3 |
|---|---|---|---|---|---|
| Samsung Galaxy A32 4G (SM-A325F, Helio G80) | 13 (API 33) | 3.6 GB | Pass; S1b 2.5 s | 1.90–2.04 s, Pass | Pass |
| Samsung Galaxy A52 5G (SM-A526B) | 14 (API 34) | 5.4 GB | Pass; S1b 1.4 s (a first attempt with a wrong passphrase was rejected as expected) | 1.07–1.08 s, Pass | Pass |

The A32 meets the low-end criterion of this README (4 GB RAM or less),
so S2 is closed: the worst derivation stays under 2.1 s, no
out-of-memory, key equal to the reference.

Still open: a device at the D13 floor (API 26), if available.

## Pre-check without a device

Run on 2026-10-04 on Linux, on the merge of this branch with `main`
(v2.16.0), and repeated from a clean output before the device runs. Toolchain:
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
| `run-s4.ps1` (PowerShell 7.6.6), default and `-TrimMode full` | Both runs exit 0 and write the report. Strip candidates 2, deleted 2, none left. The `-Install` path reports `adb not on PATH` when adb is missing |
| S1b on CoreCLR, with an archive written in the export format (default KDF) by the production cipher | Manifest read and payload decrypted with the right passphrase. A wrong passphrase gives `ImportFailedException` ("The passphrase does not match this file"), with no secret in the report |
| S1–S3 logic on CoreCLR (linux-x64), production cipher and persistence | Every check passes. AES-GCM and Argon2id known answers equal the references. Argon2id default about 0.4–0.5 s on the build machine. 32 tables created. `journal_mode` WAL, SQLite 3.53.3 |

The earlier note that `StripReleaseDebugArtifacts` might run before the
publish folder is filled did not show up in this build: nothing was
left to strip. It still has to be confirmed on Windows, where the
target was designed.

## S5 and S8

Both run over hours and survive the app being closed: alarms, the
boot receiver and the WorkManager job write to log files in the app's
private folder, and **Collect S5 and S8 results** turns the logs into
report lines, then **Share report** as usual. Use the Release APK
(`run-s4.ps1 -Install`); Debug runs in the interpreter and starts more
slowly from a cold process.

### S5 — local notifications

A *battery* schedules four alarms per time offset, one per kind:

| Kind | API | Needs exact-alarm permission |
|---|---|---|
| Exact | `setExactAndAllowWhileIdle` | Yes |
| AlarmClock | `setAlarmClock` | Yes |
| Inexact | `setAndAllowWhileIdle` (the planned fallback, §8.2) | No |
| Window | `setWindow`, 10-minute window | No |

The short battery fires at +2, +5, +10 and +20 minutes; the long one at
+30 minutes and +1, +2, +4 and +8 hours. Each alarm posts a
notification titled `S5 <kind> #<id>` with its delay. The scenario
chosen in the *S5 scenario* list is written with the alarms, so pick it
**before** scheduling.

1. Tap **S5 request notification permission** (Android 13+) and allow.
2. Tap **S5 open exact alarm settings** and turn exact alarms **on**:
   on Android 14+ they are off by default for a new install, and every
   scenario except `exact-denied` needs them. The *Schedule …* report
   line must then say `0 denied; exact alarms allowed: True`.
3. Run each scenario on a fresh battery, then collect:

| Scenario | What to do after scheduling the short battery |
|---|---|
| `screen-off` | Lock the phone, leave it on battery for 25 minutes |
| `swiped` | Swipe the app away from the recent apps, lock the phone, wait 25 minutes |
| `force-stopped` | Settings → Apps → MedReminder Spikes → Force stop, wait 25 minutes. Android cancels the alarms of a force-stopped app: the expected result is "not fired", which documents the limit |
| `reboot` | Restart the phone, unlock it, do **not** open the app, wait 25 minutes. The boot receiver re-plans the alarms still due |
| `exact-denied` | Turn exact alarms **off** with **S5 open exact alarm settings** before scheduling, and on again afterwards: Exact and AlarmClock are refused, Inexact and Window must still fire |
| `doze-overnight` | Long battery in the evening, phone unplugged and still overnight |
| `foreground` | Keep the app open (baseline) |

4. Collect at least 25 minutes after scheduling (the last alarm is at
   +20 minutes), then **Share report** and **Clear S5 and S8 data**
   before the next scenario.

### S8 — background work cadence

1. Tap **S8 start periodic work (15 min)**, then leave the phone in
   normal use for at least 8 hours, including a night unplugged.
2. Turn battery saver on for at least 3 hours of that time.
3. Open the app, **Collect S5 and S8 results**, **Share report**.
   Leave the work running if you want a longer record; **S8 stop
   periodic work** ends it.

The worker records only its own run (no network call): time, battery
saver, Doze, standby bucket, charging, and whether it started the
process.

### Acceptance (S5, S8)

| Spike | Pass when |
|---|---|
| S5 | Exact and AlarmClock fire within 60 s in `screen-off`, `swiped`, `doze-overnight` and, after the boot re-plan, `reboot`. With exact alarms denied, the app sees it (*Exact alarms allowed* False), the exact kinds are refused, and Inexact and Window fire, with their delay recorded. `force-stopped` is recorded, not judged |
| S8 | The periodic work runs, unattended, at least every hour on average with battery saver off. The gaps with battery saver on are recorded; they set what the Phase 5 sync status can promise, not a pass or fail |

## Privacy

The report contains device model, Android version, ABI, RAM, build
settings and results. It contains no account, no device identifier,
and no passphrase. S1b reports format facts and row counts only. S3
writes one synthetic medicine to a scratch database in the app folder.

Commit only the JSON reports and the `s4-*.md` files. The build logs
(`s4-*-build.log`) contain local paths.

# Spike S6 — OneDrive app folder

Throw-away tool for spike S6 (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
§13 Phase 0, §5.8). It checks what the Microsoft Graph app folder offers
the Phase 4a sync transport. It is not part of `MedReminder.sln`, is
never shipped, and this branch is not merged: the results go to §18 of
the analysis in the Phase 4a PR.

Android is out of scope here; the Android half of S6 runs with Phase 5.

## App registration prerequisites

In Microsoft Entra admin center → App registrations → the MedReminder
registration:

1. **Supported account types**: personal Microsoft accounts (alone, or
   together with work and school accounts).
2. **Authentication** → platform *Mobile and desktop applications* →
   redirect URI `http://localhost`.
3. **Authentication** → *Allow public client flows*: Yes.
4. **API permissions**: Microsoft Graph, delegated,
   `Files.ReadWrite.AppFolder` (and `offline_access`, requested at
   sign-in). No client secret.

The app folder is created as `/Apps/<registration display name>` on the
first request; Explorer may show `Apps` localized (`App` in Italian).

## Run (Windows)

```powershell
cd spikes/S6-OneDrive
dotnet run -c Release -- --client-id <client-id>
# second run, to test the persisted token cache (C1):
dotnet run -c Release -- --client-id <client-id> --wait-minutes 0
# optional: include a file written by the OneDrive client (C17)
dotnet run -c Release -- --client-id <client-id> --write-local
```

Options: `--tenant` (default `consumers`), `--wait-minutes` (default 5),
`--large-mib` (default 6), `--burst` (default 20), `--keep`.

The tool writes only to `/Apps/<name>/s6-<utc>/` in OneDrive (deleted at
the end unless `--keep`) and to `%LOCALAPPDATA%\MedReminder\spike-s6\`
(DPAPI-protected token cache, `report-*.md`). With `--write-local` it
also writes one probe file into the local copy of that run folder. It
never prints tokens, the account name or the drive id. Delete
`%LOCALAPPDATA%\MedReminder\spike-s6\` when done.

## Checks

| Id | Question | Decides in 4a |
|---|---|---|
| C0 | Interactive sign-in through the system browser and `http://localhost` works | MSAL setup |
| C1, C2 | The DPAPI-protected cache gives tokens silently after a restart | token store (`onedrive.protected`) |
| C3 | `special/approot` resolves; folder name and drive type | root of the transport |
| C4 | The drive root is not readable with `Files.ReadWrite.AppFolder` | scope isolation claim |
| C5 | A PUT to a deep path creates the missing folders | no explicit folder creation |
| C6 | `conflictBehavior=fail` returns 409 on an existing name | `CreateAsync` single-writer rule |
| C7 | `conflictBehavior=replace` overwrites | `WriteAsync` (device record) |
| C8, C9 | Read back; a missing file is 404 | `ReadAsync` returns null |
| C10, C10b | Upload session for files above 4 MiB; a partial upload is not listed | checkpoints and genesis images |
| C11 | Create-only through an upload session: refused at start or at commit | large-file `CreateAsync` |
| C12 | Latency of small creates; throttling | publish batching |
| C13 | Delete, then delete again (404) | `DeleteAsync` no-op |
| C14 | Recursive listing cost | `ListAsync` |
| C15 | `delta` on the app folder works and reports a new file | change cursor (§5.8) |
| C16 | The Windows OneDrive client downloads `/Apps/<name>` | mixing the folder and API transports |
| C17 | A file written in the local OneDrive folder becomes visible through Graph | same |

Paste the generated `report-*.md` into the PR or the chat.

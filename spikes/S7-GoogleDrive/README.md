# Spike S7 — Google Drive

Throw-away tool for spike S7 (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
§13 Phase 0, §5.8). It checks what the Drive REST API v3 offers the
Phase 4b transport, with the OAuth flow the app would use (system
browser, loopback redirect on `127.0.0.1`, PKCE). Not part of
`MedReminder.sln`, never shipped, branch not merged: results go to §18
in the Phase 4b PR.

The key question is cross-client visibility: the desktop and the
Android app are two OAuth clients of one Cloud project. Two **Desktop**
clients of the same project stand in for them here.

## Google Cloud prerequisites

1. One Cloud project with the **Google Drive API** enabled.
2. OAuth consent screen (Google Auth Platform → Branding / Audience /
   Data access): user type External; scopes `.../auth/drive.file` and
   `.../auth/drive.appdata`; your account as a test user while the app
   is in Testing.
3. Two OAuth clients of type **Desktop app** in that project (A and B).
   A Desktop client's secret is not confidential for Google (installed
   apps), but keep it out of the repository: pass it on the command
   line only.

## Run (Windows)

```powershell
cd spikes/S7-GoogleDrive
dotnet run -c Release -- --client-a-id <A id> --client-a-secret <A secret> --client-b-id <B id> --client-b-secret <B secret>
# second run: refresh from the cached tokens, no browser (C1A, C1B)
dotnet run -c Release -- --client-a-id <A id> --client-a-secret <A secret> --client-b-id <B id> --client-b-secret <B secret>
```

Sign in with the **same Google account** for A and B. Options:
`--large-mib` (default 6), `--burst` (default 20), `--keep`.

The tool writes a `MedReminder-S7-<utc>` folder in My Drive and one
file in the app data folder, both deleted at the end unless `--keep`,
and, under `%LOCALAPPDATA%\MedReminder\spike-s7\`, the DPAPI-protected
refresh tokens and `report-*.md`. It never prints tokens, secrets, the
account e-mail or file ids. Delete that folder when done, and remove
the app's access at myaccount.google.com → Security → Third-party
access if you want.

## Checks

| Id | Question | Decides in 4b |
|---|---|---|
| C0, C1 | Loopback + PKCE sign-in works; the refresh token survives a restart; `refresh_token_expires_in` present (7 days while the consent screen is in Testing) | token store, publishing status |
| C2 | Folder creation cost (Drive has ids, not paths) | path-to-id cache |
| C3, C5, C7 | Create, read, replace content, delete twice | transport basics |
| C4, C4b | Duplicate names are allowed; a create with a pre-generated id conflicts on retry | how create-only is emulated |
| C6, C6b | Resumable upload for large files; a partial upload is not listed | checkpoints and genesis |
| C8 | `changes` feed reports new files with parents | change cursor |
| C9, C10 | Latency, throttling, listing cost | batching |
| C11 | `appDataFolder` create and list | hidden-folder option |
| C12–C14 | `drive.file`: client B finds, reads and writes into client A's files | whether `drive.file` works for desktop + phone |
| C15 | `appDataFolder`: client B sees client A's file | whether `appDataFolder` works for desktop + phone |

Paste the generated `report-*.md` into the chat.

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
| C16, C18 | One query finds every file of a group by a public `properties` tag, from either client; `appProperties` compared | listing without walking folders |
| C17 | Client B's `changes` feed reports a file client A created, and after how long | change cursor across devices |

Paste the generated `report-*.md` into the chat.

## Round 1 (2026-09-28)

Report in `results/round1-*.md`. Client B (a second Desktop client of
the same project) finds, lists, reads and writes into the files client
A created with `drive.file`, and sees A's `appDataFolder` file (C12–C15).
Drive allows duplicate names (C4); a create with a pre-generated id
returns 409 on a retry (C4b); a partial resumable upload is not listed
(C6b); the refresh token expires in 7 days while the consent screen is
in Testing (C0). Open: C8, the changes feed did not report a file
created ~0.5 s earlier; round 2 polls up to 90 s, adds the
cross-client feed (C17) and a one-query listing by property (C16, C18).

## Round 2 (2026-09-28) and conclusion

Report in `results/round2-*.md`. Cached refresh tokens worked for both
clients (C1A, C1B).

- C8, C17: the `changes` feed reports a new file after 4–7 s, also to
  the other client. The feed lags: the transport remembers its own
  writes until the feed reports them, as for OneDrive.
- C16, C18: one query by a public `properties` tag finds a group's
  files from either client (~0.4 s); `appProperties` were also visible
  to the other client of the same project. Drive queries on properties
  are equality only, so the tag is the group and the path is filtered
  on the client.
- Create-only: duplicates are allowed by Drive (C4); retries are made
  safe with pre-generated ids (C4b); two writers of the same name (only
  `group.json` or a genesis in this layout) need a deterministic winner.
- The refresh token lasts 7 days while the consent screen is in Testing:
  publish the app before release.

S7 (Windows, two Desktop clients) passes with the mitigations above.
The check with a real Android client runs with Phase 5.

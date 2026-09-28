# Sync — Manual Two-PC Checklist

Exit check of B.1 Phase 3, Phase 4a, Phase 4b and Phase 4c (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
§13): two Windows PCs of the same user keep one profile in sync through
a shared folder, then through OneDrive and Google Drive. Run it on a release build before a release that ships
or changes sync. Record the date, the build and the result of each step.

## Setup

- PC A and PC B, each with MedReminder installed and an administrator
  profile. Use test data, not a real therapy.
- A folder synchronized between the two PCs (OneDrive, Google Drive or
  Dropbox desktop client) or a network share, the same path on both.
- Clocks within a few minutes of each other.

## Steps

| # | Step | Expected |
|---|---|---|
| 1 | A: add two medicines with stock, slots and a note. Tools → Sync… → Enable sync…, pick the folder, name "PC A", passphrase | Folder shows `<group>/group.json`, `key.1.wrap`, `genesis/1.mrg`, `devices/…mrd` |
| 2 | Inspect the folder: open files with a text editor, search for the medicine names and the note | Not found; file names are hexadecimal ids and numbers only |
| 3 | B: add one local medicine. Tools → Sync… → Join a group…, same folder, name "PC B", wrong passphrase | "The passphrase does not open any sync group" |
| 4 | B: join again with the right passphrase, confirm | Restart; B shows A's two medicines, not its local one; `medreminder.db.bak-*` next to B's database |
| 5 | A: add a package; B: register an intake; wait 1 minute (or Sync now on both, twice) | Same stock on both PCs |
| 6 | Both offline from the folder (pause the sync client): edit the same medicine's notes on A and on B; resume; sync both twice | Both show the later note; Conflicts tab lists the earlier one on both PCs |
| 7 | On A: Conflicts → Restore lost value; sync both | Both show the restored note; the conflict disappears on both |
| 8 | A: stock count with a different quantity; B, before syncing: add a package; sync both | Same stock on both; the count's correction accounts for the package recorded before it |
| 9 | Devices tab on both | Two devices, names "PC A" and "PC B", recent last-seen times |
| 10 | A: Settings → Import an export on the synced profile | Warning about the new generation; after restart, A syncs; B's status says the profile must be rebuilt |
| 11 | B: Tools → Sync… → Rebuild from the group… | Restart; B shows A's imported data |
| 12 | B: Disable sync… | B keeps its data; A keeps syncing; B no longer changes the folder |
| 13 | Logs (`%LOCALAPPDATA%\MedReminder\logs`) of both PCs | Sync runs logged with counters only; no medicine names, notes or passphrase |
| 14 | A second profile with the user role on A: Tools → Sync… | Enable, join and join with a pairing code are offered and work on that profile's data; once synced, Change key and passphrase… and Remove device… are offered and act only on that profile's group |

## OneDrive (Phase 4a)

Same PCs, sync disabled on both first (Disable sync…), one Microsoft
account used on both. The OneDrive content is checked at
onedrive.live.com → My files → Apps → MedReminder26.

| # | Step | Expected |
|---|---|---|
| O1 | A: Tools → Sync… → Enable sync… → OneDrive; sign in in the browser; name "PC A", passphrase | Status shows "Storage: OneDrive (<account>)"; `Apps/MedReminder26/sync/<group>/` holds `group.json`, `key.1.wrap`, `genesis/1.mrg`, `devices/…` |
| O2 | B: Join a group… → OneDrive, same account, same passphrase, confirm | Restart; B shows A's data |
| O3 | Repeat steps 5 to 9 above | Same results through OneDrive |
| O4 | A: close MedReminder, rename `%LOCALAPPDATA%\MedReminder\onedrive.protected`, start again, wait for a sync | Status asks to sign in again; "Sign in to OneDrive again" resumes the sync; changes made meanwhile reach B |
| O5 | A: Settings → Backup → cloud backup: storage OneDrive, sign in, backup passphrase set, save; next day or after the preferred time | `Apps/MedReminder26/backups/medreminder-<profile>-<timestamp>.mrz` |
| O6 | B: Settings → Backup → Restore from cloud… | Lists the OneDrive snapshots; restore works; no `restore-*.mrz` left in `%LOCALAPPDATA%\MedReminder` |
| O7 | Logs of both PCs | No token, account name, medicine name or note |

## Google Drive (Phase 4b)

A build with the Google client id and secret (`docs/PACKAGING.md`),
sync disabled on both PCs first, one Google account used on both. The
sync files are hidden (drive.google.com → Settings → Manage apps →
MedReminder shows the size of its hidden data); backups are visible in
My Drive → MedReminder → backups.

| # | Step | Expected |
|---|---|---|
| G1 | A: Tools → Sync… → Enable sync… → Google Drive; sign in in the browser; name "PC A", passphrase | Status shows "Storage: Google Drive (<account>)"; nothing new is visible in My Drive |
| G2 | B: Join a group… → Google Drive, same account, same passphrase, confirm | Restart; B shows A's data |
| G3 | Repeat steps 5 to 9 above | Same results through Google Drive (changes can take a few seconds longer to show) |
| G4 | A: close MedReminder, rename `%LOCALAPPDATA%\MedReminder\googledrive.protected`, start again, wait for a sync | Status asks to sign in again; "Sign in to Google Drive again" resumes the sync; changes made meanwhile reach B |
| G5 | A: Settings → Backup → cloud backup: storage Google Drive, sign in, backup passphrase set, save; after the preferred time | My Drive → MedReminder → backups holds `medreminder-<profile>-<timestamp>.mrz` |
| G6 | B: Settings → Backup → Restore from cloud folder… | Lists the Google Drive snapshots; restore works; no `restore-*.mrz` left in `%LOCALAPPDATA%\MedReminder` |
| G7 | Logs of both PCs | No token, client secret, account e-mail, medicine name or note |

## Pairing codes, key change and device removal (Phase 4c)

Three PCs (A, B, C) in one group through any storage above; C plays the
lost device. Run it once with a folder and once with a cloud account.

| # | Step | Expected |
|---|---|---|
| K1 | A: Tools → Sync… → Pair a device… | QR code, the same code as text, a countdown from 10:00 and the secrecy note; the storage holds `<group>/pairing/<A's device id>.mrp` |
| K2 | A: take a screenshot (Win+Shift+S) of the pairing window | The window is black or missing in the capture |
| K3 | D (a fourth profile or PC, sync off): Join with a pairing code…, paste the code, same storage | Restart; D shows the group's data without typing the passphrase |
| K4 | A: close the pairing window; D2: Join with a pairing code… with the same code | "The pairing code has expired or its window was closed"; the `.mrp` file is gone |
| K5 | B: record a package, do not sync. A: Devices tab → select C → Remove device…; confirm, new passphrase twice | Confirmation names C and asks to end its account sessions; status shows generation + 1; `key.2.wrap` and a new `genesis/<n>.mrg` exist |
| K6 | B: Sync now | Status says the group key was changed; nothing is sent (the log shows "needs the new key"); Enter the new key… is offered |
| K7 | B: Enter the new key… → Enter the new passphrase, type the old passphrase | "The passphrase does not open the new group key"; B keeps syncing state unchanged |
| K8 | B: Enter the new key… → Enter the new passphrase, the new one | Message with the count of kept changes; restart; after a sync, A shows B's package from K5 |
| K9 | C: Sync now; then Enter the new key… with the old passphrase | New-key status; the old passphrase is refused; C cannot read anything A or B write from now on |
| K10 | A: Change key and passphrase… (no device removed). B: Enter the new key… → Use a pairing code, with the code of A's Pair a device… | B takes the new key and rebuilds without the passphrase; its changes are kept |
| K11 | Logs of all PCs | No pairing code, passphrase, key, medicine name or note |

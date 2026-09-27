# Sync — Manual Two-PC Checklist

Exit check of B.1 Phase 3 (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
§13): two Windows PCs of the same user keep one profile in sync through
a shared folder. Run it on a release build before a release that ships
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

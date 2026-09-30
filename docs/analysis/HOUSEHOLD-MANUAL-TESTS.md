# Household and master device: manual tests (H1–H5)

Manual tests of the household feature on `feature/master-slave`
(`docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md`), to run before the
final merge into `main` (D-16). The automated tests cover the formats,
the merge rules and the use cases on Linux and Windows; these tests
cover what they cannot: the WinForms windows, DPAPI, the real storages
(folder, OneDrive, Google Drive), MailKit and several real PCs.

Status on 2026-09-29: H1–H5 written.

## Setup

- Three Windows PCs (or Windows accounts on separate machines), called
  PC1, PC2, PC3, each with a Release build of `feature/master-slave`.
- One shared storage: a shared folder first, then repeat the tests
  marked **[cloud]** with OneDrive and with Google Drive.
- An SMTP account for tests, and two mailboxes (profile recipients).
- A clean start is `%LOCALAPPDATA%\MedReminder\` renamed or removed.
- After every test, check `%LOCALAPPDATA%\MedReminder\logs\*.log` on
  each PC: no passphrase, PIN, SMTP password, pairing code, key, email
  body or medical note may appear (CLAUDE.md §7). Ids, counters and
  setting names are expected.

Notation: **Expected** is the pass condition. A test that fails is
reported with the PC, the step, the log lines around it and a
screenshot.

## H1 — Email deduplication in a profile group

1. PC1: one profile, SMTP configured, a medicine with the Email channel
   and a stock below its threshold. Enable sync (Tools → Sync…). PC2
   joins the group with the same SMTP settings.
2. Let the low-stock check run on PC1 (Tools → Check now).
3. Sync both PCs, then run Check now on PC2.

**Expected**: one email only for the stock epoch; PC2 shows the toast
but sends no email. A new stock entry below the threshold later (new
epoch) sends one new email.

## H2 — Household state on one device

### H2a — Profile administration

1. PC1, admin profile: Tools → Manage profiles… Create a user profile,
   change its role to admin and back, set and clear its PIN, delete it.
2. Try to demote the last admin and to change the role of the open
   profile.

**Expected**: every change works as before; the last admin and the
open profile cannot be changed (message shown). Restart: nothing is
lost, no error in the log.

### H2b — Installation settings

1. PC1, admin: change SMTP settings and password, cloud backup policy,
   reference country (Tools → Settings).
2. Open a user profile: the reference country is read-only.

**Expected**: values saved as before; the reference country is
admin-only; the SMTP password never appears in a plain file
(`smtp.settings.json`, `household\household.db`).

## H3 — Installation shared between devices

### H3a/H3b — Publish, replicate, keys

1. PC1 (admin, two profiles, one synced): Tools → Installation… →
   Publish the installation…, on the storage that holds the profile
   group, with an installation passphrase. **[cloud]**
2. Check the storage: `household.json`, `key.1.wrap`,
   `recovery.1.wrap`, `genesis\1.mrg`, `devices\…`.

**Expected**: published; PC1 is the master (status line). No readable
data on the storage besides `household.json`.

### H3c/H3d-1 — Add a device with a code

1. PC1: Devices → Add a device…, select the synced profile. A QR and a
   text code appear with a countdown.
2. PC2 (already set up with a local profile): Tools → Installation… →
   Join an existing installation… → With a code; enter the code.
3. Close the code window on PC1 early in a second attempt, then try the
   code on PC3.

**Expected**: PC2 shows "added to this device" for the selected
profile and restarts; the profile appears in the picker with its PIN;
PC2's local profile is added to the installation at the start; SMTP,
backup policy and reference country are PC1's. The closed or expired
code is refused ("expired"); a newer code refuses the older one. With
the profile open on PC2, after Sync now, Settings → Notifications
shows PC1's recipients. For a profile synced since before v2.9.0 (P8)
the recipients reach PC2 only after one save on PC1, even without
changes, followed by a sync of both PCs. The
code window opens with the code hidden until "Show the code"; it is
visible and usable through a remote-control tool (RustDesk, Remote
Desktop).

### H3c/H3d-1 — Join with the passphrase

1. PC3: Join an existing installation… → With the installation
   passphrase; choose the storage, type the passphrase.
2. In the approval window pick an admin profile, type a wrong PIN, then
   the right one; select the profiles.

**Expected**: the wrong PIN is refused, the right one accepted; the
selected synced profiles are added; a profile never synced cannot be
selected. A wrong passphrase: "does not open any installation".

### H3d-2 — First-run join

1. PC3 with a clean `%LOCALAPPDATA%\MedReminder\`: start, choose "Join
   an existing installation…" in the welcome window, join with a code.
2. Repeat, cancelling the join window.

**Expected**: after the join the start goes on with the profiles
received (picker, PIN). After a cancel the welcome window comes back;
`%LOCALAPPDATA%\MedReminder\setup\` does not remain.

### H3d-1 — Permissions and background sync

1. On PC2 open a user profile.
2. On PC1 change a profile's name and the reference country; wait 15
   minutes or use Sync now on PC2.

**Expected**: a user profile sees neither Tools → Sync… nor Tools →
Installation…. PC2 receives the changes.

### H3c — Two installations claiming one group

1. PC1 and PC2 each with its own installation (never joined), both
   syncing the same profile group; publish both.

**Expected**: the installation published second shows on the profile's
Installation status "belongs to another installation".

## H4 — Master device

### H4a — Only the master sends

1. PC1 master, PC2 joined, both holding a profile with a low-stock
   medicine (Email + Windows channels).
2. Run Check now on both.

**Expected**: PC1 sends the email, PC2 shows the toast only. Cloud
backup runs on PC1 only (log: "left to the master device" on PC2).

### H4a/H4b — Planned handover

1. PC1: Devices → select PC2 → Make master…
2. PC2 (admin profile open): the prompt "elected master" appears; open
   the handover window. Test the email connection, sign in to the cloud
   backup storage, type the cloud backup passphrase, confirm.
3. Sync both.

**Expected**: between election and activation neither PC sends email
(status line says so). After PC1 releases and PC2 confirms, PC2 is the
master and sends; PC1 does not. The profiles PC2 lacked are downloaded
(they appear at the next start). "Later" leaves the button "Complete
the handover…" in the Installation window.

### H4a — Lease

1. Disconnect PC2 (master) from the storage for more than 24 hours
   (or change its clock in a test environment).

**Expected**: PC2 stops sending email ("has not synced for more than 24
hours"). On PC1, Make master… on PC1 is a takeover; after 25 hours
without PC2, PC1 takes over after its handover confirmation.

### H4c — All profiles on the master, mail client elsewhere

1. PC1 master with two profiles A and B, B not open; B has a low-stock
   medicine with the Email channel and its own recipient.
2. Wait up to 15 minutes (log: "Master: profile … checked").
3. On PC2 (not master): Prescription request on a medicine; Settings →
   Email → Test.

**Expected**: B's email reaches B's recipient; no toast for B on PC1.
On PC2 only "Open in mail client" is offered; the SMTP test explains
that it runs on the master; the SMTP settings stay editable.

## H5 — Device removal

### H5a — Removal, new key with passphrase or code

1. PC1 (master), PC2, PC3 in the installation. Make a change on PC2
   (reference country) without syncing.
2. PC1: Devices → select PC3 → Remove device…, choose a new
   passphrase. Answer Yes to "Show a code now".
3. PC2: the Installation window says the key was changed; Enter the new
   key… → With a code (PC1's code). Repeat on another run with the new
   passphrase.
4. PC3: Enter the new key… with the new passphrase.
5. Try to join a new device with the old passphrase.

**Expected**: PC2 stops publishing until it has the new key, then syncs
again and PC2's change reaches PC1. PC3 is refused ("removed from the
installation") and keeps its data. The old passphrase opens nothing.
The storage has `key.2.wrap`, `recovery.2.wrap`, `genesis\2.mrg`.

### H5a — Removing the master

1. PC2 master; on PC1 remove PC2.

**Expected**: PC1 becomes the master at once and sends email; PC3,
after entering the new key, sees PC1 as master.

### H5b — Profile keys after a removal

1. PC1 (master) holds profiles A and B; PC2 holds A; PC3 holds A and B.
   Remove PC3 from PC2.
2. Remove PC3 from PC1.
3. On PC2, enter the new installation key. Keep MedReminder open with
   profile A, then restart it.
4. On PC1, wait up to 15 minutes with profile A open (profile B not
   open).
5. On a new PC4, join with the new installation passphrase and recover
   profile B through the approval window.
6. On PC3, record a change in profile A and sync.

**Expected**:

- step 1: PC2 refuses the removal and names profile B;
- step 2: the storage of each profile group has `key.<v+1>.wrap` and
  a new generation;
- step 3: PC2 offers to restart ("this profile has a new key"); after
  the restart profile A syncs again without typing anything, and its
  changes made meanwhile are kept;
- step 4: PC1 takes the new key of profile B in the background (log:
  "took key version … from the household");
- step 5: PC4 receives profile B;
- step 6: PC3's change never reaches the others; PC3 can still read what
  it had.

## Cross-cutting checks

- Languages: open every new window in each of the five languages; no
  missing key (a key name shown instead of a text).
- High DPI (150 %, 200 %): the Installation, handover and join windows
  are not clipped.
- Restart after each test: no error at start, no second tray icon.

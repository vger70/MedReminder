# MedReminder — Quick Guide

Operational guide for the end user. The
[`ANALYSIS.md`](ANALYSIS.md) file describes the technical
architecture instead.

> **MedReminder is an organizational reminder, not a medical
> device.** It does not provide diagnoses, therapy instructions,
> therapy changes or clinical suggestions. Every therapy decision
> must be taken with your doctor.

---

## First start

1. Launch `MedReminder.exe`.
2. On the very first launch the app shows a **welcome wizard** and
   asks you to create the first profile. This profile is always the
   **administrator**: it can manage the shared email server and the
   automatic backup, and it can create the other profiles (see
   *Multiple profiles*). You may set an optional PIN in the same
   wizard.
3. The database is created automatically under
   `%LOCALAPPDATA%\MedReminder\profiles\<profile-id>\medreminder.db`.
4. At the top you find the toolbar; at the bottom the status bar
   shows the active profile ("Profile: Owner (admin)" for an admin,
   "Profile: Grandma" for a regular user). The icon in the Windows
   notification area stays visible while the app is running.

### Windows SmartScreen on first launch

The published binaries are not code-signed. On the very first launch
of `MedReminder.exe`, Windows shows a blue "Windows protected your
PC" dialog. To proceed:

1. Click **More info**.
2. Click **Run anyway**.

Windows remembers the choice for that specific file: subsequent
launches do not prompt again. If you install via the MSI, the UAC
dialog reports "Unknown Publisher" for the same reason and is
expected.

## Add a medicine

1. Toolbar → **New medicine**.
2. Fill the required fields (marked with `*`): Name, Unit, Dose
   per administration, Administrations per day, Start date,
   Warning threshold (days remaining).
3. Optional fields: Active ingredient, Package, Therapy end date,
   Reference doctor, Notes.
4. **Initial stock quantity**: set the tablets/ml/doses you already
   own at the time of registration. An `InitialLoad` movement
   is created.
5. **Notification channels**: check Windows and/or Email. You must
   have configured SMTP settings (see below) for the email to work.
6. **Schedule**: leave on **Simple** for a fixed dose taken every
   day — this is the default and matches how the app has always
   worked. See *Complex regimens* below for cyclic, tapering,
   weekly or as-needed therapies.
7. **Save**.

## Complex regimens

Not every therapy consumes the same amount of medicine every day.
On the **New medicine** form the *Schedule* selector switches from
**Simple** (a fixed daily dose) to **Advanced** and reveals a
*Regime type* dropdown with four additional shapes:

- **Weekly pattern** — a per-day quantity for each day of the week
  (for example an oral anticoagulant taken at different doses on
  Mon / Wed / Fri than on the other days).
- **Cyclic (N on / M off)** — a fixed quantity for the first `N`
  days of the cycle followed by `M` days off. Typical of hormonal
  therapies and cortisone pulses.
- **Tapering** — a step-down (or step-up) dose. Two shapes are
  available through the *Linear / Stepped* selector inside the
  Tapering panel:
  - **Linear** — the dose changes by a fixed amount every fixed
    number of days until the end dose is reached, then holds.
    Typical of a simple glucocorticoid down-titration.
  - **Stepped** — an explicit list of stages, each with its own
    dose and its own duration in days (for example 4/day for 7
    days, then 2/day for 7 days, then 1/day for 14 days). Use
    *Add stage* / *Remove* to shape the sequence, and read the
    live preview below the list to check the totals before saving.
    Tick *Keep the last dose as maintenance* when the final dose
    should continue indefinitely instead of ending the course.
- **As needed (PRN)** — no scheduled consumption. MedReminder keeps
  tracking the stock but the *days remaining* column stays empty
  until the schedule shape changes.

When Advanced is selected the *Dose per administration*,
*Administrations / day* and *Administration slots* fields at the
top of the form become inactive: the schedule you configure below
is the sole source used for the daily quantity. To go back to the
one-click fixed-daily flow, switch the selector back to Simple.

To change the shape of an existing therapy mid-course, use
*Toolbar → Change schedule*. The same Simple / Advanced selector
is available there and takes effect from the *Effective from* date
you pick, so the previous schedule stays valid for the days before
that date.

MedReminder is not a medical device: it does not check maximum
daily doses, does not warn about overdoses and does not verify
drug-drug interactions. It only follows the therapy your doctor
prescribed and reminds you before the stock runs out.

## Dose-time reminder

For medicines that have **timed dose slots** (a specific time of
day set on each administration slot), you can ask MedReminder to
remind you *at the moment the dose is due*. Tick **Remind me at
dose time** on the New medicine or Edit medicine form. The option
is only available when the medicine has at least one slot with a
time and still has stock on hand; it stays greyed out otherwise.

When enabled, at each slot's time MedReminder shows a desktop
notification ("Time to take …"). If you have configured email
notifications and selected the email channel for that medicine, the
same reminder is also sent by email.

A few details worth knowing:

- **One reminder per slot per day.** Each timed slot fires at most
  once on a given calendar day, even if the app is restarted.
- **Grace window.** If the app is not running exactly at the slot
  time — for example the computer was asleep — the reminder still
  fires when the app next checks, as long as it is within 30 minutes
  of the slot time. Past that window the dose is treated as missed
  and no reminder is shown; MedReminder does not keep a missed-dose
  log and never gives clinical advice.
- **Zero stock turns it off.** When the stock reaches zero no dose
  reminder is sent, because there is nothing left to take.
- **Daylight saving time.** On the spring-forward night a slot that
  falls inside the skipped hour does not fire (that wall-clock time
  does not exist). On the fall-back night the slot fires once, as
  usual.

This reminder is a convenience prompt only. It does not record
whether you took the dose and does not change your stock — use
*Register intake* for that.

## Reference catalogue (multi-country)

MedReminder ships with two snapshots of a reference medicine
catalogue and uses them to autocomplete the medicine form.

- On the **Commercial name** and **Active ingredient** fields, start
  typing to see matches. Picking a row fills in the other side (and
  the technical fields — national code and ATC — behind the scenes)
  so you don't have to type both.
- The dropdown shows at most 20 rows and updates about 150 ms after
  you stop typing. A red circle next to a row means the product is
  **suspended or withdrawn** — you can still pick it, MedReminder
  only surfaces the status.
- **Medicine not in the catalogue?** Just keep typing what you know.
  If you never pick a row from the dropdown, MedReminder saves your
  text as-is and no reference linkage is stored — the reminder works
  exactly as before.
- The **reference country** is picked from *Settings → General →
  Reference country*. Default is Italy; a change takes effect at the
  next opening of the medicine form.

### EU centrally authorised medicines

Some medicines are authorised across the whole European Union under
the *centralised procedure*, run by the European Medicines Agency
(EMA). MedReminder embeds the EMA EPAR catalogue — *European public
assessment reports* — and shows those medicines in the same
autocomplete dropdown.

- If your **reference country is an EU member** (e.g. the default
  Italy, or any other EU country you pick from Settings), the
  autocomplete shows **your national catalogue + the EU-wide
  centrally authorised medicines**, mixed in the same list. You do
  not have to switch anything: EU rows just appear when they match.
- If you set the **reference country to `EU`**, the autocomplete
  shows **only** the EU-centralised medicines — no national rows.
  Useful if you specifically want to browse or link a product to
  its EMA authorisation.
- An EU medicine and an equivalent national product may both appear
  in the list at the same time; neither is deduplicated against the
  other. Pick whichever matches the box in your hand.

### Spanish and French national catalogues

The Spanish catalogue comes from AEMPS CIMA ("Medicamentos"
register) and the French one from ANSM BDPM (*Base de données
publique des médicaments*). They behave exactly like the Italian
catalogue in the autocomplete:

- Set **Settings → General → Reference country** to `ES` or `FR`
  once the corresponding snapshot is loaded (`ES` and `FR` appear
  automatically in the dropdown as soon as their catalogues are
  in the DB).
- The autocomplete then lists **your national catalogue + the
  EU-wide centrally authorised medicines**, mixed in the same list.
  Spain and France are EU members, so EU rows are included by
  default the same way they are for Italy.
- All the other rules stay the same: pick a row to fill both
  sides, or keep typing to store a free-text entry the app has
  never seen.

**Data sources and terms.** The Italian catalogue comes from
AIFA (Agenzia Italiana del Farmaco) open data, released under the
Creative Commons Attribution 4.0 International licence (CC BY 4.0).
The EU catalogue comes from the EMA EPAR dataset, reused under
EMA's legal notice (Commission decision 2011/833/EU on the reuse
of Commission documents). The Spanish catalogue comes from AEMPS
CIMA, reused under Spain's public-sector information reuse regime
(Law 37/2007). The French catalogue comes from ANSM BDPM, reused
under Licence Ouverte Etalab 2.0. The About dialog and
`THIRD-PARTY-NOTICES.md` at the root of the installation carry the
full attributions.

## Add stock (new package)

1. Select the medicine in the grid.
2. Toolbar → **Add stock**.
3. Choose the movement type:
   - **New package**: the normal case after a purchase.
   - **Manual addition**: e.g. if you receive samples from the doctor.
   - **Positive correction**: you had counted less than the actual amount.
4. Enter the quantity (in the medicine's unit) and confirm.

**Effect**: stock increases and the medicine's `StockEpoch` advances
by 1. This restarts the warning cycle — the next notification can
be emitted when stock falls below the threshold again.

## Correct a negative quantity

If you notice that actual stock is less than the calculated one
(lost tablet, spilled, etc.):

1. Select the medicine.
2. Toolbar → **Correct stock**.
3. The default kind is **Negative correction**: the typed quantity
   is subtracted from stock. It does not advance the epoch: it
   does not reprogram the notification cycle.

If the correction would bring stock below zero, the operation is
blocked with an error.

## Edit or deactivate a medicine

- **Edit**: double click on the row or toolbar → **Edit**.
  You can change name, active ingredient, package, unit, threshold,
  doctor, notes, end date, notification channels, and Active/Inactive
  state.
  **Dose and frequency are NOT changed from here**: use
  *Toolbar → Change schedule* (see *Complex regimens* above).
- **Deactivate**: toolbar → **Deactivate**. The medicine disappears
  from automatic checks and alerts, but historical data (movements,
  notifications) stays in the DB for audit.

## Multiple profiles and admin/user roles

MedReminder can manage medicines for **more than one person** from
the same Windows account — typical case: a parent taking care of
their own therapy and of one or two family members. Each profile
has its own database and its own email recipient; the SMTP server,
the automatic backup folder and the profile registry are shared and
administered by an **administrator profile**.

### Roles

- **Administrator** — manages the global settings (Email SMTP,
  Backup, list of profiles, PIN of any profile) in addition to
  their own data. There must always be at least one administrator.
- **User** — manages only their own profile (medicines, stock,
  therapies, personal email recipient). Does not see the Email SMTP
  tab or the Backup tab in Settings, and does not see
  `Tools → Manage profiles…`.

The role is chosen when the profile is created and **cannot be
changed later**. If in the future you need to change a profile's
role, the current workaround is to create a new profile with the
target role and copy the data over.

The role is soft security: a user with filesystem access can
edit `profiles.json` by hand and become admin. The user interface
honors the role, the filesystem does not.

### Create additional profiles (administrator)

1. `Tools → Manage profiles…` — this entry is only present for
   administrators.
2. **New profile** → enter a name, choose Administrator or User
   (default: User), optionally set a PIN. Confirm.
3. The new profile immediately appears in the picker at the next
   launch.

### Switch profile

`File → Change profile…` opens the picker. Choose the target
profile and confirm: the app restarts automatically so the new
profile is fully isolated. If the chosen profile has a PIN, the
prompt appears before the app opens.

### Rename, change PIN, delete

`Tools → Manage profiles…` (administrator only) also offers:

- **Rename** — the display name only. The internal id never
  changes.
- **Change PIN** — set, rotate or clear the PIN on any profile.
- **Delete** — asks you to **type the profile name** to confirm.
  A separate checkbox lets you also delete the profile's data
  on disk; it defaults to OFF, so the folder stays available for
  manual recovery.

The active profile cannot be deleted (switch first), and the last
remaining administrator cannot be deleted either.

### About the PIN

The PIN is **friction, not security**. It blocks accidental
switches into the wrong profile, but it does **not** encrypt the
data — anyone with access to this PC can still open the profile's
files. Three wrong attempts close the prompt and the app.

If you forget a PIN, remove it manually from
`%LOCALAPPDATA%\MedReminder\profiles.json` (delete the `PinHash`,
`PinSalt` and set `PinIterations` to `0` for the affected entry).
This is documented rather than fixed with a "reset PIN" flow on
purpose: recovery is not a bug, because the PIN is not security.

### On-disk layout

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                        ← profile registry
├── smtp.settings.json                   ← shared SMTP (admin)
├── smtp.protected                       ← DPAPI-encrypted password
├── backup.settings.json                 ← shared backup config (admin)
├── backup.state.json                    ← last automatic-backup state
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    ├── <profile-id>\                    ← one folder per profile
    │   ├── medreminder.db (+ -wal, -shm)
    │   └── notifications.settings.json  ← this profile's ToAddress
    └── …
```

### Automatic backup covers every profile

When automatic backup is enabled, each daily tick backs up **every**
profile's database into the shared folder, with filenames of the
form `medreminder-<profile-id>-YYYYMMDD-HHmmss.db`. Retention is
applied per-profile so the most recent backup of one profile does
not shield the older backups of another.

When restoring from `Settings → Backup → Restore backup…`, the
dialog asks which profile should receive the imported database.
By default it picks the profile the filename refers to. If you
restore into a profile other than the active one, the app does not
restart; if you restore into the active profile, the app restarts
so the new database can be opened cleanly.

### Auto-start with Windows

The Windows auto-start entry is unique per Windows user. On login,
the app opens the **last used** profile without showing the picker;
if that profile has a PIN, the prompt is raised over the empty
window. To open a different profile at auto-start, use
`File → Change profile…` once the app is up.

### Upgrading from a single-user install

If you already have a `medreminder.db` file at
`%LOCALAPPDATA%\MedReminder\` from an older version, the app runs
a one-shot **V1 → V2 migration** on next launch:

1. It takes a mandatory backup at
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   containing the original `medreminder.db` (and its side files)
   and the original `smtp.settings.json`.
2. It moves the database into `profiles\default\medreminder.db`
   and creates the initial `profiles.json` with a single
   administrator profile called `User`.
3. It extracts the recipient (`Smtp.ToAddress`) from
   `smtp.settings.json` into
   `profiles\default\notifications.settings.json`.

The migration is **atomic** — if any step fails after the pre-
backup, the app rolls back to the V1 state and preserves the
pre-migration backup.

The **pre-migration backup is not cleaned up automatically** —
after you have verified that the migrated app opens the same data,
you can delete the `backups\pre-migration-*` folder manually.
Rename the profile from `User` to something you prefer in
`Tools → Manage profiles… → Rename`.

## Configure email sending

**Settings → Email SMTP**:

- **Host**: e.g. `smtp.gmail.com`, `smtp-mail.outlook.com`, etc.
- **Port**: usually 587 (StartTLS) or 465 (direct SSL/TLS).
  MedReminder uses StartTLS when the relevant checkbox is on.
- **Username / New password**: if the server requires authentication.
  The password is encrypted with DPAPI and stored in
  `%LOCALAPPDATA%\MedReminder\smtp.protected`. It does not end up
  in `smtp.settings.json` nor in the logs.
- **Remove stored password**: deletes `smtp.protected` at the next
  Save.
- **Sender / Sender name**: the "from" of the sent emails.
- **Recipient**: where to receive alerts (usually your own
  personal address).
- **Timeout**: seconds before considering the connection failed.
- **Test connection**: opens an SMTP session, authenticates,
  closes. Does not send a real email.
- **Save SMTP settings**: writes
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`. Configuration
  is hot-reloaded without restarting the app.

### Example: Gmail with app-password

1. Enable 2FA on your Google account.
2. Create an app-password at
   `myaccount.google.com/apppasswords`.
3. In MedReminder: Host `smtp.gmail.com`, Port `587`,
   StartTLS on, Username `youraddress@gmail.com`, Password
   the app-password you just created.

Google and other providers can change the requirements: consult
your provider's documentation if the connection test fails.

## Caregiver notifications

**Settings → Notifications → Caregiver e-mail (optional)**.

A profile can name a second recipient — for example a family
member or a caregiver who handles the reorder on your behalf.
When this field is set, every e-mail sent to the primary
recipient is also sent to the caregiver, in the **same** message.
Nothing else changes: the transport, the message content and when
the e-mails are sent are exactly the same as before.

- **To enable it**: type the caregiver's e-mail address and save.
- **To disable it**: clear the field and save. Leaving it empty
  means no caregiver is configured — the default.
- **Both addresses are visible to both recipients**: the caregiver
  and the primary recipient can see each other's address on the
  e-mail. This is intentional, so a reply reaches everyone.
- The caregiver address cannot be the same as the primary
  recipient, and must be a valid e-mail address; otherwise the
  save is rejected with a message.

The setting is per profile: one profile's caregiver is not
another profile's caregiver.

## Windows automatic startup

**Settings → Automatic startup**: check the box. An entry is
created in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
that launches MedReminder with the `--minimized` argument
(starts in tray, window hidden). No administrator privileges
required.

## Database backup

**Settings → Backup / Restore**:

- **Export**: pick a folder. The DB is copied as
  `medreminder-YYYYMMDD-HHMMSS.db`. Save the copy on an external
  drive or personal cloud if you want resilience.
- **Restore**: select a previous backup. The current DB is
  renamed to `medreminder.db.bak-<timestamp>` (not lost!) and
  replaced. **Close and reopen MedReminder** after the restore
  to avoid inconsistencies.

## Export and import

Alongside the raw database backup, MedReminder can produce a single
**encrypted, portable file** with all of your data. Unlike a plain
backup, this file is not tied to your Windows account or PC, so it is
also the recommended way to move MedReminder to a new computer.

**Settings → Backup → Export all data (encrypted)…**:

- Pick where to save the file (extension `.mrz`).
- Choose a **passphrase** (at least 12 characters) and type it twice.
- Optionally tick the shared settings you want to include: SMTP
  transport settings, the SMTP password, backup preferences, user
  preferences (language and catalogue country). All are off by default.
  If you include the SMTP password, it is re-encrypted with your
  passphrase — it is never written in clear text.
- Click **Export**.

**The passphrase cannot be recovered.** There is no reset, no backdoor
and no server copy. If you lose the passphrase, the file can never be
read again — store it somewhere safe.

**Settings → Backup → Import from export…**:

- Pick the `.mrz` file. MedReminder shows what it contains (version,
  date, scope, included settings) before doing anything.
- Type the passphrase.
- Tick **"I understand that this will overwrite the current profile's
  data."** Import replaces the current profile's data entirely — there
  is no merge. A safety copy of the current database is kept as
  `medreminder.db.bak-<timestamp>` first.
- If the file was exported from a different profile, MedReminder asks
  for confirmation: importing it replaces the active profile's data
  with that profile's data.
- Click **Import**, then **restart** MedReminder when prompted so the
  imported data is loaded cleanly.

If the passphrase is wrong, or the file is damaged, or it was produced by
a newer version of MedReminder, the import stops with a clear message and
your current data is left untouched.

The archive format is documented publicly in
[`docs/EXPORT-FORMAT.md`](EXPORT-FORMAT.md), so your data is never locked
in — it can be decrypted with standard tools if you ever need to.

## Cloud folder backup

MedReminder can also write the automatic daily backup as an **encrypted
snapshot** into a local folder that your operating system is already
synchronizing (OneDrive, iCloud Drive, Dropbox, Google Drive Desktop, …).
This is the low-cost way to move your data from a "home PC" to a
"work PC" without running a server, and it keeps a copy off the
machine in case the disk fails.

**This is not real-time sync.** MedReminder writes a snapshot at
most once a day, and only one computer at a time should be writing.
If you edit medicines on two devices between two snapshots, the two
copies diverge — and the next restore wipes whichever machine you
restore on. Decide up front which device is "active" and only
restore on the other one when you switch.

### Setup on the first device

**Settings → Backup → Backup to a cloud-synced folder (encrypted)**:

- Tick the checkbox.
- Pick a folder inside your cloud provider's local sync folder
  (for example `C:\Users\<name>\OneDrive\MedReminder`). MedReminder
  never talks to OneDrive / iCloud / Dropbox itself — it just writes
  files there, and your OS-level agent uploads them.
- Set the number of snapshots to keep (default: 30).
- Click **Set / change…** next to Backup passphrase and choose a
  passphrase (at least 12 characters). This passphrase never leaves
  the machine.
- Save.

From the next daily tick onward, MedReminder writes
`medreminder-<profileId>-<timestamp>.mrz` into the folder. The file
is encrypted with a key derived from your backup passphrase; the
cloud provider never sees your data in the clear.

One snapshot is written for **every profile** on the computer, like
the local backup, and all of them are encrypted with the same backup
passphrase. Whoever knows the passphrase can therefore read the data
of every profile, including profiles protected by a PIN.

### Setup on the second device

- Install MedReminder.
- **Settings → Backup → Set / change…** and enter the **same** backup
  passphrase you configured on the first device. This is the only
  irreducible step: without the same passphrase, the second machine
  cannot decrypt what the first one wrote.
- The daily automatic snapshot is off on the second device — you
  only need it on one machine.

### Restore on the second device

**Settings → Backup → Restore from cloud folder…**:

- Point the dialog at the local sync folder (the same one the first
  device writes into).
- Pick the most recent snapshot from the list. Each row shows the
  date, the profile name (or its id, if the profile does not exist on
  this computer), and a short "device hash" so you can tell snapshots
  from different machines apart. The device hash is a SHA-256
  fingerprint of the source machine's host name — enough to group
  snapshots by origin, not enough to identify the machine.
- The newest snapshot of the active profile is preselected. Restore
  always overwrites the **active** profile: to restore another
  profile, switch to it first. If you pick a snapshot of a different
  profile, MedReminder asks for confirmation before replacing the
  active profile's data with it.
- Tick **"I understand that this will overwrite the current profile's
  data."** — restore is Overwrite-only.
- Click **Restore**. MedReminder decrypts the snapshot, replaces
  the current profile's database, and prompts you to restart.

### Notes

- **Losing the passphrase is losing the data.** There is no reset.
  The passphrase is stored locally, encrypted with your Windows
  account credentials; it never leaves the machine and never appears
  in the cloud.
- The daily automatic snapshot does **not** include your SMTP
  password or your user preferences — for that, use the one-shot
  encrypted export above with the shared-settings tickboxes.
- MedReminder's own retention only deletes old files from the visible
  folder. Your cloud provider likely keeps deleted files in its own
  recycle bin (OneDrive: 30 days by default) — MedReminder cannot
  purge that on your behalf, and doesn't try to.
- Do **not** place the live database file into a cloud-synced folder.
  Only the encrypted `.mrz` snapshots belong there.

## Check now

The monitor runs automatically every 30 minutes (configurable in
`appsettings.json` at `Monitoring:IntervalMinutes`). If you want
to force an immediate check: toolbar → **Check now** or tray
menu → **Check now**.

## Notification area icon

- **Double click** → opens the window.
- **Context menu (right click)**:
  - Open MedReminder
  - Check now
  - Settings…
  - Exit

Closing the main window with the X minimizes to tray; the app
keeps running in background. To really exit: tray menu → **Exit**.

## Interface language

**Settings → General**: pick the language from the dropdown
(English or Italian) and click **Save language**. MedReminder
restarts automatically to apply the change.

Notes:
- Windows toast notifications always follow the system language
  (Windows), independently from the language chosen here.
- Email notifications and the therapy report use the language
  selected here.

## Support Development

If the maintainer has enabled it, the **Help → Support development…**
entry opens a small dialog where you can, entirely voluntarily,
contribute to the project. It is optional and never required to use
MedReminder.

- Pick a fixed amount (€2, €5, €10, €20) or, when offered, a **custom
  amount**.
- Pick a payment method (Stripe or PayPal).
- Click **Continue with …** — MedReminder opens the provider's official
  payment page in your default browser.

With a custom amount you choose the exact figure **on the provider's
page**, not inside MedReminder. The application never processes the
payment itself, never sees your card details, and cannot confirm that a
payment completed — it only opens the page. If the maintainer has not
configured this feature, the menu entry does not appear.

## Diagnostics

- **Logs**: `%LOCALAPPDATA%\MedReminder\logs\medreminder-YYYYMMDD.log`.
  Contains scheduler ticks, notification sends, errors.
- **Corrupted or incompatible DB**: delete `medreminder.db`,
  `medreminder.db-shm`, `medreminder.db-wal` under
  `%LOCALAPPDATA%\MedReminder\`. On next start the DB is
  recreated empty. Make a manual backup first if you have
  important data.
- **App already running**: only one instance per Windows user.
  If the launch says "already running", look for the icon in
  the notification area.

## What MedReminder does NOT do

- It does not record whether you took a dose, does not track
  adherence and does not alert on missed doses (the dose-time
  reminder is a convenience prompt only, not an adherence system).
- It does not provide therapy instructions or drug interactions.
- It does not sync between different devices.
- It does not automatically order medicines.
- It does not contact your doctor directly.

Its only purpose is to let you know in time that you need to
request a new prescription.

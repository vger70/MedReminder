# MedReminder — User guide

MedReminder tells you **in time** when a medicine is about to run out,
so you can ask your doctor for a new prescription before you are left
without it. It can also remind you at the time of each dose, keep
track of several people, and work on more than one computer.

> **MedReminder is an organizational reminder, not a medical
> device.** It does not provide diagnoses, therapy instructions,
> therapy changes or clinical suggestions. Every therapy decision
> must be taken with your doctor.

Press **F1** or open **? → User guide** to read this guide inside the
app. The technical architecture is described in `docs/ANALYSIS.md`.

---

## Contents

1. [Getting started](#start)
   - [First start](#first-start) · [The main window](#main-window) ·
     [Where to find what](#where)
2. [Medicines](#medicines)
   - [Add a medicine](#add-medicine) ·
     [Administration times](#slots) ·
     [Complex regimens](#regimens) ·
     [Dose-time reminder](#dose-reminder) ·
     [Edit, deactivate, delete](#edit-medicine)
3. [Find a medicine: catalogue and barcode](#catalogue)
4. [Stock](#stock)
   - [Add a package](#add-package) · [Register an intake](#intake) ·
     [Correct stock](#correct) · [Count stock](#count) ·
     [History](#history)
5. [Timeline, report and prescription requests](#documents)
6. [Notifications and email](#notifications)
7. [Several people: profiles and roles](#profiles)
8. [Protect your data: backup and export](#backup)
9. [Several computers](#devices)
   - [Which option do I need?](#devices-choice) ·
     [Sync a profile between PCs](#sync) ·
     [Share the installation](#installation) ·
     [The master device](#master) ·
     [Lost or replaced device](#remove-device)
10. [Settings and everyday use](#settings)
11. [Problems and answers](#faq)
12. [Where MedReminder keeps its data](#data)
13. [What MedReminder does not do](#limits)

---

<a id="start"></a>
## 1. Getting started

<a id="first-start"></a>
### First start

1. Launch `MedReminder.exe`.
2. The **Welcome to MedReminder** window opens. Choose one of:
   - **Create profile** — the normal case on your first computer. Type
     your name and, if you want, a PIN. This first profile is the
     **administrator**: it manages email, backup and the other profiles
     (see [Several people](#profiles)).
   - **Join an existing installation…** — only if MedReminder is
     already used on another computer of yours and you want this one to
     take part (see [Share the installation](#installation)).
3. The main window opens. The MedReminder icon in the Windows
   notification area (near the clock) stays visible while the app runs.

**Windows SmartScreen.** The program is not code-signed. On the very
first launch Windows may show a blue "Windows protected your PC"
window: click **More info**, then **Run anyway**. Windows remembers the
choice. If you install from the MSI package, the permission window says
"Unknown Publisher" for the same reason.

<a id="main-window"></a>
### The main window

- **Menus** at the top: **File**, **Therapy**, **Stock**, **Tools** and
  **?** (help).
- **Toolbar** below the menus: *New medicine*, *Register intake* and a
  search box on the right (**Ctrl+F**) that filters the list by name.
- **Navigation** on the left: *Medicines* (this list), then *Therapy
  timeline*, *Therapy report*, *Request prescription*, *Installation*
  (administrators) and *Settings*, which open their own windows. In a
  narrow window it shows icons only.
- **Summary** above the list: how many medicines are *Empty*, *Running
  low*, *Suspended*, and all of them. Click a box to show only those
  medicines; click it again to show all.
- **Medicine list** in the middle: one row per medicine, with the stock,
  the days remaining and the estimated run-out date. The **Status**
  column shows the state as a coloured label. Right-click a row for the
  commands on that medicine (edit, register intake, add package…);
  double-click or **F2** edits it.
- **Status bar** at the bottom: the open profile ("Profile: Anna
  (admin)" for an administrator).

Closing the window with **X** does not quit MedReminder: it keeps
running in the notification area, so reminders still arrive. To quit,
right-click the icon and choose **Exit**.

<a id="where"></a>
### Where to find what

| I want to… | Go to |
|---|---|
| Add a medicine | **Therapy → New medicine…** |
| Change dose or frequency | **Therapy → Change dose/frequency…** |
| Record a purchased package | **Stock → Add package…** or **Stock → Restock from barcode…** |
| Fix the stock to what I really have | **Stock → Count stock…** |
| Undo a mistaken entry | **Stock → History…** |
| Print the therapy for a doctor | **Therapy → Therapy report…** |
| Ask for a prescription | **Therapy → Request prescription…** |
| Set up email, language, backup | **Tools → Settings…** |
| Add a person | **Tools → Manage profiles…** (administrator) |
| Use MedReminder on another PC | **Tools → Sync…** and **Tools → Installation…** (administrator) |
| Open another person's profile | **File → Change profile…** |

---

<a id="medicines"></a>
## 2. Medicines

<a id="add-medicine"></a>
### Add a medicine

1. **Therapy → New medicine…** (or the *New medicine* button).
2. Start typing the **name**: the catalogue suggests matching medicines
   (see [Find a medicine](#catalogue)). Picking one fills in the active
   ingredient and the package. You can also click **Scan barcode…**.
3. Fill in the required fields, marked with `*`: *Unit*, *Dose per
   admin*, *Admins per day*, *Therapy start date* and *Warning threshold
   (days)*.
   - The **warning threshold** is how many days before running out you
     want to be warned. Leave enough time to get the prescription and
     buy the medicine, for example 10 days.
4. **Initial stock quantity**: how many tablets (or ml, doses…) you have
   now.
5. **Notification channels**: tick **Windows notification** and/or
   **Email**. Email works only after it is set up (see
   [Notifications and email](#notifications)).
6. Optional: *Therapy end date*, *Reference doctor*, *Notes*,
   administration times, *Remind me at dose time*.
7. **Save**.

From now on MedReminder deducts the daily dose by itself every day. You
do not need to record each tablet you take.

<a id="slots"></a>
### Administration times

In **Administration times (optional)** you can split the daily dose
into slots: **Add…** opens a window where you set the dose, an optional
time (*With specific time*) and a label such as "After breakfast" (pick
one of the *Common labels* or type your own). The slots appear in the
therapy report, and timed slots can remind you at dose time.

Without slots, the medicine uses "dose × administrations per day".

<a id="regimens"></a>
### Complex regimens

Not every therapy uses the same amount every day. In the medicine
window, set **Schedule** to **Advanced** and choose a **Regime type**:

| Regime type | Example |
|---|---|
| **Weekly pattern** | A different quantity for each day of the week, e.g. an anticoagulant with different doses on Mon, Wed, Fri. |
| **Cyclic (N on / M off)** | A quantity for N days, then M days without, e.g. 21 days on, 7 off. |
| **Tapering** — *Linear* | The dose changes by a fixed step every few days until the end dose, then holds. |
| **Tapering** — *Stepped* | A list of stages, each with its dose and duration, e.g. 4 a day for 7 days, 2 a day for 7 days, 1 a day for 14 days. Use **Add stage** / **Remove**; the total is shown below the list. Tick *Keep the last dose as maintenance* if the last dose continues indefinitely. |
| **As needed (PRN)** | No scheduled consumption: the stock is tracked, but no run-out date is estimated. |

With **Advanced**, the dose fields at the top of the window are not
used. Switch back to **Simple** for a fixed daily dose.

**The therapy changes?** Use **Therapy → Change dose/frequency…** and
pick the **Effective from** date. The old schedule stays valid for the
days before that date. If the date is in the past, the consumption
already deducted from that date on is recalculated with the new
schedule.

MedReminder does not check maximum doses, overdoses or drug
interactions: it only follows the therapy your doctor prescribed.

<a id="dose-reminder"></a>
### Dose-time reminder

Tick **Remind me at dose time** in the medicine window to get a
reminder ("Time to take …") at each timed slot. It is available when
the medicine has at least one slot with a time and some stock left.

- The reminder appears on screen; if the medicine uses the **Email**
  channel and email is set up, it is also sent by email.
- **Once per slot per day**, even if you restart the app.
- If the PC was asleep at that time, the reminder still arrives within
  **30 minutes**; later than that it is skipped.
- With **zero stock** no dose reminder is sent.
- On the night the clock moves forward, a slot inside the skipped hour
  does not fire.

The reminder does not record whether you took the dose and does not
change the stock.

<a id="edit-medicine"></a>
### Edit, deactivate, delete

- **Edit**: double-click the row, or **Therapy → Edit**. You can change
  everything except dose and frequency (use *Change dose/frequency…*).
- **Deactivate**: **Therapy → Deactivate** when you stop a therapy. The
  medicine is hidden and gets no more reminders; its history is kept.
  **Therapy → Show inactive medicines** shows it again; to reactivate
  it, open it with **Edit** and tick **Active**. The days it was
  inactive are not counted as consumption.
- **Delete**: **Therapy → Delete…** removes a medicine entered by
  mistake. It works only while nothing was recorded for it (no stock,
  not even the initial quantity, no intake, no count). Otherwise
  deactivate it. With sync on, it disappears from the other computers
  too.

---

<a id="catalogue"></a>
## 3. Find a medicine: catalogue and barcode

### The reference catalogue

MedReminder includes the official medicine lists of **Italy** (AIFA),
**Spain** (AEMPS), **France** (ANSM) and the medicines authorised for
the whole **European Union** (EMA).

- In the medicine window, type part of the **name** or of the **active
  ingredient**: up to 20 matches appear. Picking one fills in the other
  fields.
- A **red circle** next to a row means the product is suspended or
  withdrawn. You can still pick it.
- **Not in the list?** Just type the name and save: the medicine works
  the same, only without the catalogue link.
- **Which country?** **Tools → Settings… → General → Reference
  country** (default: Italy). Only an administrator can change it: it
  applies to every profile and, with a shared installation, to every
  device. With IT, ES or FR the list also contains the EU medicines;
  with **EU** it contains only those. A medicine may appear twice
  (national and EU): pick the one that matches your box.
- **Automatic updates.** When **Check for updates on startup (GitHub)**
  is on (Settings → General), MedReminder downloads at start the latest
  monthly list of your country and the EU list, if newer. Without an
  internet connection nothing changes. With several profiles, each is
  updated the first time it is opened.

**Sources.** AIFA open data (CC BY 4.0); EMA EPAR data (EMA legal
notice, Commission decision 2011/833/EU); AEMPS CIMA (Spanish Law
37/2007 on reuse of public-sector information); ANSM BDPM (Licence
Ouverte Etalab 2.0). Full attributions are in **? → About MedReminder…**
and in `THIRD-PARTY-NOTICES.md`.

### Scan the barcode

With a USB barcode scanner or a webcam you can fill in a medicine
without typing.

1. In the medicine window click **Scan barcode…**.
2. Scan the barcode of the box, or type the code printed under it and
   press **Enter**.
3. If the code is in the catalogue, the form is filled in. Otherwise
   the window shows the code read and nothing changes.

Tips:

- Click **Scan barcode…** *before* scanning, otherwise the code is typed
  into whichever field has the focus.
- On Italian boxes the code to scan is the **AIC** barcode (`A`
  followed by 9 digits). If the scanner does not recognise it, enable
  the **Code 32** (Italian Pharmacode) symbology in its settings. The
  square code (DataMatrix) needs a 2D scanner and often is not in the
  catalogue.
- Set the scanner to the same keyboard layout as Windows (e.g. AZERTY,
  QWERTZ).

**With the webcam.** Click **Use webcam** in the scan window. Hold the
box 10–20 cm away, barcode inside the frame, in good light. The camera
turns off when a code is read, when you click **Use scanner**, when you
close the window, or after 30 seconds. If Windows blocks the camera,
click **Open privacy settings**, turn on *Let desktop apps access your
camera*, then **Try again**. No image is saved or sent.

**Restock by scanning.** **Stock → Restock from barcode…**: scan the new
box and the matching medicine opens in the stock window, already set to
*New package* with the usual quantity. If no medicine has that code,
you can add a new medicine or link the code to an existing one.

---

<a id="stock"></a>
## 4. Stock

MedReminder lowers the stock by itself every day according to the
schedule. You only record what changes the stock in another way.

<a id="add-package"></a>
### Add a package

1. Select the medicine.
2. **Stock → Add package…**.
3. Choose the type:
   - **New package** — after a purchase (the usual case);
   - **Manual addition** — e.g. samples from the doctor;
   - **Positive correction** — you had counted too few.
4. Enter the quantity and confirm.

A new package restarts the warning cycle: when the stock falls below
the threshold again, you get a new warning.

<a id="intake"></a>
### Register an intake

**Therapy → Register intake…** (or the toolbar button) records a single
intake as **Taken**, **Skipped** or **Cancelled**, with the day and the
quantity. You do not need it on normal days. Use it when a day differs
from the schedule: once you register an intake for a day, that day's
automatic deduction is replaced by what you registered.

<a id="correct"></a>
### Correct stock

If you have less than the app shows (a lost tablet, a spilled bottle):
**Stock → Correct stock…**, keep the type **Negative correction** and
enter the quantity to subtract. The stock cannot go below zero.

<a id="count"></a>
### Count stock

When the count in your cabinet does not match the app, count and let
the app fix it:

1. Select the medicine, then **Stock → Count stock…**.
2. Type the **Counted quantity**. The window shows the expected stock,
   the difference and how the run-out date changes.
3. In **Already taken today**, enter what you had already taken today
   when you counted (the app suggests the doses whose time has passed).
4. Click **Record count**.

The app records one correction so that the stock equals what you
counted. The difference is only a stock figure: it is not interpreted
as missed or extra doses.

<a id="history"></a>
### History and mistaken entries

**Stock → History…** (Ctrl+H) lists packages, corrections, intakes,
counts and suspensions of the selected medicine, newest first. Select a
mistaken entry and click **Delete**: stock and consumption are
recalculated. Only entries after the latest stock count can be deleted
(to fix older ones, count again); entries from versions before this
feature cannot be deleted, fix them with a correction.

---

<a id="documents"></a>
## 5. Timeline, report and prescription requests

### Therapy timeline

**Therapy → Therapy timeline…** (Ctrl+T) shows one row per medicine on
a calendar (60 days back, 120 forward):

- **solid bar**: therapy in progress; **hatched bar**: suspension;
- **filled diamond**: a new dose or regimen starts; **hollow diamond**:
  next stage of a stepped taper;
- **triangle**: estimated run-out date; **dashed line**: today;
- grey row: deactivated medicine.

**Earlier** / **Later** move by 30 days, **Today** goes back; the
arrow keys move by a week. The **Details** box describes the selected
medicine in words. **Show in list** (or Enter) selects it in the main
list. The timeline changes nothing; run-out dates are estimates.

### Therapy report (print and PDF)

**Therapy → Therapy report…** (Ctrl+P) prepares a card of your active
medicines for a doctor, an emergency room or a pharmacist: active
ingredient, dosage, therapy period, doctor.

- **Include notes** is off by default: notes may be private.
- **Paper**: A4 or Letter.
- **Print…** shows a preview; **Save as PDF…** uses the Windows
  "Microsoft Print to PDF" printer (if it was removed, the window says
  how to add it back); **Save to file…** and **Copy to clipboard** give
  plain text.

MedReminder keeps no copy of what you save or print.

### Request a prescription

Select a medicine, then **Therapy → Request prescription…**. MedReminder
prepares a short message with the medicine name, the package, the
product code and your name; with a reference doctor, the greeting uses
the doctor's name. No dosage or notes are included. You can edit
everything before sending.

- **Copy** — to paste in webmail, a messaging app or a patient portal.
- **Open in mail client** — a new email in your usual mail program.
- **Send…** — sends it with MedReminder's email account, after a
  confirmation. Available when email is set up and a **Doctor e-mail**
  is filled in (Settings → Notifications). With a shared installation,
  only the [master device](#master) sends: on the other devices use
  **Open in mail client**.

MedReminder never sends a request by itself.

---

<a id="notifications"></a>
## 6. Notifications and email

### How reminders work

- Every 30 minutes MedReminder checks the medicines. When a medicine
  falls below its **warning threshold**, it warns you **once**, through
  the channels chosen for that medicine: a Windows notification and/or
  an email. After a new package the cycle starts again.
- **Tools → Check now** (**Ctrl+R**, or the tray menu) runs
  the check immediately.
- MedReminder must be running to send reminders. Turn on automatic
  startup (see [Settings](#settings)).

### Step 1 — the email account (administrator)

**Tools → Settings… → Email SMTP**:

| Field | What to enter |
|---|---|
| **Host** | Your provider's outgoing server, e.g. `smtp.gmail.com` |
| **Port** | Usually `587` (with *Use StartTLS*) or `465` |
| **Username** / **New password** | Your email account. The password is stored encrypted and never written to the logs |
| **Sender (from)** / **Sender name** | Who the emails come from |
| **Timeout (s)** | Seconds before giving up |

Click **Test connection** (it logs in without sending anything), then
**Save SMTP settings**.

*Example with Gmail:* turn on two-step verification in your Google
account, create an app password at `myaccount.google.com/apppasswords`,
then use Host `smtp.gmail.com`, Port `587`, StartTLS on, your Gmail
address as Username and the app password as password. Providers change
their rules: if the test fails, check your provider's instructions.

### Step 2 — the recipients (each profile)

**Tools → Settings… → Notifications**, for the open profile:

- **Recipient (to)** — who receives this profile's reminders.
- **Caregiver e-mail (optional)** — a family member or carer who gets a
  copy of every reminder, in the same email (both addresses are
  visible to both). It must differ from the recipient.
- **Doctor e-mail (optional)** — used only for prescription requests
  you send yourself; automatic reminders never go there.

Click **Save recipients**. In the same section, **My PIN** lets you set or
change the PIN of your own profile.

---

<a id="profiles"></a>
## 7. Several people: profiles and roles

One MedReminder can follow the medicines of several people, e.g. you
and a parent. Each person has a **profile** with its own medicines and
its own recipients.

### Roles

| | Administrator | User |
|---|---|---|
| Own medicines, stock, recipients | yes | yes |
| Email account, backup, reference country | yes | no |
| Create, rename, delete profiles; PINs of all | yes | no |
| Tools → Sync… and Tools → Installation… | yes | no |

There is always at least one administrator.

### Manage profiles (administrator)

**Tools → Manage profiles…**:

- **New profile** — name, role (default *User*), optional PIN.
- **Rename** — changes the displayed name.
- **Change PIN** — set, change or clear a profile's PIN.
- **Change role…** — makes a profile administrator or user. The role of
  the open profile cannot be changed (open another administrator
  profile first). Before making a profile without PIN an administrator,
  consider adding a PIN.
- **Delete** — type the profile name to confirm. The data on disk is
  kept unless you tick *Also delete the profile's data on disk*. The
  open profile and the last administrator cannot be deleted.

### Switch profile

**File → Change profile…**, choose the profile, confirm. MedReminder
restarts with that profile (and asks its PIN, if it has one). At
Windows startup the last used profile opens.

### About the PIN

The PIN avoids opening the wrong profile by accident. It is **not**
protection: it does not encrypt anything, and anyone using the same
Windows account can read every profile's files. Three wrong attempts
close the app. For real privacy, give each person their own Windows
account. If a PIN is forgotten, an administrator clears it with
**Change PIN**; if the only administrator forgot it, see
[Problems and answers](#faq).

---

<a id="backup"></a>
## 8. Protect your data: backup and export

| Option | What it is for | Where |
|---|---|---|
| **Automatic daily backup** | A copy of every profile, every day, in a folder of yours | Settings → Backup / Restore |
| **Encrypted export** | One portable file to move to a new PC or keep safe | Settings → Backup / Restore → Export all data (encrypted)… |
| **Cloud backup** | An encrypted daily copy in OneDrive, Google Drive or a synced folder | Settings → Backup / Restore → Backup to a cloud-synced folder |
| **Sync / Installation** | Several PCs working on the same data, continuously | [Several computers](#devices) |

The backup settings are managed by an administrator.

### Automatic daily backup

1. **Tools → Settings… → Backup / Restore**.
2. Tick **Daily automatic backup**, choose the **Backup folder** (ideally
   an external disk), the **Preferred time** and **Retention (days)**.
3. **Save backup settings**. **Run backup now** makes one at once.

Every profile is saved, as `medreminder-<profile>-<date>-<time>.db`.
MedReminder must be running at the chosen time; if the PC was off, the
backup runs at the next start. Do not choose a cloud-synced folder
for this backup: the files are not encrypted (MedReminder warns you).

- **Export to specific folder…** — one copy now, where you want.
- **Restore backup…** — choose a `.db` file and the profile that
  receives it. The current data is kept aside as
  `medreminder.db.bak-<date>`. If you restore into the open profile,
  MedReminder restarts.

### Encrypted export and import

An export is **one encrypted file** (`.mrz`) with all the data of a
profile. It is not tied to your PC: it is the recommended way to move
to a new computer.

**Export** — **Settings → Backup / Restore → Export all data
(encrypted)…**:

1. Choose the destination file.
2. Choose a **passphrase** of at least 12 characters and type it twice.
3. Optionally include the SMTP password, the backup preferences and
   the user preferences (language, catalogue country). The SMTP password
   is encrypted with your passphrase.
4. **Export**.

An administrator with several profiles can tick **Export every profile
(one encrypted file per profile)** and choose a folder.

> **The passphrase cannot be recovered.** Without it, the file can
> never be read again. Write it down somewhere safe.

**Import** — **Settings → Backup / Restore → Import from export…**: choose
the file (MedReminder shows what it contains), type the passphrase, tick
*I understand that this will overwrite the current profile's data*,
click **Import** and restart when asked. Import **replaces** the open
profile's data; a safety copy is kept. A wrong passphrase, a damaged
file or a file from a newer version stops the import without touching
your data. The format is public (`docs/EXPORT-FORMAT.md`): your data is
never locked in.

### Cloud backup

An encrypted copy of every profile, once a day, in the cloud.

1. **Settings → Backup / Restore → Backup to a cloud-synced folder
   (encrypted)**: tick the box.
2. **Storage**:
   - **OneDrive (app folder)** or **Google Drive (MedReminder/backups
     folder)** — click **Sign in…** and sign in with your account;
   - **Folder** — a folder already synchronized by OneDrive, Dropbox,
     iCloud or Google Drive on this PC.
3. **Snapshots to keep** (default 30).
4. **Backup passphrase → Set / change…**: at least 12 characters. It
   stays on this PC and is never sent anywhere.
5. Save.

**Restore** — **Settings → Backup / Restore → Restore from cloud
folder…**: choose the folder or account, pick a copy (date, profile,
device), type the passphrase, tick the confirmation and click
**Restore**. The copy replaces the **open** profile: to restore another
profile, open it first.

Good to know:

- Losing the backup passphrase means losing the copies.
- The copies do not contain the SMTP password or the preferences: use
  the encrypted export for those.
- Whoever knows the passphrase can read every profile's copy, PIN
  protected ones included.
- Your cloud provider may keep deleted files in its recycle bin.
- This is a **backup, not synchronisation**: to work on several PCs use
  [Sync](#sync).
- With a shared installation, only the [master device](#master) makes
  the cloud backup.

---

<a id="devices"></a>
## 9. Several computers

<a id="devices-choice"></a>
### Which option do I need?

| Situation | Use |
|---|---|
| One PC only | Nothing to do. Keep a [backup](#backup). |
| Move to a new PC once | [Encrypted export](#backup) on the old PC, import on the new one. |
| The **same profile** on two or more PCs, always up to date | [Sync](#sync) (Tools → Sync…). |
| The **whole family setup** (profiles, roles, PINs, email, backup) on several PCs, with **one** PC sending the emails | [Installation](#installation) (Tools → Installation…), on top of sync. |

Both options need a storage that every PC can reach: **OneDrive**,
**Google Drive**, or a **shared folder** (a folder synced by Dropbox or
similar, or a network share). The data there is always encrypted. No
MedReminder server is involved.

Every PC of a group must run the same MedReminder version: update all
of them together.

<a id="sync"></a>
### Sync a profile between PCs

Sync keeps **one profile** identical on several PCs: medicines, stock,
intakes, profile name and recipients. What you record on one PC appears
on the others within minutes. It is set up **for each profile**, with
the profile open, by an administrator.

**On the first PC**

1. Open the profile, then **Tools → Sync… → Enable sync…**.
2. Choose where the group lives:
   - **OneDrive** or **Google Drive**: sign in in the browser window.
     Every PC must use the **same** account. MedReminder only uses its
     own app folder;
   - **a shared folder**: choose it.
3. Enter a name for this PC and a **sync passphrase** (at least 10
   characters, twice). It is not the backup passphrase. Keep it safe:
   it cannot be recovered.

**On each other PC**

1. Create a profile (any name: it will be replaced), or open the one to
   replace.
2. **Tools → Sync… → Join a group…**, choose the same storage, enter a
   name for this PC and the same passphrase. Instead of the passphrase
   you can use **Join with a pairing code…** (see below).
3. Confirm: **this profile's data on this PC is replaced** by the
   group's (a copy is kept). MedReminder restarts.

With a shared folder, wait until it is fully downloaded on the new PC
first. With OneDrive or Google Drive the join may take a minute. If
the passphrase opens several groups (several profiles synced with the
same passphrase), MedReminder asks which one to join.

**Pairing code instead of passphrase.** On a PC already in the group,
**Tools → Sync… → Pair a device…** and click **Show the code** when the
other PC is ready. On the other PC, **Join with a pairing code…** and
type the code. The code works for 10 minutes and only while its window
is open. Anyone who sees it can read the data: never send it by email
or message, and show it only when needed (remote-assistance tools see
it too).

**Everyday use**

- Sync runs a few seconds after each change, every 5 minutes, and with
  **Sync now**. The **Devices** tab shows the PCs and when each was last
  seen.
- If two PCs changed the same thing before syncing, the latest change
  wins and the case appears in **Conflicts**: **Restore lost value**
  brings the other value back, **Dismiss** removes the entry.
- A low-stock email is sent **once per group**, not once per PC. (Two
  PCs that check before they have synced may both send; a
  [master device](#master) removes that case.)
- Importing an export or restoring a backup on a synced profile starts
  a new **generation**: the other PCs are warned and must use **Rebuild
  from the group…**.
- **Disable sync…** stops syncing on this PC and keeps its data.
- If the OneDrive or Google Drive session ends (password change, long
  inactivity), click **Sign in to OneDrive again** / **Sign in to Google
  Drive again**; nothing is lost.

**A PC is lost or the passphrase leaked.** In the **Devices** tab select
the PC and click **Remove device…**, or use **Change key and
passphrase…**. Choose a new sync passphrase: the removed PC cannot read
anything written from then on. Also sign that PC out in your Microsoft
or Google account security settings. On every other PC click **Enter
the new key…** and type the new passphrase or a pairing code: its
changes are kept, and MedReminder restarts.

<a id="installation"></a>
### Share the installation

Sync works profile by profile. The **installation** adds everything
around the profiles, so that every PC is set up the same way:

| Shared by all devices | Kept per device |
|---|---|
| Profiles: names, roles, PINs | Which profiles the device holds |
| Email account (SMTP, password included) | Interface language, text size |
| Cloud backup policy (storage, number of copies) | Cloud backup passphrase and sign-in |
| Reference country | Local automatic backup |

Each device holds **only the profiles an administrator gives it**: the
PC of a grandparent can hold only their profile, while the family PC
holds everyone's.

**Before you start**

- An administrator profile, on the PC that will be the main one.
- **Sync enabled for each profile** you want to share (see
  [Sync](#sync)); a profile without sync cannot be given to another
  device.

**Step 1 — Publish (on the main PC)**

1. **Tools → Installation… → Publish the installation…**.
2. Choose the **same storage** that holds the profiles' sync groups.
3. Choose an **installation passphrase**. It lets an administrator add
   a device and recover every profile when no other device is at hand.
   Keep it for administrators only; it cannot be recovered.

This PC becomes the [master device](#master).

**Step 2 — Add a device**

1. On the main PC: **Tools → Installation… → Devices → Add a device…**,
   tick the profiles for the new device, then **Show the code**.
2. On the new PC:
   - if MedReminder was never used there: in the welcome window choose
     **Join an existing installation…**;
   - otherwise: **Tools → Installation… → Join an existing
     installation…**.
3. Choose **With a code** and type the code. The code lasts 10 minutes,
   or until its window closes.
4. The window lists each profile ("added to this device", "already on
   this device", …). MedReminder restarts with the new profiles and the
   installation's settings. Profiles already on the new PC are added to
   the installation.

*No other device at hand?* Choose **With the installation passphrase**,
select the storage and type the passphrase. An administrator then picks
their profile, types its PIN and selects the profiles for this device.

**Everyday use**

- The installation syncs by itself every 15 minutes.
- A change of profile, role, PIN, email account, cloud backup policy
  or reference country made on one device reaches the others.
- A profile created later: enable sync for it (Tools → Sync…), then give
  it to other devices with **Add a device…** on a device that holds it.
- **Tools → Installation… → Status** shows the storage, the master and
  any action needed.

<a id="master"></a>
### The master device

In a shared installation **one device, the master**, sends every email
(low-stock and dose reminders, of every profile it holds, also those
not open) and makes the cloud backup. The other devices show their
reminders on screen only. This way each email arrives once.

- The device that publishes the installation is the master. The
  **Devices** tab shows it in the **Role** column.
- Choose a master that is **often switched on** and has MedReminder
  running.
- On other devices, prescription requests open in the mail client, and
  **Test connection** works only on the master. The email settings can
  be edited everywhere and reach every device.
- A master that has not synced the installation for **24 hours** stops
  sending email until it syncs again.
- Installations published before this version have no master until an
  administrator chooses one; until then every device sends.

**Move the master to another device**

1. **Tools → Installation… → Devices**, select the new device, **Make
   master…**, confirm. No device sends email until the handover is
   complete.
2. On the new device, with an administrator profile open, the
   **Master handover** window opens by itself (or later from **Tools →
   Installation… → Complete the handover…**). It shows the settings and
   asks you to:
   - **Test the email connection from this device**;
   - **Sign in** to the cloud backup storage with the same account;
   - type the **cloud backup passphrase** again (it is never copied
     between devices);
   - optionally type the installation passphrase, to bring profiles no
     device at hand holds.
3. **Confirm**. The new device takes over when the old master hands over
   at its next sync. New profiles appear at the next start.

**The master is broken or lost.** Do the same from another device: the
new master takes over by itself once the old one has been silent for 25
hours. Then remove the old one (below).

<a id="remove-device"></a>
### Lost or replaced device

When a device is lost, sold or given away:

1. On the **master** (it holds every profile): **Tools → Installation…
   → Devices**, select the device, **Remove device…**.
2. Choose a **new installation passphrase**. The removed device keeps
   what it already has but receives nothing new; the profiles it held
   get new keys too.
3. MedReminder offers to show a code. The other devices stop syncing
   until they get the new key: on each of them an administrator opens
   **Tools → Installation… → Enter the new key…** and types the new
   passphrase or the code. Changes made there meanwhile are kept.
4. The new profile keys reach the other devices by themselves; a
   profile open at that moment asks to restart MedReminder.

Removing the master from another device makes that device the master.
Also sign the lost device out in your Microsoft or Google account.

---

<a id="settings"></a>
## 10. Settings and everyday use

All in **Tools → Settings…**. The sections are listed on the left;
**Ctrl+Tab** moves to the next one. The window can be resized.

- **General → Interface language**: English, Italian, French, Spanish
  or German. Emails and the therapy report use it too. MedReminder
  restarts.
- **General → Text size (this profile)**: Normal, Large, Extra large,
  for each profile. MedReminder also follows the Windows scaling and
  contrast themes. On a small screen prefer Large.
- **General → Appearance (this profile)**: Same as Windows, Light or
  Dark, for each profile on this computer. "Same as Windows" is dark
  only on Windows 11 with dark mode on; with a Windows high-contrast
  theme its colours are used. Applies after a restart. Date fields stay
  light in Dark.
- **General → Check for updates on startup (GitHub)**: checks for a new
  version (nothing is installed by itself) and updates the catalogue.
  **? → Check for updates…** checks now.
- **Startup → Start MedReminder at Windows login**: starts hidden in
  the notification area. No administrator rights needed.

**Notification area icon.** Double-click opens the window; right-click
offers *Open MedReminder*, *Check now*, *Settings…*, *Exit*.

**Support development.** If enabled, **? → Support development…** opens
a voluntary contribution page (Stripe or PayPal) in your browser.
MedReminder never sees your payment details.

---

<a id="faq"></a>
## 11. Problems and answers

**I get no email.**
Check, in order: *Test connection* in Settings → Email SMTP; the
*Recipient* in Settings → Notifications; the **Email** channel ticked on
the medicine; MedReminder running. With a shared installation, only the
master sends: check **Tools → Installation… → Status**.

**"This device is the master but has not synced the installation for
more than 24 hours".**
The master cannot reach the storage. Check the internet connection and
the OneDrive / Google Drive sign-in, then **Sync now**.

**"The installation key was changed on another device".**
A device was removed. Open **Tools → Installation… → Enter the new
key…** and type the new passphrase, or a code shown by a device that
already has it.

**"A device was removed … this profile has a new key. Restart now?"**
Answer yes: the profile takes its new key when MedReminder restarts.

**"This device was removed from the installation".**
This device keeps its data but receives nothing more. To use it again,
an administrator adds it as a new device.

**The master handover does not finish.**
The old master hands over at its next sync. If it is off for good, the
new master takes over 25 hours after the old one was last seen.

**"The sync group of this profile belongs to another installation".**
The profile was published from another installation. Join that
installation (**Join an existing installation…**).

**I forgot a PIN.**
An administrator clears it with **Tools → Manage profiles… → Change
PIN**. If nobody else can: close MedReminder, open
`%LOCALAPPDATA%\MedReminder\profiles.json` with Notepad and, for that
profile, delete the values of `PinHash` and `PinSalt` and set
`PinIterations` to `0`. MedReminder records the change at the next
start; with a shared installation it reaches the other devices.

**I forgot a passphrase.**
Export, backup, sync and installation passphrases cannot be recovered.
You can still set new ones (new export, new cloud backup passphrase,
*Change key and passphrase…*), but files encrypted with the old one
stay unreadable.

**"Already running".**
MedReminder is already open: look for its icon in the notification
area.

**The data looks damaged.**
Restore a backup (Settings → Backup / Restore → Restore backup…) or
an export. The log files (below) help to understand what happened.

---

<a id="data"></a>
## 12. Where MedReminder keeps its data

Everything is under `%LOCALAPPDATA%\MedReminder\` (paste it in the
File Explorer address bar). MedReminder writes nowhere else, except the
backup and export files you place yourself.

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json              list of profiles, roles, PINs (hashed)
├── smtp.settings.json         email account (no password)
├── smtp.protected             email password, encrypted by Windows
├── backup.settings.json       backup settings
├── cloud-backup.protected     cloud backup passphrase, encrypted by Windows
├── user.settings.json         language, reference country, update check
├── household\                 shared installation (only when used)
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    └── <profile>\
        ├── medreminder.db     the profile's medicines and stock
        ├── notifications.settings.json   recipients
        ├── ui.settings.json   text size
        └── sync.*             sync settings (only when used)
```

- The **logs** record what the app did (checks, emails sent, errors).
  They never contain passwords, email text or medical notes.
- The database is not encrypted: it is protected by your Windows
  account. Exports and cloud copies are encrypted.
- **Upgrading from a very old version** (a single `medreminder.db`
  directly in the folder): the first start moves it into a profile
  called *User* and keeps a copy in `backups\pre-migration-…`, which you
  can delete once everything looks right.

---

<a id="limits"></a>
## 13. What MedReminder does not do

- It does not track whether you took your doses and does not alert on
  missed doses (the dose-time reminder is only a prompt).
- It does not give therapy instructions and does not check doses or
  drug interactions.
- It does not order medicines and does not contact your doctor by
  itself.
- It does not merge data restored from a backup: restore and import
  always replace.

Its purpose is to let you know in time that you need a new
prescription.

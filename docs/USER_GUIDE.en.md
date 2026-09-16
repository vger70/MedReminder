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
2. On first open the window is empty: the database is created
   automatically under `%LOCALAPPDATA%\MedReminder\medreminder.db`.
3. At the top you find the toolbar; at the bottom the status bar.
   The icon in the Windows notification area stays visible while
   the app is running.

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
6. **Save**.

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
  **Dose and frequency are NOT changed from here**: use the
  schedule change (command-line function or DB edit for the MVP).
- **Deactivate**: toolbar → **Deactivate**. The medicine disappears
  from automatic checks and alerts, but historical data (movements,
  notifications) stays in the DB for audit.

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

- It does not remind you to take a specific dose (it is not an
  alarm clock).
- It does not provide therapy instructions or drug interactions.
- It does not sync between different devices.
- It does not automatically order medicines.
- It does not contact your doctor directly.

Its only purpose is to let you know in time that you need to
request a new prescription.

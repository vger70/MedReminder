# MedReminder — Privacy Policy

Last updated: 5 October 2026

This policy describes how the MedReminder desktop application for
Windows handles personal data. It applies to every distribution of the
application: the ZIP packages, the MSI installers and the Microsoft
Store.

## 1. Summary

- MedReminder stores your data on your PC. The developer runs no
  server and receives none of your data.
- There is no account with the developer, no telemetry, no analytics,
  no advertising and no tracking.
- Data leaves your PC only through features you turn on: email
  reminders, cloud backup, sync, and the update check. Each one
  sends data only to the service you choose.

## 2. Who is responsible

MedReminder is free, open-source software (Apache License 2.0)
developed by vger70. Because the developer does not collect or receive
your data, you remain in control of it: the application processes it on
your own device, on your behalf.

Contact: info@medreminder26.org, or an issue at
https://github.com/vger70/MedReminder/issues (do not post health data
in a public issue).

## 3. Data stored on your PC

MedReminder keeps everything in `%LOCALAPPDATA%\MedReminder\` under
your Windows account:

- profiles: name, role, optional PIN (stored as a hash);
- medicines, doses, schedules, stock, intakes, prescriptions,
  administrative deadlines and notes, one database per profile;
- email settings: SMTP server, user name and password (the password is
  encrypted with Windows DPAPI), sender and recipient addresses,
  including an optional doctor or caregiver address;
- sign-in tokens for OneDrive or Google Drive, if you connect them;
- settings, backups you configure, and log files.

The data concerns your health (medicines and therapy). The databases
are not encrypted: anyone who can use your Windows account can read
them. The profile PIN prevents opening the wrong profile by mistake; it
is not protection. To keep data apart from other people, give each
person their own Windows account.

Log files never contain passwords, email bodies or medical notes.

Uninstalling MedReminder does not delete this folder. Delete it to
remove all data.

## 4. Data that leaves your PC

Only these features send data, and only when you use them:

| Feature | What is sent | Where |
|---|---|---|
| Email reminders and prescription requests | The email you see in the app: medicine names, stock, dates; a prescription request also carries the product code and your name. No dosage or notes | The SMTP server of the email account you configure, then the recipients you enter |
| Cloud backup (optional) | A daily copy of your profiles, encrypted on your PC with a passphrase that never leaves it | Your own OneDrive (app folder) or Google Drive (MedReminder folder), or a folder of your choice |
| Sync between PCs (optional) | Medicines, stock, intakes, profile name and recipients, end-to-end encrypted | Your own OneDrive, Google Drive or shared folder |
| Calendar export | An `.ics` file, with generic titles unless you choose to include medicine names | Saved where you choose; low-stock emails attach it |
| Update check and catalogue update (on by default, can be turned off) | A request for the latest version and for public medicine lists; no personal data. GitHub sees your IP address and the app version | GitHub (`api.github.com`, `raw.githubusercontent.com`) |

When you connect OneDrive or Google Drive, MedReminder asks only for
access to its own folder (OneDrive `Files.ReadWrite.AppFolder`; Google
Drive `drive.file` and `drive.appdata`), not to your other files.
Turning cloud backup or sync off stops every upload; the access granted
can be revoked from your Microsoft or Google account settings. These
services process the data under their own privacy policies, as does
your email provider.

## 5. Webcam

The barcode scanner can use your webcam. Images are decoded on your PC
and are never saved or sent. The camera turns off when a code is read,
when you close the scan window, or after 30 seconds.

## 6. Links that open your browser

Some commands open a web page in your default browser: the medicine
leaflet or information page, the user guide, the project page, and, if
you choose, a donation page from Stripe or PayPal. MedReminder sends no
data to these sites; your browser does, under the sites' own policies.
MedReminder never sees payment details.

## 7. Children

MedReminder is not directed to children and collects no data from
anyone.

## 8. Your rights

All data is on your PC, under your control: you can view, correct,
export (Settings → Backup / Restore → Export all data) and delete it at
any time. For data in your email, OneDrive or Google Drive account,
contact those providers.

## 9. Changes

Changes to this policy are published in this file, with a new "Last
updated" date. The history is visible in the repository:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.md

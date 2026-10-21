# MedReminder — Privacy Policy

Last updated: 11 October 2026

Translations: [Italiano](PRIVACY.it.md) · [Français](PRIVACY.fr.md) ·
[Español](PRIVACY.es.md) · [Deutsch](PRIVACY.de.md). The English
version prevails.

This policy describes how MedReminder handles personal data on Windows
desktop and Android. It applies to the versions and features made
available for each platform; a feature may not yet be available in every
release. The Windows desktop app is distributed as ZIP packages, MSI
installers and through the Microsoft Store. Android is distributed
through Google Play, including its testing tracks.

## 1. Summary

- MedReminder stores your data on your device. The developer runs no
  server and receives none of your data.
- There is no account with the developer, no telemetry, no analytics,
  no advertising and no tracking.
- Data leaves your device only through features you use or turn on, such
  as email, export or sharing, cloud backup, sync and catalogue
  downloads. Each feature sends data only to the service or destination
  described below. Platform-specific services, such as Google Play
  updates, are described separately.

## 2. Who is responsible

MedReminder is free, open-source software (Apache License 2.0)
developed by vger70. Because the developer does not collect or receive
your data, you remain in control of it: the application processes it on
your own device, on your behalf.

Contact: info@medreminder26.org, or an issue at
https://github.com/vger70/MedReminder/issues (do not post health data
in a public issue).

## 3. Data stored on your device

### Windows desktop

MedReminder keeps its data in `%LOCALAPPDATA%\MedReminder\` under your
Windows account. This includes:

- profiles: name, role, optional PIN (stored as a hash);
- medicines, doses, schedules, stock, intakes, prescriptions,
  administrative deadlines and notes, one database per profile;
- email settings: SMTP server, user name and password (the password is
  encrypted with Windows DPAPI), sender and recipient addresses,
  including an optional doctor or caregiver address;
- sign-in tokens for OneDrive or Google Drive, if you connect them;
- settings, backups you configure, and log files.

The profile databases are not encrypted on Windows: anyone who can use
your Windows account can read them. The profile PIN prevents opening the
wrong profile by mistake; it is not protection. To keep data apart from
other people, give each person their own Windows account.

Windows log files never contain passwords, email bodies or medical
notes. Uninstalling the Windows app does not delete its data folder;
delete it separately to remove that data.

### Android

MedReminder stores its databases, settings and log files in the app's
private storage on the phone. Android's automatic app-data backup is
disabled, so the operating system does not copy this health data to an
Android backup account. The backup passphrase is kept using Android's
secure key storage. Google Drive access is managed by Google Play
services, which keep the grant and give the app short-lived access
tokens. OneDrive sign-in tokens are kept by the Microsoft sign-in
library in the app's private storage, which other apps cannot read and
which is excluded from Android backup. Android log files do not contain
passwords, email bodies or medical notes.

Clearing MedReminder's app data or uninstalling it removes its local
copy. Before doing so, use the app's export or cloud-backup features if
you want to keep a copy.

## 4. Data that leaves your device

Only these features send or share data, and only when available and
used or enabled:

| Feature | What is sent | Where |
|---|---|---|
| Email reminders and prescription requests (when available and configured) | The email shown in the app: medicine names, stock and dates; a prescription request also includes the product code and your name. No dosage or notes | The SMTP server configured by you, then the recipients you enter |
| Cloud backup (optional) | A daily copy of your profiles, encrypted on your device with your backup passphrase. The passphrase is not sent to the cloud provider | Your OneDrive or Google Drive account; on Windows, also a folder of your choice |
| Sync (optional) | Medicines, stock, intakes, profile name and recipients, end-to-end encrypted | Your OneDrive or Google Drive account; on Windows, also a shared folder |
| Export, calendar export and sharing | An encrypted `.mrz` profile archive, an `.ics` calendar file, or the content you choose to share. Calendar titles are generic unless you choose to include medicine names | The file location or app/recipient you select using Windows or Android file and sharing controls |
| Catalogue and public data updates | Requests for public medicine lists and related public data; no personal health data | GitHub (`raw.githubusercontent.com`). GitHub receives your IP address and request details |
| Windows app update check | A request for the latest desktop version; no personal health data. GitHub receives your IP address and app version | GitHub (`api.github.com`) |
| Android app updates | Google Play delivers app updates and may process account, device or diagnostic information under its privacy policy and your settings | Google Play |

When you connect OneDrive or Google Drive, MedReminder requests access
for its app data and backup files, not general access to your other
files. Turning cloud backup or sync off stops future uploads from that
feature; you can revoke the app's access in your Microsoft or Google
account settings. Turning a feature off does not by itself delete copies
already stored with the provider. The cloud and email providers process
data under their own privacy policies.

On Windows, cloud backup and sync can also use folders you select. Those
folders may be synchronized by a cloud provider or shared with other
people; their access and processing are governed by that provider and
your folder settings. Android cloud backup and sync use the connected
OneDrive or Google Drive account. Scheduled work on Android may be
delayed by Android or device battery-management settings.

## 5. Camera

The barcode scanner can use the Windows webcam or Android camera when
you choose to scan. Images are processed on your device and are not
saved or sent by MedReminder. On Windows, the camera turns off when a
code is read, when you close the scan window, or after 30 seconds. On
Android, camera access is controlled by the Android permission and app
lifecycle. On Android, the camera also reads the QR code another device
shows when you join a circle; it is used only while the scan page
is open, and the code is not saved.

## 6. Links that open your browser

Some commands open a web page in your browser, such as a medicine
leaflet or information page, the user guide, or the project page. On
Windows, a donation link may also open a Stripe or PayPal page if you
choose it. MedReminder sends no data to these sites; your browser does,
under the sites' own policies. MedReminder never sees payment details.

## 7. Children

MedReminder is not directed to children. The developer does not collect
personal data from children through the app.

## 8. Your rights

You can view, correct, export and delete data in MedReminder. On
Windows, use Settings → Backup / Restore → Export all data; on Android,
use the app's export and data-management controls. You can also remove
local Android app data in Android settings. Copies in email, exported or
shared files, OneDrive or Google Drive must be managed with the relevant
recipients, apps or providers.

## 9. Changes

Changes to this policy are published in this file, with a new "Last
updated" date. The history is visible in the repository:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.md

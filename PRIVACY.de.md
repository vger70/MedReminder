# MedReminder — Datenschutzerklärung

Zuletzt aktualisiert: 5. Oktober 2026

Übersetzung von [PRIVACY.md](PRIVACY.md). Bei Abweichungen ist die
englische Fassung maßgeblich.

Diese Erklärung beschreibt, wie die Desktop-Anwendung MedReminder für
Windows personenbezogene Daten verarbeitet. Sie gilt für jede
Vertriebsform der Anwendung: die ZIP-Pakete, die MSI-Installer und den
Microsoft Store.

## 1. Kurzfassung

- MedReminder speichert Ihre Daten auf Ihrem PC. Der Entwickler
  betreibt keinen Server und erhält keine Ihrer Daten.
- Es gibt kein Konto beim Entwickler, keine Telemetrie, keine
  Nutzungsstatistik, keine Werbung und kein Tracking.
- Daten verlassen Ihren PC nur über Funktionen, die Sie einschalten:
  E-Mail-Erinnerungen, Cloud-Sicherung, Synchronisierung und die
  Suche nach Updates. Jede davon sendet Daten nur an den Dienst, den
  Sie wählen.

## 2. Verantwortlichkeit

MedReminder ist freie Open-Source-Software (Apache License 2.0),
entwickelt von vger70. Da der Entwickler Ihre Daten weder erhebt noch
erhält, behalten Sie die Kontrolle darüber: Die Anwendung verarbeitet
sie auf Ihrem eigenen Gerät, in Ihrem Auftrag.

Kontakt: info@medreminder26.org oder ein Issue unter
https://github.com/vger70/MedReminder/issues (veröffentlichen Sie keine
Gesundheitsdaten in einem öffentlichen Issue).

## 3. Auf Ihrem PC gespeicherte Daten

MedReminder speichert alles in `%LOCALAPPDATA%\MedReminder\` in Ihrem
Windows-Konto:

- Profile: Name, Rolle, optionale PIN (als Hash gespeichert);
- Medikamente, Dosen, Einnahmepläne, Vorrat, Einnahmen, Rezepte,
  Verwaltungsfristen und Notizen, eine Datenbank pro Profil;
- E-Mail-Einstellungen: SMTP-Server, Benutzername und Passwort (das
  Passwort wird mit Windows DPAPI verschlüsselt), Absender- und
  Empfängeradressen, einschließlich einer optionalen Adresse des
  Arztes oder der betreuenden Person;
- Anmelde-Token für OneDrive oder Google Drive, falls Sie diese
  verbinden;
- Einstellungen, von Ihnen eingerichtete Sicherungen und
  Protokolldateien.

Die Daten betreffen Ihre Gesundheit (Medikamente und Therapie). Die
Datenbanken sind nicht verschlüsselt: Jeder, der Ihr Windows-Konto
benutzen kann, kann sie lesen. Die Profil-PIN verhindert, dass
versehentlich das falsche Profil geöffnet wird; sie ist kein Schutz.
Um die Daten anderer Personen zu trennen, geben Sie jeder Person ein
eigenes Windows-Konto.

Protokolldateien enthalten niemals Passwörter, E-Mail-Inhalte oder
medizinische Notizen.

Die Deinstallation von MedReminder löscht diesen Ordner nicht. Löschen
Sie ihn, um alle Daten zu entfernen.

## 4. Daten, die Ihren PC verlassen

Nur diese Funktionen senden Daten, und nur wenn Sie sie verwenden:

| Funktion | Was gesendet wird | Wohin |
|---|---|---|
| E-Mail-Erinnerungen und Rezeptanforderungen | Die E-Mail, die Sie in der Anwendung sehen: Medikamentennamen, Vorrat, Daten; eine Rezeptanforderung enthält außerdem den Produktcode und Ihren Namen. Keine Dosierung und keine Notizen | Der SMTP-Server des von Ihnen eingerichteten E-Mail-Kontos, danach die von Ihnen eingegebenen Empfänger |
| Cloud-Sicherung (optional) | Eine tägliche Kopie Ihrer Profile, auf Ihrem PC verschlüsselt mit einer Passphrase, die den PC nie verlässt | Ihr eigenes OneDrive (App-Ordner) oder Google Drive (Ordner MedReminder) oder ein Ordner Ihrer Wahl |
| Synchronisierung zwischen PCs (optional) | Medikamente, Vorrat, Einnahmen, Profilname und Empfänger, Ende-zu-Ende-verschlüsselt | Ihr eigenes OneDrive, Google Drive oder ein freigegebener Ordner |
| Kalenderexport | Eine `.ics`-Datei, mit allgemeinen Titeln, sofern Sie nicht die Medikamentennamen einschließen | Gespeichert, wo Sie wählen; E-Mails bei niedrigem Vorrat hängen sie an |
| Update-Suche und Katalogaktualisierung (standardmäßig aktiv, abschaltbar) | Eine Abfrage der neuesten Version und der öffentlichen Arzneimittellisten; keine personenbezogenen Daten. GitHub sieht Ihre IP-Adresse und die Version der Anwendung | GitHub (`api.github.com`, `raw.githubusercontent.com`) |

Wenn Sie OneDrive oder Google Drive verbinden, fordert MedReminder nur
Zugriff auf den eigenen Ordner an (OneDrive
`Files.ReadWrite.AppFolder`; Google Drive `drive.file` und
`drive.appdata`), nicht auf Ihre übrigen Dateien. Das Abschalten der
Cloud-Sicherung oder der Synchronisierung beendet jede Übertragung;
der erteilte Zugriff lässt sich in den Einstellungen Ihres Microsoft-
oder Google-Kontos widerrufen. Diese Dienste verarbeiten die Daten nach
ihren eigenen Datenschutzbestimmungen, ebenso wie Ihr E-Mail-Anbieter.

## 5. Webcam

Der Barcode-Scanner kann Ihre Webcam verwenden. Die Bilder werden auf
Ihrem PC ausgewertet und niemals gespeichert oder gesendet. Die Kamera
schaltet sich ab, wenn ein Code gelesen wurde, wenn Sie das
Scanfenster schließen, oder nach 30 Sekunden.

## 6. Links, die Ihren Browser öffnen

Einige Befehle öffnen eine Webseite in Ihrem Standardbrowser: die
Packungsbeilage oder Informationsseite des Medikaments, das
Benutzerhandbuch, die Projektseite und, wenn Sie es wünschen, eine
Spendenseite von Stripe oder PayPal. MedReminder sendet keine Daten an
diese Seiten; das tut Ihr Browser, nach den Bestimmungen dieser
Seiten. MedReminder sieht niemals Zahlungsdaten.

## 7. Kinder

MedReminder richtet sich nicht an Kinder und erhebt von niemandem
Daten.

## 8. Ihre Rechte

Alle Daten liegen auf Ihrem PC, unter Ihrer Kontrolle: Sie können sie
jederzeit einsehen, berichtigen, exportieren (Einstellungen →
Sicherung / Wiederherstellung → Alle Daten exportieren) und löschen.
Für Daten in Ihrem E-Mail-, OneDrive- oder Google-Drive-Konto wenden
Sie sich an diese Anbieter.

## 9. Änderungen

Änderungen dieser Erklärung werden in dieser Datei mit einem neuen
Datum „Zuletzt aktualisiert" veröffentlicht. Der Verlauf ist im
Repository einsehbar:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.de.md

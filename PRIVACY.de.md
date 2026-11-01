# MedReminder — Datenschutzerklärung

Zuletzt aktualisiert: 1. November 2026

Übersetzung von [PRIVACY.md](PRIVACY.md). Bei Abweichungen ist die englische Fassung maßgeblich.

Diese Erklärung beschreibt, wie MedReminder personenbezogene Daten unter Windows (Desktop), Android und iOS (iPhone) verarbeitet. Sie gilt für die auf den jeweiligen Plattformen verfügbaren Versionen und Funktionen; möglicherweise sind nicht alle Funktionen in jeder Version verfügbar. Die Windows-Desktop-App wird als ZIP-Paket, MSI-Installer und über den Microsoft Store vertrieben. Android wird über Google Play vertrieben, einschließlich der Testkanäle. iOS wird über den App Store von Apple vertrieben, einschließlich TestFlight.

## 1. Kurzfassung

- MedReminder speichert Ihre Daten auf Ihrem Gerät. Der Entwickler betreibt keinen Server und erhält Ihre Daten nicht.
- Es gibt kein Konto beim Entwickler, keine Telemetrie, keine Nutzungsstatistiken, keine Werbung und kein Tracking.
- Daten verlassen Ihr Gerät nur über Funktionen, die Sie verwenden oder aktivieren, etwa E-Mail, Export oder Teilen, Cloud-Sicherung, Synchronisierung und Katalog-Downloads. Jede Funktion übermittelt Daten nur an den unten beschriebenen Dienst oder das angegebene Ziel. Plattformspezifische Dienste wie Updates über Google Play und den App Store werden gesondert aufgeführt.

## 2. Verantwortlichkeit

MedReminder ist freie Open-Source-Software (Apache License 2.0), entwickelt von vger70. Da der Entwickler Ihre Daten weder erhebt noch erhält, behalten Sie die Kontrolle darüber: Die Anwendung verarbeitet sie auf Ihrem Gerät in Ihrem Auftrag.

Kontakt: info@medreminder26.org oder ein Issue unter https://github.com/vger70/MedReminder/issues (veröffentlichen Sie keine Gesundheitsdaten in einem öffentlichen Issue).

## 3. Auf Ihrem Gerät gespeicherte Daten

### Windows-Desktop

MedReminder speichert seine Daten in `%LOCALAPPDATA%\MedReminder\` in Ihrem Windows-Konto. Dazu gehören:

- Profile: Name, Rolle, optionale PIN (als Hash gespeichert);
- Medikamente, Dosen, Einnahmepläne, Vorrat, Einnahmen, Rezepte, Verwaltungsfristen und Notizen, eine Datenbank pro Profil;
- E-Mail-Einstellungen: SMTP-Server, Benutzername und Passwort (mit Windows DPAPI verschlüsselt), Absender- und Empfängeradressen, gegebenenfalls auch die Adresse eines Arztes oder einer Betreuungsperson;
- Zugriffstoken für OneDrive oder Google Drive, falls Sie diese verbinden;
- Einstellungen, von Ihnen eingerichtete Sicherungen und Protokolldateien.

Unter Windows sind die Profildatenbanken nicht verschlüsselt: Jeder, der Ihr Windows-Konto benutzen kann, kann sie lesen. Die Profil-PIN verhindert, dass versehentlich das falsche Profil geöffnet wird; sie ist kein Schutz. Um die Daten verschiedener Personen zu trennen, geben Sie jeder Person ein eigenes Windows-Konto.

Windows-Protokolldateien enthalten weder Passwörter noch E-Mail-Inhalte oder medizinische Notizen. Die Deinstallation der Windows-App löscht den Datenordner nicht; löschen Sie ihn separat, um diese Daten zu entfernen.

### Android

MedReminder speichert Datenbanken, Einstellungen und Protokolldateien im privaten App-Speicher des Telefons. Die automatische Android-App-Datensicherung ist deaktiviert; das Betriebssystem kopiert diese Gesundheitsdaten daher nicht in ein Android-Sicherungskonto. Die Sicherungs-Passphrase wird mithilfe des sicheren Android-Schlüsselspeichers aufbewahrt. Der Zugriff auf Google Drive wird von den Google Play-Diensten verwaltet, die die Berechtigung speichern und der App kurzlebige Zugriffstoken geben. Die Anmeldetoken für OneDrive werden von der Microsoft-Anmeldebibliothek im privaten App-Speicher aufbewahrt, den andere Apps nicht lesen können und der von der Android-Sicherung ausgeschlossen ist. Android-Protokolle enthalten weder Passwörter noch E-Mail-Inhalte oder medizinische Notizen.

Wenn Sie die MedReminder-App-Daten löschen oder die App deinstallieren, wird die lokale Kopie entfernt. Verwenden Sie vorher die Export- oder Cloud-Sicherungsfunktionen, wenn Sie eine Kopie behalten möchten.

### iOS (iPhone)

MedReminder speichert seine Datenbanken und Einstellungen im privaten Speicher der App auf dem iPhone, den andere Apps nicht lesen können. Dieser Speicher ist von iCloud- und Computer-Backups ausgeschlossen: iOS kopiert diese Gesundheitsdaten daher weder in ein iCloud-Backup noch auf einen Computer, und ein auf einem anderen iPhone wiederhergestelltes Backup enthält sie nicht. Lokale Geheimnisse wie die Sicherungs-Passphrase, das E-Mail-Passwort und die Anmeldetoken für OneDrive und Google Drive werden mit einem Schlüssel verschlüsselt, der im iOS-Schlüsselbund liegt. Nur MedReminder auf diesem iPhone kann diesen Schlüssel lesen, sobald das iPhone nach dem Einschalten einmal entsperrt wurde; er wird nie mit dem iCloud-Schlüsselbund synchronisiert. Sie melden sich bei OneDrive oder Google Drive auf der Seite des Anbieters an, die iOS öffnet; MedReminder sieht Ihr Microsoft- oder Google-Passwort nie. Die iOS-App schreibt keine Protokolldateien.

Wenn Sie MedReminder vom iPhone löschen, wird die lokale Kopie entfernt. iOS kann den Schlüsselbund-Schlüssel der App auch nach dem Löschen behalten; nur MedReminder kann ihn lesen, und ohne die gelöschten Daten öffnet er nichts. Verwenden Sie vor dem Löschen die Export- oder Cloud-Sicherungsfunktionen, wenn Sie eine Kopie behalten möchten.

Erinnerungen sind lokale Mitteilungen, die auf dem iPhone geplant werden; es wird kein Push-Mitteilungsdienst verwendet. Je nach Ihren iOS-Mitteilungseinstellungen kann ihr Text, etwa der Name eines Medikaments, auf dem Sperrbildschirm und auf einer gekoppelten Apple Watch erscheinen.

## 4. Daten, die Ihr Gerät verlassen

Diese Funktionen übermitteln oder teilen Daten nur, wenn sie verfügbar sind und verwendet oder aktiviert werden:

| Funktion | Übermittelte Daten | Ziel |
|---|---|---|
| E-Mail-Erinnerungen und Rezeptanforderungen (falls verfügbar und eingerichtet) | Die in der Anwendung angezeigte E-Mail: Medikamentennamen, Vorrat und Daten; eine Rezeptanforderung enthält außerdem den Produktcode und Ihren Namen. Keine Dosierung und keine Notizen | Der von Ihnen eingerichtete SMTP-Server und anschließend die von Ihnen angegebenen Empfänger |
| Cloud-Sicherung (optional) | Eine tägliche Kopie Ihrer Profile, auf Ihrem Gerät mit Ihrer Sicherungs-Passphrase verschlüsselt. Die Passphrase wird nicht an den Cloud-Anbieter gesendet | Ihr OneDrive- oder Google-Drive-Konto; unter Windows zusätzlich ein Ordner Ihrer Wahl |
| Synchronisierung (optional) | Medikamente, Vorrat, Einnahmen, Profilname und Empfänger, Ende-zu-Ende-verschlüsselt | Ihr OneDrive- oder Google-Drive-Konto; unter Windows zusätzlich ein freigegebener Ordner |
| Export, Kalenderexport und Teilen | Ein verschlüsseltes `.mrz`-Profilarchiv, eine `.ics`-Kalenderdatei oder von Ihnen ausgewählte Inhalte. Kalendertitel sind allgemein gehalten, sofern Sie nicht die Medikamentennamen einschließen | Der Dateiablageort oder die App/der Empfänger, die bzw. den Sie über die Datei- und Freigabefunktionen von Windows, Android oder iOS auswählen |
| Katalog- und öffentliche Datenaktualisierungen | Abfragen öffentlicher Medikamentenlisten und weiterer öffentlicher Daten; keine persönlichen Gesundheitsdaten | GitHub (`raw.githubusercontent.com`). GitHub erhält Ihre IP-Adresse und die Anfragedetails |
| Windows-Updateprüfung | Eine Abfrage der neuesten Desktop-Version; keine persönlichen Gesundheitsdaten. GitHub erhält Ihre IP-Adresse und die App-Version | GitHub (`api.github.com`) |
| Android- und iOS-App-Updates | Google Play und der App Store stellen Updates bereit und können gemäß ihren eigenen Datenschutzrichtlinien und Ihren Einstellungen Konto-, Geräte- oder Diagnoseinformationen verarbeiten | Google Play; App Store von Apple |

Wenn Sie OneDrive oder Google Drive verbinden, fordert MedReminder Zugriff auf App-Daten und Sicherungsdateien an, nicht allgemein auf Ihre übrigen Dateien. Wenn Sie eine Funktion deaktivieren, werden künftige Übertragungen dieser Funktion beendet; den gewährten Zugriff können Sie in den Einstellungen Ihres Microsoft- oder Google-Kontos widerrufen. Bereits beim Anbieter gespeicherte Kopien werden dadurch nicht gelöscht. Cloud- und E-Mail-Dienste verarbeiten Daten nach ihren eigenen Datenschutzbestimmungen.

Unter Windows können Cloud-Sicherung und Synchronisierung auch von Ihnen ausgewählte Ordner verwenden. Ein Cloud-Anbieter kann diese Ordner synchronisieren, oder Sie können sie für andere Personen freigeben; Zugriff und Verarbeitung richten sich nach dem Anbieter und den Ordnereinstellungen. Unter Android verwenden diese Funktionen das verbundene OneDrive- oder Google-Drive-Konto. Android oder die Energiespareinstellungen des Geräts können geplante Vorgänge verzögern. Auf dem iPhone verwenden Cloud-Sicherung und Synchronisierung das verbundene OneDrive- oder Google-Drive-Konto, und iOS entscheidet, wann Hintergrundvorgänge laufen: Es kann sie verzögern oder, bei einer selten geöffneten App, nicht ausführen.

Microsoft Store, Google Play und der App Store können dem Entwickler je nach den Diagnose- und Freigabeeinstellungen Ihres Geräts zusammengefasste Statistiken, etwa Downloads, und Absturzberichte bereitstellen (auf dem iPhone „Mit App-Entwicklern teilen“). Sie enthalten keine in MedReminder gespeicherten Daten.

## 5. Kamera und App-Sperre

Der Barcode-Scanner kann die Windows-Webcam oder die Android-Kamera verwenden, wenn Sie einen Scan starten. Die Bilder werden auf Ihrem Gerät verarbeitet und von MedReminder weder gespeichert noch gesendet. Unter Windows schaltet sich die Kamera aus, wenn ein Code gelesen wird, wenn Sie das Scanfenster schließen oder nach 30 Sekunden. Unter Android richtet sich der Kamerazugriff nach der Android-Berechtigung und dem App-Lebenszyklus. Unter Android liest die Kamera außerdem den QR-Code, den ein anderes Gerät beim Beitritt zu einem Kreis anzeigt; sie wird nur verwendet, solange die Scanseite geöffnet ist, und der Code wird nicht gespeichert. Auf dem iPhone wird die Kamera mit Ihrer Erlaubnis nur verwendet, solange eine Scanseite geöffnet ist, um Medikamenten-Barcodes oder den QR-Code eines Kreises zu lesen; Bilder und Code werden nicht gespeichert.

Unter Android und auf dem iPhone verwendet die optionale App-Sperre die Entsperrung des Telefons: biometrische Merkmale wie Fingerabdruck oder Face ID oder die PIN, das Muster oder den Code der Bildschirmsperre. Die Prüfung übernimmt das Betriebssystem; MedReminder erhält nur das Ergebnis, nie biometrische Daten.

## 6. Links, die den Browser öffnen

Einige Befehle öffnen eine Webseite in Ihrem Browser, etwa die Packungsbeilage oder Informationsseite des Medikaments, das Benutzerhandbuch oder die Projektseite. Unter Windows kann auf Ihre Auswahl hin auch eine Spendenseite von Stripe oder PayPal geöffnet werden. MedReminder sendet keine Daten an diese Seiten; Ihr Browser tut dies gemäß den Richtlinien der jeweiligen Seiten. MedReminder sieht niemals Zahlungsdaten.

## 7. Kinder

MedReminder richtet sich nicht an Kinder. Der Entwickler erhebt über die Anwendung keine personenbezogenen Daten von Kindern.

## 8. Ihre Rechte

Sie können Daten in MedReminder einsehen, berichtigen, exportieren und löschen. Unter Windows verwenden Sie Einstellungen → Sicherung / Wiederherstellung → Alle Daten exportieren; unter Android und auf dem iPhone verwenden Sie die Export- und Datenverwaltungsfunktionen der App. Sie können lokale Android-App-Daten auch in den Android-Einstellungen entfernen oder die App vom iPhone löschen. Kopien in E-Mails, exportierten oder geteilten Dateien sowie OneDrive- oder Google-Drive-Konten verwalten Sie bei den jeweiligen Empfängern, Apps oder Anbietern.

## 9. Änderungen

Änderungen dieser Erklärung werden in dieser Datei mit einem neuen Datum „Zuletzt aktualisiert“ veröffentlicht. Der Verlauf ist im Repository einsehbar:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.de.md

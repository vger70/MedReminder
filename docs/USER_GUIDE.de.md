# MedReminder — Kurzanleitung

Bedienungsanleitung für den Endanwender. Die technische
Architektur ist stattdessen in [`ANALYSIS.md`](ANALYSIS.md)
beschrieben.

> **MedReminder ist eine organisatorische Erinnerung, kein
> Medizinprodukt.** Es liefert keine Diagnosen, keine
> Therapieanweisungen, keine Therapieänderungen und keine
> klinischen Empfehlungen. Jede Therapieentscheidung muss mit
> deinem Arzt getroffen werden.

---

## Erster Start

1. Starte `MedReminder.exe`.
2. Beim ersten Öffnen ist das Fenster leer: die Datenbank wird
   automatisch unter `%LOCALAPPDATA%\MedReminder\medreminder.db`
   angelegt.
3. Oben findest du die Symbolleiste, unten die Statusleiste. Das
   Symbol im Windows-Infobereich bleibt sichtbar, solange die
   Anwendung läuft.

### Windows SmartScreen beim ersten Start

Die veröffentlichten Binärdateien sind nicht codesigniert. Beim
allerersten Start von `MedReminder.exe` zeigt Windows den blauen
Dialog „Der Computer wurde durch Windows geschützt“. Um
fortzufahren:

1. Auf **Weitere Informationen** klicken.
2. Auf **Trotzdem ausführen** klicken.

Windows merkt sich die Entscheidung für diese Datei: bei den
nächsten Starts erscheint der Dialog nicht mehr. Bei einer
Installation über das MSI meldet der UAC-Dialog aus demselben
Grund „Unbekannter Herausgeber“, was zu erwarten ist.

## Ein Medikament hinzufügen

1. Symbolleiste → **Neues Medikament**.
2. Pflichtfelder (mit `*` markiert) ausfüllen: Name, Einheit,
   Dosis pro Einnahme, Einnahmen pro Tag, Startdatum,
   Warnschwelle (verbleibende Tage).
3. Optionale Felder: Wirkstoff, Packung, Therapieende,
   Zuständiger Arzt, Notizen.
4. **Anfangsbestand**: gib die Tabletten/ml/Dosen an, die du
   zum Zeitpunkt der Erfassung bereits besitzt. Eine
   Bestandsbewegung vom Typ `InitialLoad` wird angelegt.
5. **Benachrichtigungskanäle**: aktiviere Windows und/oder
   E-Mail. Für den E-Mail-Kanal müssen die SMTP-Einstellungen
   konfiguriert sein (siehe unten).
6. **Speichern**.

## Bestand hinzufügen (neue Packung)

1. Wähle das Medikament in der Liste aus.
2. Symbolleiste → **Bestand hinzufügen**.
3. Wähle die Bewegungsart:
   - **Neue Packung**: der Normalfall nach einem Einkauf.
   - **Manuelle Zugabe**: z. B. wenn du Muster vom Arzt bekommst.
   - **Positive Korrektur**: du hast weniger gezählt als
     tatsächlich vorhanden war.
4. Gib die Menge (in der Einheit des Medikaments) ein und
   bestätige.

**Wirkung**: der Bestand steigt und die `StockEpoch` des
Medikaments erhöht sich um 1. Damit beginnt der Warnzyklus von
vorn — die nächste Benachrichtigung wird erneut ausgelöst, wenn
der Bestand wieder unter die Schwelle fällt.

## Eine negative Menge korrigieren

Wenn du feststellst, dass der tatsächliche Bestand kleiner ist
als der berechnete (verlorene Tablette, verschüttet usw.):

1. Wähle das Medikament aus.
2. Symbolleiste → **Bestand korrigieren**.
3. Voreingestellt ist **Negative Korrektur**: die eingegebene
   Menge wird vom Bestand abgezogen. Die Epoche wird nicht
   erhöht: der Benachrichtigungszyklus wird nicht neu gestartet.

Würde die Korrektur den Bestand unter null bringen, wird der
Vorgang mit einer Fehlermeldung blockiert.

## Ein Medikament bearbeiten oder deaktivieren

- **Bearbeiten**: Doppelklick auf die Zeile oder Symbolleiste →
  **Bearbeiten**. Du kannst Name, Wirkstoff, Packung, Einheit,
  Warnschwelle, Arzt, Notizen, Enddatum, Benachrichtigungskanäle
  und den Zustand Aktiv/Inaktiv ändern.
  **Dosis und Häufigkeit werden hier NICHT geändert**: dafür ist
  die Planänderung vorgesehen (Kommandozeilenfunktion bzw.
  direkte DB-Bearbeitung im MVP).
- **Deaktivieren**: Symbolleiste → **Deaktivieren**. Das
  Medikament verschwindet aus den automatischen Prüfungen und
  Meldungen, die historischen Daten (Bewegungen,
  Benachrichtigungen) bleiben aus Audit-Gründen in der
  Datenbank.

## E-Mail-Versand konfigurieren

**Einstellungen → E-Mail-SMTP**:

- **Host**: z. B. `smtp.gmail.com`, `smtp-mail.outlook.com`.
- **Port**: meist 587 (StartTLS) oder 465 (direktes SSL/TLS).
  MedReminder verwendet StartTLS, wenn die entsprechende
  Checkbox aktiviert ist.
- **Benutzername / Neues Passwort**: falls der Server eine
  Authentifizierung verlangt. Das Passwort wird mit DPAPI
  verschlüsselt und in
  `%LOCALAPPDATA%\MedReminder\smtp.protected` gespeichert. Es
  landet weder in `smtp.settings.json` noch in den Protokollen.
- **Gespeichertes Passwort entfernen**: beim nächsten Speichern
  wird `smtp.protected` gelöscht.
- **Absender / Absendername**: das „Von“ der versendeten
  E-Mails.
- **Empfänger**: wohin die Meldungen gehen (üblicherweise deine
  eigene persönliche Adresse).
- **Zeitüberschreitung**: Sekunden, bevor die Verbindung als
  fehlgeschlagen gilt.
- **Verbindung testen**: öffnet eine SMTP-Sitzung,
  authentifiziert und schließt sie wieder. Es wird keine echte
  E-Mail versendet.
- **SMTP-Einstellungen speichern**: schreibt
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`. Die
  Konfiguration wird ohne Neustart der Anwendung übernommen.

### Beispiel: Gmail mit App-Passwort

1. Aktiviere die 2-Faktor-Authentifizierung für dein
   Google-Konto.
2. Erstelle ein App-Passwort unter
   `myaccount.google.com/apppasswords`.
3. In MedReminder: Host `smtp.gmail.com`, Port `587`, StartTLS
   aktiviert, Benutzername `deineadresse@gmail.com`, Passwort
   das eben erzeugte App-Passwort.

Google und andere Anbieter können ihre Anforderungen ändern:
konsultiere bei fehlgeschlagenem Verbindungstest die
Dokumentation deines Anbieters.

## Automatischer Windows-Start

**Einstellungen → Autostart**: aktiviere die Checkbox. In
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` wird ein
Eintrag angelegt, der MedReminder mit dem Argument
`--minimized` startet (im Infobereich, Fenster verborgen).
Administratorrechte werden nicht benötigt.

## Datenbanksicherung

**Einstellungen → Sicherung / Wiederherstellung**:

- **Export**: wähle einen Ordner. Die Datenbank wird als
  `medreminder-YYYYMMDD-HHMMSS.db` kopiert. Speichere die
  Kopie auf einem externen Laufwerk oder in einer persönlichen
  Cloud, wenn du Ausfallsicherheit möchtest.
- **Wiederherstellen**: wähle eine frühere Sicherung. Die
  aktuelle Datenbank wird in `medreminder.db.bak-<Zeitstempel>`
  umbenannt (also nicht verloren!) und ersetzt. **Schließe und
  öffne MedReminder erneut** nach der Wiederherstellung, um
  Inkonsistenzen zu vermeiden.

## Jetzt prüfen

Der Monitor läuft automatisch alle 30 Minuten (einstellbar in
`appsettings.json` unter `Monitoring:IntervalMinutes`). Willst
du eine sofortige Prüfung erzwingen: Symbolleiste → **Jetzt
prüfen** oder Menü im Infobereich → **Jetzt prüfen**.

## Symbol im Infobereich

- **Doppelklick** → öffnet das Fenster.
- **Kontextmenü (rechte Maustaste)**:
  - MedReminder öffnen
  - Jetzt prüfen
  - Einstellungen…
  - Beenden

Das Schließen des Hauptfensters über das X minimiert in den
Infobereich; die Anwendung läuft im Hintergrund weiter. Zum
wirklichen Beenden: Menü im Infobereich → **Beenden**.

## Oberflächensprache

**Einstellungen → Allgemein**: wähle die Sprache aus dem
Dropdown-Menü und klicke auf **Sprache speichern**. MedReminder
startet automatisch neu, um die Änderung zu übernehmen.

Hinweise:
- Windows-Toast-Benachrichtigungen folgen immer der
  Systemsprache (Windows), unabhängig von der hier gewählten
  Sprache.
- E-Mail-Benachrichtigungen und der Therapieplan verwenden die
  hier gewählte Sprache.

## Diagnose

- **Protokolle**:
  `%LOCALAPPDATA%\MedReminder\logs\medreminder-YYYYMMDD.log`.
  Enthält Scheduler-Ticks, Benachrichtigungsversand und Fehler.
- **Beschädigte oder inkompatible Datenbank**: lösche
  `medreminder.db`, `medreminder.db-shm`, `medreminder.db-wal`
  unter `%LOCALAPPDATA%\MedReminder\`. Beim nächsten Start wird
  die Datenbank leer neu erstellt. Erstelle vorher eine manuelle
  Sicherung, wenn du wichtige Daten hast.
- **Anwendung läuft bereits**: nur eine Instanz pro
  Windows-Benutzer. Wenn der Start meldet „läuft bereits“, suche
  das Symbol im Infobereich.

## Was MedReminder NICHT tut

- Es erinnert nicht an eine konkrete Einnahme (es ist kein
  Wecker).
- Es liefert keine Therapieanweisungen und keine
  Wechselwirkungen.
- Es synchronisiert nicht zwischen mehreren Geräten.
- Es bestellt keine Medikamente automatisch.
- Es kontaktiert deinen Arzt nicht direkt.

Sein einziger Zweck ist es, dich rechtzeitig darüber zu
informieren, dass du ein neues Rezept anfordern musst.

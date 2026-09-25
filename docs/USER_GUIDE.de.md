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
2. Beim allerersten Start zeigt die Anwendung einen
   **Willkommens-Assistenten** und fordert dich auf, das erste
   Profil anzulegen. Dieses Profil ist immer der
   **Administrator**: es verwaltet den gemeinsamen E-Mail-Server
   und die automatische Sicherung und kann die weiteren Profile
   anlegen (siehe *Mehrere Profile*). Im selben Assistenten kannst
   du eine optionale PIN festlegen.
3. Die Datenbank wird automatisch unter
   `%LOCALAPPDATA%\MedReminder\profiles\<profil-id>\medreminder.db`
   angelegt.
4. Oben findest du die Symbolleiste, unten zeigt die Statusleiste
   das aktive Profil an („Profil: Owner (Administrator)“ für einen
   Administrator, „Profil: Oma“ für ein normales Benutzerprofil).
   Das Symbol im Windows-Infobereich bleibt sichtbar, solange die
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
6. **Schema**: belasse es auf **Einfach** für eine feste
   Tagesdosis — das ist der Standard und entspricht dem
   bisherigen Verhalten der App. Für zyklische, absteigende,
   wöchentliche oder Bedarfstherapien siehe *Komplexe Schemata*
   unten.
7. **Speichern**.

## Komplexe Schemata

Nicht jede Therapie verbraucht jeden Tag die gleiche Menge
Medikament. Im Formular **Neues Medikament** wechselt der
Selektor *Schema* von **Einfach** (feste Tagesdosis) auf
**Erweitert** und blendet ein Dropdown *Regime-Typ* mit vier
weiteren Formen ein:

- **Wochenmuster** — eine unterschiedliche Menge für jeden
  Wochentag (zum Beispiel ein oraler Gerinnungshemmer mit
  anderen Dosen an Mo/Mi/Fr als an den übrigen Tagen).
- **Zyklisch (N Tage an / M aus)** — eine feste Menge für die
  ersten `N` Tage des Zyklus, gefolgt von `M` Tagen ohne
  Einnahme. Typisch für Hormontherapien und Kortison-Stöße.
- **Ausschleichen** — eine Dosis, die schrittweise bis zur
  Enddosis sinkt (oder steigt). Im Ausschleichen-Panel stehen über
  den Selektor *Linear / Stufenweise* zwei Varianten zur Verfügung:
  - **Linear** — die Dosis verringert sich alle X Tage um einen
    festen Schritt, bis die Enddosis erreicht ist, und bleibt dann.
    Typisch für ein einfaches Ausschleichen von Glukokortikoiden.
  - **Stufenweise** — eine explizite Liste von Stufen, jede mit
    eigener Dosis und Dauer in Tagen (zum Beispiel 4/Tag für 7
    Tage, dann 2/Tag für 7 Tage, dann 1/Tag für 14 Tage). Verwende
    *Stufe hinzufügen* / *Entfernen*, um die Sequenz aufzubauen,
    und prüfe die Live-Vorschau unter der Liste vor dem Speichern.
    Aktiviere *Letzte Dosis als Erhaltungsdosis beibehalten*, wenn
    die letzte Dosis unbegrenzt weiterlaufen soll, anstatt die
    Therapie zu beenden.
- **Bei Bedarf (PRN)** — kein geplanter Verbrauch. MedReminder
  verfolgt weiterhin den Bestand, aber die Spalte *verbleibende
  Tage* bleibt leer, bis sich die Schema-Form ändert.

Wenn Erweitert ausgewählt ist, werden die Felder *Dosis pro
Einnahme*, *Einnahmen pro Tag* und *Einnahmezeitpunkte* oben im
Formular deaktiviert: Das Schema, das du unten konfigurierst, ist
die einzige Quelle für die Tagesmenge. Um zum "Ein-Klick"-Ablauf
mit fester Dosis zurückzukehren, stelle den Selektor wieder auf
Einfach.

Um die Form einer laufenden Therapie zu ändern, verwende
*Symbolleiste → Schema ändern*. Dort steht derselbe
Einfach/Erweitert-Selektor zur Verfügung und wirkt ab dem *Gültig
ab*-Datum, das du wählst — das vorherige Schema bleibt für die
Tage davor gültig.

MedReminder ist kein Medizinprodukt: Es prüft keine maximalen
Tagesdosen, warnt nicht vor Überdosierungen und kontrolliert
keine Wechselwirkungen zwischen Medikamenten. Es folgt lediglich
der von deinem Arzt verordneten Therapie und erinnert dich, bevor
der Bestand zur Neige geht.

## Erinnerung zur Einnahmezeit

Für Medikamente mit **Einnahmezeiten** (eine feste Uhrzeit, die für
jeden Einnahmeslot festgelegt ist) kannst du MedReminder bitten, dich
*genau dann zu erinnern, wenn die Dosis fällig ist*. Aktiviere **Zur
Einnahmezeit erinnern** im Formular zum Hinzufügen oder Bearbeiten des
Medikaments. Die Option ist nur verfügbar, wenn das Medikament
mindestens einen Slot mit Uhrzeit hat und noch Bestand vorhanden ist;
andernfalls bleibt sie ausgegraut.

Wenn sie aktiviert ist, zeigt MedReminder zu jeder Slot-Uhrzeit eine
Desktop-Benachrichtigung an („Zeit für die Einnahme von …"). Wenn du
E-Mail-Benachrichtigungen eingerichtet und den E-Mail-Kanal für dieses
Medikament ausgewählt hast, wird dieselbe Erinnerung auch per E-Mail
versendet.

Einige nützliche Details:

- **Eine Erinnerung pro Slot und Tag.** Jeder Slot mit Uhrzeit löst an
  einem bestimmten Kalendertag höchstens einmal aus, auch wenn die App
  neu gestartet wird.
- **Toleranzfenster.** Wenn die App nicht genau zur Slot-Uhrzeit läuft
  — zum Beispiel weil der Computer im Ruhezustand war — löst die
  Erinnerung trotzdem bei der nächsten Prüfung der App aus, sofern dies
  innerhalb von 30 Minuten nach der Slot-Uhrzeit geschieht. Nach diesem
  Fenster gilt die Dosis als verpasst und es wird keine Erinnerung
  angezeigt; MedReminder führt kein Protokoll verpasster Dosen und gibt
  niemals klinische Ratschläge.
- **Bestand null schaltet sie aus.** Wenn der Bestand null erreicht,
  wird keine Erinnerung gesendet, weil nichts mehr einzunehmen ist.
- **Sommerzeit.** In der Nacht der Zeitumstellung auf Sommerzeit löst
  ein Slot, der in die übersprungene Stunde fällt, nicht aus (diese
  Uhrzeit existiert nicht). In der Nacht der Umstellung auf Winterzeit
  löst der Slot wie gewohnt einmal aus.

Diese Erinnerung ist nur ein praktischer Hinweis. Sie erfasst nicht, ob
du die Dosis eingenommen hast, und ändert den Bestand nicht — dafür
nutze *Einnahme erfassen*.

## Referenzkatalog (mehrere Länder)

MedReminder liefert zwei Momentaufnahmen eines
Referenzarzneimittelkatalogs mit und nutzt sie, um das
Medikamentenformular automatisch zu vervollständigen.

- Beginne in den Feldern **Handelsname** und **Wirkstoff** zu
  tippen, um Treffer zu sehen. Ein Klick auf einen Eintrag füllt
  auch das andere Feld (und im Hintergrund die technischen Felder
  nationaler Code und ATC-Code), sodass du nicht beides eintippen
  musst.
- Die Dropdown-Liste zeigt höchstens 20 Zeilen und aktualisiert
  sich etwa 150 ms nach dem letzten Tastendruck. Ein roter Punkt
  neben einer Zeile bedeutet, dass das Produkt **ausgesetzt oder
  zurückgezogen** ist: du kannst es trotzdem auswählen, MedReminder
  weist nur auf den Status hin.
- **Medikament nicht im Katalog?** Tippe einfach weiter, was du
  weißt. Wenn du keine Zeile aus der Liste auswählst, speichert
  MedReminder deinen Text unverändert und speichert keine
  Katalogverknüpfung — die Erinnerung funktioniert genau wie zuvor.
- Das **Referenzland** wird unter *Einstellungen → Allgemein →
  Referenzland* gewählt. Standard ist Italien; eine Änderung wirkt
  beim nächsten Öffnen des Medikamentenformulars.

### Zentral zugelassene EU-Arzneimittel

Einige Arzneimittel sind über das *zentralisierte Verfahren* in der
gesamten Europäischen Union zugelassen; das Verfahren wird von der
Europäischen Arzneimittel-Agentur (EMA) durchgeführt. MedReminder
enthält den EMA-EPAR-Katalog — *European public assessment
reports* — und zeigt diese Arzneimittel in derselben
Autovervollständigungs-Dropdown-Liste an.

- Ist dein **Referenzland ein EU-Mitgliedstaat** (z. B. das
  standardmäßig eingestellte Italien oder ein anderes in den
  Einstellungen ausgewähltes EU-Land), zeigt die
  Autovervollständigung **deinen nationalen Katalog + die
  EU-weit zentral zugelassenen Arzneimittel**, gemischt in
  derselben Liste. Du musst nichts umstellen: EU-Zeilen erscheinen
  automatisch, wenn sie passen.
- Stellst du das **Referenzland auf `EU`**, zeigt die
  Autovervollständigung **nur** die EU-zentralisiert zugelassenen
  Arzneimittel, ohne nationale Zeilen. Nützlich, wenn du gezielt
  ein Produkt seiner EMA-Zulassung zuordnen willst.
- Ein EU-Arzneimittel und ein entsprechendes nationales Produkt
  können gleichzeitig in der Liste erscheinen; die beiden Zeilen
  werden nicht dedupliziert. Wähle die, die zur Packung in deiner
  Hand passt.

### Spanischer und französischer nationaler Katalog

Der spanische Katalog stammt aus AEMPS CIMA
(„Medicamentos"-Register) und der französische Katalog aus ANSM
BDPM (*Base de données publique des médicaments*). In der
Autovervollständigung verhalten sie sich genau wie der
italienische Katalog:

- Setze **Einstellungen → Allgemein → Referenzland** auf `ES`
  oder `FR`, sobald der entsprechende Snapshot geladen ist (`ES`
  und `FR` erscheinen automatisch im Dropdown, sobald ihre
  Kataloge in der Datenbank sind).
- Die Autovervollständigung listet dann **deinen nationalen
  Katalog + die EU-weit zentral zugelassenen Arzneimittel**,
  gemischt in derselben Liste. Spanien und Frankreich sind
  EU-Mitgliedstaaten, daher werden EU-Zeilen standardmäßig
  einbezogen — genauso wie für Italien.
- Alle übrigen Regeln bleiben identisch: eine Zeile auswählen,
  um beide Seiten auszufüllen, oder weitertippen, um einen
  freien Text zu speichern, den die Anwendung nicht kennt.

**Datenquellen und Nutzungsbedingungen.** Der italienische Katalog
stammt aus den offenen Daten der AIFA (Agenzia Italiana del
Farmaco), veröffentlicht unter der Creative Commons Attribution
4.0 International-Lizenz (CC BY 4.0). Der EU-Katalog stammt aus
dem EMA-EPAR-Datensatz, weiterverwendet gemäß dem rechtlichen
Hinweis der EMA (Beschluss 2011/833/EU über die Weiterverwendung
von Kommissionsdokumenten). Der spanische Katalog stammt aus
AEMPS CIMA, weiterverwendet gemäß der spanischen Regelung zur
Weiterverwendung von Informationen des öffentlichen Sektors
(Gesetz 37/2007). Der französische Katalog stammt aus ANSM BDPM,
weiterverwendet unter Licence Ouverte Etalab 2.0. Der Info-Dialog
und die Datei `THIRD-PARTY-NOTICES.md` im Installationsstamm
enthalten die vollständigen Namensnennungen.

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
  **Dosis und Häufigkeit werden hier NICHT geändert**: verwende
  *Symbolleiste → Schema ändern* (siehe *Komplexe Schemata* oben).
- **Deaktivieren**: Symbolleiste → **Deaktivieren**. Das
  Medikament verschwindet aus den automatischen Prüfungen und
  Meldungen, die historischen Daten (Bewegungen,
  Benachrichtigungen) bleiben aus Audit-Gründen in der
  Datenbank.

## Mehrere Profile und Rollen Administrator/Benutzer

MedReminder kann Medikamente für **mehrere Personen** aus demselben
Windows-Konto verwalten — typischer Fall: ein Elternteil, das die
eigene Therapie und die eines oder zweier Angehöriger begleitet.
Jedes Profil hat seine eigene Datenbank und seinen eigenen
E-Mail-Empfänger; der SMTP-Server, der Ordner für die automatische
Sicherung und das Profilregister werden gemeinsam genutzt und vom
**Administrator-Profil** verwaltet.

### Rollen

- **Administrator** — verwaltet die globalen Einstellungen (SMTP,
  Sicherung, Profilliste, PIN eines beliebigen Profils) zusätzlich
  zu den eigenen Daten. Es muss immer mindestens einen
  Administrator geben.
- **Benutzer** — verwaltet nur das eigene Profil (Medikamente,
  Bestand, Therapien, persönlicher E-Mail-Empfänger). Sieht in den
  Einstellungen weder die SMTP- noch die Sicherungs-Registerkarte
  und sieht `Extras → Profile verwalten…` nicht.

Die Rolle wird bei der Profilerstellung gewählt und kann
**danach nicht mehr geändert werden**. Wenn du in Zukunft die Rolle
eines Profils ändern möchtest, ist der aktuelle Weg, ein neues
Profil mit der gewünschten Rolle anzulegen und die Daten
darüberzukopieren.

Die Rolle ist eine „weiche“ Hürde: wer Zugriff auf das Dateisystem
hat, kann `profiles.json` von Hand ändern und zum Administrator
werden. Die Benutzeroberfläche respektiert die Rolle, das
Dateisystem nicht.

### Weitere Profile anlegen (Administrator)

1. `Extras → Profile verwalten…` — dieser Eintrag existiert nur
   für Administratoren.
2. **Neues Profil** → Name eingeben, Administrator oder Benutzer
   wählen (Standard: Benutzer), optional eine PIN setzen.
   Bestätigen.
3. Das neue Profil erscheint beim nächsten Start sofort im
   Profilauswahldialog.

### Profil wechseln

`Datei → Profil wechseln…` öffnet die Profilauswahl. Wähle das
Zielprofil und bestätige: die Anwendung startet automatisch neu,
damit das neue Profil vollständig isoliert läuft. Hat das
gewählte Profil eine PIN, wird die Abfrage vor dem Öffnen der
Anwendung angezeigt.

### Umbenennen, PIN ändern, löschen

`Extras → Profile verwalten…` (nur Administrator) bietet außerdem:

- **Umbenennen** — nur den Anzeigenamen. Die interne ID ändert
  sich nie.
- **PIN ändern** — PIN eines beliebigen Profils setzen,
  aktualisieren oder entfernen.
- **Löschen** — fragt, den **Profilnamen einzutippen**, um zu
  bestätigen. Eine separate Auswahlbox erlaubt zusätzlich das
  Löschen der Profildaten auf der Festplatte; sie ist
  standardmäßig deaktiviert, damit der Ordner für eine manuelle
  Wiederherstellung erhalten bleibt.

Das aktive Profil kann nicht gelöscht werden (wechsle vorher das
Profil), ebenso wenig der letzte verbleibende Administrator.

### Zur PIN

Die PIN ist eine **Hürde, kein Schutz**. Sie verhindert
versehentliche Profilwechsel, **verschlüsselt** die Daten aber
nicht — jeder mit Zugriff auf diesen PC kann die Profildateien
weiterhin öffnen. Drei Fehlversuche schließen die Abfrage und die
Anwendung.

Wenn du eine PIN vergessen hast, entferne sie von Hand aus
`%LOCALAPPDATA%\MedReminder\profiles.json` (lösche `PinHash` und
`PinSalt` und setze `PinIterations` für den betroffenen Eintrag
auf `0`). Das ist absichtlich so dokumentiert und nicht durch
einen „PIN zurücksetzen“-Ablauf gelöst: die Wiederherstellung ist
kein Fehler, weil die PIN keine Sicherheit ist.

### Aufbau auf der Festplatte

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                        ← Profilregister
├── smtp.settings.json                   ← gemeinsames SMTP (admin)
├── smtp.protected                       ← DPAPI-verschlüsseltes Passwort
├── backup.settings.json                 ← gemeinsame Backup-Config (admin)
├── backup.state.json                    ← Status der letzten automatischen Sicherung
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    ├── <profil-id>\                     ← ein Ordner pro Profil
    │   ├── medreminder.db (+ -wal, -shm)
    │   └── notifications.settings.json  ← ToAddress dieses Profils
    └── …
```

### Die automatische Sicherung erfasst alle Profile

Wenn die automatische Sicherung aktiv ist, sichert jeder
tägliche Lauf die Datenbank **jedes** Profils im gemeinsamen
Ordner, mit Dateinamen der Form
`medreminder-<profil-id>-YYYYMMDD-HHmmss.db`. Die Aufbewahrung
wird pro Profil angewendet, sodass die neueste Sicherung eines
Profils die älteren Sicherungen eines anderen Profils nicht
schützt.

Bei der Wiederherstellung über `Einstellungen → Sicherung →
Sicherung wiederherstellen…` fragt der Dialog, welches Profil die
importierte Datenbank erhalten soll. Standardmäßig wählt er das
Profil, das im Dateinamen angegeben ist. Bei einer
Wiederherstellung in ein anderes als das aktive Profil startet die
Anwendung nicht neu; bei einer Wiederherstellung in das aktive
Profil startet sie neu, um die neue Datenbank sauber zu öffnen.

### Automatischer Windows-Start

Der Windows-Autostart-Eintrag ist pro Windows-Benutzer eindeutig.
Beim Anmelden öffnet die Anwendung das **zuletzt** verwendete
Profil ohne den Auswahldialog anzuzeigen; hat dieses Profil eine
PIN, wird die Abfrage über dem leeren Fenster angezeigt. Um beim
Autostart ein anderes Profil zu öffnen, verwende
`Datei → Profil wechseln…`, sobald die Anwendung offen ist.

### Aktualisierung von einer Einzelbenutzer-Installation

Falls du bereits eine `medreminder.db`-Datei unter
`%LOCALAPPDATA%\MedReminder\` aus einer älteren Version hast,
führt die Anwendung beim nächsten Start eine einmalige
**V1 → V2-Migration** aus:

1. Sie legt eine verpflichtende Sicherung unter
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   an, die die ursprüngliche `medreminder.db` (und ihre
   Nebendateien) sowie die ursprüngliche `smtp.settings.json`
   enthält.
2. Sie verschiebt die Datenbank nach
   `profiles\default\medreminder.db` und legt die initiale
   `profiles.json` mit einem einzigen Administrator-Profil namens
   `User` an.
3. Sie extrahiert den Empfänger (`Smtp.ToAddress`) aus
   `smtp.settings.json` nach
   `profiles\default\notifications.settings.json`.

Die Migration ist **atomar** — schlägt ein Schritt nach der
Vorab-Sicherung fehl, kehrt die Anwendung in den V1-Zustand
zurück und behält die Pre-Migration-Sicherung.

Die **Pre-Migration-Sicherung wird nicht automatisch aufgeräumt**:
Nachdem du geprüft hast, dass die migrierte Anwendung dieselben
Daten öffnet, kannst du den Ordner `backups\pre-migration-*` von
Hand löschen. Benenne das Profil `User` unter
`Extras → Profile verwalten… → Umbenennen` nach Belieben um.

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

## Benachrichtigungen für Pflegepersonen

**Einstellungen → Benachrichtigungen → Pflegeperson-E-Mail (optional)**.

Ein Profil kann eine zweite Empfängeradresse angeben — zum Beispiel ein
Familienmitglied oder eine Pflegeperson, die die Rezepterneuerung für
dich erledigt. Wenn dieses Feld gesetzt ist, wird jede E-Mail an den
Hauptempfänger gleichzeitig auch an die Pflegeperson in der **selben**
Nachricht gesendet. Nichts anderes ändert sich: Transport,
Nachrichteninhalt und Sendezeitpunkte sind exakt wie bisher.

- **Aktivieren**: gib die E-Mail-Adresse der Pflegeperson ein und
  speichere.
- **Deaktivieren**: leere das Feld und speichere. Ein leeres Feld
  bedeutet keine konfigurierte Pflegeperson — das Standardverhalten.
- **Beide Adressen sind für beide Empfänger sichtbar**: die
  Pflegeperson und der Hauptempfänger können die Adresse des anderen
  in der E-Mail sehen. Das ist beabsichtigt, damit eine Antwort alle
  erreicht.
- Die Pflegeperson-Adresse darf nicht mit der des Hauptempfängers
  übereinstimmen und muss eine gültige E-Mail-Adresse sein;
  andernfalls wird das Speichern mit einer Meldung abgelehnt.

Die Einstellung gilt pro Profil: die Pflegeperson eines Profils ist
nicht die Pflegeperson eines anderen Profils.

## Autostart konfigurieren

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

## Export und Import

Neben der einfachen Datenbanksicherung kann MedReminder eine einzelne
**verschlüsselte, portable Datei** mit allen deinen Daten erstellen.
Anders als eine normale Sicherung ist diese Datei nicht an dein
Windows-Konto oder deinen PC gebunden — sie ist daher der empfohlene
Weg, MedReminder auf einen neuen Computer umzuziehen.

**Einstellungen → Sicherung → Alle Daten exportieren
(verschlüsselt)…**:

- Wähle, wo die Datei gespeichert werden soll (Endung `.mrz`).
- Wähle eine **Passphrase** (mindestens 12 Zeichen) und gib sie
  zweimal ein.
- Aktiviere optional gemeinsam genutzte Einstellungen: SMTP-
  Einstellungen, SMTP-Passwort, Sicherungseinstellungen,
  Benutzereinstellungen (Sprache und Referenzland des Katalogs). Alle
  sind standardmäßig deaktiviert. Wenn du das SMTP-Passwort
  einschließt, wird es mit deiner Passphrase neu verschlüsselt — es
  wird nie im Klartext gespeichert.
- Klicke auf **Exportieren**.

**Administrator: alle Profile auf einmal.** Wenn mehr als ein Profil
existiert, sieht ein Administratorprofil zusätzlich **Alle Profile
exportieren**. Wähle einen Ordner statt einer Datei: MedReminder
schreibt eine verschlüsselte Datei pro Profil
(`medreminder-export-<profileId>-<timestamp>.mrz`), alle mit derselben
Passphrase. Um ein Profil wiederherzustellen, öffne dieses Profil und
importiere seine Datei.

**Die Passphrase kann nicht wiederhergestellt werden.** Es gibt keinen
Reset, keine Hintertür und keine Server-Kopie. Wenn du die Passphrase
verlierst, kann die Datei nie wieder gelesen werden — bewahre sie sicher
auf.

**Einstellungen → Sicherung → Aus Export importieren…**:

- Wähle die `.mrz`-Datei. MedReminder zeigt den Inhalt an (Version,
  Datum, Umfang, enthaltene Einstellungen), bevor irgendetwas passiert.
- Gib die Passphrase ein.
- Hake **„Ich verstehe, dass dies die Daten des aktuellen Profils
  überschreibt."** ab. Der Import ersetzt die Daten des aktuellen
  Profils vollständig — es gibt keinen Zusammenführungsmodus. Vorher
  wird eine Sicherungskopie als `medreminder.db.bak-<Zeitstempel>`
  aufbewahrt.
- Wurde die Datei aus einem anderen Profil exportiert, fragt
  MedReminder nach einer Bestätigung: Der Import ersetzt die Daten des
  aktiven Profils durch die des anderen Profils.
- Klicke auf **Importieren** und **starte** MedReminder neu, wenn
  aufgefordert, damit die importierten Daten sauber geladen werden.

Wenn die Passphrase falsch ist, die Datei beschädigt ist oder von einer
neueren Version von MedReminder stammt, bricht der Import mit einer
klaren Meldung ab und deine aktuellen Daten bleiben unverändert.

Das Archivformat ist öffentlich dokumentiert in
[`docs/EXPORT-FORMAT.md`](EXPORT-FORMAT.md) — deine Daten sind also
niemals eingesperrt und können bei Bedarf mit Standardwerkzeugen
entschlüsselt werden.

## Sicherung in einen Cloud-Ordner

MedReminder kann die tägliche automatische Sicherung zusätzlich als
**verschlüsselten Snapshot** in einen lokalen Ordner schreiben, den
dein Betriebssystem bereits synchronisiert (OneDrive, iCloud Drive,
Dropbox, Google Drive Desktop, …). So bringst du deine Daten ohne
eigenen Server kostengünstig von einem „Heim-PC" auf einen
„Arbeits-PC", und eine Kopie liegt außerhalb des Rechners, falls die
Festplatte ausfällt.

**Dies ist keine Echtzeit-Synchronisation.** MedReminder schreibt
höchstens einen Snapshot pro Tag, und es sollte immer nur ein Rechner
schreiben. Wenn du zwischen zwei Snapshots auf zwei Geräten
Medikamente bearbeitest, laufen die beiden Kopien auseinander — und
die nächste Wiederherstellung überschreibt die Daten des Rechners, auf
dem du wiederherstellst. Lege vorab fest, welches Gerät das „aktive"
ist, und stelle auf dem anderen nur wieder her, wenn du wechselst.

### Einrichtung auf dem ersten Gerät

**Einstellungen → Sicherung → Sicherung in synchronisierten Ordner
(verschlüsselt)**:

- Setze das Häkchen.
- Wähle einen Ordner innerhalb des lokalen Synchronisationsordners
  deines Cloud-Anbieters (zum Beispiel
  `C:\Users\<Name>\OneDrive\MedReminder`). MedReminder kommuniziert
  nie selbst mit OneDrive / iCloud / Dropbox — es schreibt nur die
  Dateien dorthin, und der Synchronisationsdienst des Systems lädt
  sie hoch.
- Lege die Anzahl der zu behaltenden Snapshots fest (Standard: 30).
- Klicke neben Backup-Passphrase auf **Festlegen / ändern…** und wähle
  eine Passphrase (mindestens 12 Zeichen). Diese Passphrase verlässt
  den Rechner nie.
- Speichere.

Ab dem nächsten täglichen Durchlauf schreibt MedReminder
`medreminder-<profileId>-<timestamp>.mrz` in den Ordner. Die Datei ist
mit einem aus deiner Backup-Passphrase abgeleiteten Schlüssel
verschlüsselt; der Cloud-Anbieter sieht deine Daten nie im Klartext.

Für **jedes Profil** auf dem Rechner wird ein Snapshot geschrieben,
wie bei der lokalen Sicherung, und alle werden mit derselben
Backup-Passphrase verschlüsselt. Wer die Passphrase kennt, kann daher
die Daten aller Profile lesen, auch die von PIN-geschützten Profilen.

### Einrichtung auf dem zweiten Gerät

- Installiere MedReminder.
- **Einstellungen → Sicherung → Festlegen / ändern…** und gib
  **dieselbe** Backup-Passphrase ein wie auf dem ersten Gerät. Das ist
  der einzige unverzichtbare Schritt: Ohne dieselbe Passphrase kann
  der zweite Rechner nicht entschlüsseln, was der erste geschrieben
  hat.
- Der tägliche automatische Snapshot bleibt auf dem zweiten Gerät
  ausgeschaltet — er wird nur auf einem Rechner gebraucht.

### Wiederherstellung auf dem zweiten Gerät

**Einstellungen → Sicherung → Aus Cloud-Ordner wiederherstellen…**:

- Wähle im Dialog den lokalen Synchronisationsordner (denselben, in
  den das erste Gerät schreibt).
- Wähle den neuesten Snapshot aus der Liste. Jede Zeile zeigt das
  Datum, den Profilnamen (oder die Profil-ID, wenn das Profil auf
  diesem Rechner nicht existiert) und einen kurzen „Geräte-Hash",
  damit du Snapshots verschiedener Rechner unterscheiden kannst. Der
  Geräte-Hash ist ein SHA-256-Fingerabdruck des Hostnamens des
  Ursprungsrechners — genug, um Snapshots nach Herkunft zu gruppieren,
  nicht genug, um den Rechner zu identifizieren.
- Vorausgewählt ist der neueste Snapshot des aktiven Profils. Die
  Wiederherstellung überschreibt immer das **aktive** Profil: Um ein
  anderes Profil wiederherzustellen, wechsle zuerst zu diesem Profil.
  Wählst du den Snapshot eines anderen Profils, fragt MedReminder nach
  einer Bestätigung, bevor die Daten des aktiven Profils damit ersetzt
  werden.
- Setze das Häkchen bei **„Ich verstehe, dass dies die Daten des
  aktuellen Profils überschreibt."** — die Wiederherstellung
  überschreibt immer.
- Klicke auf **Wiederherstellen**. MedReminder entschlüsselt den
  Snapshot, ersetzt die Datenbank des aktuellen Profils und fordert
  dich zum Neustart auf.

### Hinweise

- **Passphrase verloren heißt Daten verloren.** Es gibt kein
  Zurücksetzen. Die Passphrase wird lokal gespeichert, verschlüsselt
  mit den Anmeldedaten deines Windows-Kontos; sie verlässt den Rechner
  nie und erscheint nie in der Cloud.
- Der tägliche automatische Snapshot enthält **nicht** das
  SMTP-Passwort und nicht deine Benutzereinstellungen — dafür nutze
  den oben beschriebenen einmaligen verschlüsselten Export mit den
  Kästchen für gemeinsam genutzte Einstellungen.
- Die Aufbewahrung von MedReminder löscht alte Dateien nur aus dem
  sichtbaren Ordner. Dein Cloud-Anbieter behält gelöschte Dateien
  wahrscheinlich in seinem eigenen Papierkorb (OneDrive: standardmäßig
  30 Tage) — MedReminder kann diesen nicht für dich leeren und
  versucht es auch nicht.
- Lege die aktive Datenbankdatei **nicht** in einen synchronisierten
  Ordner. Dorthin gehören nur die verschlüsselten `.mrz`-Snapshots.

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

## Entwicklung unterstützen

Wenn der Betreuer sie aktiviert hat, öffnet der Eintrag **? →
Entwicklung unterstützen…** ein kleines Fenster, in dem Sie das Projekt
ganz freiwillig unterstützen können. Es ist optional und für die
Nutzung von MedReminder niemals erforderlich.

- Wählen Sie einen festen Betrag (2 €, 5 €, 10 €, 20 €) oder, sofern
  angeboten, einen **individuellen Betrag**.
- Wählen Sie eine Zahlungsmethode (Stripe oder PayPal).
- Klicken Sie auf **Mit … fortfahren**: MedReminder öffnet die
  offizielle Zahlungsseite des Anbieters in Ihrem Standardbrowser.

Bei einem individuellen Betrag wählen Sie die genaue Summe **auf der
Seite des Anbieters**, nicht in MedReminder. Die Anwendung wickelt die
Zahlung nie selbst ab, sieht Ihre Kartendaten nicht und kann nicht
bestätigen, dass eine Zahlung abgeschlossen wurde – sie öffnet nur die
Seite. Hat der Betreuer diese Funktion nicht konfiguriert, erscheint
der Menüeintrag nicht.

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

- Es erfasst nicht, ob du eine Dosis eingenommen hast, verfolgt
  keine Therapietreue und warnt nicht bei verpassten Dosen (die
  Einnahme-Erinnerung ist nur ein praktischer Hinweis, kein
  Adherence-System).
- Es liefert keine Therapieanweisungen und keine
  Wechselwirkungen.
- Es synchronisiert nicht zwischen mehreren Geräten.
- Es bestellt keine Medikamente automatisch.
- Es kontaktiert deinen Arzt nicht direkt.

Sein einziger Zweck ist es, dich rechtzeitig darüber zu
informieren, dass du ein neues Rezept anfordern musst.

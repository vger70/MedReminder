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

Liegt das *Gültig ab*-Datum in der Vergangenheit, wird der bereits
gebuchte automatische Verbrauch ab diesem Datum bei der nächsten
Prüfung mit dem neuen Schema neu berechnet. Bestandsbewegungen, die
vor der Installation dieser Version erfasst wurden, werden nie neu
berechnet.

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
  beim nächsten Öffnen des Medikamentenformulars. Das Land gilt für
  die ganze Installation: Nur ein Administrator kann es ändern.

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

## Den Barcode der Packung scannen

Ist der Referenzkatalog aktiviert, hat das Medikamentenformular neben
dem Handelsnamen die Schaltfläche **Barcode scannen…**. Sie füllt das
Medikament in einem Schritt aus dem Katalog aus.

1. Klicken Sie auf **Barcode scannen…**. Ein kleines Fenster öffnet
   sich und wartet auf den Code.
2. Scannen Sie den Barcode auf der Schachtel mit einem USB-
   Barcodescanner, oder geben Sie den unter dem Barcode aufgedruckten
   Code ein und drücken Sie die **Eingabetaste**.
3. Ist der Code im Katalog, schließt sich das Fenster und das
   Formular wird so ausgefüllt, als hätten Sie die Zeile in der
   Auswahlliste gewählt. Andernfalls zeigt eine Meldung den gelesenen
   Code an, und es wird nichts geändert.

Hinweise:

- Klicken Sie zuerst auf **Barcode scannen…** und scannen Sie dann.
  Ein Scan, während das Medikamentenformular selbst den Fokus hat,
  schreibt den Code in das aktive Feld.
- Auf italienischen Packungen liest der Scanner den **AIC-Code** (den
  Barcode mit dem Text `A` gefolgt von 9 Ziffern). Jeder USB-Scanner
  für 1D-Codes ist geeignet; wird der Code nicht erkannt, aktivieren
  Sie in den Scannereinstellungen die Symbologie **Code 32** (Italian
  Pharmacode). Der quadratische 2D-Code (DataMatrix) erfordert einen
  2D-Scanner und entspricht oft noch keinem Katalogeintrag.
- Der Scanner muss dasselbe Tastaturlayout wie Windows verwenden. Bei
  einer deutschen Tastatur (QWERTZ) stellen Sie den Scanner auf
  dieses Layout ein, sonst wird der Code nicht erkannt.
- Auch ein Scanner, der nach dem Code keine Eingabetaste sendet,
  funktioniert: Der Code wird einen Moment nach dem Scan übernommen.

### Mit der Webcam

Kein Scanner? Klicken Sie im Scanfenster auf **Webcam verwenden**. Die
Kamera schaltet sich erst dann ein und wieder aus, sobald ein Code
gelesen wurde, Sie auf **Scanner verwenden** klicken, das Fenster
schließen oder 30 Sekunden ohne Code vergehen.

- Halten Sie die Packung 10–20 cm vor die Kamera, mit dem Barcode im
  gestrichelten Rahmen, gut beleuchtet und scharf. Laptop-Webcams mit
  Fixfokus tun sich mit dem schmalen AIC-Barcode oft schwer; ein
  USB-Scanner ist zuverlässiger.
- Blockiert Windows die Kamera, meldet das Fenster dies und bietet
  **Datenschutzeinstellungen öffnen** an: Aktivieren Sie unter
  Einstellungen → Datenschutz und Sicherheit → Kamera die Option
  **Desktop-Apps den Zugriff auf Ihre Kamera erlauben** und klicken
  Sie dann auf **Erneut versuchen**.
- Kamerabilder werden nie gespeichert oder gesendet; verwendet wird
  nur der gelesene Code.

### Per Scan auffüllen

Wenn Sie eine neue Packung eines Medikaments kaufen, das schon in
Ihrer Liste steht, verwenden Sie **Bestand → Per Barcode auffüllen…**.
Sie müssen das Medikament nicht vorher auswählen: Der Scan findet es.

1. Scannen Sie die Packung wie oben mit dem Scanner oder der Webcam.
2. Das Medikament mit diesem Code wird ausgewählt und das Fenster
   **Bestandsbewegung** öffnet sich, eingestellt auf neue Packung, mit der Menge seiner letzten
   neuen Packung bereits eingetragen. Prüfen und bestätigen Sie sie.

- Haben mehrere Medikamente denselben Code, wählen Sie das
  aufzufüllende aus.
- Hat kein Medikament den Code, kennt der Katalog ihn aber, können Sie
  es als neues Medikament hinzufügen oder den Code mit einem
  Medikament Ihrer Liste verknüpfen, das noch keinen Code hat (zum
  Beispiel ein von Hand eingegebenes); die Packung wird dann diesem
  Medikament hinzugefügt.
- Das Medikament wird über seinen AIC-Code gefunden. Eine Packung, die
  nur den quadratischen 2D-Code (DataMatrix) trägt, wird nicht
  erkannt: Verwenden Sie **Bestand → Packung hinzufügen**.

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

## Bestand zählen

Wenn die Tabletten im Schrank nicht mehr mit dem in der App
angezeigten Bestand übereinstimmen, zählen Sie sie und lassen Sie die
App die Korrektur erfassen:

1. Wählen Sie das Medikament aus.
2. Menü **Bestand → Bestand zählen…**.
3. Geben Sie die gezählte Menge ein. Der Dialog zeigt den erwarteten
   Bestand (mit aktualisiertem automatischem Verbrauch), die
   Bestandsabweichung mit Vorzeichen und das voraussichtliche
   Aufbrauchdatum vor und nach der Korrektur.
4. Geben Sie unter **Heute bereits eingenommen** an, wie viel der
   heute geplanten Menge Sie beim Zählen bereits eingenommen hatten.
   Die App schlägt die Dosen vor, deren Uhrzeit vorbei ist; ohne
   festgelegte Uhrzeiten schlägt sie 0 vor. Bei einer Teilmenge zeigt
   die Liste den Bestand zu Tagesbeginn, bis der heutige Verbrauch
   erfasst ist.
5. Fügen Sie bei Bedarf eine Notiz hinzu (Standard:
   „Bestandszählung") und bestätigen Sie mit **Zählung erfassen**.

**Wirkung**: Es wird genau eine positive oder negative Korrektur
erfasst, sodass der Bestand der gezählten Menge entspricht. Eine
Abweichung von null erfasst nichts. Eine positive Korrektur, die den Bestand über die
Warnschwelle hebt, erhöht die Epoche, damit später eine neue Warnung
bei niedrigem Bestand gesendet werden kann; eine negative Korrektur
erhöht sie nie. Die
Abweichung ist nur eine Bestandsangabe: Sie wird nicht als
vergessene oder zusätzliche Dosen interpretiert.

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
- **Deaktivierte Medikamente anzeigen**: Deaktivierte Medikamente
  sind in der Liste ausgeblendet. **Therapie → Deaktivierte
  Medikamente anzeigen** zeigt sie wieder an, mit dem Status
  *Deaktiviert*, bis die App geschlossen wird. Die Statusleiste nennt,
  wie viele ausgeblendet sind.
- **Reaktivieren**: deaktivierte Medikamente anzeigen, dann
  **Bearbeiten** → **Aktiv** anhaken. Tage, an denen das Medikament
  deaktiviert war, zählen nicht als Verbrauch.
- **Löschen**: **Therapie → Löschen…** entfernt ein versehentlich
  angelegtes Medikament samt Dosierungsschema endgültig. Das geht nur,
  solange nichts dafür erfasst wurde: kein Bestandseintrag (auch nicht
  die Anfangsmenge), keine Einnahme, Zählung oder Aussetzung. Sonst
  deaktiviere es oder lösche diese Einträge zuerst unter **Bestand →
  Verlauf…**. Bei aktiver Synchronisierung wird das Medikament auch
  auf den anderen Geräten entfernt, mit allem, was dort inzwischen
  erfasst wurde.

## Bestandsverlauf und Löschen eines falschen Eintrags

**Bestand → Verlauf…** (Strg+H) listet, was du für das ausgewählte
Medikament erfasst hast, die neuesten Einträge zuerst: neue Packungen
und Korrekturen, Einnahmen, Bestandszählungen und Unterbrechungen.

- **Löschen** entfernt einen falschen Eintrag; Bestand und Verbrauch
  werden neu berechnet.
- Nur Einträge, die nach der letzten Bestandszählung erfasst wurden,
  können gelöscht werden: Eine Zählung enthält frühere Fehler bereits,
  zähle den Bestand also erneut, um sie zu korrigieren.
- Einträge, die vor der Installation dieser Version erfasst wurden,
  können nicht gelöscht werden: Korrigiere sie mit einer Korrektur.

## Therapieverlauf

**Therapie → Therapieverlauf…** (Strg+T) oder die Schaltfläche
**Therapieverlauf** in der Symbolleiste öffnet eine schreibgeschützte
Ansicht mit einer Zeile pro Medikament und den Tagen auf der
waagerechten Achse. Standardmäßig zeigt sie 60 Tage zurück und 120
Tage voraus.

- **Voller Balken**: aktive Therapie, vom Beginn bis zum Enddatum (oder
  bis zum Rand der Ansicht, wenn es kein Enddatum gibt).
- **Schraffierter Balken mit gestricheltem Rand**: Unterbrechung,
  geplant oder laufend.
- **Gefüllte Raute**: eine neue Dosis, Häufigkeit oder ein neues Schema
  gilt ab diesem Tag. **Leere Raute**: nächste Stufe eines
  stufenweisen Ausschleichens.
- **Dreieck auf einer senkrechten Linie**: geschätztes Aufbrauchdatum.
  Es ist dieselbe Schätzung wie in der Spalte *Aufgebraucht am*:
  aktueller Vorrat geteilt durch die heutige Tagesmenge. Künftige
  Unterbrechungen oder Dosisänderungen werden nicht berücksichtigt.
- **Gestrichelte senkrechte Linie**: heute.
- Ein deaktiviertes Medikament wird grau dargestellt; sein Balken endet
  heute, weil das Deaktivierungsdatum nicht gespeichert wird.

Bedienung:

- **Früher** / **Später** verschieben den Zeitraum um 30 Tage,
  **Heute** stellt den Standardzeitraum wieder her. Im Diagramm
  verschieben ihn die Pfeiltasten links und rechts (oder Umschalt +
  Mausrad) um eine Woche.
- Die Pfeiltasten nach oben und unten wählen ein Medikament; das Feld
  **Details** unter dem Diagramm zeigt dieselben Informationen als Text
  (Dosierung, Vorrat, geschätzter Aufbrauch, Unterbrechungen und
  Dosisänderungen im Zeitraum). Beim Überfahren eines Elements mit der
  Maus erscheint derselbe Text als QuickInfo.
- **In der Liste zeigen** (oder Eingabetaste bzw. Doppelklick) schließt
  den Verlauf und wählt das Medikament in der Hauptliste aus, wo die
  üblichen Aktionen gelten.

Der Therapieverlauf ändert keine Daten. Die Aufbrauchdaten sind
Schätzungen: Sie dienen zur Planung von Nachkäufen und sind keine
medizinische Beratung.

## Therapieplan (Druck und PDF)

**Therapie → Therapieplan…** (Strg+P) oder die Schaltfläche
**Therapieplan** in der Symbolleiste öffnet eine Übersicht der aktiven
Medikamente für Hausarzt, Notaufnahme oder Apotheke. Für jedes aktive
Medikament zeigt sie den Wirkstoff, die Dosierung (Einnahmezeiten oder
Dosis × Mal am Tag), den Therapiezeitraum und den Arzt. Deaktivierte
Medikamente erscheinen nicht.

- **Notizen einbeziehen**: standardmäßig aus. Notizen sind Freitext
  und können privat sein; setz das Häkchen nur, wenn der Plan sie
  enthalten soll.
- **Papier**: A4 oder Letter (US), vorausgewählt nach der
  Windows-Region.
- **Drucken…** öffnet die Vorschau der Tabelle. Lange Listen werden
  auf der nächsten Seite fortgesetzt, mit wiederholter Kopfzeile und
  Spaltenüberschriften.
- **Als PDF speichern…** fragt nach dem Speicherort und schreibt
  dieselbe Tabelle als PDF über den Windows-Drucker „Microsoft Print
  to PDF“. Wurde dieser Drucker entfernt, erklärt der Dialog, wie er
  wieder hinzugefügt wird (Systemsteuerung → Programme →
  Windows-Features aktivieren oder deaktivieren).
- **In Datei speichern…** und **In Zwischenablage kopieren**
  bleiben bei der Textfassung.

PDF und Textdatei werden nur dort gespeichert, wo du es wählst;
MedReminder behält keine Kopie. Jede Seite trägt den Hinweis, dass
MedReminder eine organisatorische Erinnerung und kein Medizinprodukt
ist.

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

Die Rolle wird bei der Profilerstellung gewählt. Ein Administrator
kann sie später ändern: `Extras → Profile verwalten…`, das Profil
auswählen, **Rolle ändern…**. Die Rolle des geöffneten Profils kann
nicht geändert werden: öffne zuerst ein anderes Administratorprofil.
Mindestens ein Administrator bleibt immer erhalten. Bevor du ein
Profil ohne PIN zum Administrator machst, richte besser eine PIN ein:
sonst könnte jeder am PC es öffnen.

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

**Echte Trennung erfordert getrennte Windows-Konten.** Jedes
Windows-Konto hat einen eigenen Ordner `%LOCALAPPDATA%\MedReminder\`,
den andere Standardbenutzer von Windows (ohne Administratorrechte)
nicht lesen können. Profile innerhalb eines Windows-Kontos sind eine
Bequemlichkeit, keine Schutzgrenze für die Privatsphäre: Wer dieses
Konto benutzt, kann die Dateien aller Profile lesen, und die vom
Administrator eingerichteten automatischen Sicherungen enthalten alle
Profile, auch PIN-geschützte.

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
    │   ├── notifications.settings.json  ← ToAddress dieses Profils
    │   └── ui.settings.json             ← Textgröße dieses Profils
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

## Rezept beim Arzt anfordern

Ein Medikament auswählen und **Therapie → Rezept anfordern…** wählen
(oder die Schaltfläche **Rezept anfordern** in der Symbolleiste). Die
Aktion steht für jedes Medikament zur Verfügung, unabhängig vom
Bestand.

MedReminder bereitet eine kurze Nachricht mit dem Namen des
Medikaments, der Packung, dem Produktcode (wenn das Medikament mit dem
Katalog verknüpft ist) und dem Profilnamen als Unterschrift vor. Hat
das Medikament einen zuständigen Arzt, verwendet die Anrede diesen
Namen. Dosierung, Notizen und andere klinische Angaben sind nicht
enthalten. Betreff und Nachricht können vor dem Versand bearbeitet
werden.

Drei Wege, die Nachricht zu übermitteln:

- **Kopieren**: Betreff und Nachricht landen in der Zwischenablage,
  zum Einfügen in ein Webmail, eine Messaging-App oder ein
  Patientenportal.
- **Im E-Mail-Programm öffnen**: öffnet eine neue, bereits ausgefüllte
  E-Mail im Standard-E-Mail-Programm. Ist die Nachricht für das
  E-Mail-Programm zu lang oder ist kein E-Mail-Programm eingerichtet,
  wird sie stattdessen in die Zwischenablage kopiert.
- **Senden…**: sendet die Nachricht über das unter **Einstellungen →
  E-Mail-SMTP** konfigurierte E-Mail-Konto, nach einer ausdrücklichen
  Bestätigung. Nur verfügbar, wenn der SMTP-Versand konfiguriert und
  die E-Mail des Arztes festgelegt ist.

Die E-Mail des Arztes wird unter **Einstellungen → Benachrichtigungen
→ E-Mail des Arztes (optional)** festgelegt. Sie gilt pro Profil, ist
in Export und Import enthalten und wird nur für selbst gesendete
Anfragen verwendet: Automatische Benachrichtigungen gehen nie an diese
Adresse.

MedReminder sendet eine Rezeptanfrage nie von selbst. Nachrichteninhalt
und Empfänger werden nicht in die Protokolldateien geschrieben.

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

### OneDrive statt eines Ordners

Im selben Abschnitt kann **Speicherort** auf **OneDrive (App-Ordner)**
gestellt werden: auf **Anmelden…** klicken, mit einem Microsoft-Konto
anmelden, die Sicherungs-Passphrase festlegen und speichern. Die
Sicherungen landen verschlüsselt wie oben in `Apps/MedReminder26/backups`
in OneDrive; ein lokaler Ordner ist nicht nötig. **Aus Cloud-Ordner
wiederherstellen…** listet dann die OneDrive-Sicherungen nach Datum und
Profil und lädt nur die wiederhergestellte herunter.

**Google Drive (Ordner MedReminder/backups)** funktioniert ebenso mit
einem Google-Konto: Die Sicherungen landen verschlüsselt wie oben in
einem sichtbaren Ordner **MedReminder → backups** in Ihrer Ablage, und
**Aus Cloud-Ordner wiederherstellen…** listet sie von dort.

## Synchronisierung zwischen PCs

Mehrere PCs können dasselbe Profil aktuell halten: Was Sie auf einem
erfassen, erscheint auf den anderen. Die PCs tauschen nur verschlüsselte
Änderungen über einen gemeinsamen Ordner aus (einen OneDrive-, Google-Drive-
oder Dropbox-Ordner, den dessen Desktop-App synchronisiert, oder eine
Netzwerkfreigabe). Kein Server ist beteiligt, und der Ordner enthält nie
lesbare Daten.

Öffnen Sie **Extras → Synchronisierung…** in einem Administratorprofil (andere Profile
sehen den Eintrag nicht). Jedes synchronisierte Profil hat eine eigene
Gruppe, einen eigenen Schlüssel und eigene Geräte; das Fenster verwaltet
die Gruppe des geöffneten Profils.

### OneDrive oder freigegebener Ordner

Beim Aktivieren der Synchronisierung oder beim Beitreten zu einer Gruppe
fragt MedReminder, wo die Gruppe liegen soll:

- **OneDrive**: im sich öffnenden Browserfenster mit einem
  Microsoft-Konto anmelden. MedReminder kann nur seinen eigenen App-Ordner
  verwenden (`Apps/MedReminder26` in OneDrive); die Daten dort sind
  verschlüsselt. Jeder PC meldet sich mit **demselben** Microsoft-Konto
  an. Die OneDrive-App auf dem PC wird nicht benötigt.
- **Google Drive**: im sich öffnenden Browserfenster mit einem
  Google-Konto anmelden. Die Synchronisierungsdaten liegen verschlüsselt
  im ausgeblendeten App-Datenordner von MedReminder in Google Drive (er
  erscheint nicht in Ihrer Ablage). Jeder PC meldet sich mit
  **demselben** Google-Konto an.
- **Ein freigegebener Ordner**: ein Ordner, den ein anderes Programm
  synchron hält, oder eine Netzwerkfreigabe, wie unten beschrieben.

Läuft die OneDrive- oder Google-Drive-Sitzung ab (Kennwortänderung,
lange Inaktivität), zeigt der Status das an, und **Erneut bei OneDrive
anmelden** (bzw. **Erneut bei Google Drive anmelden**) setzt die
Synchronisierung fort; zwischenzeitliche Änderungen werden danach
gesendet.

### Auf dem ersten PC aktivieren

1. **Synchronisierung aktivieren…**, wählen Sie den gemeinsamen Ordner.
2. Geben Sie einen Namen für diesen PC und eine **Synchronisierungs-
   Passphrase** ein (mindestens 10 Zeichen, zweimal eingegeben). Sie ist
   nicht die Sicherungs-Passphrase. Bewahren Sie sie gut auf: Ohne sie sind
   die Daten im Ordner nicht lesbar, und sie kann nicht wiederhergestellt
   werden.

### Von einem anderen PC beitreten

1. Warten Sie, bis der Synchronisierungsclient den gemeinsamen Ordner
   heruntergeladen hat.
2. **Einer Gruppe beitreten…**, wählen Sie denselben Ordner, geben Sie einen
   Namen für diesen PC und dieselbe Passphrase ein.
3. Bestätigen Sie: **Die Daten dieses Profils auf diesem PC werden durch die
   der Gruppe ersetzt** (eine Kopie bleibt neben der Datenbank erhalten).
   MedReminder wird neu gestartet.

Öffnet die Passphrase mehr als eine Gruppe im Ordner oder Konto (mehrere
synchronisierte Profile mit derselben Passphrase), fragt MedReminder,
welcher beigetreten werden soll, und zeigt jede Gruppe mit ihren
Geräten.

Mit OneDrive oder Google Drive kann der Beitritt bis zu einer Minute
dauern: MedReminder wartet, bis das Konto den neuen PC auflistet, damit
die anderen PCs die Änderungen aufbewahren, die er noch braucht.

### Im Alltag

- MedReminder synchronisiert einige Sekunden nach jeder Änderung, alle 5
  Minuten und mit **Jetzt synchronisieren**.
- Die Registerkarte **Geräte** zeigt die PCs der Gruppe und wann sie zuletzt
  gesehen wurden.
- Ändern zwei PCs dasselbe, bevor sie die Änderung des anderen gesehen
  haben, bleibt die neueste erhalten und der Fall erscheint unter
  **Konflikte**. Für ein Medikamentenfeld stellt **Verlorenen Wert
  wiederherstellen** den anderen Wert wieder her; **Verwerfen** entfernt den
  Eintrag aus der Liste.
- Ein Import oder eine Wiederherstellung auf einem synchronisierten Profil
  startet eine neue **Generation**: Nach einer Warnung verwerfen die anderen
  PCs, was sie noch nicht gesendet hatten, und müssen mit **Aus der Gruppe
  neu aufbauen…** neu aufgebaut werden.
- Der **Profilname** und die **Benachrichtigungsempfänger**
  (Einstellungen → Benachrichtigungen) gehören zur Gruppe: Eine
  Änderung auf einem PC erreicht die anderen, und ein PC, der beitritt,
  übernimmt die der Gruppe. Um ein anderes synchronisiertes Profil
  umzubenennen, öffnen Sie zuerst dieses Profil.
- **E-Mail**: Jeder PC mit eingerichteter E-Mail (Einstellungen →
  E-Mail-SMTP) sendet seine eigenen Nachrichten zu niedrigem Bestand und
  an Pflegepersonen; mit zwei synchronisierten PCs kommt dieselbe
  Nachricht daher zweimal an. Richten Sie die E-Mail nur auf einem PC
  der Gruppe ein.
- **Synchronisierung deaktivieren…** beendet die Synchronisierung auf diesem
  PC und behält seine Daten.

### Kopplungscodes

Ein PC, der bereits in der Gruppe ist, kann einen **Kopplungscode**
anzeigen: **Gerät koppeln…** zeigt einen QR-Code (für die künftige
Smartphone-App) und denselben Code als Text. Auf einem anderen PC
verwendet **Mit einem Kopplungscode beitreten…** diesen Code statt der
Passphrase; der PC braucht trotzdem Zugriff auf dasselbe Konto oder
denselben Ordner.

- Der Code gilt 10 Minuten und nur, solange sein Fenster geöffnet ist.
  Beim Schließen des Fensters wird er zurückgezogen.
- Wer den Code liest, solange er gültig ist, kann die Daten der Gruppe
  lesen: Zeigen Sie ihn nur Ihren eigenen Geräten und senden Sie ihn
  nicht per Nachricht oder E-Mail. Das Fenster erscheint nicht auf
  Bildschirmfotos.

### Schlüssel ändern, einen verlorenen PC entfernen

- Registerkarte **Geräte** → einen PC auswählen → **Gerät
  entfernen…**: für einen verlorenen oder gestohlenen PC. **Schlüssel
  und Passphrase ändern…** tut dasselbe, ohne einen PC zu nennen, zum
  Beispiel wenn jemand die Passphrase kennt.
- Sie wählen eine **neue Synchronisierungs-Passphrase**. Die Gruppe
  erhält einen neuen Schlüssel; der entfernte PC erhält ihn nicht und
  kann nichts lesen, was danach geschrieben wird. Was er bereits hatte,
  bleibt für ihn lesbar.
- Beenden Sie außerdem die Sitzungen des verlorenen PCs in den
  Sicherheitseinstellungen des Microsoft- oder Google-Kontos: Bis dahin
  kann er den Speicher noch erreichen.
- Jeder andere PC sendet keine Änderungen mehr und meldet, dass der
  Schlüssel geändert wurde. Verwenden Sie dort **Neuen Schlüssel
  eingeben…**, mit der neuen Passphrase oder einem Kopplungscode eines
  PCs, der den neuen Schlüssel bereits hat. Das Profil wird aus der
  Gruppe neu aufgebaut und **die auf diesem PC gemachten Änderungen
  bleiben erhalten**, auch die während der Wartezeit erfassten.
  MedReminder wird neu gestartet.

### Installation: mehrere Geräte

**Extras → Installation…** (nur Administratoren) teilt die gesamte
Installation zwischen Geräten: Profile, Rollen und PINs,
E-Mail-Einstellungen, Einstellungen der Cloud-Sicherung und das
Referenzland.

- **Installation veröffentlichen…** auf dem ersten Gerät: Wählen Sie den
  Speicher, der bereits die Synchronisationsgruppen der Profile enthält,
  und eine Passphrase der Installation. Die Passphrase ist ein Geheimnis
  der Administratoren: Mit ihr lässt sich ein Gerät hinzufügen und jedes
  Profil wiederherstellen, wenn kein anderes Gerät verfügbar ist.
- **Geräte → Gerät hinzufügen…**: Wählen Sie die Profile für das neue
  Gerät und geben Sie dort den angezeigten Code ein. Der Code gilt 10
  Minuten oder bis das Fenster geschlossen wird. Das neue Gerät erhält
  nur die gewählten Profile.
- **Einer bestehenden Installation beitreten…** auf dem neuen Gerät: mit
  dem Code oder mit der Passphrase der Installation und der PIN eines
  Administrators der Installation. Ihre Einstellungen ersetzen die dieses
  Geräts, und die bereits vorhandenen Profile werden der Installation
  hinzugefügt. MedReminder startet mit den neuen Profilen neu. Auf einem
  neuen PC bietet das Willkommensfenster des ersten Starts dasselbe mit
  **Einer bestehenden Installation beitreten…** an.

**Master-Gerät.** Nur ein Gerät, der Master, sendet die
E-Mail-Erinnerungen und führt die Cloud-Sicherung aus; die anderen
zeigen ihre Erinnerungen auf dem Bildschirm. Das Gerät, das die
Installation veröffentlicht, ist der Master. Um die Rolle zu übertragen,
wählen Sie unter **Geräte** ein Gerät und dann **Zum Master machen…**:
Der bisherige Master übergibt bei seiner nächsten Synchronisierung, und
bis dahin sendet kein Gerät E-Mails. Ein Master, der die Installation
seit 24 Stunden nicht synchronisiert hat, sendet keine E-Mails mehr; ist
er verloren, übernimmt das an seiner Stelle gewählte Gerät eine Stunde
später. Installationen, die vor dieser Version veröffentlicht wurden,
haben keinen Master, bis ein Administrator einen festlegt.

Der Master prüft jedes Profil, das er hat, auch die nicht geöffneten,
und sendet deren E-Mail-Erinnerungen an die Empfänger des jeweiligen
Profils. Auf den anderen Geräten öffnet sich eine Rezeptanfrage im
E-Mail-Programm, und der Test der E-Mail-Verbindung ist nur auf dem
Master verfügbar.

Auf dem neuen Master schließt ein Administrator die Übergabe ab: Das
Fenster öffnet sich von selbst oder über **Extras → Installation… →
Übergabe abschließen…**. Es zeigt die Einstellungen der Installation,
testet die E-Mail-Verbindung von diesem Gerät, meldet sich beim
Speicher der Cloud-Sicherung an und fragt erneut nach der Passphrase der
Cloud-Sicherung (sie wird nie zwischen Geräten kopiert) und lädt dann
die Profile herunter, die der neue Master braucht. Die Passphrase der
Installation holt die Profile, die kein verfügbares Gerät hat.

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
Dropdown-Menü und klicke auf **Speichern**. MedReminder
fragt nach einem Neustart, um die Änderung zu übernehmen.

Hinweise:
- Windows-Toast-Benachrichtigungen verwenden die hier gewählte
  Sprache, wie der Rest der App.
- E-Mail-Benachrichtigungen und der Therapieplan verwenden die
  hier gewählte Sprache.

## Textgröße

**Einstellungen → Allgemein → Textgröße (dieses Profil)**: wähle
**Normal**, **Groß** oder **Sehr groß** und klicke auf **Speichern**.
MedReminder fragt nach einem Neustart; danach zeigen alle Fenster
dieses Profils größeren Text, größere Schaltflächen und höhere
Listenzeilen. Auch das Benutzerhandbuch wird vergrößert.

Hinweise:
- Die Größe gehört zum Profil: Auf einem gemeinsam genutzten PC
  behält jede Person ihre eigene. Die Profilauswahl und die
  PIN-Abfrage, die vor dem Öffnen eines Profils erscheinen, verwenden
  immer Normal.
- MedReminder folgt auch der Anzeigeskalierung und den
  Kontrastdesigns von Windows. Mit einem Kontrastdesign verzichtet die
  Medikamentenliste auf farbige Zeilen und verwendet die Farben des
  Designs; die Spalte **Status** nennt weiterhin den Zustand jedes
  Medikaments.
- Auf einem kleinen Bildschirm wird ein Fenster in **Sehr groß** auf
  die Bildschirmgröße verkleinert, und ein Teil ist möglicherweise
  nicht sichtbar; wähle dann **Groß**.

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

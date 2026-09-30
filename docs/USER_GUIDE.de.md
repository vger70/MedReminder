# MedReminder — Benutzerhandbuch

MedReminder sagt dir **rechtzeitig**, wenn ein Medikament bald
aufgebraucht ist, damit du dir ein neues Rezept holen kannst, bevor es
ausgeht. Es kann dich auch an jede Einnahmezeit erinnern, mehrere
Personen begleiten und auf mehreren Computern laufen.

> **MedReminder ist eine organisatorische Erinnerungshilfe, kein
> Medizinprodukt.** Es stellt keine Diagnosen, gibt keine
> Therapieanweisungen, ändert keine Therapie und macht keine
> klinischen Vorschläge. Jede Therapieentscheidung muss mit deinem
> Arzt getroffen werden.

Drücke **F1** oder öffne **? → Benutzerhandbuch**, um dieses Handbuch
in der App zu lesen. Die technische Architektur ist in
`docs/ANALYSIS.md` beschrieben.

---

## Inhalt

1. [Erste Schritte](#start)
   - [Erster Start](#first-start) · [Das Hauptfenster](#main-window) ·
     [Was finde ich wo](#where)
2. [Medikamente](#medicines)
   - [Ein Medikament hinzufügen](#add-medicine) ·
     [Einnahmezeiten](#slots) ·
     [Komplexe Schemata](#regimens) ·
     [Erinnerung zur Einnahmezeit](#dose-reminder) ·
     [Bearbeiten, deaktivieren, löschen](#edit-medicine)
3. [Ein Medikament finden: Katalog und Barcode](#catalogue)
4. [Bestand](#stock)
   - [Packung hinzufügen](#add-package) · [Einnahme erfassen](#intake) ·
     [Bestand korrigieren](#correct) · [Bestand zählen](#count) ·
     [Verlauf](#history)
5. [Therapieverlauf, Therapieplan und Rezeptanforderung](#documents)
6. [Benachrichtigungen und E-Mail](#notifications)
7. [Mehrere Personen: Profile und Rollen](#profiles)
8. [Daten schützen: Sicherung und Export](#backup)
9. [Mehrere Computer](#devices)
   - [Welche Option brauche ich?](#devices-choice) ·
     [Ein Profil zwischen PCs synchronisieren](#sync) ·
     [Die Installation teilen](#installation) ·
     [Das Master-Gerät](#master) ·
     [Verlorenes oder ersetztes Gerät](#remove-device)
10. [Einstellungen und Alltag](#settings)
11. [Probleme und Antworten](#faq)
12. [Wo MedReminder die Daten speichert](#data)
13. [Was MedReminder nicht tut](#limits)

---

<a id="start"></a>
## 1. Erste Schritte

<a id="first-start"></a>
### Erster Start

1. Starte `MedReminder.exe`.
2. Das Fenster **Willkommen bei MedReminder** öffnet sich. Wähle:
   - **Profil erstellen** — der Normalfall auf deinem ersten Computer.
     Gib deinen Namen und, wenn du willst, eine PIN ein. Dieses erste
     Profil ist der **Administrator**: Es verwaltet E-Mail, Sicherung
     und die anderen Profile (siehe [Mehrere Personen](#profiles)).
   - **Einer bestehenden Installation beitreten…** — nur wenn
     MedReminder schon auf einem anderen deiner Computer läuft und
     dieser hier dazugehören soll (siehe
     [Die Installation teilen](#installation)).
3. Das Hauptfenster öffnet sich. Das MedReminder-Symbol im
   Windows-Infobereich (neben der Uhr) bleibt sichtbar, solange die App
   läuft.

**Windows SmartScreen.** Das Programm ist nicht digital signiert. Beim
allerersten Start kann Windows ein blaues Fenster „Der Computer wurde
durch Windows geschützt“ zeigen: Klicke auf **Weitere Informationen**,
dann auf **Trotzdem ausführen**. Windows merkt sich die Wahl. Bei der
Installation über das MSI-Paket zeigt das Berechtigungsfenster aus
demselben Grund „Unbekannter Herausgeber“.

<a id="main-window"></a>
### Das Hauptfenster

- **Menüs** oben: **Datei**, **Therapie**, **Bestand**, **Extras** und
  **?** (Hilfe).
- **Symbolleiste** unter den Menüs mit den häufigsten Aktionen: *Neues
  Medikament*, *Bearbeiten*, *Einnahme erfassen*, *Jetzt prüfen*,
  *Therapieplan*, *Therapieverlauf*, *Rezept anfordern*.
- **Medikamentenliste** in der Mitte: eine Zeile pro Medikament, mit
  Bestand, verbleibenden Tagen und voraussichtlichem Ende des Bestands.
  Die Zeilen ändern die Farbe, wenn der Bestand niedrig ist; die Spalte
  **Status** sagt es auch in Worten.
- **Statusleiste** unten: das geöffnete Profil („Profil: Anna (Admin)“
  für einen Administrator).

Das Schließen des Fensters mit dem **X** beendet MedReminder nicht: Es
läuft im Infobereich weiter, damit Erinnerungen weiter ankommen. Zum
Beenden klicke mit der rechten Maustaste auf das Symbol und wähle
**Beenden**.

<a id="where"></a>
### Was finde ich wo

| Ich möchte… | Gehe zu |
|---|---|
| Ein Medikament hinzufügen | **Therapie → Neues Medikament…** |
| Dosis oder Häufigkeit ändern | **Therapie → Dosis/Häufigkeit ändern…** |
| Eine gekaufte Packung erfassen | **Bestand → Packung hinzufügen…** oder **Bestand → Per Barcode auffüllen…** |
| Den Bestand an das anpassen, was ich wirklich habe | **Bestand → Bestand zählen…** |
| Einen falschen Eintrag rückgängig machen | **Bestand → Verlauf…** |
| Die Therapie für einen Arzt drucken | **Therapie → Therapieplan…** |
| Ein Rezept anfordern | **Therapie → Rezept anfordern…** |
| E-Mail, Sprache, Sicherung einrichten | **Extras → Einstellungen…** |
| Eine Person hinzufügen | **Extras → Profile verwalten…** (Administrator) |
| MedReminder auf einem anderen PC nutzen | **Extras → Synchronisierung…** und **Extras → Installation…** (Administrator) |
| Das Profil einer anderen Person öffnen | **Datei → Profil wechseln…** |

---

<a id="medicines"></a>
## 2. Medikamente

<a id="add-medicine"></a>
### Ein Medikament hinzufügen

1. **Therapie → Neues Medikament…** (oder die Schaltfläche *Neues
   Medikament*).
2. Beginne, den **Namen** zu tippen: Der Katalog schlägt passende
   Medikamente vor (siehe [Ein Medikament finden](#catalogue)). Die
   Auswahl eines Eintrags füllt Wirkstoff und Packung aus. Du kannst
   auch auf **Barcode scannen…** klicken.
3. Fülle die Pflichtfelder aus, die mit `*` markiert sind: *Einheit*,
   *Dosis pro Einnahme*, *Einnahmen pro Tag*, *Beginn der Therapie* und
   *Warnschwelle (Tage)*.
   - Die **Warnschwelle** gibt an, wie viele Tage vor dem Aufbrauchen du
     gewarnt werden willst. Lass genug Zeit für Rezept und Kauf, zum
     Beispiel 10 Tage.
4. **Anfangsbestand**: wie viele Tabletten (oder ml, Dosen…) du jetzt
   hast.
5. **Benachrichtigungskanäle**: Hake **Windows-Benachrichtigung**
   und/oder **E-Mail** an. E-Mail funktioniert erst nach der
   Einrichtung (siehe [Benachrichtigungen und E-Mail](#notifications)).
6. Optional: *Ende der Therapie*, *Zuständiger Arzt*, *Notizen*,
   Einnahmezeiten, *Zur Einnahmezeit erinnern*.
7. **Speichern**.

Ab jetzt zieht MedReminder die Tagesdosis jeden Tag selbst ab. Du musst
nicht jede eingenommene Tablette erfassen.

<a id="slots"></a>
### Einnahmezeiten

Unter **Einnahmezeiten (optional)** kannst du die Tagesdosis aufteilen:
**Hinzufügen…** öffnet ein Fenster, in dem du die Dosis, eine optionale
Uhrzeit (*Mit konkreter Uhrzeit*) und eine Bezeichnung wie „Nach dem
Frühstück“ angibst (wähle eine der *Häufigen Bezeichnungen* oder tippe
eine eigene). Die Einnahmezeiten erscheinen im Therapieplan und können,
wenn sie eine Uhrzeit haben, an die Einnahme erinnern.

Ohne Einnahmezeiten verwendet das Medikament „Dosis × Einnahmen pro
Tag“.

<a id="regimens"></a>
### Komplexe Schemata

Nicht jede Therapie braucht jeden Tag dieselbe Menge. Stelle im
Medikamentenfenster **Schema** auf **Erweitert** und wähle einen
**Regime-Typ**:

| Regime-Typ | Beispiel |
|---|---|
| **Wochenmuster** | Eine eigene Menge für jeden Wochentag, zum Beispiel ein Gerinnungshemmer mit anderen Dosen am Mo, Mi, Fr. |
| **Zyklisch (N Tage an / M aus)** | Eine Menge für N Tage, dann M Tage ohne, zum Beispiel 21 Tage ja, 7 nein. |
| **Ausschleichen** — *Linear* | Die Dosis ändert sich alle paar Tage um einen festen Schritt bis zur Enddosis und bleibt dann gleich. |
| **Ausschleichen** — *Stufenweise* | Eine Liste von Stufen, jede mit eigener Dosis und Dauer, zum Beispiel 4 pro Tag für 7 Tage, 2 pro Tag für 7 Tage, 1 pro Tag für 14 Tage. Nutze **Stufe hinzufügen** / **Entfernen**; die Summe steht unter der Liste. Hake *Letzte Dosis als Erhaltungsdosis beibehalten* an, wenn die letzte Dosis unbegrenzt weiterläuft. |
| **Bei Bedarf (PRN)** | Kein geplanter Verbrauch: Der Bestand wird verfolgt, aber kein Ende geschätzt. |

Mit **Erweitert** werden die Dosisfelder oben im Fenster nicht
verwendet. Wechsle zurück zu **Einfach** für eine feste Tagesdosis.

**Die Therapie ändert sich?** Nutze **Therapie → Dosis/Häufigkeit
ändern…** und wähle das Datum **Gültig ab**. Das alte Schema bleibt für
die Tage davor gültig. Liegt das Datum in der Vergangenheit, wird der
ab diesem Datum bereits abgezogene Verbrauch mit dem neuen Schema neu
berechnet.

MedReminder prüft keine Höchstdosen, Überdosierungen oder
Wechselwirkungen: Es folgt nur der Therapie, die dein Arzt verordnet
hat.

<a id="dose-reminder"></a>
### Erinnerung zur Einnahmezeit

Hake im Medikamentenfenster **Zur Einnahmezeit erinnern** an, um zu
jeder Einnahmezeit mit Uhrzeit eine Erinnerung zu bekommen („Zeit für
die Einnahme von …“). Das geht, wenn das Medikament mindestens eine
Einnahmezeit mit Uhrzeit und noch Bestand hat.

- Die Erinnerung erscheint auf dem Bildschirm; nutzt das Medikament den
  Kanal **E-Mail** und ist E-Mail eingerichtet, kommt sie auch per
  E-Mail.
- **Einmal pro Einnahmezeit und Tag**, auch nach einem Neustart der App.
- War der PC zu dieser Zeit im Ruhezustand, kommt die Erinnerung
  trotzdem innerhalb von **30 Minuten**; danach entfällt sie.
- Bei **Bestand null** wird keine Erinnerung gesendet.
- In der Nacht der Zeitumstellung auf Sommerzeit löst eine Einnahmezeit
  in der übersprungenen Stunde nicht aus.

Die Erinnerung erfasst nicht, ob du die Dosis genommen hast, und
ändert den Bestand nicht.

<a id="edit-medicine"></a>
### Bearbeiten, deaktivieren, löschen

- **Bearbeiten**: Doppelklick auf die Zeile oder **Therapie →
  Bearbeiten**. Du kannst alles ändern außer Dosis und Häufigkeit
  (dafür *Dosis/Häufigkeit ändern…*).
- **Deaktivieren**: **Therapie → Deaktivieren**, wenn du eine Therapie
  beendest. Das Medikament wird ausgeblendet und bekommt keine Warnungen
  mehr; sein Verlauf bleibt erhalten. **Therapie → Deaktivierte
  Medikamente anzeigen** blendet es wieder ein; zum Reaktivieren öffne
  es mit **Bearbeiten** und hake **Aktiv** an. Die inaktiven Tage
  zählen nicht als Verbrauch.
- **Löschen**: **Therapie → Löschen…** entfernt ein versehentlich
  angelegtes Medikament. Das geht nur, solange nichts erfasst wurde
  (kein Bestand, nicht einmal der Anfangsbestand, keine Einnahme, keine
  Zählung). Sonst deaktiviere es. Bei aktiver Synchronisierung
  verschwindet es auch von den anderen Computern.

---

<a id="catalogue"></a>
## 3. Ein Medikament finden: Katalog und Barcode

### Der Referenzkatalog

MedReminder enthält die amtlichen Arzneimittellisten von **Italien**
(AIFA), **Spanien** (AEMPS), **Frankreich** (ANSM) sowie die für die
ganze **Europäische Union** zugelassenen Arzneimittel (EMA).

- Tippe im Medikamentenfenster einen Teil des **Namens** oder des
  **Wirkstoffs**: Bis zu 20 Treffer erscheinen. Die Auswahl eines
  Treffers füllt die anderen Felder aus.
- Ein **roter Kreis** neben einer Zeile bedeutet, dass das Produkt
  ausgesetzt oder zurückgezogen ist. Du kannst es trotzdem wählen.
- **Nicht in der Liste?** Tippe einfach den Namen und speichere: Das
  Medikament funktioniert genauso, nur ohne Katalogverknüpfung.
- **Welches Land?** **Extras → Einstellungen… → Allgemein →
  Referenzland** (Standard: Italien). Nur ein Administrator kann es
  ändern: Es gilt für alle Profile und, bei einer geteilten
  Installation, für alle Geräte. Mit IT, ES oder FR enthält die Liste
  auch die EU-Arzneimittel; mit **EU** nur diese. Ein Arzneimittel kann
  zweimal erscheinen (national und EU): Wähle das, das zu deiner
  Packung passt.
- **Automatische Aktualisierung.** Wenn **Beim Start nach Updates
  suchen (GitHub)** aktiviert ist (Einstellungen → Allgemein), lädt
  MedReminder beim Start die neueste Monatsliste deines Landes und die
  EU-Liste herunter, falls neuer. Ohne Internetverbindung ändert sich
  nichts. Bei mehreren Profilen wird jedes beim ersten Öffnen
  aktualisiert.

**Quellen.** AIFA Open Data (CC BY 4.0); EMA-EPAR-Daten (rechtlicher
Hinweis der EMA, Beschluss der Kommission 2011/833/EU); AEMPS CIMA
(spanisches Gesetz 37/2007 über die Weiterverwendung von Informationen
des öffentlichen Sektors); ANSM BDPM (Licence Ouverte Etalab 2.0). Die
vollständigen Quellenangaben stehen unter **? → Über MedReminder…** und
in `THIRD-PARTY-NOTICES.md`.

### Den Barcode scannen

Mit einem USB-Barcodescanner oder einer Webcam kannst du ein
Medikament ohne Tippen ausfüllen.

1. Klicke im Medikamentenfenster auf **Barcode scannen…**.
2. Scanne den Barcode der Packung oder tippe den darunter gedruckten
   Code und drücke **Enter**.
3. Ist der Code im Katalog, wird das Formular ausgefüllt. Sonst zeigt
   das Fenster den gelesenen Code und es ändert sich nichts.

Tipps:

- Klicke *vor* dem Scannen auf **Barcode scannen…**, sonst landet der
  Code im Feld mit dem Cursor.
- Auf italienischen Packungen wird der **AIC**-Barcode gelesen (`A`
  gefolgt von 9 Ziffern). Erkennt der Scanner ihn nicht, aktiviere in
  seinen Einstellungen die Symbologie **Code 32** (italienischer
  Pharmacode). Der quadratische Code (DataMatrix) braucht einen
  2D-Scanner und ist oft nicht im Katalog.
- Stelle den Scanner auf dasselbe Tastaturlayout wie Windows ein (z. B.
  QWERTZ).

**Mit der Webcam.** Klicke im Scanfenster auf **Webcam verwenden**.
Halte die Packung 10–20 cm entfernt, den Barcode im Rahmen, bei gutem
Licht. Die Kamera schaltet sich aus, wenn ein Code gelesen wurde, wenn
du auf **Scanner verwenden** klickst, wenn du das Fenster schließt oder
nach 30 Sekunden. Blockiert Windows die Kamera, klicke auf
**Datenschutzeinstellungen öffnen**, aktiviere *Desktop-Apps den Zugriff
auf Ihre Kamera erlauben* und dann **Erneut versuchen**. Kein Bild wird
gespeichert oder gesendet.

**Per Scan auffüllen.** **Bestand → Per Barcode auffüllen…**: Scanne die
neue Packung, und das passende Medikament öffnet sich im
Bestandsfenster, bereits auf *Neue Packung* mit der üblichen Menge. Hat
kein Medikament diesen Code, kannst du ein neues Medikament anlegen oder
den Code einem vorhandenen zuordnen.

---

<a id="stock"></a>
## 4. Bestand

MedReminder senkt den Bestand jeden Tag selbst nach dem Schema. Du
erfasst nur, was den Bestand auf andere Weise ändert.

<a id="add-package"></a>
### Packung hinzufügen

1. Wähle das Medikament aus.
2. **Bestand → Packung hinzufügen…**.
3. Wähle die Art:
   - **Neue Packung** — nach einem Kauf (der Normalfall);
   - **Manuelle Zugabe** — zum Beispiel Muster vom Arzt;
   - **Positive Korrektur** — du hattest zu wenig gezählt.
4. Gib die Menge ein und bestätige.

Eine neue Packung startet den Warnzyklus neu: Fällt der Bestand wieder
unter die Schwelle, bekommst du eine neue Warnung.

<a id="intake"></a>
### Einnahme erfassen

**Therapie → Einnahme erfassen…** (oder die Schaltfläche in der
Symbolleiste) erfasst eine Einnahme als **Eingenommen**,
**Übersprungen** oder **Storniert**, mit Tag und Menge. An normalen
Tagen brauchst du das nicht. Nutze es, wenn ein Tag vom Schema abweicht:
Sobald du für einen Tag eine Einnahme erfasst, ersetzt sie den
automatischen Abzug dieses Tages.

<a id="correct"></a>
### Bestand korrigieren

Hast du weniger, als die App zeigt (eine verlorene Tablette, eine
verschüttete Flasche): **Bestand → Bestand korrigieren…**, lass die Art
**Negative Korrektur** und gib die abzuziehende Menge ein. Der Bestand
kann nicht unter null fallen.

<a id="count"></a>
### Bestand zählen

Wenn dein Medikamentenschrank nicht mehr zur App passt, zähle und lass
die App korrigieren:

1. Wähle das Medikament, dann **Bestand → Bestand zählen…**.
2. Gib die **Gezählte Menge** ein. Das Fenster zeigt den erwarteten
   Bestand, die Abweichung und wie sich das voraussichtliche Ende
   ändert.
3. Trage unter **Heute bereits eingenommen** ein, was du heute beim
   Zählen schon genommen hattest (die App schlägt die Dosen vor, deren
   Uhrzeit vorbei ist).
4. Klicke auf **Zählung erfassen**.

Die App erfasst eine Korrektur, damit der Bestand dem Gezählten
entspricht. Die Abweichung ist nur eine Bestandszahl: Sie wird nicht als
vergessene oder zusätzliche Dosen gedeutet.

<a id="history"></a>
### Verlauf und falsche Einträge

**Bestand → Verlauf…** (Strg+H) listet Packungen, Korrekturen,
Einnahmen, Zählungen und Aussetzungen des gewählten Medikaments, die
neuesten zuerst. Wähle einen falschen Eintrag und klicke auf
**Löschen**: Bestand und Verbrauch werden neu berechnet. Nur Einträge
nach der letzten Zählung können gelöscht werden (für ältere zähle
erneut); Einträge aus Versionen vor dieser Funktion können nicht
gelöscht werden, korrigiere sie mit einer Korrektur.

---

<a id="documents"></a>
## 5. Therapieverlauf, Therapieplan und Rezeptanforderung

### Therapieverlauf

**Therapie → Therapieverlauf…** (Strg+T) zeigt eine Zeile pro
Medikament auf einem Kalender (60 Tage zurück, 120 voraus):

- **durchgehender Balken**: laufende Therapie; **schraffierter
  Balken**: Aussetzung;
- **gefüllte Raute**: eine neue Dosis oder ein neues Schema beginnt;
  **leere Raute**: nächste Stufe eines stufenweisen Ausschleichens;
- **Dreieck**: voraussichtliches Ende des Bestands; **gestrichelte
  Linie**: heute;
- graue Zeile: deaktiviertes Medikament.

**Früher** / **Später** verschieben um 30 Tage, **Heute** springt
zurück; die Pfeiltasten verschieben um eine Woche. Das Feld mit den
**Details** beschreibt das gewählte Medikament in Worten. **In der Liste
zeigen** (oder Enter) wählt es in der Hauptliste aus. Der
Therapieverlauf ändert nichts; die Enddaten sind Schätzungen.

### Therapieplan (Druck und PDF)

**Therapie → Therapieplan…** (Strg+P) erstellt eine Übersicht der
aktiven Medikamente für einen Arzt, eine Notaufnahme oder eine
Apotheke: Wirkstoff, Dosierung, Therapiezeitraum, Arzt.

- **Notizen einbeziehen** ist standardmäßig aus: Notizen können privat
  sein.
- **Papier**: A4 oder Letter.
- **Drucken…** zeigt eine Vorschau; **Als PDF speichern…** nutzt den
  Windows-Drucker „Microsoft Print to PDF“ (wurde er entfernt, erklärt
  das Fenster, wie man ihn wieder hinzufügt); **In Datei speichern…**
  und **In Zwischenablage kopieren** liefern reinen Text.

MedReminder behält keine Kopie dessen, was du speicherst oder druckst.

### Ein Rezept anfordern

Wähle ein Medikament, dann **Therapie → Rezept anfordern…**. MedReminder
bereitet eine kurze Nachricht mit Medikamentenname, Packung,
Produktcode und deinem Namen vor; mit einem zuständigen Arzt nutzt die
Anrede dessen Namen. Dosierung und Notizen sind nicht enthalten. Du
kannst vor dem Senden alles ändern.

- **Kopieren** — zum Einfügen in Webmail, einen Messenger oder ein
  Patientenportal.
- **Im E-Mail-Programm öffnen** — eine neue E-Mail in deinem üblichen
  E-Mail-Programm.
- **Senden…** — sendet sie nach einer Bestätigung über das E-Mail-Konto
  von MedReminder. Verfügbar, wenn E-Mail eingerichtet und die **E-Mail
  des Arztes** ausgefüllt ist (Einstellungen → Benachrichtigungen). Bei
  einer geteilten Installation sendet nur das [Master-Gerät](#master):
  Nutze auf den anderen Geräten **Im E-Mail-Programm öffnen**.

MedReminder sendet nie von selbst eine Anforderung.

---

<a id="notifications"></a>
## 6. Benachrichtigungen und E-Mail

### So funktionieren die Warnungen

- Alle 30 Minuten prüft MedReminder die Medikamente. Fällt ein
  Medikament unter seine **Warnschwelle**, warnt es dich **einmal**,
  über die für dieses Medikament gewählten Kanäle: eine
  Windows-Benachrichtigung und/oder eine E-Mail. Nach einer neuen
  Packung beginnt der Zyklus von vorn.
- **Extras → Jetzt prüfen** (oder die Schaltfläche in der Symbolleiste
  oder das Menü des Symbols) prüft sofort.
- MedReminder muss laufen, um Warnungen zu senden. Aktiviere den
  Autostart (siehe [Einstellungen](#settings)).

### Schritt 1 — das E-Mail-Konto (Administrator)

**Extras → Einstellungen… → E-Mail-SMTP**:

| Feld | Was eintragen |
|---|---|
| **Host** | Der Postausgangsserver deines Anbieters, z. B. `smtp.gmail.com` |
| **Port** | Meist `587` (mit *StartTLS verwenden*) oder `465` |
| **Benutzername** / **Neues Passwort** | Dein E-Mail-Konto. Das Passwort wird verschlüsselt gespeichert und nie protokolliert |
| **Absender (from)** / **Absendername** | Von wem die E-Mails kommen |
| **Zeitüberschreitung (s)** | Sekunden bis zum Abbruch |

Klicke auf **Verbindung testen** (meldet sich an, ohne etwas zu
senden), dann auf **SMTP-Einstellungen speichern**.

*Beispiel mit Gmail:* Aktiviere die Bestätigung in zwei Schritten im
Google-Konto, erstelle ein App-Passwort unter
`myaccount.google.com/apppasswords` und verwende Host `smtp.gmail.com`,
Port `587`, StartTLS an, deine Gmail-Adresse als Benutzername und das
App-Passwort als Passwort. Anbieter ändern ihre Regeln: Schlägt der Test
fehl, sieh in der Anleitung deines Anbieters nach.

### Schritt 2 — die Empfänger (jedes Profil)

**Extras → Einstellungen… → Benachrichtigungen**, für das geöffnete
Profil:

- **Empfänger (to)** — wer die Warnungen dieses Profils erhält.
- **E-Mail der Betreuungsperson (optional)** — ein Angehöriger oder eine
  Pflegekraft, die eine Kopie jeder Warnung erhält, in derselben E-Mail
  (beide Adressen sind für beide sichtbar). Sie muss sich vom Empfänger
  unterscheiden.
- **E-Mail des Arztes (optional)** — nur für Rezeptanforderungen, die du
  selbst sendest; automatische Warnungen gehen nie dorthin.

Klicke auf **Empfänger speichern**. Im selben Reiter kannst du unter
**Meine PIN** die PIN deines eigenen Profils festlegen oder ändern.

---

<a id="profiles"></a>
## 7. Mehrere Personen: Profile und Rollen

Ein MedReminder kann die Medikamente mehrerer Personen begleiten, zum
Beispiel deine und die eines Elternteils. Jede Person hat ein **Profil**
mit eigenen Medikamenten und eigenen Empfängern.

### Rollen

| | Administrator | Benutzer |
|---|---|---|
| Eigene Medikamente, Bestand, Empfänger | ja | ja |
| E-Mail-Konto, Sicherung, Referenzland | ja | nein |
| Profile anlegen, umbenennen, löschen; PINs aller | ja | nein |
| Extras → Synchronisierung… und Extras → Installation… | ja | nein |

Es gibt immer mindestens einen Administrator.

### Profile verwalten (Administrator)

**Extras → Profile verwalten…**:

- **Neues Profil** — Name, Rolle (Standard *Benutzer*), optionale PIN.
- **Umbenennen** — ändert den angezeigten Namen.
- **PIN ändern** — legt die PIN eines Profils fest, ändert oder löscht
  sie.
- **Rolle ändern…** — macht ein Profil zum Administrator oder Benutzer.
  Die Rolle des geöffneten Profils kann nicht geändert werden (öffne
  zuerst ein anderes Administratorprofil). Bevor du ein Profil ohne PIN
  zum Administrator machst, gib ihm besser eine PIN.
- **Löschen** — tippe zur Bestätigung den Profilnamen. Die Daten auf der
  Festplatte bleiben, außer du hakst *Auch die Profildaten auf der
  Festplatte löschen* an. Das geöffnete Profil und der letzte
  Administrator können nicht gelöscht werden.

### Profil wechseln

**Datei → Profil wechseln…**, Profil wählen, bestätigen. MedReminder
startet mit diesem Profil neu (und fragt nach dessen PIN, falls
vorhanden). Beim Windows-Start öffnet sich das zuletzt genutzte Profil.

### Zur PIN

Die PIN verhindert, dass versehentlich das falsche Profil geöffnet
wird. Sie ist **kein** Schutz: Sie verschlüsselt nichts, und jeder, der
dasselbe Windows-Konto nutzt, kann die Dateien aller Profile lesen.
Drei Fehlversuche schließen die App. Für echte Privatsphäre gib jeder
Person ein eigenes Windows-Konto. Ist eine PIN vergessen, löscht ein
Administrator sie mit **PIN ändern**; hat der einzige Administrator sie
vergessen, siehe [Probleme und Antworten](#faq).

---

<a id="backup"></a>
## 8. Daten schützen: Sicherung und Export

| Option | Wozu | Wo |
|---|---|---|
| **Tägliche automatische Sicherung** | Eine Kopie aller Profile, jeden Tag, in einem eigenen Ordner | Einstellungen → Sicherung / Wiederherstellung |
| **Verschlüsselter Export** | Eine einzelne, übertragbare Datei, zum Umzug auf einen neuen PC oder zur Aufbewahrung | Einstellungen → Sicherung / Wiederherstellung → Alle Daten exportieren (verschlüsselt)… |
| **Cloud-Sicherung** | Eine tägliche verschlüsselte Kopie in OneDrive, Google Drive oder einem synchronisierten Ordner | Einstellungen → Sicherung / Wiederherstellung → Sicherung in synchronisierten Ordner |
| **Synchronisierung / Installation** | Mehrere PCs, die laufend mit denselben Daten arbeiten | [Mehrere Computer](#devices) |

Die Sicherungseinstellungen verwaltet ein Administrator.

### Tägliche automatische Sicherung

1. **Extras → Einstellungen… → Sicherung / Wiederherstellung**.
2. Hake **Tägliche automatische Sicherung** an, wähle den
   **Sicherungsordner** (am besten eine externe Festplatte), die
   **Bevorzugte Uhrzeit** und die **Aufbewahrung (Tage)**.
3. **Sicherungseinstellungen speichern**. **Sicherung jetzt ausführen**
   erstellt sofort eine.

Jedes Profil wird gesichert, als `medreminder-<Profil>-<Datum>-<Zeit>.db`.
MedReminder muss zur gewählten Uhrzeit laufen; war der PC aus, läuft die
Sicherung beim nächsten Start. Wähle für diese Sicherung keinen
Cloud-synchronisierten Ordner: Die Dateien sind nicht verschlüsselt
(MedReminder warnt dich).

- **In bestimmten Ordner exportieren…** — eine Kopie jetzt, wohin du
  willst.
- **Sicherung wiederherstellen…** — wähle eine `.db`-Datei und das
  Profil, das sie erhält. Die aktuellen Daten werden als
  `medreminder.db.bak-<Datum>` beiseitegelegt. Stellst du ins geöffnete
  Profil wieder her, startet MedReminder neu.

### Verschlüsselter Export und Import

Ein Export ist **eine einzelne verschlüsselte Datei** (`.mrz`) mit allen
Daten eines Profils. Sie ist nicht an deinen PC gebunden: Das ist der
empfohlene Weg, auf einen neuen Computer umzuziehen.

**Exportieren** — **Einstellungen → Sicherung / Wiederherstellung →
Alle Daten exportieren (verschlüsselt)…**:

1. Wähle die Zieldatei.
2. Wähle eine **Passphrase** mit mindestens 12 Zeichen und gib sie
   zweimal ein.
3. Nimm, wenn du willst, das SMTP-Passwort, die Sicherungseinstellungen
   und die Benutzereinstellungen (Sprache, Katalogland) mit auf. Das
   SMTP-Passwort wird mit deiner Passphrase verschlüsselt.
4. **Exportieren**.

Ein Administrator mit mehreren Profilen kann **Alle Profile exportieren
(eine verschlüsselte Datei pro Profil)** anhaken und einen Ordner
wählen.

> **Die Passphrase kann nicht wiederhergestellt werden.** Ohne sie ist
> die Datei nie wieder lesbar. Bewahre sie sicher auf.

**Importieren** — **Einstellungen → Sicherung / Wiederherstellung → Aus
Export importieren…**: Wähle die Datei (MedReminder zeigt, was sie
enthält), gib die Passphrase ein, hake *Ich verstehe, dass dadurch die
Daten des aktuellen Profils überschrieben werden* an, klicke auf
**Importieren** und starte neu, wenn du gefragt wirst. Der Import
**ersetzt** die Daten des geöffneten Profils; eine Sicherheitskopie wird
behalten. Eine falsche Passphrase, eine beschädigte Datei oder eine
Datei aus einer neueren Version stoppt den Import, ohne deine Daten
anzurühren. Das Format ist öffentlich (`docs/EXPORT-FORMAT.md`): Deine
Daten sind nie eingesperrt.

### Cloud-Sicherung

Eine verschlüsselte Kopie aller Profile, einmal täglich, in der Cloud.

1. **Einstellungen → Sicherung / Wiederherstellung → Sicherung in
   synchronisierten Ordner (verschlüsselt)**: Kästchen anhaken.
2. **Speicherort**:
   - **OneDrive (App-Ordner)** oder **Google Drive (Ordner
     MedReminder/backups)** — klicke auf **Anmelden…** und melde dich
     mit deinem Konto an;
   - **Ordner** — ein Ordner, den OneDrive, Dropbox, iCloud oder Google
     Drive auf diesem PC bereits synchronisiert.
3. **Zu behaltende Snapshots** (Standard 30).
4. **Backup-Passphrase → Festlegen / ändern…**: mindestens 12 Zeichen.
   Sie bleibt auf diesem PC und wird nie gesendet.
5. Speichern.

**Wiederherstellen** — **Einstellungen → Sicherung /
Wiederherstellung → Aus Cloud-Ordner wiederherstellen…**: Wähle Ordner
oder Konto, wähle eine Kopie (Datum, Profil, Gerät), gib die Passphrase
ein, hake die Bestätigung an und klicke auf **Wiederherstellen**. Die
Kopie ersetzt das **geöffnete** Profil: Um ein anderes Profil
wiederherzustellen, öffne es zuerst.

Gut zu wissen:

- Wer die Backup-Passphrase verliert, verliert die Kopien.
- Die Kopien enthalten weder das SMTP-Passwort noch die Einstellungen:
  Nutze dafür den verschlüsselten Export.
- Wer die Passphrase kennt, kann die Kopie jedes Profils lesen, auch die
  der PIN-geschützten.
- Dein Cloud-Anbieter behält gelöschte Dateien eventuell in seinem
  Papierkorb.
- Das ist eine **Sicherung, keine Synchronisierung**: Für die Arbeit auf
  mehreren PCs nutze die [Synchronisierung](#sync).
- Bei einer geteilten Installation erstellt nur das
  [Master-Gerät](#master) die Cloud-Sicherung.

---

<a id="devices"></a>
## 9. Mehrere Computer

<a id="devices-choice"></a>
### Welche Option brauche ich?

| Situation | Nutze |
|---|---|
| Nur ein PC | Nichts zu tun. Halte eine [Sicherung](#backup) bereit. |
| Einmal auf einen neuen PC umziehen | [Verschlüsselter Export](#backup) auf dem alten PC, Import auf dem neuen. |
| **Dasselbe Profil** auf zwei oder mehr PCs, immer aktuell | [Synchronisierung](#sync) (Extras → Synchronisierung…). |
| **Die ganze Familien-Einrichtung** (Profile, Rollen, PINs, E-Mail, Sicherung) auf mehreren PCs, mit **einem** PC, der die E-Mails sendet | [Installation](#installation) (Extras → Installation…), zusätzlich zur Synchronisierung. |

Beide Optionen brauchen einen Speicherort, den alle PCs erreichen:
**OneDrive**, **Google Drive** oder einen **freigegebenen Ordner**
(einen von Dropbox o. Ä. synchronisierten Ordner oder eine
Netzwerkfreigabe). Die Daten dort sind immer verschlüsselt. Es gibt
keinen MedReminder-Server.

Alle PCs einer Gruppe müssen dieselbe MedReminder-Version haben:
Aktualisiere sie gemeinsam.

<a id="sync"></a>
### Ein Profil zwischen PCs synchronisieren

Die Synchronisierung hält **ein Profil** auf mehreren PCs gleich:
Medikamente, Bestand, Einnahmen, Profilname und Empfänger. Was du auf
einem PC erfasst, erscheint auf den anderen innerhalb weniger Minuten.
Sie wird **für jedes Profil** eingerichtet, bei geöffnetem Profil, von
einem Administrator.

**Auf dem ersten PC**

1. Öffne das Profil, dann **Extras → Synchronisierung… →
   Synchronisierung aktivieren…**.
2. Wähle, wo die Gruppe liegt:
   - **OneDrive** oder **Google Drive**: Melde dich im Browserfenster
     an. Alle PCs müssen **dasselbe** Konto verwenden. MedReminder nutzt
     nur seinen eigenen App-Ordner;
   - **ein freigegebener Ordner**: Wähle ihn aus.
3. Gib einen Namen für diesen PC und eine **Sync-Passphrase** ein
   (mindestens 10 Zeichen, zweimal). Das ist nicht die
   Backup-Passphrase. Bewahre sie gut auf: Sie kann nicht
   wiederhergestellt werden.

**Auf jedem weiteren PC**

1. Lege ein Profil an (beliebiger Name: er wird ersetzt) oder öffne das
   zu ersetzende.
2. **Extras → Synchronisierung… → Einer Gruppe beitreten…**, wähle
   denselben Speicherort, gib einen Namen für diesen PC und dieselbe
   Passphrase ein. Statt der Passphrase kannst du **Mit einem
   Kopplungscode beitreten…** nutzen (siehe unten).
3. Bestätige: **Die Daten dieses Profils auf diesem PC werden durch die
   der Gruppe ersetzt** (eine Kopie bleibt erhalten). MedReminder
   startet neu.

Bei einem freigegebenen Ordner warte vorher, bis er auf dem neuen PC
vollständig heruntergeladen ist. Mit OneDrive oder Google Drive kann der
Beitritt eine Minute dauern. Öffnet die Passphrase mehrere Gruppen
(mehrere Profile mit derselben Passphrase synchronisiert), fragt
MedReminder, welcher beigetreten werden soll.

**Kopplungscode statt Passphrase.** Auf einem PC, der schon in der
Gruppe ist, **Extras → Synchronisierung… → Gerät koppeln…** und klicke
auf **Code anzeigen**, wenn der andere PC bereit ist. Auf dem anderen PC
**Mit einem Kopplungscode beitreten…** und den Code eingeben. Der Code
gilt 10 Minuten und nur, solange sein Fenster offen ist. Wer ihn sieht,
kann die Daten lesen: Sende ihn nie per E-Mail oder Nachricht und zeige
ihn nur, wenn nötig (auch Fernwartungsprogramme sehen ihn).

**Im Alltag**

- Die Synchronisierung läuft einige Sekunden nach jeder Änderung, alle 5
  Minuten und mit **Jetzt synchronisieren**. Der Reiter **Geräte** zeigt
  die PCs und wann jeder zuletzt gesehen wurde.
- Haben zwei PCs dasselbe geändert, bevor sie sich synchronisiert haben,
  gewinnt die neueste Änderung und der Fall erscheint unter
  **Konflikte**: **Verlorenen Wert wiederherstellen** holt den anderen
  Wert zurück, **Verwerfen** entfernt den Eintrag.
- Eine E-Mail zu niedrigem Bestand wird **einmal pro Gruppe** gesendet,
  nicht einmal pro PC. (Zwei PCs, die vor ihrer Synchronisierung prüfen,
  können sie beide senden; ein [Master-Gerät](#master) beseitigt diesen
  Fall.)
- Das Importieren eines Exports oder das Wiederherstellen einer
  Sicherung in einem synchronisierten Profil startet eine neue
  **Generation**: Die anderen PCs werden gewarnt und müssen **Aus der
  Gruppe neu aufbauen…** verwenden.
- **Synchronisierung deaktivieren…** beendet die Synchronisierung auf
  diesem PC und behält seine Daten.
- Läuft die OneDrive- oder Google-Drive-Sitzung ab (Passwortwechsel,
  lange Inaktivität), klicke auf **Erneut bei OneDrive anmelden** /
  **Erneut bei Google Drive anmelden**; nichts geht verloren.

**Ein PC ist verloren oder die Passphrase wurde bekannt.** Wähle im
Reiter **Geräte** den PC und klicke auf **Gerät entfernen…**, oder
nutze **Schlüssel und Passphrase ändern…**. Wähle eine neue
Sync-Passphrase: Der entfernte PC kann nichts mehr lesen, was ab dann
geschrieben wird. Melde diesen PC auch in den Sicherheitseinstellungen
deines Microsoft- oder Google-Kontos ab. Klicke auf jedem anderen PC auf
**Neuen Schlüssel eingeben…** und gib die neue Passphrase oder einen
Kopplungscode ein: Seine Änderungen bleiben erhalten, und MedReminder
startet neu.

<a id="installation"></a>
### Die Installation teilen

Die Synchronisierung arbeitet Profil für Profil. Die **Installation**
ergänzt alles rund um die Profile, damit jeder PC gleich eingerichtet
ist:

| Von allen Geräten geteilt | Pro Gerät |
|---|---|
| Profile: Namen, Rollen, PINs | Welche Profile das Gerät enthält |
| E-Mail-Konto (SMTP, inklusive Passwort) | Oberflächensprache, Textgröße |
| Regeln der Cloud-Sicherung (Speicherort, Anzahl Kopien) | Passphrase und Anmeldung der Cloud-Sicherung |
| Referenzland | Lokale automatische Sicherung |

Jedes Gerät enthält **nur die Profile, die ein Administrator ihm
zuweist**: Der PC der Großeltern kann nur ihr Profil enthalten, der
Familien-PC alle.

**Bevor du beginnst**

- Ein Administratorprofil auf dem PC, der der Haupt-PC wird.
- **Aktivierte Synchronisierung für jedes Profil**, das geteilt werden
  soll (siehe [Synchronisierung](#sync)); ein Profil ohne
  Synchronisierung kann keinem anderen Gerät zugewiesen werden.

**Schritt 1 — Veröffentlichen (auf dem Haupt-PC)**

1. **Extras → Installation… → Installation veröffentlichen…**.
2. Wähle **denselben Speicherort**, der die Synchronisationsgruppen der
   Profile enthält.
3. Wähle eine **Passphrase der Installation**. Damit kann ein
   Administrator ein Gerät hinzufügen und alle Profile wiederherstellen,
   wenn kein anderes Gerät zur Hand ist. Gib sie nur Administratoren;
   sie kann nicht wiederhergestellt werden.

Dieser PC wird das [Master-Gerät](#master).

**Schritt 2 — Ein Gerät hinzufügen**

1. Auf dem Haupt-PC: **Extras → Installation… → Geräte → Gerät
   hinzufügen…**, hake die Profile für das neue Gerät an, dann **Code
   anzeigen**.
2. Auf dem neuen PC:
   - wurde MedReminder dort noch nie genutzt: im Willkommensfenster
     **Einer bestehenden Installation beitreten…** wählen;
   - sonst: **Extras → Installation… → Einer bestehenden Installation
     beitreten…**.
3. Wähle **Mit einem Code** und gib den Code ein. Der Code gilt 10
   Minuten oder bis sein Fenster geschlossen wird.
4. Das Fenster listet jedes Profil („diesem Gerät hinzugefügt“, „bereits
   auf diesem Gerät“, …). MedReminder startet mit den neuen Profilen und
   den Einstellungen der Installation neu. Profile, die schon auf dem
   neuen PC waren, werden der Installation hinzugefügt.

*Kein anderes Gerät zur Hand?* Wähle **Mit der Passphrase der
Installation**, wähle den Speicherort und gib die Passphrase ein. Ein
Administrator wählt dann sein Profil, gibt dessen PIN ein und wählt die
Profile für dieses Gerät.

**Im Alltag**

- Die Installation synchronisiert sich alle 15 Minuten von selbst.
- Eine Änderung an Profil, Rolle, PIN, E-Mail-Konto, Regeln der
  Cloud-Sicherung oder Referenzland auf einem Gerät erreicht die
  anderen.
- Ein später angelegtes Profil: Aktiviere seine Synchronisierung
  (Extras → Synchronisierung…) und weise es dann mit **Gerät
  hinzufügen…** von einem Gerät aus, das es enthält, weiteren Geräten
  zu.
- **Extras → Installation… → Status** zeigt Speicherort, Master und
  eventuell nötige Schritte.

<a id="master"></a>
### Das Master-Gerät

In einer geteilten Installation sendet **nur ein Gerät, der Master**,
alle E-Mails (Warnungen zu niedrigem Bestand und Einnahmeerinnerungen,
für alle Profile, die es enthält, auch nicht geöffnete) und erstellt die
Cloud-Sicherung. Die anderen Geräte zeigen ihre Warnungen nur auf dem
Bildschirm. So kommt jede E-Mail genau einmal.

- Das Gerät, das die Installation veröffentlicht, ist der Master. Der
  Reiter **Geräte** zeigt es in der Spalte **Rolle**.
- Wähle als Master ein Gerät, das **oft eingeschaltet** ist und auf dem
  MedReminder läuft.
- Auf den anderen Geräten öffnen sich Rezeptanforderungen im
  E-Mail-Programm, und **Verbindung testen** funktioniert nur auf dem
  Master. Die E-Mail-Einstellungen lassen sich überall bearbeiten und
  erreichen alle Geräte.
- Ein Master, der die Installation seit **24 Stunden** nicht
  synchronisiert hat, sendet keine E-Mails mehr, bis er wieder
  synchronisiert.
- Installationen, die vor dieser Version veröffentlicht wurden, haben
  keinen Master, bis ein Administrator einen wählt; bis dahin sendet
  jedes Gerät.

**Den Master auf ein anderes Gerät verlegen**

1. **Extras → Installation… → Geräte**, wähle das neue Gerät, **Zum
   Master machen…**, bestätigen. Bis die Übergabe abgeschlossen ist,
   sendet kein Gerät E-Mails.
2. Auf dem neuen Gerät öffnet sich bei geöffnetem Administratorprofil
   das Fenster **Master-Übergabe** von selbst (oder später über **Extras
   → Installation… → Übergabe abschließen…**). Es zeigt die
   Einstellungen und bittet dich:
   - die **E-Mail-Verbindung von diesem Gerät zu testen**;
   - dich beim Speicherort der Cloud-Sicherung mit demselben Konto
     **anzumelden**;
   - die **Passphrase der Cloud-Sicherung** erneut einzugeben (sie wird
     nie zwischen Geräten kopiert);
   - optional die Passphrase der Installation einzugeben, um Profile zu
     holen, die kein erreichbares Gerät enthält.
3. **Bestätigen**. Das neue Gerät übernimmt, sobald der alte Master bei
   seiner nächsten Synchronisierung abgibt. Neue Profile erscheinen beim
   nächsten Start.

**Der Master ist defekt oder verloren.** Mach dasselbe von einem anderen
Gerät aus: Der neue Master übernimmt von selbst, wenn der alte sich seit
25 Stunden nicht gemeldet hat. Entferne danach den alten (unten).

<a id="remove-device"></a>
### Verlorenes oder ersetztes Gerät

Wenn ein Gerät verloren, verkauft oder verschenkt wird:

1. Auf dem **Master** (er enthält alle Profile): **Extras →
   Installation… → Geräte**, Gerät wählen, **Gerät entfernen…**.
2. Wähle eine **neue Passphrase der Installation**. Das entfernte Gerät
   behält, was es schon hat, erhält aber nichts Neues; auch die Profile,
   die es enthielt, bekommen neue Schlüssel.
3. MedReminder bietet an, einen Code anzuzeigen. Die anderen Geräte
   synchronisieren nicht mehr, bis sie den neuen Schlüssel haben: Auf
   jedem öffnet ein Administrator **Extras → Installation… → Neuen
   Schlüssel eingeben…** und gibt die neue Passphrase oder den Code ein.
   Dort inzwischen gemachte Änderungen bleiben erhalten.
4. Die neuen Profilschlüssel kommen von selbst auf die anderen Geräte;
   ein in diesem Moment geöffnetes Profil bittet um einen Neustart von
   MedReminder.

Wird der Master von einem anderen Gerät aus entfernt, wird dieses Gerät
zum Master. Melde das verlorene Gerät auch in deinem Microsoft- oder
Google-Konto ab.

---

<a id="settings"></a>
## 10. Einstellungen und Alltag

Alles unter **Extras → Einstellungen…**:

- **Allgemein → Oberflächensprache**: Englisch, Italienisch,
  Französisch, Spanisch oder Deutsch. Auch E-Mails und der Therapieplan
  verwenden sie. MedReminder startet neu.
- **Allgemein → Textgröße (dieses Profil)**: Normal, Groß, Sehr groß,
  pro Profil. MedReminder folgt auch der Windows-Skalierung und den
  Kontrastdesigns. Auf einem kleinen Bildschirm wähle lieber Groß.
- **Allgemein → Beim Start nach Updates suchen (GitHub)**: sucht nach
  einer neuen Version (nichts wird von selbst installiert) und
  aktualisiert den Katalog. **? → Nach Updates suchen…** sucht sofort.
- **Autostart → MedReminder bei der Windows-Anmeldung starten**: startet
  ausgeblendet im Infobereich. Keine Administratorrechte nötig.

**Symbol im Infobereich.** Ein Doppelklick öffnet das Fenster; ein
Rechtsklick bietet *MedReminder öffnen*, *Jetzt prüfen*,
*Einstellungen…*, *Beenden*.

**Entwicklung unterstützen.** Falls aktiviert, öffnet **? → Entwicklung
unterstützen…** im Browser eine Seite für einen freiwilligen Beitrag
(Stripe oder PayPal). MedReminder sieht nie deine Zahlungsdaten.

---

<a id="faq"></a>
## 11. Probleme und Antworten

**Ich bekomme keine E-Mails.**
Prüfe der Reihe nach: *Verbindung testen* unter Einstellungen →
E-Mail-SMTP; den *Empfänger* unter Einstellungen →
Benachrichtigungen; den Kanal **E-Mail** beim Medikament; ob
MedReminder läuft. Bei einer geteilten Installation sendet nur der
Master: Sieh unter **Extras → Installation… → Status** nach.

**„Dieses Gerät ist der Master, hat die Installation aber seit mehr als
24 Stunden nicht synchronisiert“.**
Der Master erreicht den Speicherort nicht. Prüfe die
Internetverbindung und die Anmeldung bei OneDrive / Google Drive, dann
**Jetzt synchronisieren**.

**„Der Schlüssel der Installation wurde auf einem anderen Gerät
geändert“.**
Ein Gerät wurde entfernt. Öffne **Extras → Installation… → Neuen
Schlüssel eingeben…** und gib die neue Passphrase ein oder einen Code,
den ein Gerät mit dem neuen Schlüssel anzeigt.

**„Ein Gerät wurde aus der Installation entfernt … dieses Profil hat
einen neuen Schlüssel. Jetzt neu starten?“**
Antworte mit Ja: Das Profil übernimmt den neuen Schlüssel beim Neustart
von MedReminder.

**„Dieses Gerät wurde aus der Installation entfernt“.**
Das Gerät behält seine Daten, erhält aber nichts mehr. Um es wieder zu
nutzen, fügt ein Administrator es als neues Gerät hinzu.

**Die Master-Übergabe wird nicht fertig.**
Der alte Master gibt bei seiner nächsten Synchronisierung ab. Ist er
dauerhaft aus, übernimmt der neue Master 25 Stunden nachdem der alte
zuletzt gesehen wurde.

**„Die Synchronisationsgruppe dieses Profils gehört zu einer anderen
Installation“.**
Das Profil wurde von einer anderen Installation veröffentlicht. Tritt
dieser Installation bei (**Einer bestehenden Installation
beitreten…**).

**Ich habe eine PIN vergessen.**
Ein Administrator löscht sie mit **Extras → Profile verwalten… → PIN
ändern**. Kann das niemand sonst: Beende MedReminder, öffne
`%LOCALAPPDATA%\MedReminder\profiles.json` mit dem Editor und lösche für
dieses Profil die Werte von `PinHash` und `PinSalt` und setze
`PinIterations` auf `0`. MedReminder erfasst die Änderung beim nächsten
Start; bei einer geteilten Installation erreicht sie die anderen
Geräte.

**Ich habe eine Passphrase vergessen.**
Die Passphrasen für Export, Sicherung, Synchronisierung und
Installation können nicht wiederhergestellt werden. Du kannst neue
festlegen (neuer Export, neue Backup-Passphrase, *Schlüssel und
Passphrase ändern…*), aber mit der alten verschlüsselte Dateien bleiben
unlesbar.

**„Läuft bereits“.**
MedReminder ist schon geöffnet: Suche sein Symbol im Infobereich.

**Die Daten wirken beschädigt.**
Stelle eine Sicherung (Einstellungen → Sicherung / Wiederherstellung →
Sicherung wiederherstellen…) oder einen Export wieder her. Die
Protokolldateien (unten) helfen zu verstehen, was passiert ist.

---

<a id="data"></a>
## 12. Wo MedReminder die Daten speichert

Alles liegt unter `%LOCALAPPDATA%\MedReminder\` (in die Adressleiste
des Datei-Explorers einfügen). MedReminder schreibt nirgendwo sonst hin,
außer den Sicherungs- und Exportdateien, die du selbst ablegst.

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json              Profilliste, Rollen, PINs (gehasht)
├── smtp.settings.json         E-Mail-Konto (ohne Passwort)
├── smtp.protected             E-Mail-Passwort, von Windows verschlüsselt
├── backup.settings.json       Sicherungseinstellungen
├── cloud-backup.protected     Passphrase der Cloud-Sicherung, von Windows verschlüsselt
├── user.settings.json         Sprache, Referenzland, Update-Prüfung
├── household\                 geteilte Installation (nur wenn genutzt)
├── logs\medreminder-JJJJMMTT.log
└── profiles\
    └── <Profil>\
        ├── medreminder.db     Medikamente und Bestand des Profils
        ├── notifications.settings.json   Empfänger
        ├── ui.settings.json   Textgröße
        └── sync.*             Synchronisierungseinstellungen (nur wenn genutzt)
```

- Die **Protokolle** halten fest, was die App getan hat (Prüfungen,
  gesendete E-Mails, Fehler). Sie enthalten nie Passwörter,
  E-Mail-Texte oder medizinische Notizen.
- Die Datenbank ist nicht verschlüsselt: Sie wird durch dein
  Windows-Konto geschützt. Exporte und Cloud-Kopien sind verschlüsselt.
- **Aktualisierung von einer sehr alten Version** (eine einzelne
  `medreminder.db` direkt im Ordner): Der erste Start verschiebt sie in
  ein Profil namens *User* und behält eine Kopie in
  `backups\pre-migration-…`, die du löschen kannst, sobald alles passt.

---

<a id="limits"></a>
## 13. Was MedReminder nicht tut

- Es verfolgt nicht, welche Dosen du genommen hast, und warnt nicht vor
  vergessenen Dosen (die Erinnerung zur Einnahmezeit ist nur ein
  Hinweis).
- Es gibt keine Therapieanweisungen und prüft weder Dosen noch
  Wechselwirkungen.
- Es bestellt keine Medikamente und kontaktiert deinen Arzt nicht von
  selbst.
- Es führt aus einer Sicherung wiederhergestellte Daten nicht zusammen:
  Wiederherstellen und Importieren ersetzen immer.

Sein Zweck ist, dir rechtzeitig zu sagen, dass du ein neues Rezept
brauchst.

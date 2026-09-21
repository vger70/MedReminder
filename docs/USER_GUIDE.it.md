# MedReminder — Guida rapida

Guida operativa per l'utente finale. Il file
[`ANALYSIS.md`](ANALYSIS.md) descrive invece l'architettura tecnica.

> **MedReminder è un promemoria organizzativo, non un dispositivo
> medico.** Non fornisce diagnosi, indicazioni terapeutiche, modifiche
> di terapia o suggerimenti clinici. Ogni decisione sulla terapia va
> presa con il proprio medico.

---

## Primo avvio

1. Lancia `MedReminder.exe`.
2. Al primissimo avvio l'app mostra una **procedura guidata di
   benvenuto** e chiede di creare il primo profilo. Questo profilo
   è sempre l'**amministratore**: può gestire il server email
   condiviso e il backup automatico e può creare gli altri profili
   (vedi *Profili multipli*). Puoi impostare un PIN opzionale nella
   stessa procedura.
3. Il database viene creato automaticamente sotto
   `%LOCALAPPDATA%\MedReminder\profiles\<id-profilo>\medreminder.db`.
4. In alto trovi la toolbar; in basso la barra di stato mostra il
   profilo attivo ("Profilo: Owner (amministratore)" per un
   amministratore, "Profilo: Nonna" per un utente normale). L'icona
   nell'area di notifica di Windows resta sempre visibile finché
   l'app è in esecuzione.

### SmartScreen di Windows al primo lancio

I binari distribuiti non sono firmati digitalmente. Al primo lancio
di `MedReminder.exe`, Windows mostra una finestra blu "PC protetto
da Windows". Per procedere:

1. Clicca **Ulteriori informazioni**.
2. Clicca **Esegui comunque**.

Windows ricorda la scelta per quello specifico file: gli avvii
successivi non chiederanno più conferma. Se installi tramite MSI, la
finestra UAC riporta "Editore sconosciuto" per lo stesso motivo ed è
normale.

## Aggiungere una medicina

1. Toolbar → **Nuova medicina**.
2. Compila i campi obbligatori (marcati con `*`): Nome, Unità, Dose per
   somministrazione, Somministrazioni al giorno, Data inizio,
   Soglia avviso (giorni residui).
3. Campi opzionali: Principio attivo, Confezione, Data fine terapia,
   Medico di riferimento, Note.
4. **Quantità iniziale in scorta**: imposta le compresse/ml/dosi già
   in tuo possesso al momento della registrazione. Verrà creato un
   movimento `InitialLoad`.
5. **Canali di notifica**: spunta Windows e/o Email. Devi aver
   configurato le impostazioni SMTP (vedi sotto) perché l'email
   funzioni.
6. **Schema**: lascia su **Semplice** per una dose fissa presa ogni
   giorno — è il default e corrisponde al comportamento storico
   dell'app. Consulta *Regimi complessi* qui sotto per terapie
   cicliche, a scalare, settimanali o al bisogno.
7. **Salva**.

## Regimi complessi

Non tutte le terapie consumano la stessa quantità di farmaco ogni
giorno. Nel form **Nuova medicina** il selettore *Schema* passa da
**Semplice** (una dose giornaliera fissa) a **Avanzato** e mostra
un menu *Tipo di regime* con quattro forme aggiuntive:

- **Settimanale** — una quantità diversa per ciascun giorno della
  settimana (per esempio un anticoagulante orale con dosi diverse
  lun/mer/ven rispetto agli altri giorni).
- **Ciclico (N on / M off)** — una quantità fissa per i primi `N`
  giorni del ciclo seguiti da `M` giorni off. Tipico delle terapie
  ormonali e dei bolo di cortisone.
- **Scalare** — una dose che scende (o sale) di un valore fisso ogni
  intervallo di giorni finché non raggiunge la dose finale, e poi si
  ferma lì. Tipico della decalage di glucocorticoidi.
- **Al bisogno (PRN)** — nessun consumo pianificato. MedReminder
  continua a tenere traccia della scorta ma la colonna *giorni
  residui* resta vuota finché non cambi il tipo di schema.

Quando selezioni Avanzato i campi *Dose per somministrazione*,
*Somministrazioni al giorno* e *Orari* in alto nel form vengono
disattivati: lo schema che configuri sotto è l'unica sorgente per la
quantità giornaliera. Per tornare al flusso "un click" con dose
fissa, riporta il selettore su Semplice.

Per cambiare la forma di una terapia in corso, usa *Toolbar → Cambia
schedulazione*. Lo stesso selettore Semplice/Avanzato è disponibile
lì e si applica a partire dalla *Data di decorrenza* che scegli, in
modo che lo schema precedente resti valido per i giorni prima di
quella data.

MedReminder non è un dispositivo medico: non controlla le dosi
massime giornaliere, non avvisa di sovradosaggi e non verifica
interazioni farmacologiche. Segue soltanto la terapia che il tuo
medico ha prescritto e ti ricorda prima che la scorta finisca.

## Promemoria all'orario della dose

Per le medicine che hanno **slot di dose con orario** (un momento
preciso della giornata impostato su ciascuno slot di somministrazione)
puoi chiedere a MedReminder di ricordarti *nel momento in cui la dose
è dovuta*. Spunta **Ricordami all'orario della dose** nel modulo di
aggiunta o modifica della medicina. L'opzione è disponibile solo
quando la medicina ha almeno uno slot con un orario e ha ancora scorta
a disposizione; altrimenti resta disattivata.

Quando è attiva, a ogni orario dello slot MedReminder mostra una
notifica sul desktop ("È ora di prendere …"). Se hai configurato le
notifiche via email e hai selezionato il canale email per quella
medicina, lo stesso promemoria viene inviato anche via email.

Alcuni dettagli utili da sapere:

- **Un promemoria per slot al giorno.** Ogni slot con orario si attiva
  al massimo una volta in un dato giorno di calendario, anche se l'app
  viene riavviata.
- **Finestra di tolleranza.** Se l'app non è in esecuzione esattamente
  all'orario dello slot — per esempio il computer era sospeso — il
  promemoria si attiva comunque al successivo controllo dell'app,
  purché entro 30 minuti dall'orario dello slot. Oltre questa finestra
  la dose è considerata mancata e non viene mostrato alcun promemoria;
  MedReminder non tiene un registro delle dosi mancate e non fornisce
  mai consigli clinici.
- **Scorta a zero disattiva il promemoria.** Quando la scorta arriva a
  zero non viene inviato alcun promemoria, perché non c'è più nulla da
  prendere.
- **Ora legale.** Nella notte in cui l'orologio va avanti, uno slot che
  cade nell'ora saltata non si attiva (quell'orario non esiste). Nella
  notte in cui l'orologio torna indietro, lo slot si attiva una volta
  sola, come di consueto.

Questo promemoria è solo un avviso di comodità. Non registra se hai
preso la dose e non modifica la scorta: per quello usa *Registra
assunzione*.

## Catalogo di riferimento (multi-paese)

MedReminder include due snapshot di un catalogo di medicinali di
riferimento e li usa per completare automaticamente il form della
medicina.

- Nei campi **Nome commerciale** e **Principio attivo** inizia a
  scrivere per vedere le corrispondenze. Selezionando una riga viene
  compilato anche l'altro campo (e, dietro le quinte, il codice
  nazionale e il codice ATC), così non devi digitare entrambi.
- L'elenco a tendina mostra al massimo 20 righe e si aggiorna circa
  150 ms dopo che smetti di digitare. Un pallino rosso accanto a una
  riga indica che il prodotto è **sospeso o ritirato dal commercio**:
  puoi comunque sceglierlo, MedReminder si limita a segnalare lo stato.
- **Medicina non presente in catalogo?** Continua a scrivere il testo
  che vuoi. Se non selezioni alcuna riga dell'elenco, MedReminder
  salva il testo così com'è e non memorizza alcun collegamento al
  catalogo: il promemoria funziona esattamente come prima.
- Il **paese di riferimento** si sceglie da *Impostazioni → Generale
  → Paese di riferimento*. Il valore predefinito è Italia; una
  modifica ha effetto alla successiva apertura del form della
  medicina.

### Medicinali ad autorizzazione centralizzata UE

Alcuni medicinali sono autorizzati per tutta l'Unione Europea con
la *procedura centralizzata*, gestita dall'Agenzia Europea per i
Medicinali (EMA). MedReminder incorpora il catalogo EMA EPAR —
*European public assessment reports* — e mostra questi medicinali
nello stesso elenco a tendina dell'autocomplete.

- Se il tuo **paese di riferimento è uno Stato UE** (es. l'Italia
  predefinita, o un altro paese UE che scegli da Impostazioni),
  l'autocomplete mostra **il tuo catalogo nazionale + i medicinali
  centralizzati validi in tutta l'UE**, mescolati nello stesso
  elenco. Non devi cambiare nulla: le righe UE compaiono da sole
  quando corrispondono.
- Se imposti il **paese di riferimento su `EU`**, l'autocomplete
  mostra **solo** i medicinali centralizzati UE, senza righe
  nazionali. Utile quando vuoi specificamente scorrere o collegare
  un prodotto alla sua autorizzazione EMA.
- Un medicinale UE e un prodotto nazionale equivalente possono
  comparire contemporaneamente nell'elenco; le due righe non
  vengono deduplicate. Scegli quella che corrisponde alla confezione
  che hai in mano.

### Cataloghi nazionali spagnolo e francese

Il catalogo spagnolo proviene da AEMPS CIMA (registro
"Medicamentos") e quello francese da ANSM BDPM (*Base de données
publique des médicaments*). Nell'autocomplete si comportano
esattamente come il catalogo italiano:

- Imposta **Impostazioni → Generale → Paese di riferimento** su
  `ES` o `FR` una volta caricato lo snapshot corrispondente (`ES`
  e `FR` compaiono automaticamente nel menu a tendina non appena
  i loro cataloghi sono in DB).
- L'autocomplete elenca allora **il tuo catalogo nazionale + i
  medicinali centralizzati UE**, mescolati nello stesso elenco.
  Spagna e Francia sono Stati UE, quindi le righe UE sono
  incluse per impostazione predefinita esattamente come per
  l'Italia.
- Tutte le altre regole restano identiche: seleziona una riga per
  riempire entrambi i lati, oppure continua a digitare per
  memorizzare una voce a testo libero che l'app non conosce.

**Fonti dei dati e termini di riuso.** Il catalogo italiano
proviene dai dati aperti AIFA (Agenzia Italiana del Farmaco),
pubblicati sotto licenza Creative Commons Attribution 4.0
International (CC BY 4.0). Il catalogo UE proviene dal dataset
EMA EPAR, riutilizzato secondo la nota legale EMA (decisione
2011/833/UE sul riuso dei documenti della Commissione). Il
catalogo spagnolo proviene da AEMPS CIMA, riutilizzato secondo il
regime spagnolo di riuso dell'informazione del settore pubblico
(Legge 37/2007). Il catalogo francese proviene da ANSM BDPM,
riutilizzato sotto Licence Ouverte Etalab 2.0. La finestra
Informazioni e il file `THIRD-PARTY-NOTICES.md` nella radice
dell'installazione riportano le attribuzioni complete.

## Aggiungere scorte (nuova confezione)

1. Seleziona la medicina nella griglia.
2. Toolbar → **Aggiungi scorte**.
3. Scegli il tipo di movimento:
   - **Nuova confezione**: il caso normale dopo un acquisto.
   - **Aggiunta manuale**: ad esempio se ricevi campioni dal medico.
   - **Correzione in eccesso**: hai contato meno del vero.
4. Inserisci la quantità (in unità della medicina) e conferma.

**Effetto**: la scorta aumenta e lo `StockEpoch` della medicina
avanza di 1. Questo fa ripartire il ciclo di avviso — la prossima
notifica potrà essere emessa quando la scorta scenderà sotto soglia
di nuovo.

## Correggere una quantità in difetto

Se ti accorgi che la scorta effettiva è minore di quella calcolata
(compressa persa, versata, ecc.):

1. Seleziona la medicina.
2. Toolbar → **Correggi scorte**.
3. Il default kind è **Correzione in difetto**: la quantità digitata
   viene sottratta dalla scorta. Non fa avanzare l'epoch: non
   riprograma il ciclo notifiche.

Se la correzione porterebbe la scorta sotto zero, l'operazione viene
bloccata con un errore.

## Modificare o disattivare una medicina

- **Modifica**: doppio click sulla riga oppure toolbar → **Modifica**.
  Puoi cambiare nome, principio attivo, package, unità, soglia, medico,
  note, data fine, canali di notifica, e stato Attiva/Non attiva.
  **La dose e la frequenza NON si modificano da qui**: usare il
  cambio schedule (funzione da linea di comando o edit DB per l'MVP).
- **Disattiva**: toolbar → **Disattiva**. La medicina scompare dai
  controlli automatici e dagli avvisi, ma i dati storici (movimenti,
  notifiche) restano nel DB per audit.

## Profili multipli e ruoli amministratore/utente

MedReminder può gestire farmaci per **più persone** dallo stesso
account Windows — caso tipico: un genitore che segue la propria
terapia e quella di uno o due familiari. Ogni profilo ha il proprio
database e il proprio destinatario email; il server SMTP, la
cartella del backup automatico e il registro dei profili sono
condivisi e gestiti da un profilo **amministratore**.

### Ruoli

- **Amministratore** — gestisce le impostazioni globali (SMTP,
  Backup, elenco profili, PIN di qualunque profilo) oltre ai propri
  dati. Deve sempre esistere almeno un amministratore.
- **Utente** — gestisce solo il proprio profilo (medicine, scorte,
  terapie, destinatario email personale). Non vede la scheda SMTP
  né la scheda Backup nelle Impostazioni, e non vede
  `Strumenti → Gestisci profili…`.

Il ruolo si sceglie alla creazione del profilo e **non può essere
cambiato in seguito**. Se in futuro dovessi voler cambiare il ruolo
a un profilo, la soluzione oggi è creare un nuovo profilo con il
ruolo desiderato e copiarci sopra i dati.

Il ruolo è una barriera "soft": chi ha accesso al filesystem può
modificare `profiles.json` a mano e diventare amministratore.
L'interfaccia rispetta il ruolo, il filesystem no.

### Creare altri profili (amministratore)

1. `Strumenti → Gestisci profili…` — la voce esiste solo per gli
   amministratori.
2. **Nuovo profilo** → inserisci un nome, scegli Amministratore o
   Utente (predefinito: Utente), imposta eventualmente un PIN.
   Conferma.
3. Il nuovo profilo appare subito nel picker al successivo avvio.

### Cambiare profilo

`File → Cambia profilo…` apre il picker. Scegli il profilo di
destinazione e conferma: l'app si riavvia automaticamente in modo
che il nuovo profilo sia completamente isolato. Se il profilo
scelto ha un PIN, la richiesta appare prima che l'app si apra.

### Rinominare, cambiare PIN, eliminare

`Strumenti → Gestisci profili…` (solo amministratore) offre anche:

- **Rinomina** — solo il nome visualizzato. L'id interno non
  cambia mai.
- **Cambia PIN** — imposta, sostituisci o rimuovi il PIN di
  qualunque profilo.
- **Elimina** — chiede di **digitare il nome del profilo** per
  confermare. Una casella separata consente anche di eliminare i
  dati del profilo su disco; è disattivata di default, così la
  cartella resta disponibile per un ripristino manuale.

Il profilo attivo non è eliminabile (cambia prima profilo), e
neppure l'ultimo amministratore rimasto.

### Il PIN

Il PIN è una **barriera, non protezione**. Blocca cambi di profilo
accidentali, ma **non** cifra i dati — chiunque abbia accesso a
questo PC può comunque aprire i file del profilo. Tre tentativi
errati chiudono la richiesta e l'app.

Se dimentichi un PIN, rimuovilo a mano da
`%LOCALAPPDATA%\MedReminder\profiles.json` (cancella `PinHash` e
`PinSalt` e imposta `PinIterations` a `0` nella voce interessata).
Questo è documentato invece di essere risolto con una funzione
"resetta PIN" per scelta: il recupero non è un bug, perché il PIN
non è sicurezza.

### Struttura su disco

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                        ← registro dei profili
├── smtp.settings.json                   ← SMTP condiviso (admin)
├── smtp.protected                       ← password DPAPI-cifrata
├── backup.settings.json                 ← config backup condivisa (admin)
├── backup.state.json                    ← stato ultimo backup automatico
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    ├── <id-profilo>\                    ← una cartella per profilo
    │   ├── medreminder.db (+ -wal, -shm)
    │   └── notifications.settings.json  ← ToAddress di questo profilo
    └── …
```

### Il backup automatico copre tutti i profili

Quando il backup automatico è attivo, ogni tick giornaliero salva
il database di **tutti** i profili nella cartella condivisa, con
nomi del tipo `medreminder-<id-profilo>-YYYYMMDD-HHmmss.db`. La
retention viene applicata per-profilo, in modo che il backup più
recente di un profilo non protegga i backup più vecchi di un
altro.

Quando ripristini da `Impostazioni → Backup → Ripristina backup…`,
la finestra chiede in quale profilo caricare il database
importato. In automatico seleziona il profilo indicato nel nome
del file. Se ripristini in un profilo diverso da quello attivo,
l'app non si riavvia; se ripristini nel profilo attivo, l'app si
riavvia per aprire il nuovo database in modo pulito.

### Avvio automatico con Windows

La voce di avvio automatico di Windows è unica per utente Windows.
Al login l'app apre il profilo **più recente** senza mostrare il
picker; se quel profilo ha un PIN, la richiesta viene mostrata
sopra la finestra vuota. Per aprire un profilo diverso all'avvio,
usa `File → Cambia profilo…` una volta che l'app è aperta.

### Aggiornamento da un'installazione single-user

Se hai già un file `medreminder.db` in
`%LOCALAPPDATA%\MedReminder\` da una versione precedente, al
prossimo avvio l'app esegue una **migrazione V1 → V2** una tantum:

1. Fa un backup obbligatorio in
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   che contiene il `medreminder.db` originale (e i suoi file
   collaterali) e il `smtp.settings.json` originale.
2. Sposta il database in `profiles\default\medreminder.db` e crea
   il `profiles.json` iniziale con un unico profilo amministratore
   di nome `User`.
3. Estrae il destinatario (`Smtp.ToAddress`) da
   `smtp.settings.json` in
   `profiles\default\notifications.settings.json`.

La migrazione è **atomica** — se un passo dopo il pre-backup
fallisce, l'app fa il rollback allo stato V1 e conserva il backup
di pre-migrazione.

Il **backup di pre-migrazione non viene ripulito automaticamente**:
dopo aver verificato che l'app migrata apre gli stessi dati, puoi
eliminare a mano la cartella `backups\pre-migration-*`. Rinomina
il profilo `User` come preferisci da
`Strumenti → Gestisci profili… → Rinomina`.

## Configurare l'invio email

**Impostazioni → Email SMTP**:

- **Host**: es. `smtp.gmail.com`, `smtp.libero.it`, ecc.
- **Porta**: solitamente 587 (StartTLS) o 465 (SSL/TLS diretto).
  MedReminder usa StartTLS quando la relativa spunta è attiva.
- **Username / Nuova password**: se il server richiede autenticazione.
  La password viene cifrata con DPAPI e salvata in
  `%LOCALAPPDATA%\MedReminder\smtp.protected`. Non finisce in
  `smtp.settings.json` né nei log.
- **Rimuovi la password salvata**: cancella `smtp.protected` alla
  prossima Salva.
- **Mittente / Nome mittente**: il "from" delle email inviate.
- **Destinatario**: dove ricevere gli avvisi (di solito il tuo
  indirizzo personale).
- **Timeout**: secondi prima di considerare fallita la connessione.
- **Prova connessione**: apre una sessione SMTP, autentica, chiude.
  Non invia una vera email.
- **Salva impostazioni SMTP**: scrive
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`. La configurazione
  viene ricaricata a caldo, senza riavviare l'app.

### Esempio: Gmail con app-password

1. Attiva la 2FA sul tuo account Google.
2. Crea una app-password su
   `myaccount.google.com/apppasswords`.
3. In MedReminder: Host `smtp.gmail.com`, Porta `587`,
   StartTLS attivo, Username `tuoindirizzo@gmail.com`, Password
   l'app-password appena creata.

Google e altri provider possono cambiare i requisiti: consulta la
documentazione del tuo provider se il test connessione fallisce.

## Avvio automatico con Windows

**Impostazioni → Avvio automatico**: spunta la casella. Viene creata
una voce in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` che
lancia MedReminder con l'argomento `--minimized` (parte in tray,
finestra nascosta). Non richiede privilegi amministrativi.

## Backup del database

**Impostazioni → Backup / Ripristino**:

- **Esporta**: scegli una cartella. Il DB viene copiato come
  `medreminder-YYYYMMDD-HHMMSS.db`. Salva la copia su un drive
  esterno o cloud personale se vuoi resilienza.
- **Ripristina**: seleziona un backup precedente. Il DB corrente
  viene rinominato in `medreminder.db.bak-<timestamp>` (non perso!)
  e sostituito. **Chiudi e riapri MedReminder** dopo il ripristino
  per evitare inconsistenze.

## Controlla ora

Il monitor gira automaticamente ogni 30 minuti (configurabile in
`appsettings.json` alla voce `Monitoring:IntervalMinutes`). Se vuoi
forzare un check subito: toolbar → **Controlla ora** oppure menu
tray → **Controlla ora**.

## Icona nell'area di notifica

- **Doppio click** → apre la finestra.
- **Menu contestuale (destro)**:
  - Apri MedReminder
  - Controlla ora
  - Impostazioni…
  - Esci

Chiudere la finestra principale con la X minimizza in tray;
l'app continua a girare in background. Per uscire davvero: menu
tray → **Esci**.

## Lingua interfaccia

**Impostazioni → Generale**: scegli la lingua dal dropdown
(Italiano o Inglese) e clicca **Salva lingua**. MedReminder si
riavvia automaticamente per applicare la modifica.

Note:
- Le notifiche toast di Windows seguono sempre la lingua di
  sistema (Windows), indipendentemente dalla lingua qui scelta.
- Le notifiche email e la scheda terapia usano la lingua
  selezionata qui.

## Diagnostica

- **Log**: `%LOCALAPPDATA%\MedReminder\logs\medreminder-YYYYMMDD.log`.
  Contiene tick dello scheduler, invii notifica, errori.
- **DB corrotto o incompatibile**: cancella `medreminder.db`,
  `medreminder.db-shm`, `medreminder.db-wal` sotto
  `%LOCALAPPDATA%\MedReminder\`. Al prossimo avvio il DB viene
  ricreato vuoto. Fai prima un backup manuale se hai dati importanti.
- **App già in esecuzione**: solo un'istanza per utente Windows.
  Se il lancio segnala "già in esecuzione", cerca l'icona nell'area
  di notifica.

## Cosa NON fa MedReminder

- Non ricorda di prendere la medicina alla singola dose (non è una
  sveglia).
- Non fornisce indicazioni terapeutiche o interazioni farmacologiche.
- Non sincronizza tra dispositivi diversi.
- Non ordina medicine automaticamente.
- Non contatta il tuo medico direttamente.

Serve solo a farti sapere per tempo che devi richiedere una nuova
ricetta.

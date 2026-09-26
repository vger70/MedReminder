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
- **Scalare** — una dose che scende (o sale) gradualmente fino alla
  dose finale. Il pannello Scalare offre due varianti tramite il
  selettore *Lineare / A stadi*:
  - **Lineare** — la dose varia di un valore fisso ogni numero fisso
    di giorni finché non raggiunge la dose finale, e poi si ferma.
    Tipico della decalage semplice di glucocorticoidi.
  - **A stadi** — un elenco esplicito di stadi, ognuno con la propria
    dose e la propria durata in giorni (per esempio 4/giorno per 7
    giorni, poi 2/giorno per 7 giorni, poi 1/giorno per 14 giorni).
    Usa *Aggiungi stadio* / *Rimuovi* per costruire la sequenza e
    controlla l'anteprima live sotto l'elenco prima di salvare. Spunta
    *Mantieni l'ultima dose come dose di mantenimento* se la dose
    finale deve proseguire indefinitamente anziché terminare il ciclo.
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

## Scansionare il codice a barre della confezione

Con il catalogo di riferimento attivo, la scheda della medicina ha un
pulsante **Scansiona codice…** accanto al nome commerciale. Compila
la medicina dal catalogo in un solo passaggio.

1. Fai clic su **Scansiona codice…**. Si apre una piccola finestra
   pronta a ricevere il codice.
2. Scansiona il codice a barre sulla scatola con un lettore di codici
   a barre USB, oppure digita il codice stampato sotto il codice a
   barre e premi **Invio**.
3. Se il codice è nel catalogo, la finestra si chiude e la scheda
   viene compilata come se avessi scelto la riga dal menu a tendina.
   Altrimenti un messaggio mostra il codice letto e non viene
   modificato nulla.

Note:

- Fai prima clic su **Scansiona codice…**, poi scansiona. Una
  scansione fatta mentre è attiva la scheda della medicina scrive il
  codice nel campo in cui ti trovi.
- Sulle confezioni italiane il lettore legge il **codice AIC** (il
  codice a barre con il testo `A` seguito da 9 cifre). Va bene
  qualsiasi lettore USB per codici 1D; se il codice non viene
  riconosciuto, abilita la simbologia **Code 32** (Italian
  Pharmacode) nelle impostazioni del lettore. Il codice quadrato 2D
  (DataMatrix) richiede un lettore 2D e spesso non corrisponde ancora
  a una voce del catalogo.
- Il lettore deve usare lo stesso layout di tastiera di Windows. Con
  una tastiera francese (AZERTY) o tedesca (QWERTZ) imposta il
  lettore su quel layout, altrimenti il codice non viene riconosciuto.
- Funziona anche un lettore che non invia Invio dopo il codice: il
  codice viene accettato un istante dopo la scansione.

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

## Contare le scorte

Quando le compresse nell'armadietto non corrispondono più alla scorta
mostrata dall'app, contale e lascia che l'app registri la correzione:

1. Seleziona la medicina.
2. Menu **Scorte → Conta scorte…**.
3. Digita la quantità contata. Il dialogo mostra la scorta attesa
   (con i consumi automatici aggiornati), la differenza di scorta con
   il segno e la data di esaurimento prima e dopo la correzione.
4. In **Già assunto oggi** indica quanto della quantità prevista per
   oggi avevi già assunto al momento del conteggio. L'app propone le
   dosi il cui orario è passato; senza orari impostati propone 0. Se
   indichi solo una parte della quantità di oggi, l'elenco mostra la
   scorta a inizio giornata finché il consumo di oggi non viene
   registrato.
5. Aggiungi una nota se vuoi (predefinita: "Conteggio scorte") e
   conferma con **Registra conteggio**.

**Effetto**: viene registrata una sola correzione, positiva o
negativa, in modo che la scorta coincida con la quantità contata. Una
differenza nulla non registra nulla. Una correzione positiva che riporta la scorta sopra la soglia di
avviso fa avanzare l'epoca, così in seguito può partire un nuovo
avviso di scorta bassa; una correzione negativa non la fa mai
avanzare. La differenza
è solo un dato di scorta: non viene interpretata come dosi saltate o
in più.

## Modificare o disattivare una medicina

- **Modifica**: doppio click sulla riga oppure toolbar → **Modifica**.
  Puoi cambiare nome, principio attivo, package, unità, soglia, medico,
  note, data fine, canali di notifica, e stato Attiva/Non attiva.
  **La dose e la frequenza NON si modificano da qui**: usa
  *Toolbar → Cambia schedulazione* (vedi *Regimi complessi* qui sopra).
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

**Una separazione reale richiede account Windows separati.** Ogni
account Windows ha la propria cartella `%LOCALAPPDATA%\MedReminder\`,
che gli altri utenti Windows standard (non amministratori) non possono
leggere. I profili all'interno di uno stesso account Windows sono una
comodità, non una barriera per la privacy: chiunque usi quell'account
può leggere i file di tutti i profili, e i backup automatici
configurati dall'amministratore includono tutti i profili, anche
quelli protetti da PIN.

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

## Notifiche al caregiver

**Impostazioni → Notifiche → E-mail caregiver (facoltativa)**.

Un profilo può indicare un secondo destinatario — ad esempio un
familiare o un caregiver che gestisce il rinnovo della ricetta per
conto tuo. Quando questo campo è impostato, ogni email inviata al
destinatario principale viene recapitata anche al caregiver, nello
**stesso** messaggio. Non cambia nulla altro: il trasporto, il
contenuto del messaggio e i momenti in cui le email vengono inviate
sono esattamente gli stessi di prima.

- **Per attivarlo**: digita l'indirizzo email del caregiver e salva.
- **Per disattivarlo**: svuota il campo e salva. Il campo vuoto
  significa nessun caregiver configurato — il comportamento predefinito.
- **Entrambi gli indirizzi sono visibili a entrambi i destinatari**:
  il caregiver e il destinatario principale possono vedere l'indirizzo
  dell'altro sull'email. È intenzionale, in modo che una risposta
  raggiunga tutti.
- L'indirizzo del caregiver non può coincidere con quello principale
  e deve essere un indirizzo email valido; altrimenti il salvataggio
  viene rifiutato con un messaggio.

L'impostazione è per profilo: il caregiver di un profilo non è il
caregiver di un altro profilo.

## Configurare l'avvio automatico

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

## Esportazione e importazione

Oltre al backup grezzo del database, MedReminder può produrre un
unico **file cifrato e portabile** con tutti i tuoi dati. A
differenza di un backup normale, questo file non è legato al tuo
account Windows né al PC, ed è quindi il percorso consigliato per
spostare MedReminder su un nuovo computer.

**Impostazioni → Backup → Esporta tutti i dati (cifrato)…**:

- Scegli dove salvare il file (estensione `.mrz`).
- Scegli una **passphrase** (almeno 12 caratteri) e digitala due volte.
- Attiva facoltativamente le impostazioni condivise da includere:
  impostazioni SMTP, password SMTP, preferenze di backup, preferenze
  utente (lingua e paese di riferimento del catalogo). Tutte
  disattivate per impostazione predefinita. Se includi la password
  SMTP, viene cifrata nuovamente con la tua passphrase — non viene
  mai scritta in chiaro.
- Clicca **Esporta**.

**Amministratore: tutti i profili insieme.** Se esiste più di un
profilo, un profilo amministratore vede anche **Esporta tutti i
profili**. Scegli una cartella invece di un file: MedReminder scrive
un file crittografato per profilo
(`medreminder-export-<profileId>-<timestamp>.mrz`), tutti con la
stessa passphrase. Per ripristinare un profilo, apri quel profilo e
importa il suo file.

**La passphrase non può essere recuperata.** Non esiste reset,
backdoor né copia sul server. Se perdi la passphrase, il file non
potrà mai più essere letto — conservala in un posto sicuro.

**Impostazioni → Backup → Importa da esportazione…**:

- Seleziona il file `.mrz`. MedReminder mostra cosa contiene
  (versione, data, ambito, impostazioni incluse) prima di fare
  qualsiasi cosa.
- Digita la passphrase.
- Spunta **"Capisco che questo sovrascriverà i dati del profilo
  corrente."** L'importazione sostituisce interamente i dati del
  profilo corrente — non esiste una modalità di fusione. Prima viene
  mantenuta una copia di sicurezza del database corrente come
  `medreminder.db.bak-<timestamp>`.
- Se il file è stato esportato da un altro profilo, MedReminder chiede
  conferma: importarlo sostituisce i dati del profilo attivo con
  quelli dell'altro profilo.
- Clicca **Importa**, poi **riavvia** MedReminder quando richiesto in
  modo che i dati importati vengano caricati in modo pulito.

Se la passphrase è errata, il file è danneggiato o è stato prodotto
da una versione più recente di MedReminder, l'importazione si
interrompe con un messaggio chiaro e i tuoi dati correnti restano
invariati.

Il formato dell'archivio è documentato pubblicamente in
[`docs/EXPORT-FORMAT.md`](EXPORT-FORMAT.md), quindi i tuoi dati non
sono mai bloccati — possono essere decifrati con strumenti standard
se necessario.

## Backup su cartella cloud

MedReminder può scrivere il backup automatico giornaliero anche come
**snapshot cifrato** in una cartella locale che il sistema operativo
sta già sincronizzando (OneDrive, iCloud Drive, Dropbox, Google Drive
Desktop, …). È il modo economico per portare i tuoi dati da un "PC di
casa" a un "PC di lavoro" senza un server, e tiene una copia fuori dal
computer nel caso il disco si guasti.

**Questa non è sincronizzazione in tempo reale.** MedReminder scrive
al massimo uno snapshot al giorno, e un solo computer alla volta
dovrebbe scrivere. Se modifichi i medicinali su due dispositivi tra
due snapshot, le due copie divergono — e il ripristino successivo
cancella i dati del computer su cui ripristini. Decidi in anticipo
quale dispositivo è quello "attivo" e ripristina sull'altro solo
quando cambi.

### Configurazione sul primo dispositivo

**Impostazioni → Backup → Backup su cartella sincronizzata (cifrato)**:

- Spunta la casella.
- Scegli una cartella all'interno della cartella di sincronizzazione
  locale del tuo servizio cloud (per esempio
  `C:\Users\<nome>\OneDrive\MedReminder`). MedReminder non comunica
  mai direttamente con OneDrive / iCloud / Dropbox — scrive solo i
  file lì, e l'agente di sincronizzazione del sistema li carica.
- Imposta il numero di snapshot da conservare (predefinito: 30).
- Clicca **Imposta / cambia…** accanto a Passphrase di backup e
  scegli una passphrase (almeno 12 caratteri). Questa passphrase non
  lascia mai il computer.
- Salva.

Dal tick giornaliero successivo MedReminder scrive
`medreminder-<profileId>-<timestamp>.mrz` nella cartella. Il file è
cifrato con una chiave derivata dalla tua passphrase di backup; il
servizio cloud non vede mai i tuoi dati in chiaro.

Viene scritto uno snapshot per **ogni profilo** presente sul computer,
come per il backup locale, e tutti sono cifrati con la stessa
passphrase di backup. Chi conosce la passphrase può quindi leggere i
dati di tutti i profili, compresi quelli protetti da PIN.

### Configurazione sul secondo dispositivo

- Installa MedReminder.
- **Impostazioni → Backup → Imposta / cambia…** e inserisci la
  **stessa** passphrase di backup configurata sul primo dispositivo.
  È l'unico passaggio irrinunciabile: senza la stessa passphrase, il
  secondo computer non può decifrare ciò che ha scritto il primo.
- Lo snapshot automatico giornaliero resta disattivato sul secondo
  dispositivo — serve su un solo computer.

### Ripristino sul secondo dispositivo

**Impostazioni → Backup → Ripristina da cartella cloud…**:

- Indica nella finestra la cartella di sincronizzazione locale (la
  stessa in cui scrive il primo dispositivo).
- Scegli lo snapshot più recente dall'elenco. Ogni riga mostra la
  data, il nome del profilo (o il suo id, se il profilo non esiste su
  questo computer) e un breve "hash del dispositivo" per distinguere
  gli snapshot provenienti da computer diversi. L'hash del dispositivo
  è un'impronta SHA-256 del nome host del computer di origine —
  sufficiente a raggruppare gli snapshot per provenienza, non a
  identificare il computer.
- È preselezionato lo snapshot più recente del profilo attivo. Il
  ripristino sovrascrive sempre il profilo **attivo**: per
  ripristinare un altro profilo, passa prima a quel profilo. Se scegli
  lo snapshot di un profilo diverso, MedReminder chiede conferma prima
  di sostituire con esso i dati del profilo attivo.
- Spunta **"Confermo che questa operazione sovrascriverà i dati del
  profilo corrente."** — il ripristino è solo in sovrascrittura.
- Clicca **Ripristina**. MedReminder decifra lo snapshot, sostituisce
  il database del profilo corrente e ti chiede di riavviare.

### Note

- **Perdere la passphrase significa perdere i dati.** Non esiste
  reset. La passphrase è salvata in locale, cifrata con le
  credenziali del tuo account Windows; non lascia mai il computer e
  non compare mai nel cloud.
- Lo snapshot automatico giornaliero **non** include la password
  SMTP né le preferenze utente — per quelle usa l'esportazione
  cifrata descritta sopra, con le caselle delle impostazioni
  condivise.
- La conservazione di MedReminder elimina i file vecchi solo dalla
  cartella visibile. Il tuo servizio cloud probabilmente conserva i
  file eliminati nel proprio cestino (OneDrive: 30 giorni per
  impostazione predefinita) — MedReminder non può svuotarlo al posto
  tuo, e non ci prova.
- **Non** mettere il file del database in uso in una cartella
  sincronizzata. Lì vanno solo gli snapshot cifrati `.mrz`.

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
(italiano, inglese, francese, spagnolo o
tedesco) e clicca **Salva lingua**. MedReminder si
riavvia automaticamente per applicare la modifica.

Note:
- Le notifiche toast di Windows seguono sempre la lingua di
  sistema (Windows), indipendentemente dalla lingua qui scelta.
- Le notifiche email e la scheda terapia usano la lingua
  selezionata qui.

## Sostieni lo sviluppo

Se il manutentore l'ha abilitata, la voce **? → Sostieni lo
sviluppo…** apre una piccola finestra in cui puoi, in modo del tutto
volontario, contribuire al progetto. È facoltativo e non è mai
necessario per usare MedReminder.

- Scegli un importo fisso (€2, €5, €10, €20) oppure, quando proposto,
  un **importo personalizzato**.
- Scegli un metodo di pagamento (Stripe o PayPal).
- Fai clic su **Continua con …**: MedReminder apre la pagina di
  pagamento ufficiale del fornitore nel browser predefinito.

Con un importo personalizzato scegli la cifra esatta **sulla pagina
del fornitore**, non dentro MedReminder. L'applicazione non gestisce
mai il pagamento, non vede i dati della tua carta e non può confermare
che un pagamento sia andato a buon fine: si limita ad aprire la pagina.
Se il manutentore non ha configurato questa funzione, la voce di menu
non compare.

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

- Non registra se hai preso la singola dose, non tiene traccia
  dell'aderenza terapeutica e non avvisa per dosi mancate (il
  promemoria all'orario della dose è solo un avviso di comodità,
  non è un sistema di aderenza terapeutica).
- Non fornisce indicazioni terapeutiche o interazioni farmacologiche.
- Non sincronizza tra dispositivi diversi.
- Non ordina medicine automaticamente.
- Non contatta il tuo medico direttamente.

Serve solo a farti sapere per tempo che devi richiedere una nuova
ricetta.

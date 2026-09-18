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
2. Alla prima apertura la finestra è vuota: il database viene creato
   automaticamente sotto `%LOCALAPPDATA%\MedReminder\medreminder.db`.
3. In alto trovi la toolbar; in basso la barra di stato. L'icona
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
6. **Salva**.

## Catalogo di riferimento (Italia + UE)

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

**Fonti dei dati e termini di riuso.** Il catalogo italiano
proviene dai dati aperti AIFA (Agenzia Italiana del Farmaco),
pubblicati sotto licenza Creative Commons Attribution 4.0
International (CC BY 4.0). Il catalogo UE proviene dal dataset
EMA EPAR, riutilizzato secondo la nota legale EMA (decisione
2011/833/UE sul riuso dei documenti della Commissione). La
finestra Informazioni e il file `THIRD-PARTY-NOTICES.md` nella
radice dell'installazione riportano le attribuzioni complete.

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

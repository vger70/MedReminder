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

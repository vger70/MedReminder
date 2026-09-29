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

Se la *Data di decorrenza* è nel passato, al controllo successivo il
consumo automatico già registrato da quella data in poi viene
ricalcolato con il nuovo schema. I movimenti di scorta registrati
prima dell'installazione di questa versione non vengono mai
ricalcolati.

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

### Con la webcam

Non hai un lettore? Nella finestra di scansione fai clic su **Usa la
webcam**. La webcam si accende solo in quel momento e si spegne appena
viene letto un codice, quando fai clic su **Usa il lettore**, quando
chiudi la finestra o dopo 30 secondi senza codice.

- Tieni la confezione a 10–20 cm dalla webcam, con il codice a barre
  dentro la cornice tratteggiata, ben illuminato e a fuoco. Le webcam
  dei portatili a fuoco fisso faticano spesso con il sottile codice
  AIC; un lettore USB è più affidabile.
- Se Windows blocca la fotocamera, la finestra lo segnala e propone
  **Apri impostazioni privacy**: attiva **Consenti alle app desktop di
  accedere alla fotocamera** in Impostazioni → Privacy e sicurezza →
  Fotocamera, poi fai clic su **Riprova**.
- Le immagini della webcam non vengono mai salvate né inviate; viene
  usato solo il codice letto.

### Rifornire con la scansione

Quando compri una nuova confezione di un farmaco già nel tuo elenco,
usa **Scorte → Rifornisci da codice a barre…**. Non serve selezionare
prima il farmaco: lo trova la scansione.

1. Scansiona la confezione con il lettore o con la webcam, come sopra.
2. Il farmaco con quel codice viene selezionato e si apre la finestra
   **Movimento di magazzino**, impostata su nuova confezione, con già inserita la quantità della sua
   ultima nuova confezione. Controllala e conferma.

- Se più farmaci hanno lo stesso codice, scegli quello da rifornire.
- Se nessun farmaco ha il codice ma il catalogo lo conosce, puoi
  aggiungerlo come nuovo farmaco, oppure collegare il codice a un
  farmaco del tuo elenco che non ha ancora un codice (per esempio uno
  inserito a mano); la confezione viene poi aggiunta a quel farmaco.
- Il farmaco viene trovato tramite il codice AIC. Una confezione con
  il solo codice 2D quadrato (DataMatrix) non viene riconosciuta: usa
  **Scorte → Aggiungi confezione**.

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
- **Mostrare le medicine inattive**: le medicine disattivate sono
  nascoste dall'elenco. **Terapia → Mostra medicine inattive** le
  mostra di nuovo, con stato *Inattiva*, fino alla chiusura dell'app.
  La barra di stato indica quante sono nascoste.
- **Riattiva**: mostra le medicine inattive, poi **Modifica** → spunta
  **Attiva**. I giorni in cui la medicina era disattivata non vengono
  conteggiati come consumo.
- **Elimina**: **Terapia → Elimina…** rimuove definitivamente una
  medicina inserita per errore, con il suo schema posologico. È
  possibile solo finché non è stato registrato nulla: nessun carico di
  scorta (compresa la quantità iniziale), assunzione, conteggio o
  sospensione. Altrimenti disattivala, oppure elimina prima quelle voci
  da **Scorte → Storico…**. Con la sincronizzazione attiva la medicina
  viene rimossa anche dagli altri dispositivi, insieme a quanto vi è
  stato registrato nel frattempo.

## Storico delle scorte ed eliminazione di una voce errata

**Scorte → Storico…** (Ctrl+H) elenca ciò che hai registrato per la
medicina selezionata, dalla voce più recente: nuove confezioni e
correzioni, assunzioni, conteggi e sospensioni.

- **Elimina** rimuove una voce errata; scorte e consumi vengono
  ricalcolati.
- Si possono eliminare solo le voci registrate dopo l'ultimo
  conteggio: un conteggio include già gli errori precedenti, quindi
  per correggerli conta di nuovo le scorte.
- Le voci registrate prima dell'installazione di questa versione non
  si possono eliminare: correggile con una rettifica.

## Linea del tempo della terapia

**Terapia → Linea del tempo terapia…** (Ctrl+T) o il pulsante **Linea
del tempo** della barra strumenti apre una vista di sola lettura con
una riga per medicina e i giorni sull'asse orizzontale. Per
impostazione predefinita mostra 60 giorni indietro e 120 in avanti.

- **Barra piena**: terapia attiva, dalla data di inizio alla data di
  fine (o fino al bordo della vista se non c'è data di fine).
- **Barra tratteggiata con bordo a trattini**: sospensione,
  pianificata o in corso.
- **Rombo pieno**: entra in vigore una nuova dose, frequenza o schema.
  **Rombo vuoto**: fase successiva di uno scalaggio a fasi.
- **Triangolo su una linea verticale**: data di esaurimento stimata. È
  la stessa stima della colonna *Esaurimento*: scorta attuale divisa
  per la quantità giornaliera di oggi. Non tiene conto di sospensioni
  o cambi di dosaggio futuri.
- **Linea verticale a trattini**: oggi.
- Una medicina disattivata è mostrata in grigio; la sua barra si ferma
  a oggi perché la data di disattivazione non è registrata.

Comandi:

- **Prima** / **Dopo** spostano il periodo di 30 giorni, **Oggi**
  ripristina il periodo predefinito. Nel grafico le frecce sinistra e
  destra (o Maiusc + rotellina del mouse) lo spostano di una
  settimana.
- Le frecce su e giù selezionano una medicina; il riquadro
  **Dettagli** sotto il grafico riporta le stesse informazioni come
  testo (dosaggio, scorta, esaurimento stimato, sospensioni e cambi di
  dosaggio nel periodo). Passando il mouse su un elemento compare lo
  stesso testo come suggerimento.
- **Mostra nell'elenco** (oppure Invio o doppio clic) chiude la linea
  del tempo e seleziona la medicina nell'elenco principale, dove si
  usano le azioni consuete.

La linea del tempo non modifica alcun dato. Le date di esaurimento sono
stime: servono a pianificare i rifornimenti, non sono un consiglio
clinico.

## Scheda terapia (stampa e PDF)

**Terapia → Scheda terapia…** (Ctrl+P) o il pulsante **Scheda
terapia** della barra strumenti apre una scheda delle medicine attive
da consegnare al medico di base, al pronto soccorso o al farmacista.
Per ogni medicina attiva riporta il principio attivo, il dosaggio
(fasce di somministrazione, oppure dose × volte al giorno), il periodo
di terapia e il medico. Le medicine disattivate non compaiono.

- **Includi le note**: disattivato per impostazione predefinita. Le
  note sono testo libero e possono essere private; spunta la casella
  solo se la scheda deve riportarle.
- **Carta**: A4 o Letter (USA), preselezionata in base alla regione di
  Windows.
- **Stampa…** apre l'anteprima della tabella. Gli elenchi lunghi
  proseguono nella pagina successiva, con intestazione e titoli delle
  colonne ripetuti.
- **Salva come PDF…** chiede dove salvare il file e scrive la stessa
  tabella in PDF tramite la stampante di Windows "Microsoft Print to
  PDF". Se quella stampante è stata rimossa, il dialogo spiega come
  aggiungerla di nuovo (Pannello di controllo → Programmi → Attiva o
  disattiva funzionalità di Windows).
- **Salva su file…** e **Copia negli appunti** restano sulla versione
  in testo semplice.

Il PDF e il file di testo vengono scritti solo dove scegli tu;
MedReminder non ne conserva copia. Ogni pagina riporta l'avviso che
MedReminder è un promemoria organizzativo, non un dispositivo medico.

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

Il ruolo si sceglie alla creazione del profilo. Un amministratore può
cambiarlo in seguito: `Strumenti → Gestisci profili…`, seleziona il
profilo, **Cambia ruolo…**. Il ruolo del profilo aperto non può
essere cambiato: apri prima un altro profilo amministratore. Resta
sempre almeno un amministratore. Prima di rendere amministratore un
profilo senza PIN, valuta di impostarne uno: altrimenti chiunque al
PC potrebbe aprirlo.

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
    │   ├── notifications.settings.json  ← ToAddress di questo profilo
    │   └── ui.settings.json             ← dimensione del testo di questo profilo
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

## Richiedere la ricetta al medico

Seleziona un farmaco e scegli **Terapia → Richiedi ricetta…** (oppure
il pulsante **Richiedi ricetta** della barra strumenti). L'azione è
disponibile per ogni farmaco, qualunque sia la scorta.

MedReminder prepara un breve messaggio con il nome del farmaco, la
confezione, il codice AIC (quando il farmaco è collegato al catalogo)
e il nome del profilo come firma. Se il farmaco ha un medico di
riferimento, il saluto usa quel nome. Posologia, note e altri dettagli
clinici non sono inclusi. Oggetto e messaggio sono modificabili prima
dell'invio.

Tre modi per recapitare il messaggio:

- **Copia**: oggetto e messaggio vanno negli appunti, da incollare in
  una webmail, in un'app di messaggistica o in un portale del medico.
- **Apri nel programma di posta**: apre una nuova e-mail nel programma
  di posta predefinito, già compilata. Se il messaggio è troppo lungo
  per il programma di posta, o nessun programma di posta è
  configurato, viene invece copiato negli appunti.
- **Invia…**: invia il messaggio tramite l'account e-mail configurato
  in **Impostazioni → Email SMTP**, dopo una conferma esplicita.
  Disponibile solo quando l'invio SMTP è configurato e l'e-mail del
  medico è impostata.

L'e-mail del medico si imposta in **Impostazioni → Notifiche → E-mail
del medico (facoltativa)**. È per profilo, è inclusa in esportazione e
importazione, ed è usata solo per le richieste che invii tu: le
notifiche automatiche non vengono mai inviate a questo indirizzo.

MedReminder non invia mai una richiesta di ricetta da solo. Il
contenuto del messaggio e il destinatario non vengono scritti nei file
di log.

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

### OneDrive al posto di una cartella

Nella stessa sezione, **Archivio** può essere impostato su **OneDrive
(cartella app)**: fai clic su **Accedi…**, accedi con un account
Microsoft, imposta la passphrase dei backup e salva. Le copie vanno in
`Apps/MedReminder26/backups` in OneDrive, cifrate come sopra; non serve
una cartella locale. **Ripristina da cartella cloud…** elenca allora le copie in
OneDrive per data e profilo e scarica solo quella che ripristini.

**Google Drive (cartella MedReminder/backups)** funziona allo stesso modo
con un account Google: le copie vanno in una cartella visibile
**MedReminder → backups** del tuo Il mio Drive, cifrate come sopra, e
**Ripristina da cartella cloud…** le elenca da lì.

## Sincronizzazione tra PC

Più PC possono tenere aggiornato lo stesso profilo: quello che registri su
uno compare sugli altri. I PC si scambiano solo modifiche cifrate attraverso
una cartella condivisa (una cartella OneDrive, Google Drive o Dropbox tenuta
sincronizzata dalla sua app desktop, o una condivisione di rete). Non serve
nessun server e la cartella non contiene mai dati leggibili.

Apri **Strumenti → Sincronizzazione…**. Ogni profilo gestisce la sincronizzazione dei
propri dati, compresi il cambio di chiave e la rimozione di un
dispositivo.

### OneDrive o cartella condivisa

Quando attivi la sincronizzazione o ti unisci a un gruppo, MedReminder
chiede dove tenere il gruppo:

- **OneDrive**: accedi con un account Microsoft nella finestra del browser
  che si apre. MedReminder può usare solo la propria cartella app
  (`Apps/MedReminder26` in OneDrive); i dati lì sono cifrati. Ogni PC
  accede con lo **stesso** account Microsoft. L'app OneDrive sul PC non
  serve.
- **Google Drive**: accedi con un account Google nella finestra del
  browser che si apre. I dati di sincronizzazione vanno nella cartella
  dati nascosta di MedReminder in Google Drive (non compare in Il mio
  Drive), cifrati. Ogni PC accede con lo **stesso** account Google.
- **Una cartella condivisa**: una cartella tenuta sincronizzata da un
  altro programma, o una cartella di rete, come descritto sotto.

Se la sessione OneDrive o Google Drive scade (cambio password, lunga
inattività), lo stato lo segnala e **Accedi di nuovo a OneDrive** (o
**Accedi di nuovo a Google Drive**) riprende la sincronizzazione; le
modifiche registrate nel frattempo vengono inviate dopo.

### Attivare sul primo PC

1. **Attiva sincronizzazione…**, scegli la cartella condivisa.
2. Inserisci un nome per questo PC e una **passphrase di sincronizzazione**
   (almeno 10 caratteri, da scrivere due volte). Non è la passphrase dei
   backup. Conservala: senza di essa i dati nella cartella non si possono
   leggere, e non si può recuperare.

### Unirsi da un altro PC

1. Attendi che il client di sincronizzazione abbia scaricato la cartella
   condivisa.
2. **Unisciti a un gruppo…**, scegli la stessa cartella, inserisci un nome
   per questo PC e la stessa passphrase.
3. Conferma: **i dati di questo profilo su questo PC vengono sostituiti**
   da quelli del gruppo (una copia resta accanto al database). MedReminder
   si riavvia.

Se la passphrase apre più di un gruppo nella cartella o nell'account
(più profili sincronizzati con la stessa passphrase), MedReminder chiede
a quale unirsi e mostra ogni gruppo con i suoi dispositivi.

Con OneDrive o Google Drive l'unione può richiedere fino a un minuto:
MedReminder attende che l'account elenchi il nuovo PC, così gli altri PC
conservano le modifiche che gli servono ancora.

### Uso quotidiano

- MedReminder sincronizza pochi secondi dopo ogni modifica, ogni 5 minuti
  e con **Sincronizza ora**.
- La scheda **Dispositivi** elenca i PC del gruppo e quando ciascuno si è
  collegato l'ultima volta.
- Se due PC cambiano la stessa cosa prima di vedere la modifica dell'altro,
  resta la modifica più recente e il caso compare in **Conflitti**. Per un
  campo del medicinale, **Ripristina valore perso** rimette l'altro valore;
  **Ignora** toglie la voce dall'elenco.
- Importare un'esportazione o ripristinare un backup su un profilo
  sincronizzato avvia una nuova **generazione**: dopo un avviso, gli altri
  PC scartano quello che non avevano ancora inviato e vanno ricostruiti con
  **Ricostruisci dal gruppo…**.
- Il **nome del profilo** e i **destinatari delle notifiche**
  (Impostazioni → Notifiche) appartengono al gruppo: una modifica su un
  PC arriva agli altri, e un PC che si unisce prende quelli del gruppo.
  Per rinominare un altro profilo sincronizzato, apri prima quel profilo.
- **Email**: ogni PC con l'email configurata (Impostazioni → Email SMTP)
  invia i propri messaggi di scorta bassa e al caregiver, quindi con due
  PC sincronizzati lo stesso messaggio arriva due volte. Configura
  l'email su un solo PC del gruppo.
- **Disattiva sincronizzazione…** ferma la sincronizzazione su questo PC e
  ne conserva i dati.

### Codici di abbinamento

Un PC già nel gruppo può mostrare un **codice di abbinamento**:
**Abbina un dispositivo…** mostra un codice QR (per la futura app per
telefono) e lo stesso codice come testo. Su un altro PC, **Unisciti con
un codice di abbinamento…** usa quel codice al posto della passphrase;
il PC deve comunque accedere allo stesso account o alla stessa
cartella.

- Il codice vale 10 minuti e solo finché la sua finestra resta aperta.
  Chiudendo la finestra viene ritirato.
- Chi legge il codice mentre è valido può leggere i dati del gruppo:
  mostralo solo ai tuoi dispositivi, non inviarlo per messaggio o
  email. La finestra non compare nelle schermate catturate.

### Cambiare la chiave, rimuovere un PC perso

- Scheda **Dispositivi** → seleziona un PC → **Rimuovi dispositivo…**:
  per un PC perso o rubato. **Cambia chiave e passphrase…** fa lo stesso
  senza indicare un PC, per esempio quando la passphrase è diventata
  nota a qualcuno.
- Scegli una **nuova passphrase di sincronizzazione**. Il gruppo riceve
  una nuova chiave; il PC rimosso non la riceve e non può leggere nulla
  di ciò che viene scritto da quel momento. Ciò che aveva già resta
  leggibile per lui.
- Chiudi anche le sessioni del PC perso nelle impostazioni di sicurezza
  dell'account Microsoft o Google: fino ad allora può ancora raggiungere
  l'archiviazione.
- Ogni altro PC smette di inviare modifiche e segnala che la chiave è
  cambiata. Lì usa **Inserisci la nuova chiave…**, con la nuova
  passphrase o un codice di abbinamento di un PC che ha già la nuova
  chiave. Il profilo viene ricostruito dal gruppo e **le modifiche fatte
  su quel PC vengono conservate**, anche quelle registrate durante
  l'attesa. MedReminder si riavvia.

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
tedesco) e clicca **Salva**. MedReminder chiede di
riavviarsi per applicare la modifica.

Note:
- Le notifiche toast di Windows usano la lingua qui scelta, come il
  resto dell'app.
- Le notifiche email e la scheda terapia usano la lingua
  selezionata qui.

## Dimensione del testo

**Impostazioni → Generale → Dimensione del testo (questo profilo)**:
scegli **Normale**, **Grande** o **Molto grande** e clicca **Salva**.
MedReminder chiede di riavviarsi; dopo il riavvio tutte le finestre
di questo profilo mostrano testo, pulsanti e righe degli elenchi più
grandi. Anche la guida viene ingrandita.

Note:
- La dimensione appartiene al profilo: su un PC condiviso ogni
  persona mantiene la propria. La scelta del profilo e la richiesta
  del PIN, mostrate prima di aprire un profilo, usano sempre Normale.
- MedReminder segue anche il ridimensionamento dello schermo e i
  temi a contrasto di Windows. Con un tema a contrasto l'elenco dei
  farmaci non usa le righe colorate ma i colori del tema; la colonna
  **Stato** indica comunque la condizione di ogni farmaco.
- Su uno schermo piccolo una finestra in **Molto grande** viene
  ridotta alla dimensione dello schermo e una parte potrebbe non
  essere visibile; in quel caso scegli **Grande**.

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

# MedReminder — Guida utente

MedReminder ti avvisa **per tempo** quando una medicina sta per
finire, così puoi chiedere la ricetta al medico prima di restarne
senza. Può anche ricordarti l'orario di ogni dose, seguire più
persone e funzionare su più computer.

> **MedReminder è un promemoria organizzativo, non un dispositivo
> medico.** Non fornisce diagnosi, indicazioni terapeutiche,
> modifiche di terapia né suggerimenti clinici. Ogni decisione sulla
> terapia va presa con il proprio medico.

Premi **F1** o apri **? → Guida utente** per leggere questa guida
dentro l'app. L'architettura tecnica è descritta in `docs/ANALYSIS.md`.

---

## Indice

1. [Per iniziare](#start)
   - [Primo avvio](#first-start) · [La finestra principale](#main-window) ·
     [Dove trovo cosa](#where)
2. [Medicine](#medicines)
   - [Aggiungere una medicina](#add-medicine) ·
     [Orari di somministrazione](#slots) ·
     [Regimi complessi](#regimens) ·
     [Promemoria all'orario della dose](#dose-reminder) ·
     [Modificare, disattivare, eliminare](#edit-medicine)
3. [Trovare una medicina: catalogo e codice a barre](#catalogue)
4. [Scorte](#stock)
   - [Aggiungere una confezione](#add-package) ·
     [Confezioni e scadenze](#packages) ·
     [Registrare un'assunzione](#intake) ·
     [Correggere le scorte](#correct) · [Contare le scorte](#count) ·
     [Storico](#history)
5. [Linea del tempo, scheda terapia e richiesta ricetta](#documents)
6. [Notifiche ed email](#notifications)
   - [Parametri email dei principali provider](#smtp-providers)
7. [Più persone: profili e ruoli](#profiles)
8. [Proteggere i dati: backup ed esportazione](#backup)
9. [Più computer](#devices)
   - [Quale opzione mi serve?](#devices-choice) ·
     [Sincronizzare un profilo tra PC](#sync) ·
     [Condividere l'installazione](#installation) ·
     [Il dispositivo master](#master) ·
     [Dispositivo perso o sostituito](#remove-device)
10. [Impostazioni e uso quotidiano](#settings)
11. [Problemi e risposte](#faq)
12. [Dove MedReminder conserva i dati](#data)
13. [Cosa MedReminder non fa](#limits)

---

<a id="start"></a>
## 1. Per iniziare

<a id="first-start"></a>
### Primo avvio

1. Avvia `MedReminder.exe`.
2. Si apre la finestra **Benvenuto in MedReminder**. Scegli:
   - **Crea profilo** — il caso normale sul primo computer. Scrivi il
     tuo nome e, se vuoi, un PIN. Questo primo profilo è
     l'**amministratore**: gestisce email, backup e gli altri profili
     (vedi [Più persone](#profiles)).
   - **Unisciti a un'installazione esistente…** — solo se MedReminder
     è già usato su un altro tuo computer e vuoi che anche questo ne
     faccia parte (vedi [Condividere l'installazione](#installation)).
3. Si apre la finestra principale. L'icona di MedReminder nell'area di
   notifica di Windows (vicino all'orologio) resta visibile finché l'app
   è in esecuzione.

**Windows SmartScreen.** Il programma è firmato digitalmente con un
certificato Certum. Finché il certificato non ha accumulato
reputazione, al primo avvio Windows può comunque mostrare una finestra
blu "Windows ha protetto il PC": fai clic su **Ulteriori
informazioni**, poi su **Esegui comunque**. Windows ricorda la scelta.
Se installi dal pacchetto MSI, la finestra dei permessi mostra
l'autore verificato.

<a id="main-window"></a>
### La finestra principale

- **Menu** in alto: **File**, **Terapia**, **Scorte**, **Strumenti** e
  **?** (aiuto).
- **Barra degli strumenti** sotto i menu: *Nuova medicina*, *Registra
  assunzione* e, a destra, una casella di ricerca (**Ctrl+F**) che
  filtra l'elenco per nome.
- **Navigazione** a sinistra: *Medicine* (questo elenco), poi *Linea del
  tempo terapia*, *Scheda terapia*, *Richiedi ricetta*, *Pianifica scorte*, *Ricette*, *Scadenze amministrative*, *Installazione*
  (amministratori) e *Impostazioni*, che si aprono in una finestra
  propria. Se la finestra è stretta mostra solo le icone.
- **Riepilogo** sopra l'elenco: quante medicine sono *Esaurite*, *In
  esaurimento*, *Sospese*, e il totale. Fai clic su un riquadro per
  vedere solo quelle medicine; un secondo clic le mostra tutte.
- **Elenco delle medicine** al centro: una riga per medicina, con le
  scorte, i giorni rimanenti e la data stimata di esaurimento. Durante
  la giornata scorte e giorni rimanenti tolgono già le dosi di oggi il
  cui orario è passato ([dettagli](#stock-estimate)). La
  colonna **Stato** mostra lo stato con un'etichetta colorata. Il clic
  destro su una riga offre i comandi per quella medicina (modifica,
  registra assunzione, aggiungi confezione…); doppio clic o **F2** la
  modificano.
- **Barra di stato** in basso: il profilo aperto ("Profilo: Anna
  (admin)" per un amministratore).

Chiudere la finestra con la **X** non chiude MedReminder: resta attivo
nell'area di notifica, così i promemoria continuano ad arrivare. Per
uscire, fai clic destro sull'icona e scegli **Esci**.

<a id="where"></a>
### Dove trovo cosa

| Voglio… | Vai a |
|---|---|
| Aggiungere una medicina | **Terapia → Nuova medicina…** |
| Cambiare dose o frequenza | **Terapia → Cambia dose/frequenza…** |
| Registrare una confezione comprata | **Scorte → Aggiungi confezione…** o **Scorte → Rifornisci da codice a barre…** |
| Allineare le scorte a quello che ho davvero | **Scorte → Conta scorte…** |
| Registrare una dose presa al bisogno | **Terapia → Registra assunzione…** (opzione *Dose extra al bisogno* se la medicina ha anche dosi programmate) |
| Cambiare l'orario di "Al mattino", "Prima di pranzo"… | **Terapia → Orari delle dosi…** |
| Annullare una registrazione sbagliata | **Scorte → Storico…** |
| Stampare la terapia per un medico | **Terapia → Scheda terapia…** |
| Chiedere una ricetta | **Terapia → Richiedi ricetta…** |
| Seguire una ricetta fino alla farmacia | **Terapia → Ricette…** |
| Ricordare un piano terapeutico, un'esenzione o un controllo | **Terapia → Scadenze amministrative…** |
| Vedere le prossime date in Outlook, Google Calendar o sul telefono | **Terapia → Esporta nel calendario…** |
| Controllare la scorta per un viaggio o fino al prossimo passaggio in farmacia | **Terapia → Pianifica scorte…** |
| Configurare email, lingua, backup | **Strumenti → Impostazioni…** |
| Aggiungere una persona | **Strumenti → Gestisci profili…** (amministratore) |
| Usare MedReminder su un altro PC | **Strumenti → Sincronizzazione…** e **Strumenti → Installazione…** (amministratore) |
| Aprire il profilo di un'altra persona | **File → Cambia profilo…** |

---

<a id="medicines"></a>
## 2. Medicine

<a id="add-medicine"></a>
### Aggiungere una medicina

1. **Terapia → Nuova medicina…** (o il pulsante *Nuova medicina*).
2. Inizia a scrivere il **nome**: il catalogo propone le medicine
   corrispondenti (vedi [Trovare una medicina](#catalogue)). Sceglierne
   una compila principio attivo e confezione. Puoi anche fare clic su
   **Scansiona codice…**.
3. Compila i campi obbligatori, segnati con `*`: *Unità*, *Dose per
   somministrazione*, *Somministrazioni al giorno*, *Data inizio
   terapia* e *Soglia avviso (giorni)*.
   - La **soglia di avviso** indica quanti giorni prima dell'esaurimento
     vuoi essere avvisato. Lascia il tempo di ottenere la ricetta e
     comprare la medicina, per esempio 10 giorni.
4. **Quantità iniziale in scorta**: quante compresse (o ml, dosi…) hai
   adesso.
5. **Canali di notifica**: spunta **Notifica Windows** e/o **Email**.
   L'email funziona solo dopo averla configurata (vedi
   [Notifiche ed email](#notifications)).
6. Facoltativi: *Data fine terapia*, *Medico di riferimento*, *Note*,
   orari di somministrazione, *Ricordami all'orario della dose*.
7. **Salva**.

Da quel momento MedReminder scala da solo la dose ogni giorno. Non devi
registrare ogni compressa che prendi.

<a id="slots"></a>
### Orari di somministrazione

In **Orari di somministrazione (opzionale)** puoi dividere la dose
giornaliera in più momenti: **Aggiungi…** apre una finestra dove
indichi la dose, un orario facoltativo (*Con orario specifico*) e una
descrizione come "Dopo colazione" (scegline una tra le *Descrizioni
comuni* o scrivi la tua). Gli orari compaiono nella scheda terapia e,
se hanno un'ora, possono ricordarti la dose.

Senza orari, la medicina usa "dose × somministrazioni al giorno".

Una dose presa solo quando serve (per esempio un antidolorifico per il
mal di testa) va segnata **Al bisogno** nella finestra dell'orario: la
descrizione *Al bisogno* la spunta da sola. Una dose al bisogno non viene
mai scalata automaticamente e non fa parte del totale giornaliero: la
scorta scende solo quando registri l'assunzione. Se tutti gli orari di
una medicina sono al bisogno, la medicina si comporta come uno schema
*Al bisogno (PRN)*.

Dalla versione che ha introdotto questa opzione, gli orari descritti
come "Al bisogno" vengono considerati al bisogno da quel giorno in poi.
Prima venivano scalati ogni giorno: se la scorta mostrata è più bassa di
quella reale, fai un [conteggio](#count) per riallinearla.

**Terapia → Orari delle dosi…** elenca i momenti della giornata con il
loro orario ("Al mattino" = 08:00, "Prima di pranzo" = 13:00, …) e gli
orari usati per le medicine senza orari (1 al giorno = 08:00, 2 al
giorno = 08:00 e 20:00, …). Puoi cambiare gli orari, nascondere i
momenti che non usi e aggiungerne di tuoi; nella finestra dell'orario,
scegliendo un momento vedi il suo orario. Questi orari servono solo a
collocare le dosi nella giornata e non cambiano mai la scorta
registrata. Valgono per questo computer: non vengono sincronizzati.

<a id="regimens"></a>
### Regimi complessi

Non tutte le terapie usano la stessa quantità ogni giorno. Nella
finestra della medicina imposta **Schema** su **Avanzato** e scegli un
**Tipo di regime**:

| Tipo di regime | Esempio |
|---|---|
| **Settimanale** | Una quantità diversa per ogni giorno della settimana, per esempio un anticoagulante con dosi diverse lun, mer, ven. |
| **Ciclico (N on / M off)** | Una quantità per N giorni, poi M giorni senza, per esempio 21 giorni sì e 7 no. |
| **Scalare** — *Lineare* | La dose cambia di un passo fisso ogni tot giorni fino alla dose finale, poi resta costante. |
| **Scalare** — *A stadi* | Un elenco di fasi, ognuna con la sua dose e durata, per esempio 4 al giorno per 7 giorni, 2 al giorno per 7 giorni, 1 al giorno per 14 giorni. Usa **Aggiungi fase** / **Rimuovi**; il totale compare sotto l'elenco. Spunta *Mantieni l'ultima dose* se l'ultima dose continua a tempo indeterminato. |
| **Al bisogno (PRN)** | Nessun consumo programmato: le scorte sono seguite, ma non viene stimata una data di esaurimento. |

Con **Avanzato** i campi della dose in alto nella finestra non vengono
usati. Torna a **Semplice** per una dose giornaliera fissa.

**Gli orari di somministrazione valgono anche con Avanzato.** Lo schema
decide quanto prendere ogni giorno; gli [orari di somministrazione](#slots)
decidono quando. La quantità del giorno è ripartita tra gli orari in
proporzione alle loro dosi: con due orari da 1, un giorno a scalare da 4
dà 2 + 2, un giorno da 1 dà 0,5 + 0,5. In un giorno di pausa del ciclo
non c'è nulla da prendere e non arriva alcun promemoria.

**La terapia cambia?** Usa **Terapia → Cambia dose/frequenza…** e scegli
la data **Effettiva dal**. Lo schema precedente resta valido per i
giorni prima di quella data. Se la data è nel passato, il consumo già
scalato da quella data in poi viene ricalcolato con il nuovo schema.

MedReminder non controlla dosi massime, sovradosaggi o interazioni tra
farmaci: segue solo la terapia prescritta dal medico.

<a id="dose-reminder"></a>
### Promemoria all'orario della dose

Spunta **Ricordami all'orario della dose** nella finestra della medicina
per ricevere un promemoria ("È ora di prendere …") a ogni orario con ora
impostata. È disponibile quando la medicina ha almeno un orario con ora
e ha ancora scorte.

- Il promemoria compare sullo schermo; se la medicina usa il canale
  **Email** e l'email è configurata, arriva anche per email.
- **Una volta per orario al giorno**, anche se riavvii l'app.
- Se il PC era in sospensione a quell'ora, il promemoria arriva comunque
  entro **30 minuti**; oltre viene saltato.
- Con **scorte a zero** non arriva nessun promemoria.
- Nella notte in cui l'orologio va avanti di un'ora, un orario che cade
  nell'ora saltata non suona.

Il promemoria non registra se hai preso la dose e non cambia le scorte.

<a id="edit-medicine"></a>
### Modificare, disattivare, eliminare

- **Modifica**: doppio clic sulla riga, o **Terapia → Modifica**. Puoi
  cambiare tutto tranne dose e frequenza (usa *Cambia
  dose/frequenza…*).
- **Disattiva**: **Terapia → Disattiva** quando interrompi una terapia.
  La medicina viene nascosta e non riceve più avvisi; il suo storico
  resta. **Terapia → Mostra medicine inattive** la fa ricomparire; per
  riattivarla aprila con **Modifica** e spunta **Attiva**. I giorni in
  cui era inattiva non contano come consumo.
- **Elimina**: **Terapia → Elimina…** rimuove una medicina inserita per
  errore. Funziona solo finché non è stato registrato nulla (nessuna
  scorta, nemmeno la quantità iniziale, nessuna assunzione, nessun
  conteggio). Altrimenti disattivala. Con la sincronizzazione attiva
  sparisce anche dagli altri computer.

---

<a id="catalogue"></a>
## 3. Trovare una medicina: catalogo e codice a barre

### Il catalogo di riferimento

MedReminder contiene gli elenchi ufficiali dei medicinali di **Italia**
(AIFA), **Spagna** (AEMPS), **Francia** (ANSM) e i medicinali
autorizzati per tutta l'**Unione Europea** (EMA).

- Nella finestra della medicina scrivi parte del **nome** o del
  **principio attivo**: compaiono fino a 20 risultati. Sceglierne uno
  compila gli altri campi.
- Un **cerchio rosso** accanto a una riga indica che il prodotto è
  sospeso o revocato. Puoi comunque sceglierlo.
- **Non è nell'elenco?** Scrivi il nome e salva: la medicina funziona
  allo stesso modo, solo senza collegamento al catalogo.
- **Quale Paese?** **Strumenti → Impostazioni… → Generale → Paese di
  riferimento** (predefinito: Italia). Solo un amministratore può
  cambiarlo: vale per tutti i profili e, con un'installazione
  condivisa, per tutti i dispositivi. Con IT, ES o FR l'elenco contiene
  anche i medicinali UE; con **EU** contiene solo quelli. Un medicinale
  può comparire due volte (nazionale e UE): scegli quello che
  corrisponde alla tua scatola.
- **Aggiornamento automatico.** Quando **Controlla aggiornamenti
  automaticamente (GitHub)** è attivo (Impostazioni → Generale),
  MedReminder scarica all'avvio, e una volta al giorno finché resta
  aperto, l'ultimo elenco mensile del tuo Paese e quello UE, se più
  recenti. Senza connessione non cambia nulla. Con più profili viene
  aggiornato solo quello aperto; gli altri la prima volta che vengono
  aperti.

**Fonti.** Open data AIFA (CC BY 4.0); dati EMA EPAR (avviso legale
EMA, decisione della Commissione 2011/833/UE); AEMPS CIMA (legge
spagnola 37/2007 sul riutilizzo dell'informazione del settore
pubblico); ANSM BDPM (Licence Ouverte Etalab 2.0). Le attribuzioni
complete sono in **? → Info su MedReminder…** e in
`THIRD-PARTY-NOTICES.md`.

### Scansionare il codice a barre

Con un lettore di codici a barre USB o una webcam puoi compilare una
medicina senza scrivere.

1. Nella finestra della medicina fai clic su **Scansiona codice…**.
2. Scansiona il codice a barre della scatola, oppure scrivi il codice
   stampato sotto e premi **Invio**.
3. Se il codice è nel catalogo, il modulo viene compilato. Altrimenti la
   finestra mostra il codice letto e non cambia nulla.

Consigli:

- Fai clic su **Scansiona codice…** *prima* di scansionare, altrimenti
  il codice finisce nel campo che ha il cursore.
- Sulle confezioni italiane il codice da leggere è il codice a barre
  **AIC** (`A` seguita da 9 cifre). Se il lettore non lo riconosce,
  attiva la simbologia **Code 32** (Pharmacode italiano) nelle sue
  impostazioni. Il codice quadrato (DataMatrix) richiede un lettore 2D
  e spesso non è nel catalogo.
- Imposta il lettore sullo stesso layout di tastiera di Windows.

**Con la webcam.** Fai clic su **Usa la webcam** nella finestra di
scansione. Tieni la scatola a 10–20 cm, con il codice dentro la cornice,
in buona luce. La fotocamera si spegne quando legge un codice, quando
fai clic su **Usa il lettore**, quando chiudi la finestra o dopo 30
secondi. Se Windows blocca la fotocamera, fai clic su **Apri
impostazioni privacy**, attiva *Consenti alle app desktop di accedere
alla fotocamera*, poi **Riprova**. Nessuna immagine viene salvata o
inviata.

**Rifornire con la scansione.** **Scorte → Rifornisci da codice a
barre…**: scansiona la nuova scatola e la medicina corrispondente si
apre nella finestra delle scorte, già impostata su *Nuova confezione*
con la quantità abituale. Se nessuna medicina ha quel codice, puoi
aggiungere una nuova medicina o collegare il codice a una esistente.

### Medicine carenti (Italia)

Con l'Italia come paese di riferimento, MedReminder scarica l'elenco
AIFA dei farmaci carenti insieme al catalogo (all'avvio e una volta al
giorno, se **Controlla aggiornamenti automaticamente** è attivo). Una
medicina la cui confezione (codice AIC, compilato dal catalogo o dal
codice a barre) è nell'elenco lo mostra nella colonna
**Disponibilità** della lista:

- *Carente*: AIFA indica la confezione come difficile da trovare;
- *Carenza dal …*: AIFA annuncia una carenza da quella data.

Passa il mouse sulla cella per leggere l'inizio, la fine prevista
(spesso non comunicata, e può cambiare), il motivo, se AIFA segnala
medicinali equivalenti e la data dell'elenco. Ricevi anche una notifica
per ogni carenza, sui canali della medicina.

MedReminder non indica sostituti: chiedi al medico o al farmacista e
richiedi la ricetta per tempo.

---

<a id="stock"></a>
## 4. Scorte

MedReminder abbassa da solo le scorte ogni giorno secondo lo schema.
Registri solo ciò che cambia le scorte in altro modo.

<a id="stock-estimate"></a>
Durante la giornata la colonna della scorta mostra una stima: la scorta
a inizio giornata meno le dosi di oggi il cui orario è già passato. Per
gli orari senza ora vale l'orario del loro momento (**Terapia → Orari
delle dosi…**); un orario descritto liberamente, senza ora, viene
contato a fine giornata. Passando il mouse sulla scorta vedi il valore
a inizio giornata. La scorta registrata, lo storico e la data di
esaurimento si aggiornano dopo mezzanotte, mentre i giorni residui
seguono la scorta mostrata; se registri un'assunzione,
quel giorno conta la quantità registrata.

<a id="add-package"></a>
### Aggiungere una confezione

1. Seleziona la medicina.
2. **Scorte → Aggiungi confezione…**.
3. Scegli il tipo:
   - **Nuova confezione** — dopo un acquisto (il caso normale);
   - **Aggiunta manuale** — per esempio campioni dal medico;
   - **Correzione in eccesso** — avevi contato meno del reale.
4. Inserisci la quantità e conferma.

Una nuova confezione fa ripartire il ciclo di avviso: quando le scorte
scendono di nuovo sotto la soglia, ricevi un nuovo avviso.

Con **Nuova confezione** puoi anche registrare la scadenza, tutti campi
facoltativi:

- **Confezioni** — quante scatole uguali hai comprato; la quantità viene
  divisa tra loro.
- **Scadenza (mese/anno)** — spunta la casella e scegli mese e anno come
  sono stampati. Una scadenza `03/2027` vale fino al 31 marzo 2027.
- **Da usare entro … giorni dall'apertura** — per colliri, sciroppi,
  insuline in uso e simili, come indicato nel foglietto; 0 se non c'è.
  Viene proposto il valore dell'ultima confezione della medicina.
- **Aperta oggi** — se apri subito la prima scatola.
- **Lotto** — facoltativo.

Con **Scorte → Rifornisci da codice a barre** un codice DataMatrix
compila da solo scadenza e lotto. Se lasci vuoti tutti questi campi, la
confezione aggiunge solo quantità, come prima.

<a id="packages"></a>
### Confezioni e scadenze

**Scorte → Confezioni e scadenze…** (anche dal menu del tasto destro)
elenca le scatole della medicina selezionata con stato, scadenza
stampata, data di apertura, data entro cui usarla e quantità in scorta.

- Una confezione scade alla fine del mese stampato, o prima, se è aperta
  e finiscono i giorni dall'apertura (aperta il 1° marzo, 28 giorni:
  usare entro il 28 marzo).
- L'app considera usate per prime la confezione aperta e poi quelle che
  scadono prima. Quelle che la scorta non copre più sono **Esaurite** e
  non danno avvisi, anche se non le segni finite. Se usi le scatole in
  un altro ordine, segna quella in uso con **Aperta oggi** o chiudi
  quella giusta.
- **Nuova…** registra una scatola che hai già nell'armadietto, senza
  cambiare la scorta.
- **Aperta oggi**, **Finita**: aggiornano la confezione, la scorta non
  cambia.
- **Smaltisci…**: per una scatola buttata, tipicamente scaduta. La
  quantità rimasta (proposta dall'app) viene tolta dalla scorta. Lo
  smaltimento è definitivo: se ti sei sbagliato, elimina la confezione e
  riaggiungi le unità con una correzione in eccesso.
- **Elimina**: solo per una confezione inserita per errore; la scorta
  non cambia.

La colonna **Scadenza** della finestra principale mostra la prima
scadenza tra le confezioni in scorta, con *(scaduta)* o *(in scadenza)*.
**Scorte → Confezioni in scadenza…** riunisce in un solo elenco le
confezioni scadute o in scadenza di tutte le medicine, anche di quelle
non più in uso, prima le scadute; **Apri confezioni…** apre quelle della
medicina scelta. Per gli avvisi vedi [Notifiche ed email](#notifications).

<a id="intake"></a>
### Registrare un'assunzione

**Terapia → Registra assunzione…** (o il pulsante nella barra) registra
una singola assunzione come **Assunta**, **Saltata** o **Annullata**,
con il giorno e la quantità. Nei giorni normali non serve. Usala quando
un giorno è diverso dallo schema: appena registri un'assunzione per un
giorno, lo scalo automatico di quel giorno viene sostituito da quello
che hai registrato.

Per una dose in più rispetto allo schema, per esempio una dose al
bisogno, spunta **Dose extra al bisogno**: la quantità viene scalata e le
dosi programmate del giorno restano. L'opzione compare solo per le
medicine con uno schema ed è già spuntata se la medicina ha un orario al
bisogno.

<a id="correct"></a>
### Correggere le scorte

Se hai meno di quanto mostra l'app (una compressa persa, un flacone
rovesciato): **Scorte → Correggi scorte…**, lascia il tipo **Correzione
in difetto** e inserisci la quantità da togliere. Le scorte non possono
scendere sotto zero.

<a id="count"></a>
### Contare le scorte

Quando il conteggio nell'armadietto non corrisponde all'app, conta e
lascia che l'app corregga:

1. Seleziona la medicina, poi **Scorte → Conta scorte…**.
2. Scrivi la **Quantità contata**. La finestra mostra le scorte
   previste, la differenza e come cambia la data di esaurimento.
3. In **Già assunto oggi** inserisci quanto avevi già preso oggi al
   momento del conteggio. L'app propone le dosi il cui orario è
   passato, le stesse che l'elenco ha già tolto dalla scorta; per gli
   orari senza ora vale l'orario del loro momento.
4. Fai clic su **Registra conteggio**.

L'app registra una correzione perché le scorte coincidano con quanto
hai contato. La differenza è solo un dato di scorta: non viene
interpretata come dosi saltate o in più.

<a id="history"></a>
### Storico e registrazioni sbagliate

**Scorte → Storico…** (Ctrl+H) elenca confezioni, correzioni,
assunzioni, conteggi e sospensioni della medicina selezionata, dalla
più recente. Seleziona una registrazione sbagliata e fai clic su
**Elimina**: scorte e consumi vengono ricalcolati. Si possono eliminare
solo le registrazioni successive all'ultimo conteggio (per correggere
quelle precedenti, conta di nuovo); le registrazioni di versioni
precedenti a questa funzione non si possono eliminare, correggile con
una correzione.

---

<a id="documents"></a>
## 5. Linea del tempo, scheda terapia e richiesta ricetta

### Linea del tempo della terapia

**Terapia → Linea del tempo terapia…** (Ctrl+T) mostra una riga per
medicina su un calendario (60 giorni indietro, 120 avanti):

- **barra piena**: terapia in corso; **barra tratteggiata**: sospensione;
- **rombo pieno**: inizia una nuova dose o un nuovo regime; **rombo
  vuoto**: fase successiva di uno scalare a stadi;
- **triangolo**: data stimata di esaurimento; **linea tratteggiata**:
  oggi;
- riga grigia: medicina disattivata.

**Prima** / **Dopo** spostano di 30 giorni, **Oggi** torna indietro; le
frecce della tastiera spostano di una settimana. Il riquadro dei
**dettagli** descrive a parole la medicina selezionata. **Mostra
nell'elenco** (o Invio) la seleziona nell'elenco principale. La linea
del tempo non modifica nulla; le date di esaurimento sono stime.

### Scheda terapia (stampa e PDF)

**Terapia → Scheda terapia…** (Ctrl+P) prepara una scheda delle medicine
attive per un medico, un pronto soccorso o un farmacista: principio
attivo, posologia, periodo di terapia, medico.

- **Includi le note** è disattivato: le note possono essere private.
- **Carta**: A4 o Letter.
- **Stampa…** mostra un'anteprima; **Salva come PDF…** usa la stampante
  di Windows "Microsoft Print to PDF" (se è stata rimossa, la finestra
  spiega come aggiungerla); **Salva su file…** e **Copia negli appunti**
  danno il testo semplice.

MedReminder non conserva copie di quanto salvi o stampi.

### Pianificare le scorte (viaggio o farmacia)

**Terapia → Pianifica scorte…** risponde alla domanda "ne ho abbastanza
fino a…?". Scegli il periodo con **Dal** e **Al**, per esempio i giorni
di un viaggio o i giorni fino al prossimo passaggio in farmacia
(predefinito: i prossimi 14 giorni, oggi compreso). Per ogni medicina
attiva la finestra mostra:

- **Serve nel periodo**: la quantità consumata nel periodo, secondo lo
  schema, le sospensioni, la data di fine terapia e gli orari di
  assunzione;
- **Scorta all'inizio**: la scorta di oggi meno il consumo previsto fino
  all'inizio del periodo (*finisce prima* se non ne resterà);
- **Mancano**: quanto serve oltre quella scorta, oppure *coperto*;
- **Confezioni da procurare**: quante confezioni coprono ciò che manca,
  della stessa dimensione dell'ultima nuova confezione registrata (— se
  non ne è stata registrata nessuna).

Le medicine non coperte compaiono per prime. Le medicine al bisogno sono
elencate ma non calcolate, perché il loro consumo non è pianificato.
**Stampa…**, **Salva come PDF…** e **Copia negli appunti** funzionano
come per la scheda terapia. La finestra non modifica nulla: i valori
sono stime.

### Richiedere la ricetta

Seleziona una medicina, poi **Terapia → Richiedi ricetta…**. MedReminder
prepara un breve messaggio con nome della medicina, confezione, codice
del prodotto e il tuo nome; con un medico di riferimento, il saluto usa
il suo nome. Posologia e note non sono incluse. Puoi modificare tutto
prima di inviare.

- **Copia** — da incollare in una webmail, un'app di messaggi o un
  portale per pazienti.
- **Apri nel programma di posta** — una nuova email nel tuo programma di
  posta abituale.
- **Invia…** — la invia con l'account email di MedReminder, dopo una
  conferma. Disponibile quando l'email è configurata e l'**E-mail del
  medico** è compilata (Impostazioni → Notifiche). Con un'installazione
  condivisa invia solo il [dispositivo master](#master): sugli altri
  dispositivi usa **Apri nel programma di posta**.

MedReminder non invia mai una richiesta da solo.

### Seguire una ricetta fino alla farmacia

**Terapia → Ricette…** elenca le ricette registrate, prima quelle da
ritirare. Per ciascuna puoi annotare, quando le conosci:

- **Richiesta il**: quando l'hai chiesta al medico. **Segna come
  richiesta** nella finestra di richiesta la registra per te con la
  data di oggi;
- **Emessa il**, **Codice ricetta** e **Confezioni**: dalla ricetta
  emessa dal medico;
- **Valida fino al**: l'ultimo giorno in cui la farmacia la accetta.
  Viene compilata per 30 giorni dalla data di emissione, la validità
  abituale della ricetta elettronica italiana; controllala sulla tua
  ricetta e correggila se è diversa;
- **Ritirata il**: quando l'hai portata in farmacia. **Ritirata oggi**
  lo fa con un clic. Quando aggiungi una nuova confezione di una
  medicina con una ricetta ancora da ritirare, MedReminder chiede se la
  confezione viene da lì.

Una ricetta emessa e non ritirata è *Da ritirare*; dopo l'ultimo giorno
di validità è *Scaduta*. Da 3 giorni prima di quel giorno ricevi un
promemoria, una volta, sui canali di notifica della medicina (l'email
solo dal [dispositivo master](#master) se l'installazione è
condivisa). Il promemoria non contiene il codice.

Le ricette vengono copiate sugli altri PC di un profilo sincronizzato e
incluse nell'esportazione cifrata.

### Scadenze amministrative

**Terapia → Scadenze amministrative…** raccoglie le date che non
riguardano le scorte: il rinnovo di un piano terapeutico o di
un'esenzione, un controllo periodico o qualsiasi altra cosa tu
descriva. Per ogni scadenza:

- **Tipo** e **Descrizione**: la descrizione è facoltativa, tranne per
  il tipo *Altro*;
- **Medicina**: la medicina a cui si riferisce, oppure *(nessuna)* per
  una scadenza di tutto il profilo;
- **Data** e **Avvisa giorni prima**: il promemoria parte quel numero
  di giorni prima della data (14 di base);
- **Ripeti ogni … mesi**: per una scadenza che si ripete, come un
  rinnovo annuale;
- **Avvisa con**: notifica di Windows e/o email.

MedReminder non applica regole proprie a queste date: le validità
cambiano secondo il piano e la regione, quindi inserisci la data
riportata sui tuoi documenti.

Dal preavviso in poi ricevi un promemoria per ogni data, sui canali
scelti (l'email solo dal [dispositivo master](#master) se
l'installazione è condivisa); una scadenza superata è mostrata in
rosso. **Fatta** chiude una scadenza singola; una ricorrente passa alla
data successiva, contata dalla data precedente e non dal giorno in cui
l'hai segnata.

Le scadenze vengono copiate sugli altri PC di un profilo sincronizzato
e incluse nell'esportazione cifrata.

### Esportare le date in un calendario

**Terapia → Esporta nel calendario…** salva un file `.ics` con le
prossime date: per ogni medicina attiva il giorno in cui richiedere la
ricetta (la data di esaurimento meno la soglia di avviso) e la data di
esaurimento, l'ultimo giorno per ritirare ogni ricetta e le scadenze
amministrative aperte. Apri il file con Outlook, Google Calendar o il
calendario del telefono. Gli eventi sono promemoria, non appuntamenti:
non ti segnano come occupato. Esportando di nuovo più avanti gli stessi
eventi vengono aggiornati, senza copie.

I calendari sono spesso conservati online da un'altra azienda, quindi
gli eventi dicono solo cosa fare ("MedReminder: una medicina
finisce"). Spunta **Includi i nomi delle medicine e le descrizioni
delle scadenze** se vuoi i nomi nel calendario; la scelta viene chiesta
a ogni esportazione.

Ogni email di scorta bassa contiene anche la data di esaurimento come
file di calendario (`medreminder.ics`), con lo stesso titolo generico.

---

<a id="notifications"></a>
## 6. Notifiche ed email

### Come funzionano gli avvisi

- Ogni 30 minuti MedReminder controlla le medicine. Quando una medicina
  scende sotto la sua **soglia di avviso**, ti avvisa **una volta**, con
  i canali scelti per quella medicina: una notifica di Windows e/o
  un'email.
  Il controllo usa la scorta registrata, non la stima dell'elenco: nel
  giorno in cui la soglia viene superata, l'elenco può mostrare *In
  esaurimento* qualche ora prima dell'avviso.
- Se quando i giorni residui arrivano a **metà della soglia** non è stata
  aggiunta una nuova confezione, segue un **secondo avviso** sugli
  stessi canali (con una soglia di 10 giorni: primo avviso a 10 giorni,
  secondo a 5). Una medicina che al primo controllo è già sotto la metà
  riceve solo il secondo avviso. Dopo una nuova confezione il ciclo
  riparte.
- **Scadenza delle confezioni**: una confezione registrata con scadenza
  dà un avviso *in scadenza* 30 giorni prima della scadenza stampata (3
  giorni prima della fine del periodo dopo l'apertura) e un avviso
  *scaduta* il giorno dopo, una volta ciascuno, sui canali della
  medicina, anche se la medicina non è più in uso. Le confezioni esaurite
  o chiuse non danno avvisi. Gli anticipi si cambiano in **Strumenti →
  Impostazioni… → Notifiche → Scadenza delle confezioni**; con 0 resta
  solo l'avviso di confezione scaduta.
- **Dalla notifica di Windows**: un clic apre MedReminder su quella
  medicina (sulle ricette, per un promemoria di ricetta; sulle scadenze, per un promemoria di scadenza; sulle confezioni, per un avviso di scadenza di una confezione). Un avviso di
  scorta ha **Prepara la richiesta**, che apre la richiesta al medico;
  un promemoria di dose ha **Ricordamelo tra 15 minuti**, che lo
  ripropone più tardi, anche se nel frattempo MedReminder è chiuso. Le
  assunzioni non si registrano dalla notifica: usa **Terapia → Registra
  assunzione…**.
- **Strumenti → Controlla ora** (**Ctrl+R**, o il menu dell'icona)
  esegue subito il controllo.
- MedReminder deve essere in esecuzione per inviare gli avvisi. Attiva
  l'avvio automatico (vedi [Impostazioni](#settings)).

### Passo 1 — l'account email (amministratore)

**Strumenti → Impostazioni… → Email SMTP**:

| Campo | Cosa inserire |
|---|---|
| **Host** | Il server di posta in uscita del tuo provider, per esempio `smtp.gmail.com` |
| **Porta** | `587` con *Usa StartTLS* spuntato, oppure `465` con *Usa StartTLS* non spuntato |
| **Username** / **Nuova password** | Il tuo account email. La password è salvata cifrata e non finisce mai nei log |
| **Mittente (from)** / **Nome mittente** | Da chi arrivano le email |
| **Timeout (s)** | Secondi prima di rinunciare |

Fai clic su **Prova connessione** (accede senza inviare nulla), poi su
**Salva impostazioni SMTP**.

I valori per Gmail e per gli altri provider più diffusi, e come ottenere
una password per le app, sono in
[Parametri email dei principali provider](#smtp-providers).

### Passo 2 — i destinatari (per ogni profilo)

**Strumenti → Impostazioni… → Notifiche**, per il profilo aperto:

- **Destinatario (to)** — chi riceve gli avvisi di questo profilo.
- **E-mail assistente (facoltativa)** — un familiare o un assistente che
  riceve una copia degli avvisi, nella stessa email (i due indirizzi
  sono visibili a entrambi). Deve essere diverso dal destinatario. In
  **Copia all'assistente** scegli quali avvisi riceve (tutti finché non
  cambi): scorta bassa, promemoria delle dosi, delle ricette e delle
  scadenze, avvisi di carenza, avvisi di scadenza delle confezioni.
  **Invia all'assistente un riepilogo settimanale delle scorte**
  aggiunge, ogni 7 giorni, un'email al solo assistente con scorta, stato
  e data di esaurimento di ogni medicina attiva e le confezioni scadute o
  in scadenza, e niente sulle dosi assunte. La invia il PC che manda le
  email, una volta per profilo anche se il profilo è sincronizzato su
  più PC.
- **E-mail del medico (facoltativa)** — usata solo per le richieste di
  ricetta che invii tu; gli avvisi automatici non ci vanno mai.
- **Scadenza delle confezioni** — quanti giorni prima arriva l'avviso
  *in scadenza*: prima della scadenza stampata (predefinito 30) e prima
  della fine del periodo dopo l'apertura (predefinito 3).

Fai clic su **Salva destinatari**. Nella stessa sezione, **Il mio PIN**
permette di impostare o cambiare il PIN del tuo profilo.

<a id="smtp-providers"></a>
### Parametri email dei principali provider

Molti provider non accettano più, nei programmi, la password con cui
accedi alla webmail. Chiedono una **password per le app**: una password
separata, generata dal provider per un solo programma e revocabile in
qualsiasi momento. Va inserita in **Nuova password**. Se la revochi,
MedReminder smette di inviare finché non ne inserisci una nuova.

Per porta e cifratura vale una sola regola:

| Porta | *Usa StartTLS* |
|---|---|
| `587` | spuntato |
| `465` | non spuntato (la connessione è cifrata fin dall'inizio) |

Con tutti i provider qui sotto **Username** è l'indirizzo email completo.
Usa lo stesso indirizzo come **Mittente (from)**: molti provider
rifiutano un mittente diverso dall'account.

| Provider | Host | Porta | Password |
|---|---|---|---|
| Gmail | `smtp.gmail.com` | `587` | Password per le app (vedi sotto) |
| Yahoo Mail | `smtp.mail.yahoo.com` | `465` | Password per le app, dalla pagina *Sicurezza* dell'account Yahoo |
| iCloud Mail | `smtp.mail.me.com` | `587` | Password specifica per l'app, da `account.apple.com` → *Accesso e sicurezza*; richiede l'autenticazione a due fattori |
| Libero Mail | `smtp.libero.it` | `465` | Password dell'account; con la verifica in due passaggi attiva, una password per app da *Gestione Account* |
| Aruba (anche caselle di dominio) | `smtps.aruba.it` | `465` | Password della casella |
| GMX | `mail.gmx.net` | `587` | Password dell'account; prima attiva *POP3/IMAP* nelle impostazioni email della webmail |
| WEB.DE | `smtp.web.de` | `587` | Password dell'account; prima attiva *POP3/IMAP* nelle impostazioni email della webmail |
| Orange | `smtp.orange.fr` | `465` | Password dell'account; se viene rifiutata, controlla nello spazio cliente Orange se serve una password dedicata |

**Outlook.com, Hotmail, Live, MSN.** Microsoft accetta per questi
account solo l'accesso moderno (OAuth2), che MedReminder non supporta;
nemmeno una password per le app funziona. Lo stesso vale, di norma, per
gli account di lavoro o scuola Microsoft 365. Usa un altro account per
l'invio, per esempio un indirizzo Gmail dedicato a MedReminder.

#### Gmail: creare la password per le app

1. Accedi a `myaccount.google.com` con l'account Gmail che invierà le
   email.
2. Apri **Sicurezza**. Se la **Verifica in due passaggi** non è attiva,
   attivala seguendo la procedura guidata (telefono o app di
   autenticazione). Senza di essa le password per le app non esistono.
3. Apri `myaccount.google.com/apppasswords`, oppure cerca "Password per
   le app" nella casella di ricerca dell'account. Google può chiederti
   di nuovo la password.
4. Scrivi un nome che ricordi a cosa serve, per esempio `MedReminder`, e
   fai clic su **Crea**.
5. Google mostra una password di 16 lettere, in quattro gruppi. Copiala e
   incollala in **Nuova password**, senza spazi. Google non la mostra
   più: se la perdi, eliminala dalla stessa pagina e creane un'altra.
6. In MedReminder inserisci Host `smtp.gmail.com`, Porta `587`, *Usa
   StartTLS* spuntato, il tuo indirizzo Gmail come **Username** e come
   **Mittente (from)**. Fai clic su **Prova connessione**, poi su
   **Salva impostazioni SMTP**.

Se la pagina dice che l'opzione non è disponibile, di solito la verifica
in due passaggi non è attiva, usa solo chiavi di sicurezza, l'account è
iscritto alla Protezione avanzata, oppure è un account di lavoro o
scuola il cui amministratore ha disattivato le password per le app. Se
cambi la password dell'account Google, Google revoca le password per le
app: creane una nuova e inseriscila in MedReminder.

I provider cambiano regole e indirizzi. Se **Prova connessione**
fallisce con i valori sopra, controlla la pagina di aiuto del tuo
provider (cerca "impostazioni SMTP").

---

<a id="profiles"></a>
## 7. Più persone: profili e ruoli

Un solo MedReminder può seguire le medicine di più persone, per esempio
tu e un genitore. Ogni persona ha un **profilo** con le sue medicine e
i suoi destinatari.

### Ruoli

| | Amministratore | Utente |
|---|---|---|
| Proprie medicine, scorte, destinatari | sì | sì |
| Account email, backup, Paese di riferimento | sì | no |
| Creare, rinominare, eliminare profili; PIN di tutti | sì | no |
| Strumenti → Sincronizzazione… e Strumenti → Installazione… | sì | no |

C'è sempre almeno un amministratore.

### Gestire i profili (amministratore)

**Strumenti → Gestisci profili…**:

- **Nuovo profilo** — nome, ruolo (predefinito *Utente*), PIN
  facoltativo.
- **Rinomina** — cambia il nome visualizzato.
- **Cambia PIN** — imposta, cambia o toglie il PIN di un profilo.
- **Cambia ruolo…** — rende un profilo amministratore o utente. Il ruolo
  del profilo aperto non si può cambiare (apri prima un altro profilo
  amministratore). Prima di rendere amministratore un profilo senza PIN,
  valuta di aggiungere un PIN.
- **Elimina** — scrivi il nome del profilo per confermare. I dati su
  disco restano, a meno che spunti *Elimina anche i dati del profilo su
  disco*. Non si possono eliminare il profilo aperto e l'ultimo
  amministratore.

### Cambiare profilo

**File → Cambia profilo…**, scegli il profilo, conferma. MedReminder si
riavvia con quel profilo (e chiede il PIN, se c'è). All'avvio di
Windows si apre l'ultimo profilo usato.

### Il PIN

Il PIN evita di aprire per sbaglio il profilo sbagliato. **Non** è una
protezione: non cifra nulla, e chiunque usi lo stesso account Windows
può leggere i file di tutti i profili. Tre tentativi sbagliati chiudono
l'app. Per una vera riservatezza, dai a ogni persona il proprio account
Windows. Se un PIN è dimenticato, un amministratore lo toglie con
**Cambia PIN**; se l'ha dimenticato l'unico amministratore, vedi
[Problemi e risposte](#faq).

---

<a id="backup"></a>
## 8. Proteggere i dati: backup ed esportazione

| Opzione | A cosa serve | Dove |
|---|---|---|
| **Backup automatico giornaliero** | Una copia di tutti i profili, ogni giorno, in una tua cartella | Impostazioni → Backup / Ripristino |
| **Esportazione cifrata** | Un unico file portabile, per passare a un nuovo PC o da conservare | Impostazioni → Backup / Ripristino → Esporta tutti i dati (crittografati)… |
| **Backup cloud** | Una copia cifrata giornaliera su OneDrive, Google Drive o una cartella sincronizzata | Impostazioni → Backup / Ripristino → Backup su cartella sincronizzata |
| **Sincronizzazione / Installazione** | Più PC che lavorano sugli stessi dati, di continuo | [Più computer](#devices) |

Le impostazioni di backup sono gestite da un amministratore.

### Backup automatico giornaliero

1. **Strumenti → Impostazioni… → Backup / Ripristino**.
2. Spunta **Backup automatico giornaliero**, scegli la **Cartella
   backup** (meglio un disco esterno), l'**Orario preferito** e la
   **Retention (giorni)**.
3. **Salva impostazioni backup**. **Esegui backup adesso** ne fa uno
   subito.

Ogni profilo viene salvato, come `medreminder-<profilo>-<data>-<ora>.db`.
MedReminder deve essere in esecuzione all'orario scelto; se il PC era
spento, il backup parte al successivo avvio. Non scegliere per questo
backup una cartella sincronizzata nel cloud: i file non sono cifrati
(MedReminder ti avvisa).

- **Esporta in cartella specifica…** — una copia subito, dove vuoi.
- **Ripristina backup…** — scegli un file `.db` e il profilo che lo
  riceve. I dati attuali vengono messi da parte come
  `medreminder.db.bak-<data>`. Se ripristini nel profilo aperto,
  MedReminder si riavvia.

### Esportazione e importazione cifrate

Un'esportazione è **un unico file cifrato** (`.mrz`) con tutti i dati di
un profilo. Non è legato al tuo PC: è il modo consigliato per passare a
un nuovo computer.

**Esportare** — **Impostazioni → Backup / Ripristino → Esporta tutti i
dati (crittografati)…**:

1. Scegli il file di destinazione.
2. Scegli una **passphrase** di almeno 12 caratteri e scrivila due
   volte.
3. Se vuoi, includi la password SMTP, le preferenze di backup e le
   preferenze utente (lingua, Paese del catalogo). La password SMTP
   viene cifrata con la tua passphrase.
4. **Esporta**.

Un amministratore con più profili può spuntare **Esporta tutti i
profili (un file crittografato per profilo)** e scegliere una cartella.

> **La passphrase non si può recuperare.** Senza di essa il file non
> potrà più essere letto. Annotala in un posto sicuro.

**Importare** — **Impostazioni → Backup / Ripristino → Importa da un
export…**: scegli il file (MedReminder mostra cosa contiene), scrivi la
passphrase, spunta *Ho capito che questa operazione sovrascriverà i dati
del profilo corrente*, fai clic su **Importa** e riavvia quando
richiesto. L'importazione **sostituisce** i dati del profilo aperto;
viene conservata una copia di sicurezza. Una passphrase sbagliata, un
file danneggiato o un file di una versione più recente fermano
l'importazione senza toccare i tuoi dati. Il formato è pubblico
(`docs/EXPORT-FORMAT.md`): i tuoi dati non restano mai bloccati.

### Backup cloud

Una copia cifrata di tutti i profili, una volta al giorno, nel cloud.

1. **Impostazioni → Backup / Ripristino → Backup su cartella
   sincronizzata (cifrato)**: spunta la casella.
2. **Archivio**:
   - **OneDrive (cartella app)** o **Google Drive (cartella
     MedReminder/backups)** — fai clic su **Accedi…** ed entra con il
     tuo account;
   - **Cartella** — una cartella già sincronizzata da OneDrive,
     Dropbox, iCloud o Google Drive su questo PC.
3. **Snapshot da conservare** (predefinito 30).
4. **Passphrase di backup → Imposta / cambia…**: almeno 12 caratteri.
   Resta su questo PC e non viene mai inviata.
5. Salva.

**Ripristinare** — **Impostazioni → Backup / Ripristino → Ripristina da
cartella cloud…**: scegli la cartella o l'account, scegli una copia
(data, profilo, dispositivo), scrivi la passphrase, spunta la conferma
e fai clic su **Ripristina**. La copia sostituisce il profilo
**aperto**: per ripristinare un altro profilo, aprilo prima.

Da sapere:

- Perdere la passphrase di backup significa perdere le copie.
- Le copie non contengono la password SMTP né le preferenze: per quelle
  usa l'esportazione cifrata.
- Chi conosce la passphrase può leggere la copia di ogni profilo,
  compresi quelli protetti da PIN.
- Il tuo provider cloud può tenere i file eliminati nel suo cestino.
- È un **backup, non una sincronizzazione**: per lavorare su più PC usa
  la [sincronizzazione](#sync).
- Con un'installazione condivisa, il backup cloud lo fa solo il
  [dispositivo master](#master).

---

<a id="devices"></a>
## 9. Più computer

<a id="devices-choice"></a>
### Quale opzione mi serve?

| Situazione | Usa |
|---|---|
| Un solo PC | Niente da fare. Tieni un [backup](#backup). |
| Passare una volta a un nuovo PC | [Esportazione cifrata](#backup) sul vecchio PC, importazione sul nuovo. |
| Lo **stesso profilo** su due o più PC, sempre aggiornato | [Sincronizzazione](#sync) (Strumenti → Sincronizzazione…). |
| **Tutta la configurazione di famiglia** (profili, ruoli, PIN, email, backup) su più PC, con **un solo** PC che invia le email | [Installazione](#installation) (Strumenti → Installazione…), in aggiunta alla sincronizzazione. |

Entrambe le opzioni richiedono un archivio raggiungibile da tutti i PC:
**OneDrive**, **Google Drive** o una **cartella condivisa** (una
cartella sincronizzata da Dropbox o simili, o una cartella di rete). I
dati lì sono sempre cifrati. Non c'è nessun server di MedReminder.

Tutti i PC di un gruppo devono avere la stessa versione di MedReminder:
aggiornali insieme.

<a id="sync"></a>
### Sincronizzare un profilo tra PC

La sincronizzazione tiene **un profilo** identico su più PC: medicine,
scorte, assunzioni, nome del profilo e destinatari. Quello che registri
su un PC compare sugli altri in pochi minuti. Si configura **per ogni
profilo**, con il profilo aperto, da un amministratore.

**Sul primo PC**

1. Apri il profilo, poi **Strumenti → Sincronizzazione… → Attiva
   sincronizzazione…**.
2. Scegli dove tenere il gruppo:
   - **OneDrive** o **Google Drive**: accedi nella finestra del browser.
     Tutti i PC devono usare lo **stesso** account. MedReminder usa solo
     la propria cartella app;
   - **una cartella condivisa**: sceglila.
3. Inserisci un nome per questo PC e una **passphrase di
   sincronizzazione** (almeno 10 caratteri, due volte). Non è la
   passphrase dei backup. Conservala: non si può recuperare.

**Su ogni altro PC**

1. Crea un profilo (con un nome qualsiasi: sarà sostituito), o apri
   quello da sostituire.
2. **Strumenti → Sincronizzazione… → Unisciti a un gruppo…**, scegli lo
   stesso archivio, inserisci un nome per questo PC e la stessa
   passphrase. Al posto della passphrase puoi usare **Unisciti con un
   codice di abbinamento…** (vedi sotto).
3. Conferma: **i dati di questo profilo su questo PC vengono
   sostituiti** da quelli del gruppo (ne resta una copia). MedReminder
   si riavvia.

Con una cartella condivisa, aspetta prima che sia scaricata del tutto
sul nuovo PC. Con OneDrive o Google Drive l'unione può richiedere un
minuto. Se la passphrase apre più gruppi (più profili sincronizzati
con la stessa passphrase), MedReminder chiede a quale unirti.

**Codice di abbinamento al posto della passphrase.** Su un PC già nel
gruppo, **Strumenti → Sincronizzazione… → Abbina un dispositivo…** e fai
clic su **Mostra il codice** quando l'altro PC è pronto. Sull'altro PC,
**Unisciti con un codice di abbinamento…** e scrivi il codice. Il codice
vale 10 minuti e solo finché la sua finestra è aperta. Chiunque lo veda
può leggere i dati: non inviarlo mai per email o messaggio, e mostralo
solo quando serve (anche gli strumenti di assistenza remota lo vedono).

**Uso quotidiano**

- La sincronizzazione parte pochi secondi dopo ogni modifica, ogni 5
  minuti e con **Sincronizza ora**. La sezione **Dispositivi** mostra i
  PC e quando ognuno si è visto l'ultima volta.
- Se due PC hanno cambiato la stessa cosa prima di sincronizzarsi, vince
  la modifica più recente e il caso compare in **Conflitti**:
  **Ripristina valore perso** riporta l'altro valore, **Ignora** toglie
  la voce.
- Un'email di scorte basse viene inviata **una volta per gruppo**, non
  una per PC. (Due PC che controllano prima di essersi sincronizzati
  possono inviarla entrambi; un [dispositivo master](#master) elimina
  questo caso.)
- Importare un'esportazione o ripristinare un backup su un profilo
  sincronizzato avvia una nuova **generazione**: gli altri PC vengono
  avvisati e devono usare **Ricostruisci dal gruppo…**.
- **Disattiva sincronizzazione…** ferma la sincronizzazione su questo PC
  e ne conserva i dati.
- Se la sessione OneDrive o Google Drive scade (cambio password, lunga
  inattività), fai clic su **Accedi di nuovo a OneDrive** / **Accedi di
  nuovo a Google Drive**; non si perde nulla.

**Un PC è perso o la passphrase è stata scoperta.** Nella sezione
**Dispositivi** seleziona il PC e fai clic su **Rimuovi dispositivo…**,
oppure usa **Cambia chiave e passphrase…**. Scegli una nuova passphrase
di sincronizzazione: il PC rimosso non potrà leggere nulla di quanto
scritto da quel momento. Disconnetti anche quel PC nelle impostazioni di
sicurezza del tuo account Microsoft o Google. Su ogni altro PC fai clic
su **Inserisci la nuova chiave…** e scrivi la nuova passphrase o un
codice di abbinamento: le sue modifiche vengono conservate e
MedReminder si riavvia.

<a id="installation"></a>
### Condividere l'installazione

La sincronizzazione lavora profilo per profilo. L'**installazione**
aggiunge tutto ciò che sta intorno ai profili, così ogni PC è
configurato allo stesso modo:

| Condiviso da tutti i dispositivi | Proprio di ogni dispositivo |
|---|---|
| Profili: nomi, ruoli, PIN | Quali profili il dispositivo contiene |
| Account email (SMTP, password compresa) | Lingua dell'interfaccia, dimensione del testo |
| Regole del backup cloud (archivio, numero di copie) | Passphrase e accesso del backup cloud |
| Paese di riferimento | Backup automatico locale |

Ogni dispositivo contiene **solo i profili che un amministratore gli
assegna**: il PC di un nonno può contenere solo il suo profilo, mentre
il PC di famiglia li contiene tutti.

**Prima di iniziare**

- Un profilo amministratore, sul PC che sarà quello principale.
- **Sincronizzazione attiva per ogni profilo** da condividere (vedi
  [Sincronizzazione](#sync)); un profilo senza sincronizzazione non può
  essere assegnato a un altro dispositivo.

**Passo 1 — Pubblicare (sul PC principale)**

1. **Strumenti → Installazione… → Pubblica l'installazione…**.
2. Scegli lo **stesso archivio** che contiene i gruppi di
   sincronizzazione dei profili.
3. Scegli una **passphrase dell'installazione**. Permette a un
   amministratore di aggiungere un dispositivo e di recuperare tutti i
   profili quando nessun altro dispositivo è a portata di mano.
   Riservala agli amministratori; non si può recuperare.

Questo PC diventa il [dispositivo master](#master).

**Passo 2 — Aggiungere un dispositivo**

1. Sul PC principale: **Strumenti → Installazione… → Dispositivi →
   Aggiungi un dispositivo…**, spunta i profili per il nuovo
   dispositivo, poi **Mostra il codice**.
2. Sul nuovo PC:
   - se MedReminder non è mai stato usato lì: nella finestra di
     benvenuto scegli **Unisciti a un'installazione esistente…**;
   - altrimenti: **Strumenti → Installazione… → Unisciti a
     un'installazione esistente…**.
3. Scegli **Con un codice** e scrivi il codice. Il codice dura 10
   minuti, o finché la sua finestra resta aperta.
4. La finestra elenca ogni profilo ("aggiunto a questo dispositivo",
   "già presente su questo dispositivo", …). MedReminder si riavvia con i
   nuovi profili e le impostazioni dell'installazione. I profili già
   presenti sul nuovo PC vengono aggiunti all'installazione.

*Nessun altro dispositivo a portata di mano?* Scegli **Con la passphrase
dell'installazione**, seleziona l'archivio e scrivi la passphrase. Un
amministratore sceglie poi il suo profilo, ne scrive il PIN e seleziona
i profili per questo dispositivo.

**Uso quotidiano**

- L'installazione si sincronizza da sola ogni 15 minuti.
- Un cambio di profilo, ruolo, PIN, account email, regole del backup
  cloud o Paese di riferimento fatto su un dispositivo raggiunge gli
  altri.
- Un profilo creato dopo: attiva la sincronizzazione (Strumenti →
  Sincronizzazione…), poi assegnalo ad altri dispositivi con **Aggiungi
  un dispositivo…** da un dispositivo che lo contiene.
- **Strumenti → Installazione… → Stato** mostra l'archivio, il master
  ed eventuali azioni da fare.

<a id="master"></a>
### Il dispositivo master

In un'installazione condivisa **un solo dispositivo, il master**, invia
tutte le email (avvisi di scorte basse e promemoria delle dosi, di tutti
i profili che contiene, anche quelli non aperti) e fa il backup cloud.
Gli altri dispositivi mostrano i loro avvisi solo sullo schermo. Così
ogni email arriva una volta sola.

- Il dispositivo che pubblica l'installazione è il master. La sezione
  **Dispositivi** lo indica nella colonna **Ruolo**.
- Scegli come master un dispositivo **acceso spesso** e con MedReminder
  in esecuzione.
- Sugli altri dispositivi le richieste di ricetta si aprono nel
  programma di posta, e **Prova connessione** funziona solo sul master.
  Le impostazioni email si possono modificare ovunque e raggiungono
  tutti i dispositivi.
- Un master che non sincronizza l'installazione da **24 ore** smette di
  inviare email finché non si sincronizza di nuovo.
- Le installazioni pubblicate prima di questa versione non hanno un
  master finché un amministratore non ne sceglie uno; fino ad allora
  ogni dispositivo invia.

**Spostare il master su un altro dispositivo**

1. **Strumenti → Installazione… → Dispositivi**, seleziona il nuovo
   dispositivo, **Rendi master…**, conferma. Nessun dispositivo invia
   email finché il passaggio non è completato.
2. Sul nuovo dispositivo, con un profilo amministratore aperto, la
   finestra **Passaggio del ruolo di master** si apre da sola (o più
   tardi da **Strumenti → Installazione… → Completa il passaggio…**).
   Mostra le impostazioni e ti chiede di:
   - **Verificare la connessione email da questo dispositivo**;
   - **accedere** all'archivio del backup cloud con lo stesso account;
   - riscrivere la **passphrase del backup cloud** (non viene mai
     copiata tra dispositivi);
   - facoltativamente scrivere la passphrase dell'installazione, per
     portare i profili che nessun dispositivo a portata di mano contiene.
3. **Conferma**. Il nuovo dispositivo subentra quando il vecchio master
   cede il ruolo alla sua prossima sincronizzazione. I nuovi profili
   compaiono al prossimo avvio.

**Il master è guasto o perso.** Fai lo stesso da un altro dispositivo:
il nuovo master subentra da solo quando il vecchio non si fa sentire da
25 ore. Poi rimuovi il vecchio (sotto).

<a id="remove-device"></a>
### Dispositivo perso o sostituito

Quando un dispositivo viene perso, venduto o regalato:

1. Sul **master** (contiene tutti i profili): **Strumenti →
   Installazione… → Dispositivi**, seleziona il dispositivo, **Rimuovi
   dispositivo…**.
2. Scegli una **nuova passphrase dell'installazione**. Il dispositivo
   rimosso conserva quello che ha già ma non riceve nulla di nuovo; anche
   i profili che conteneva ricevono nuove chiavi.
3. MedReminder propone di mostrare un codice. Gli altri dispositivi
   smettono di sincronizzarsi finché non ricevono la nuova chiave: su
   ognuno un amministratore apre **Strumenti → Installazione… →
   Inserisci la nuova chiave…** e scrive la nuova passphrase o il codice.
   Le modifiche fatte lì nel frattempo vengono conservate.
4. Le nuove chiavi dei profili arrivano da sole agli altri dispositivi;
   un profilo aperto in quel momento chiede di riavviare MedReminder.

Rimuovere il master da un altro dispositivo rende master quel
dispositivo. Disconnetti anche il dispositivo perso dal tuo account
Microsoft o Google.

---

<a id="settings"></a>
## 10. Impostazioni e uso quotidiano

Tutto in **Strumenti → Impostazioni…**. Le sezioni sono elencate a
sinistra; **Ctrl+Tab** passa alla successiva. La finestra si può
ridimensionare.

- **Generale → Lingua interfaccia**: Inglese, Italiano, Francese,
  Spagnolo o Tedesco. Anche le email e la scheda terapia la usano.
  MedReminder si riavvia.
- **Generale → Dimensione del testo (questo profilo)**: Normale, Grande,
  Molto grande, per ogni profilo. MedReminder segue anche il
  ridimensionamento e i temi a contrasto di Windows. Su uno schermo
  piccolo preferisci Grande.
- **Generale → Aspetto (questo profilo)**: Come Windows, Chiaro o Scuro,
  per ogni profilo su questo computer. "Come Windows" è scuro solo su
  Windows 11 con la modalità scura attiva; con un tema a contrasto
  elevato di Windows si usano i suoi colori. Vale dopo il riavvio. In
  Scuro i campi data restano chiari.
- **Generale → Controlla aggiornamenti automaticamente (GitHub)**: cerca
  una nuova versione all'avvio (nulla viene installato da solo) e
  aggiorna il catalogo all'avvio e una volta al giorno. **? → Controlla aggiornamenti…** controlla subito.
- **Generale → Registra le query del database (diagnostica)**: solo
  amministratori. Scrive nel file di log ogni comando del database, senza
  i valori, per la diagnosi dei problemi. Vale subito; il log cresce in
  fretta, quindi disattivala dopo l'uso.
- **Avvio automatico → Avvia MedReminder all'accesso a Windows**: parte
  nascosto nell'area di notifica. Non servono diritti di
  amministratore.

**Icona nell'area di notifica.** Il doppio clic apre la finestra; il
clic destro offre *Apri MedReminder*, *Controlla ora*, *Impostazioni…*,
*Esci*.

**Sostieni lo sviluppo.** Se attivo, **? → Sostieni lo sviluppo…** apre
nel browser una pagina per un contributo volontario (Stripe o PayPal).
MedReminder non vede mai i tuoi dati di pagamento.

---

<a id="faq"></a>
## 11. Problemi e risposte

**Non ricevo email.**
Controlla, nell'ordine: *Prova connessione* in Impostazioni → Email
SMTP; il *Destinatario* in Impostazioni → Notifiche; il canale **Email**
spuntato sulla medicina; MedReminder in esecuzione. Con
un'installazione condivisa invia solo il master: controlla
**Strumenti → Installazione… → Stato**.

**"Questo dispositivo è il master ma non sincronizza l'installazione da
più di 24 ore".**
Il master non raggiunge l'archivio. Controlla la connessione internet e
l'accesso a OneDrive / Google Drive, poi **Sincronizza ora**.

**"La chiave dell'installazione è stata cambiata su un altro
dispositivo".**
È stato rimosso un dispositivo. Apri **Strumenti → Installazione… →
Inserisci la nuova chiave…** e scrivi la nuova passphrase, o un codice
mostrato da un dispositivo che la ha già.

**"Un dispositivo è stato rimosso … questo profilo ha una nuova chiave.
Riavviare ora?"**
Rispondi sì: il profilo prende la nuova chiave al riavvio di
MedReminder.

**"Questo dispositivo è stato rimosso dall'installazione".**
Il dispositivo conserva i suoi dati ma non riceve più nulla. Per usarlo
di nuovo, un amministratore lo aggiunge come nuovo dispositivo.

**Il passaggio del master non si completa.**
Il vecchio master cede il ruolo alla sua prossima sincronizzazione. Se è
spento per sempre, il nuovo master subentra 25 ore dopo l'ultima volta
che il vecchio si è visto.

**"Il gruppo di sincronizzazione di questo profilo appartiene a
un'altra installazione".**
Il profilo è stato pubblicato da un'altra installazione. Unisciti a
quell'installazione (**Unisciti a un'installazione esistente…**).

**Ho dimenticato un PIN.**
Un amministratore lo toglie con **Strumenti → Gestisci profili… →
Cambia PIN**. Se nessun altro può farlo: chiudi MedReminder, apri
`%LOCALAPPDATA%\MedReminder\profiles.json` con Blocco note e, per quel
profilo, cancella i valori di `PinHash` e `PinSalt` e imposta
`PinIterations` a `0`. MedReminder registra la modifica al prossimo
avvio; con un'installazione condivisa raggiunge gli altri dispositivi.

**Ho dimenticato una passphrase.**
Le passphrase di esportazione, backup, sincronizzazione e installazione
non si possono recuperare. Puoi impostarne di nuove (nuova esportazione,
nuova passphrase di backup cloud, *Cambia chiave e passphrase…*), ma i
file cifrati con la vecchia restano illeggibili.

**"Già in esecuzione".**
MedReminder è già aperto: cerca la sua icona nell'area di notifica.

**I dati sembrano danneggiati.**
Ripristina un backup (Impostazioni → Backup / Ripristino → Ripristina
backup…) o un'esportazione. I file di log (sotto) aiutano a capire cosa
è successo.

---

<a id="data"></a>
## 12. Dove MedReminder conserva i dati

Tutto si trova in `%LOCALAPPDATA%\MedReminder\` (incollalo nella barra
degli indirizzi di Esplora file). MedReminder non scrive altrove, tranne
i file di backup ed esportazione che posizioni tu.

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json              elenco profili, ruoli, PIN (hash)
├── smtp.settings.json         account email (senza password)
├── smtp.protected             password email, cifrata da Windows
├── backup.settings.json       impostazioni di backup
├── cloud-backup.protected     passphrase del backup cloud, cifrata da Windows
├── user.settings.json         lingua, Paese di riferimento, controllo aggiornamenti, registrazione query
├── household\                 installazione condivisa (solo se usata)
├── logs\medreminder-AAAAMMGG.log
└── profiles\
    └── <profilo>\
        ├── medreminder.db     medicine e scorte del profilo
        ├── notifications.settings.json   destinatari
        ├── ui.settings.json   dimensione del testo, aspetto, dimensioni della finestra
        └── sync.*             impostazioni di sincronizzazione (solo se usata)
```

- I **log** registrano cosa ha fatto l'app (controlli, email inviate,
  errori). Non contengono mai password, testo delle email o note
  mediche.
- Il database non è cifrato: è protetto dal tuo account Windows. Le
  esportazioni e le copie cloud sono cifrate.
- **Aggiornamento da una versione molto vecchia** (un solo
  `medreminder.db` direttamente nella cartella): il primo avvio lo sposta
  in un profilo chiamato *User* e tiene una copia in
  `backups\pre-migration-…`, che puoi eliminare quando tutto è a posto.

---

<a id="limits"></a>
## 13. Cosa MedReminder non fa

- Non tiene traccia di quali dosi hai preso e non avvisa delle dosi
  dimenticate (il promemoria all'orario della dose è solo un
  richiamo).
- Non dà indicazioni terapeutiche e non controlla dosi o interazioni tra
  farmaci.
- Non ordina medicine e non contatta il medico da solo.
- Non unisce i dati ripristinati da un backup: ripristino e importazione
  sostituiscono sempre.

Il suo scopo è farti sapere per tempo che ti serve una nuova ricetta.

Sulla base dell'architettura e della filosofia *local-first / non-medical-device* stabilita per MedReminder (vedasi `EVOLUTION.md` §1 e §9), ecco una serie di **nuove evoluzioni concrete non ancora presenti nel piano di sviluppo**, classificate per area strategica e valore d'uso:

---

### 1. Integrazioni OS e Hardware Windows (Group A — Estensioni Desktop)

* **Widget Nativo per Windows 11 (Widgets Board)**
* **Obiettivo**: Consultare al volo i farmaci in esaurimento o le prossime dosi direttamente dal pannello `Win + W` di Windows senza dover aprire o ripristinare l'applicazione.
* **Integrazione**: Implementazione di un provider *Adaptive Cards* (API `Microsoft.Windows.Widgets.Providers` in C#) sfruttando la classe background già esistente nell'applicazione.


* **Stampa e Generazione Scheda Farmacologica tascabile (PDF/QR)**
* **Obiettivo**: Avere un promemoria fisico o digitale da consegnare al medico di base, al pronto soccorso o in farmacia.
* **Funzionamento**: Generazione di un documento compatto (formato carta di credito o A4) contenente l'elenco dei farmaci attivi, i dosaggi giornalieri e un codice QR locale contenente il JSON anonimizzato della terapia.



---

### 2. Gestione Economica e Scadenze (Estensione del Dominio Stock)

* **Tracciamento dei Costi e Ticket Sanitari**
* **Obiettivo**: Monitorare la spesa periodica sostenuta per i farmaci per profilo utente.
* **Funzionamento**: Aggiunta di un campo opzionale `CostPerPackage` nei movimenti di carico stock (`StockMovement.NewPackage`). Il sistema genera report aggregati sulla spesa mensile/annuale (utile per la detrazione fiscale 730/Modello Redditi).




* **Gestione della Data di Scadenza del Lotto (Expiry Tracking)**
* **Obiettivo**: Evitare l'assunzione casuale o l'accumulo di farmaci scaduti in casa.
* **Funzionamento**: Estensione di `StockMovement` per supportare una data di scadenza associata alla confezione. L'applicazione notifica l'utente quando una confezione in stock sta per superare la data limite (distinta dal reminder di esaurimento scorte).





---

### 3. Accessibilità e UX (Inclusività per Utenti Anziani o Ipovedenti)

* **Interfaccia ad Alto Contrasto e Modalità "Caratteri Grandi" (Senior Mode)**
* **Obiettivo**: Adattare la UI WinForms per utenti anziani, la categoria di utenti primari per la gestione della terapia cronica.


* **Funzionamento**: Switch rapido nella UI per ingrandire i font della `DataGridView`, usare bottoni touch-friendly con icone ad alto contrasto e semplificare al massimo la navigazione (es. maschera a schede singole).




* **Dettatura Vocale del Nome e Dosaggio (Text-To-Speech)**
* **Obiettivo**: Aiutare chi ha difficoltà di lettura nell'identificazione del farmaco da assumere.
* **Funzionamento**: Integrazione della libreria nativa `System.Speech.Synthesis` per riprodurre un messaggio vocale durante la notifica toast/desktop ("*È ora di prendere 1 compressa di Tachipirina*").



---

### 4. Automatizzazione ed Ecologia Dati (Power Users)

* **CLI / Scripting Interface (MedReminder.CLI)**
* **Obiettivo**: Consentire ad utenti avanzati o sistemisti di automatizzare il backup o l'invio del report via script di sistema.
* **Funzionamento**: Eseguibile a riga di comando che riutilizza `MedReminder.Application` e `MedReminder.Infrastructure` per eseguire comandi batch (es. `medreminder-cli --backup --out D:\Backups` o `medreminder-cli --export-json`).




* **Sincronizzazione WebDAV Personale (Self-Hosted)**
* **Obiettivo**: Coprire le esigenze di sincronizzazione multi-dispositivo per utenti attenti alla privacy che usano Nextcloud, Synology o server NAS casalinghi senza dover passare da cloud pubblici.
* **Funzionamento**: Integrazione di un adapter WebDAV nativo per il caricamento/scaricamento automatico del file `.mrz` cifrato (estensione della strategia C.3++).





---

### Valutazione e Priorità Consigliata

| Evoluzione | Complessità Sviluppo | Impatto Utente | Note / Vincoli |
| --- | --- | --- | --- |
| **Tracciamento Scadenze Lotti** | Bassa (1-2 settimane) | **Alto** | Naturale estensione del modello `StockMovement`.

 |
| **Modalità Senior (UI Accessibile)** | Bassa (1 settimana) | **Alto** | Cruciale per l'utenza target principale.

 |
| **Widget Windows 11** | Media (1-2 settimane) | Medio | Aumenta l'integrazione con l'OS. |
| **Tracciamento Costi e Spesa** | Bassa (3-5 giorni) | Medio | Utile per reportistica e detrazioni.

 |
| **Sincronizzazione WebDAV** | Media (1 settimana) | Medio-Alto | Perfetto per gli utenti *local-first / privacy*.

 |
 
Ecco ulteriori proposte innovative, suddivise per area di valore, mantenendo rigorosamente la filosofia *local-first*, la protezione dei dati personali e il vincolo di non-dispositivo-medico:

---

### 1. Gestione Intelligente della Farmacia Domestica (Home Inventory)

* **Organizzazione della Scatola dei Farmaci (Location / Drawer Tracking)**
* **Problema**: Chi gestisce molti farmaci (o più profili familiari) spesso perde tempo a cercare dove si trova la scatola specifica (es. *"Armadietto Bagno"*, *"Frigorifero"*, *"Cassetto Studio"*).
* **Soluzione**: Aggiunta di una proprietà opzionale `StorageLocation` all'entità `Medicine`. Consentire la ricerca e il filtraggio rapido per posizione nella UI.




* **Condivisione della Scorta di Riserva (Shared Household Buffer)**
* **Problema**: Più persone nello stesso nucleo familiare usano lo stesso farmaco comune (es. un antinfiammatorio da banco o una soluzione salina) e le scorte vengono consumate da più profili.


* **Soluzione**: Introdurre la possibilità di contrassegnare una scorta come *"Condivisa tra profili"*, aggiornando il bilancio totale dello stock comune ad ogni consumo registrato dai singoli profili.



---

### 2. Semplificazione della Logistica e del Riordino

* **Template "Pillola / Blister Virtuale" per la Preparazione Settimanale**
* **Problema**: Molti utenti e caregiver preparano il portapillole settimanale (lunedì-domenica) una volta alla settimana anziché estrarre le compresse giorno per giorno.
* **Soluzione**: Una modalità *"Preparazione Portapillole"* nella UI: l'app calcola tutte le dosi della settimana per ciascun farmaco, genera un riepilogo visivo (es. *"Prendi 7 compresse dal Blister A, 14 dal Blister B"*) e permette di scalare lo stock in un unico clic programmato per l'intera settimana.




* **Bozza E-mail / Messaggio "Richiesta Ricetta" per il Medico**
* **Problema**: Quando il farmaco sta per esaurirsi, l'utente deve contattare il medico di base per la nuova ricetta.


* **Soluzione**: Generatore di bozze di testo (o invio diretto tramite e-mail) pre-compilate destinate al medico: *"Gentile Dott. [Nome], le chiedo cortesemente la prescrizione per il farmaco [Nome Farmaco] - Codice AIC [Codice] per il paziente [Nome Profilo]"*.





---

### 3. Visualizzazione, Trend e Reportistica Avanzata

* **Calendario Visuale della Terapia (Vista Gantt / Agenda)**
* **Problema**: La visualizzazione a tabella (`DataGridView`) mostra lo stato corrente, ma non permette di cogliere a colpo d'occhio sovrapposizioni o periodi di sospensione futuri.


* **Soluzione**: Una vista a calendario o diagramma di Gantt integrata nella UI che mostra graficamente:
* I periodi di sospensione programmati (`MedicationSuspension`).


* L'ETA stimata di esaurimento per ciascun farmaco basata sul forecast.


* I cambi di dosaggio nel tempo (`MedicationScheduleHistory`).






* **Analisi dell'Aderenza Teorica allo Stock (Stock Consistency Index)**
* **Problema**: Riscontrare disallineamenti tra le pillole rimanenti nella scatola reale e quelle registrate dal software.


* **Soluzione**: Una schermata di "Riconciliazione Stock" in cui l'utente conta le pillole fisiche rimaste e l'app calcola uno scostamento percentuale rispetto ai consumi teorici pianificati, registrando automaticamente la correzione (`PositiveCorrection` / `NegativeCorrection`) e fornendo indicazioni sull'accuratezza delle impostazioni.





---

### 4. Protezione Dati e Integrazione Windows Avanzata

* **Cifratura del Database Locale tramite Password di Profilo (DB Encryption at Rest)**
* **Problema**: Attualmente il database SQLite risiede in chiaro nel profilo utente. Se il PC viene condiviso, altri utenti locali potrebbero accedere direttamente al file `.db`.


* **Soluzione**: Supporto opzionale a **SQLCipher** (versione cifrata di SQLite) con chiave derivata da passphrase utente o protetta tramite Windows Hello / DPAPI.




* **Supporto alle Scorciatoie di Tastiera Rapide e Command Palette (`Ctrl + K`)**
* **Problema**: Navigare tra dialoghi e schede con il mouse richiede tempo per i power user.


* **Soluzione**: Implementazione di una *Command Palette* in stile moderno (attivabile con `Ctrl + K` o `Ctrl + P`) per eseguire azioni istantanee senza usare il mouse: *"Registra consumo Tachipirina"*, *"Aggiungi stock"*, *"Esporta report PDF"*, *"Passa a Profilo Mario"*.



---

### Tabella Riassuntiva delle Idee

| Funzionalità | Area | Complessità | Target Principale |
| --- | --- | --- | --- |
| **Bozza Richiesta Ricetta al Medico** | Logistica | Molto Bassa (2-3 giorni) | Pazienti cronici, Caregiver

 |
| **Preparazione Portapillole Settimanale** | Usabilità | Bassa (1 settimana) | Utenti anziani, Caregiver

 |
| **Ubicazione Farmaco (Cassetto/Stanza)** | Inventario | Molto Bassa (1-2 giorni) | Famiglie, Profili multipli

 |
| **Calendario Visuale / Gantt** | Visualizzazione | Media (1-2 settimane) | Tutti gli utenti |
| **Command Palette (`Ctrl + K`)** | Accessibilità/UX | Bassa (3-5 giorni) | Power users / Desktop lovers |
| **Cifratura DB (SQLCipher)** | Sicurezza | Media (1 settimana) | Utenti attenti alla privacy

 |Insieme alle funzionalità analizzate nei documenti (Export/Import manuale C.3, Backup automatico in Cloud C.3+ e l'astrazione dell'archiviazione C.3++), al termine dell'implementazione l'applicazione MedReminder offrirà un **sistema completo, sicuro e flessibile di gestione, migrazione e ripristino dei dati**.

Ecco un riassunto delle caratteristiche e delle funzionalità offerte dalla feature a completamento dello sviluppo:

---

### 1. Esportazione e Importazione Manuale dei Dati (GDPR & Migrazione)



* **Esportazione crittografata su file (`.mrz`)**: Generazione di un archivio ZIP protetto tramite algoritmo **AES-GCM** (con chiave derivata da passphrase tramite **Argon2id**). L'archivio contiene i dati strutturati in formato JSON e un file `manifest.json` leggibile in chiaro con i metadati dell'esportazione.


* **Sicurezza elevata**: Nessuna esportazione in chiaro; se l'utente non specifica una passphrase valida (almeno 12 caratteri), l'esportazione viene bloccata. Non viene usata la tecnologia DPAPI di Windows per l'archivio, consentendo la migrazione dei dati su altri computer o sistemi.


* **Contenuto ed opzioni configurabili**:
* Esportazione completa delle entità dell'applicazione (farmaci, storici somministrazioni, scorte, sospensioni, notifiche).


* Possibilità opzionale di includere le impostazioni condivise (impostazioni SMTP, preferenze utente, impostazioni di backup).


* La password SMTP (se inclusa) viene ri-crittografata con la passphrase dell'archivio anziché rimanere legata al singolo account Windows.




* **Importazione sicura con modalità Overwrite**:
* Ripristino previa verifica di integrità (checksum SHA-256) e decrittogrfia.


* Effettua un backup preventivo automatico del database locale prima di sovrascrivere i dati per evitare perdite incidentali.


* Richiede la conferma dell'utente e un riavvio dell'applicazione per ricaricare lo stato in modo pulito.





---

### 2. Backup Automatico in Cartelle Cloud e Dispositivi di Sync (C.3+)



* **Integrazione trasparente col Cloud locale**: Salvataggio automatico programmato degli archivi crittografati `.mrz` direttamente in una cartella sincronizzata dall'utente (es. OneDrive, Google Drive Desktop, Dropbox, iCloud).


* **Operazioni atomiche per evitare conflitti di sync**: L'archivio viene creato prima come file temporaneo e poi spostato nella cartella di destinazione per evitare che l'agente di sincronizzazione Cloud legga file parziali.


* **Politica di retention automatica**: Gestione e pulizia automatica dei backup più vecchi per non saturare lo spazio disco/cloud.



---

### 3. Architettura Storage Pluggabile e Predisposizione Mobile (C.3++)



* **Disaccoppiamento tramite `IArchiveStorage**`: Il motore di generazione degli archivi (`ExportService`) e la loro destinazione finale sono separati. L'applicazione usa un'interfaccia astratta che definisce le operazioni di caricamento, download, recupero della lista dei backup ed eliminazione (`UploadAsync`, `DownloadAsync`, `ListAsync`, `DeleteAsync`).


* **Indipendenza dai provider e nessun Lock-In**:
* *Fase attuale*: Utilizza la classe `LocalFolderArchiveStorage` per interagire con il file system (e gli agenti di sincronizzazione OS).


* *Predisposizione per il futuro*: L'architettura è già pronta per accogliere moduli nativi (es. integrazione diretta API con Microsoft Graph/OneDrive o Google Drive) in vista delle future versioni mobile (iOS / Android).




* **Formato aperto e documentato**: Il formato dell'archivio è descritto nel file pubblico `docs/EXPORT-FORMAT.md`, garantendo all'utente di poter decifrare e migrare i propri dati anche tramite script esterni, senza rimanere vincolato all'applicazione.
Insieme alle funzionalità analizzate nei documenti (Export/Import manuale C.3, Backup automatico in Cloud C.3+ e l'astrazione dell'archiviazione C.3++), al termine dell'implementazione l'applicazione MedReminder offrirà un **sistema completo, sicuro e flessibile di gestione, migrazione e ripristino dei dati**.

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
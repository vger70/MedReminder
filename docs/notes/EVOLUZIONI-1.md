Sulla base della documentazione fornita e dello stato attuale del progetto MedReminder (con le funzionalità A1, A5, A6, C.3 e C.3+ già completate `[DONE]`), ecco una proposta strutturata per le **prossime evoluzioni dell'applicazione**, organizzata per ambito, valore per l'utente e fattibilità tecnica.

---

### 1. Evoluzioni a Basso Impatto e Alto Valore Utente (Gruppo A)

#### **A2 — Scansione Barcode / Codice AIC del Farmaco**

* **Obiettivo**: Semplificare e velocizzare l'inserimento di un nuovo farmaco riducendo gli errori manuali.
* **Funzionamento**:
* Utilizzo di lettori barcode USB handheld (emulazione tastiera HID) su desktop o della fotocamera del PC/dispositivo mobile.
* Acquisizione del codice AIC (Autorizzazione all'Immissione in Commercio) stampato sulle confezioni dei farmaci in Italia.


* Ricerca del codice nel catalogo di riferimento locale e compilazione automatica dei dati del farmaco (Nome, Principio Attivo, Formato/Confezione).




* **Impegno stimato**: 1-2 settimane per l'integrazione desktop.



#### **A3 — Notifiche per il Caregiver**

* **Obiettivo**: Supportare la gestione della terapia per soggetti anziani o non autonomi, notificando direttamente un familiare o un caregiver.
* **Funzionamento**:
* Configurazione di un indirizzo e-mail secondario del caregiver (`CaregiverAddress`) per ogni profilo utente.


* Invio automatico della notifica al caregiver quando la scorta scende sotto la soglia critica o quando si superano i tempi di tolleranza.


* Riutilizzo del motore esistente **MailKit** (nessun nuovo trasporto richiesto).




* **Impegno stimato**: 1 settimana.



---

### 2. Canale Web e Presenza Pubblica (Sito Web di Presentazione)

* **Obiettivo**: Creare un sito web statico pubblico per aumentare la visibilità del progetto, facilitare il download da parte di utenti non tecnici e fornire un punto di accesso per il supporto/donazioni al di fuori dell'app Windows.


* **Architettura proposta**:
* **Generatore**: **Hugo** (sito statico ultra-veloce, nessun backend/Node.js, supporto multilingua nativo).


* **Multilingua**: Traduzione in 5 lingue (Inglese, Italiano, Francese, Spagnolo, Tedesco) in conformità con la localizzazione dell'app.


* **Hosting**: **Cloudflare Pages** (CDN globale gratuita, analitiche rispettose della privacy senza necessità di banner per i cookie).


* **Sezioni del sito**: Hero con CTA di download, griglia delle funzionalità, guida all'uso, galleria screenshot, requisiti di sistema, spiegazione degli avvisi SmartScreen e sezione donazioni.




* **Impegno stimato**: 2-3 settimane per la prima release.



---

### 3. Strategia Multi-Dispositivo e Mobile

#### **C.3++ — Integrazione Nativa con Cloud Provider**

* **Obiettivo**: Evolvere il sistema di backup (C.3+) integrando direttamente le API dei provider cloud senza dipendere dalle sole cartelle sincronizzate del sistema operativo.


* **Funzionamento**:
* Definizione dell'astrazione `IArchiveStorage` per disaccoppiare la creazione del file `.mrz` cifrato dalla destinazione di salvataggio.


* Implementazione progressiva degli adapter dedicati: **OneDrive** (Microsoft Graph API), **Google Drive** e **Dropbox**.





#### **B.1 — Applicazione Companion Mobile (.NET MAUI)**

* **Obiettivo**: Portare MedReminder su smartphone (Android / iOS) per la consultazione e la gestione delle notifiche in mobilità.


* **Architettura e Riutilizzo**:
* Riutilizzo diretto dei progetti `MedReminder.Domain` e `MedReminder.Application` che puntano a `.NET 10` privo di dipendenze specifiche per Windows.


* Sostituzione dei componenti `Infrastructure` specifici per Windows con adapter nativi (es. `SecureStorage` per le credenziali al posto di DPAPI, notifiche locali native tramite MAUI).


* Integrazione del sistema di ripristino da backup `.mrz` cifrato (generato tramite C.3+) per garantire il passaggio e la sincronizzazione manuale dei dati tra PC e smartphone.




* **Impegno stimato**: 2-4 mesi-uomo.



---

### 4. Architettura Cloud e Sincronizzazione End-to-End (Evoluzione a lungo termine: C.1)

Se il progetto deciderà di evolvere da applicazione desktop locale a servizio multi-dispositivo completo:

* **Sincronizzazione E2EE (End-to-End Encrypted)**:
* Modello **Zero-Knowledge**: derivazione della master key tramite **Argon2id** dal lato client e cifratura simmetrica dei record con **ChaCha20-Poly1305** / AES-GCM.


* Il server salva un log di operazioni cifrate senza mai accedere ai dati sanitari o personali in chiaro.




* **Backend**:
* Micro-servizio lightweight in **ASP.NET Core** con database PostgreSQL residente in UE per il rispetto nativo del GDPR.





---

### Sequenza di Implementazione Raccomandata

1. **A2** (Scansione Barcode AIC) + **A3** (Notifiche Caregiver) — Sviluppo immediato a basso rischio/alto valore.


2. **Sito Web Pubblico (Hugo + Cloudflare Pages)** — Miglioramento dell'onboarding e della visibilità.


3. **C.3++** (Astrazione Cloud/Storage) — Propedeutico per il mobile.


4. **B.1** (App Mobile Companion con .NET MAUI) — Espansione dell'ecosistema.
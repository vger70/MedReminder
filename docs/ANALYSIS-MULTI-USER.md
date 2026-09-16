# ANALYSIS — Incremento 15: Gestione multi-utente (caso B)

Documento di design **preliminare** all'implementazione. Approvato
questo, si procede con i sotto-increment 15a…15e.

> **Non è un'analisi speculativa.** Ogni decisione qui è già motivata
> tecnicamente e delimita cosa verrà scritto in code. Le sezioni
> "Decisioni ancora da confermare" alla fine sono le sole zone di
> ambiguità residua che chiedono un tuo input.

---

## 1. Scope

### 1.1 Cosa è "multi-utente caso B"

Una persona fisica gestisce le medicine di **più persone** dallo
stesso account Windows. Esempi:

- Genitore/caregiver che gestisce le terapie di 2-3 figli e sé stesso.
- Adulto che gestisce sia le proprie medicine sia quelle di un
  genitore anziano.
- Badante che segue 2-4 assistiti dallo stesso PC.

**Numero previsto di profili**: 2-5 (da tua indicazione). Il design
regge fino a ~50 senza refactoring, ma la UI ottimizza per 2-5.

### 1.2 Cosa NON è

- **Non è multi-tenant su server condiviso.** L'app resta offline-first
  come da spec §1. Nessun sync tra macchine diverse.
- **Non è multi-utente Windows.** Per quello basta l'account Windows
  separato (già supportato: `%LOCALAPPDATA%` + mutex `Local\...` ==
  per-session).
- **Non è un sistema di sicurezza.** Il PIN opzionale (§8) è friction
  contro cambi accidentali, non protezione da accesso non autorizzato.
- **Non c'è audit trail** di "chi ha modificato cosa" — l'utente Windows
  è uno solo per definizione.

### 1.3 Obiettivi funzionali

- Aggiungere/rinominare/rimuovere profili dalla UI.
- Ogni profilo ha il proprio database, proprie impostazioni SMTP,
  propria configurazione di backup automatico.
- Scelta del profilo all'avvio quando ce n'è più di uno.
- Cambio profilo a runtime senza chiudere manualmente l'app.
- Migrazione automatica e senza perdita dati dall'attuale schema
  single-user (`medreminder.db` in `%LOCALAPPDATA%\MedReminder\`).

---

## 2. Modello dati

### 2.1 Decisione: **un DB SQLite per profilo, file separati**

Alternative valutate:

| Approccio | Pro | Contro | Scelto |
|---|---|---|---|
| **DB per profilo** (file separati) | Isolamento naturale; switch banale (chiudi/apri path); backup/restore per profilo out-of-the-box; nessuna modifica al Domain/EF Core | Duplicazione minima dello schema; connessione ricreata al cambio profilo | ✅ |
| Colonna `UserId` in ogni tabella | Un solo file DB | Ogni query deve filtrare `WHERE UserId = @current`; rischio bug di query non filtrate = dati che perdono attraverso i profili; export/import per profilo diventa non banale; migration DB dolorosa; contraddice l'idea "il DB dell'utente Papà è dell'utente Papà" | ❌ |
| DB per profilo con schema condiviso via `ATTACH DATABASE` | Query cross-profile possibili | Nessun uso attuale lo richiede; complessità gratuita | ❌ |

**Zero cambiamenti** ai file `MedReminder.Domain.*`, `MedReminder.
Application.*`, `MedReminder.Infrastructure.Persistence.*`. Cambia
solo la **stringa di connessione** che il composition root passa a
`AddMedReminderInfrastructure`.

### 2.2 Registry dei profili

Nuovo file `%LOCALAPPDATA%\MedReminder\profiles.json`:

```json
{
  "SchemaVersion": 1,
  "ActiveProfileId": "default",
  "Profiles": [
    {
      "Id": "default",
      "DisplayName": "Mario Rossi",
      "CreatedAt": "2026-09-16T09:00:00Z",
      "LastUsedAt": "2026-09-16T18:30:00Z",
      "PinHash": null,
      "PinSalt": null,
      "PinIterations": 0
    },
    {
      "Id": "3a8c…",
      "DisplayName": "Nonna",
      "CreatedAt": "2026-09-20T14:00:00Z",
      "LastUsedAt": "2026-09-25T09:12:00Z",
      "PinHash": "base64…",
      "PinSalt": "base64…",
      "PinIterations": 100000
    }
  ]
}
```

Regole:

- `Id` è generato con `Guid.NewGuid().ToString("N")`, immutabile per
  la vita del profilo (usato come nome cartella).
- `DisplayName` è editabile, non usato come chiave.
- `ActiveProfileId`: opzionale hint sull'ultimo profilo caricato,
  usato per l'auto-start (§10). Non è "il" profilo attivo di runtime.
- Il registry vive **fuori** dai profili — è metadata dell'app, non
  di un profilo specifico.
- Scritture atomiche: tmp + File.Move, come già fatto per
  `backup.state.json`.

### 2.3 Astrazione lato codice

Nuova interfaccia in `MedReminder.Application.Abstractions`:

```csharp
public sealed record Profile(
    string Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool HasPin);

public interface IProfileRegistry
{
    IReadOnlyList<Profile> ListProfiles();
    Profile? GetById(string id);
    string? ActiveProfileIdHint { get; }

    Profile Create(string displayName);
    void Rename(string id, string newDisplayName);
    void Delete(string id, bool deleteData);   // deleteData=true rimuove anche la cartella su disco
    void SetActiveProfileHint(string id);

    void SetPin(string id, string pin);   // pin==null → clear
    bool VerifyPin(string id, string pin);
    bool HasPin(string id);
}
```

L'implementazione (`ProfileRegistry`) vive in `MedReminder.
Infrastructure.Profiles`. Legge/scrive `profiles.json`. Non conosce
EF Core: gestisce solo file di configurazione + PIN hash (§8).

### 2.4 Profile currently loaded

Un secondo pezzo, distinto dal registry:

```csharp
public interface ICurrentProfile
{
    string Id { get; }
    string DisplayName { get; }
    string DataDirectory { get; }        // %LOCALAPPDATA%\MedReminder\profiles\<id>\
    string DatabasePath { get; }         // <DataDirectory>\medreminder.db
    string SmtpSettingsPath { get; }     // <DataDirectory>\smtp.settings.json
    string BackupSettingsPath { get; }   // <DataDirectory>\backup.settings.json
    string BackupStatePath { get; }      // <DataDirectory>\backup.state.json
}
```

Registrato come `Singleton` nel container DI dopo che l'utente ha
scelto il profilo. Ogni servizio che oggi hardcoda `AppDataPaths.
GetDatabasePath()` va aggiornato per iniettare `ICurrentProfile`.

### 2.5 Modifiche a `AppDataPaths`

L'attuale classe statica in `MedReminder.Infrastructure.Storage`
espone metodi hardcoded (`GetDatabasePath`, `GetCredentialsPath`,
ecc.). Va rifattorizzata:

- Metodi che restituiscono **path a livello app** (log dir, credentials
  DPAPI, `profiles.json`) → restano statici.
- Metodi che restituiscono **path per profilo** (DB, smtp.settings.
  json, backup.settings.json, backup.state.json) → **rimossi**;
  sostituiti dai membri di `ICurrentProfile`.

Refactor invasivo ma meccanico. Impatto per file:
- `BackupService`, `BackupStateStore`, `SmtpCredentialStore`,
  `DatabaseInitializer`, `MedReminderDbContext` costruzione della
  connection string in `AddMedReminderInfrastructure`.

---

## 3. Layout su disco

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                          ← registry
├── smtp.protected                         ← credenziale DPAPI (globale? Vedi §7.2)
├── logs\
│   ├── medreminder-20260916.log
│   └── …
└── profiles\
    ├── default\
    │   ├── medreminder.db
    │   ├── medreminder.db-wal
    │   ├── medreminder.db-shm
    │   ├── smtp.settings.json
    │   ├── smtp.protected                 ← password DPAPI (per profilo, §7.2)
    │   ├── backup.settings.json
    │   └── backup.state.json
    └── 3a8c…\
        ├── medreminder.db
        ├── smtp.settings.json
        ├── smtp.protected
        ├── backup.settings.json
        └── backup.state.json
```

Note:

- I file `.log` restano centralizzati: sono metadata tecnica dell'app,
  non del profilo. Un profilo può fallire a caricare, il log deve
  comunque essere raggiungibile.
- Cartelle e file vengono creati alla `Profile.Create()`.
- La `Delete()` con `deleteData=false` rimuove solo l'entry dal
  registry; la cartella resta on-disk (recupero manuale possibile).
  Con `deleteData=true` rimuove anche la cartella.

---

## 4. Boot flow

### 4.1 Sequenza completa

```
                    ┌──────────────────────────┐
                    │ Program.Main             │
                    │  (mutex, Serilog, ...)   │
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │ MigrationV1toV2          │  ← §5
                    │  se registry mancante e  │
                    │  medreminder.db esiste   │
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │ IProfileRegistry.List()  │
                    └────────────┬─────────────┘
                                 │
             ┌───────────────────┼────────────────────┐
             │                   │                    │
     count==0│         count==1  │           count>1  │
             ▼                   ▼                    ▼
    ┌────────────────┐   ┌────────────────┐   ┌────────────────┐
    │ Wizard "Crea   │   │ Auto-select    │   │ ProfilePickerDlg
    │ primo profilo" │   │ l'unico prof.  │   │ + eventuale PIN │
    └────────┬───────┘   └────────┬───────┘   └────────┬───────┘
             │                    │                     │
             └────────────────────┴─────────────────────┘
                                  │
                    ┌─────────────▼──────────────┐
                    │ SetCurrentProfile(chosen)  │
                    │ Registry.SetActiveHint()   │
                    │ BuildHost(profile)         │
                    │ InitializeDatabase(profile)│
                    │ StartAsync + RunUi         │
                    └────────────────────────────┘
```

### 4.2 Auto-start (`--minimized`)

Se l'app parte con `--minimized` (auto-start di Windows) **non**
mostra il picker. Sceglie automaticamente il profilo indicato da
`ActiveProfileIdHint` nel registry (o l'unico se ce n'è solo uno).

Motivo: durante l'auto-login Windows, mostrare un dialog è
sgradevole. L'utente può cambiare profilo dal menu una volta
aperta l'app.

Se il profilo dell'hint ha un PIN, viene richiesto (parte già
minimizzata, il dialog appare in primo piano).

### 4.3 Flag opzionale `--profile <id>`

Argomento di command-line che seleziona esplicitamente un profilo,
saltando il picker. Utile per creare shortcut Windows separati per
profilo (es. "MedReminder — Nonna" sul desktop). L'auto-start
principale usa `--minimized` senza `--profile` (usa l'hint).

---

## 5. Migrazione da single-user

### 5.1 Detection

`MigrationV1toV2` è idempotente. Si attiva **solo se**:

- `profiles.json` **non esiste**, AND
- `%LOCALAPPDATA%\MedReminder\medreminder.db` **esiste**.

Se entrambe le condizioni sono false → skip.

### 5.2 Sequenza

1. **Backup preventivo obbligatorio.** Prima di muovere qualsiasi
   file, copia `medreminder.db` (e file `.wal`/`.shm` se presenti)
   in `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-
   HHmmss\` — nome auto-descrittivo, mai sovrascritto.
2. Crea `profiles\default\` (id sempre stringa letterale "default"
   per la migrazione; profili successivi useranno Guid).
3. `File.Move` di `medreminder.db`, `.db-wal`, `.db-shm` in
   `profiles\default\`.
4. `File.Move` di `smtp.settings.json`, `smtp.protected`,
   `backup.settings.json`, `backup.state.json` (quelli che esistono)
   nella stessa cartella.
5. Scrive `profiles.json` con un solo entry `default`, `DisplayName`
   = "Utente" (rinominabile subito dal Settings), `ActiveProfileId`
   = "default".
6. Se qualsiasi passo dai 2 al 5 fallisce: **rollback** — sposta
   indietro i file dal backup preventivo e cancella la cartella
   `profiles/default` semi-creata. L'utente riparte al prossimo
   boot con lo stato pre-migrazione intatto.

### 5.3 Regressione consapevole

Dopo migrazione, `smtp.protected` che era a livello app viene
spostato dentro il profilo `default`. Il DPAPI decrypt funziona
comunque perché è la stessa `DataProtectionScope.CurrentUser`.
Nulla da toccare a livello crypto.

### 5.4 Test

Un test di integrazione ripristina una struttura V1 finta in un
`Path.GetTempPath()`, invoca il migrator, verifica la struttura
V2 e che nessun file V1 sia rimasto.

---

## 6. Profile switching

### 6.1 Decisione: **restart guidato dell'app**

Alternative valutate:

| Approccio | Pro | Contro | Scelto |
|---|---|---|---|
| **Restart dell'exe** (come già facciamo dopo restore DB) | Semplice, garantisce zero state condiviso; il pattern esiste già (`IApplicationRestarter`) | Perdita di window state e di eventuali dialog aperti | ✅ |
| Hot swap del DbContext scope | Nessun restart | Serve fermare/riavviare tutti gli hosted service, disporre pool SQLite, ricostruire l'IHost mantenendo il message loop WinForms; molti punti di fallimento silenzioso | ❌ |
| Multi-app (una finestra per profilo) | Isolamento massimo | Rompe il pattern single-instance mutex, complica il tray | ❌ |

**Flusso**:

1. Utente: `File → Cambia profilo` → dialog picker.
2. Conferma → chiama `IProfileRegistry.SetActiveProfileHint(nuovoId)`.
3. Chiama `IApplicationRestarter.RestartAndExit()` che rilancia
   l'exe. Il nuovo processo legge l'hint e apre il nuovo profilo
   senza mostrare picker.

Riuso completo dell'infrastruttura restart già presente per il
restore backup (Incremento 11).

### 6.2 Auto-start conflict

Se il vecchio auto-start Windows era registrato con `--minimized`,
il restart post-switch NON deve partire minimizzato — l'utente ha
appena scelto attivamente il profilo. `IApplicationRestarter` non
passa `--minimized`.

---

## 7. Settings per profilo

### 7.1 SMTP e Backup

Entrambi vivono dentro la cartella profilo:

- `smtp.settings.json`: configurazione SMTP (host, port, from, to, …).
- `backup.settings.json`: enable/directory/orario/retention.
- `backup.state.json`: LastSuccessfulBackupAt, ecc.

**Program.cs** cambia:

```csharp
// PRIMA (single-user)
var userSmtpSettingsFile = Path.Combine(appDataDir, "smtp.settings.json");
var userBackupSettingsFile = Path.Combine(appDataDir, "backup.settings.json");

// DOPO (multi-utente)
var current = /* CurrentProfile scelto in boot flow */;
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile(current.SmtpSettingsPath, optional: true, reloadOnChange: true)
    .AddJsonFile(current.BackupSettingsPath, optional: true, reloadOnChange: true);
```

### 7.2 SMTP credentials (`smtp.protected`)

**Decisione da confermare (§14 punto A)**: DPAPI-encrypted password
è per-profilo o condivisa?

- **Opzione A** (proposta): per-profilo. `smtp.protected` dentro la
  cartella profilo. Ogni profilo può avere il suo account SMTP.
- Opzione B: globale, un solo `smtp.protected` in
  `%LOCALAPPDATA%\MedReminder\`. Tutti i profili condividono l'account
  di invio.

**Consiglio: A.** È più simmetrico con `smtp.settings.json` (già
per-profilo). Se l'utente vuole un solo account per tutti, imposta
lo stesso host/user/password in ogni profilo — poco lavoro visto che
sono al massimo 5 profili.

### 7.3 Auto-start Windows

**Decisione da confermare (§14 punto B)**: la voce di auto-start
`HKCU\...\Run` è per-profilo o unica?

- **Opzione A** (proposta): unica. Al login parte l'ultimo profilo
  attivo (via `ActiveProfileIdHint`).
- Opzione B: possibilità di registrare N shortcut auto-start, uno
  per profilo, con `--profile <id> --minimized`.

**Consiglio: A.** Windows Startup con più istanze della stessa app
in tray genererebbe confusione (5 icone MedReminder in tray).

---

## 8. PIN opzionale

### 8.1 Cosa E

- Ogni profilo può opzionalmente avere un PIN 4-8 caratteri numerici.
- All'avvio, se il profilo scelto ha PIN, viene richiesto prima del
  boot dell'IHost.
- Al fallimento (3 tentativi errati): dialog "PIN errato" e uscita
  dall'app (o ritorno al picker se ci sono altri profili).

### 8.2 Cosa NON è

**Il PIN è friction, non security.** Vive scritto (hashato) accanto
al DB nello stesso disco dell'utente Windows. Chiunque abbia accesso
al filesystem può bypassarlo aprendo direttamente il file `.db` (che
NON è cifrato — è SQLite in chiaro).

Il tooltip nella UI dev'essere esplicito: *"Il PIN evita cambi
accidentali tra profili. Non protegge il contenuto: chi ha accesso
al PC può leggere il database senza PIN."*

Alternative reali:
- **SQLite encryption** (SQLCipher, .NET Encrypted): richiederebbe
  un pacchetto commerciale o SQLCipher OSS; non lo affrontiamo.
- **BitLocker/EFS**: responsabilità dell'utente/OS, non dell'app.

### 8.3 Storage hash

- Algoritmo: **PBKDF2** con HMAC-SHA256, 100_000 iterazioni.
- Salt: 16 byte da `RandomNumberGenerator.Fill`.
- Salvato base64 nel registry: `PinHash`, `PinSalt`, `PinIterations`.
- `IProfileRegistry.SetPin(id, pin=null)` clear il PIN.

Nessun rate limit persistente (l'attaccante con accesso al registry
può resettare tutto lo stato): il rate limit è solo in-memory nella
sessione UI.

---

## 9. Notifiche multiprofilo

### 9.1 Ambito

**Solo il profilo attivo emette notifiche** (toast Windows + email).

Motivazione:
- Il monitor scheduler gira dentro l'IHost del profilo attivo. Un
  singolo processo, un singolo IHost.
- Monitorare N profili in parallelo dallo stesso processo
  richiederebbe N contesti EF Core, N schedulazioni, N canali email.
  Complessità gratuita: chi ha bisogno di monitoraggio 24/7 di più
  profili tiene l'app aperta sul profilo giusto o cambia.

### 9.2 Fallback: notifica "profilo inattivo"

**Idea aggiuntiva**: al boot, se profili diversi da quello attivo
hanno un backup file datato > 2 giorni fa, mostrare un badge sul
menu `File → Cambia profilo` come segnale visivo. NON emettere
toast/email dai profili inattivi. Da valutare in un ulteriore
incremento — non blocca il 15.

---

## 10. Auto-start e single-instance

### 10.1 Mutex

Il mutex `Local\MedReminder.SingleInstance.<guid>` resta **unico
per macchina + utente Windows**, indipendente dal profilo.
Motivazione: due istanze dello stesso processo che accedono a due
DB diversi va bene tecnicamente, ma:
- Duplica le icone in tray.
- Duplica gli hosted service.
- Confonde l'utente ("dove sono? con che profilo?").

Un profilo alla volta, punto.

### 10.2 Restart post-switch

`IApplicationRestarter.RestartAndExit()` rilascia il mutex, il nuovo
processo lo acquisisce con lo spin già introdotto (Incremento 11).

---

## 11. Backup automatico

### 11.1 Comportamento invariato

`AutomaticBackupHostedService` gira nel processo attivo → backuppa
il DB del profilo attivo. Retention lavora sulla directory del
profilo. Zero cambiamenti al servizio; cambia solo che:

- `IBackupService.DatabasePath` → `ICurrentProfile.DatabasePath`.
- `BackupStateStore` scrive in `ICurrentProfile.BackupStatePath`.

### 11.2 Profili "orfani" senza backup automatico

Se l'utente non ha avviato il profilo Nonna da 3 mesi, il backup
non è girato. È una conseguenza consapevole di §9.

Documentato nel USER_GUIDE: *"Il backup automatico funziona solo
per il profilo attivo. Se hai profili che usi raramente, ricordati
di attivarli periodicamente o esporta manualmente il backup."*

---

## 12. UI

### 12.1 Nuovi elementi

- **ProfilePickerForm** (nuova). Mostrata al boot quando >1 profilo.
  Lista dei profili con nome + data ultimo uso. Bottoni: Apri,
  Crea nuovo, Modifica, Elimina, Esci.
- **PinPromptForm** (nuova). Piccola dialog con TextBox `.
  UseSystemPasswordChar = true`, contatore tentativi.
- **ProfilesManagerForm** (nuova). Aperta da `File → Gestisci
  profili…`. CRUD dei profili senza uscire dall'app (crea, rinomina,
  elimina, cambia PIN).
- **`File → Cambia profilo…`** nuova voce di menu.
- **StatusStrip**: aggiunta label "Profilo: Nonna" a sinistra
  (o come tooltip sull'icona tray).
- **Title bar**: `MedReminder — Nonna` (nome profilo appeso).

### 12.2 First-run wizard

Quando la lista profili è vuota (installazione pulita, no migration):

1. Dialog "Benvenuto! Crea il primo profilo per iniziare."
2. Input: nome (obbligatorio, es. "Mario Rossi").
3. Opzionale: imposta PIN adesso o dopo.
4. Crea il profilo, salta il picker, entra direttamente.

---

## 13. Rischi e mitigazioni

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| Migration V1→V2 corrompe il DB | Bassa | Alto (perdita dati) | Backup preventivo obbligatorio (§5.2 step 1); rollback su errore (§5.2 step 6); test integration (§5.4) |
| Utente elimina un profilo per sbaglio | Media | Alto | Doppia conferma "Digita il nome del profilo per confermare"; opzione "elimina anche i dati su disco" default OFF (data preservata su disco anche dopo rimozione da registry) |
| PIN dimenticato | Media | Basso-Medio | Recovery: dal registry si può rimuovere manualmente `PinHash`. Documentato nel USER_GUIDE. Non è security, quindi il recovery non è un bug. |
| Auto-start apre il profilo sbagliato | Bassa | Medio | `ActiveProfileIdHint` è aggiornato ad ogni switch e ad ogni chiusura pulita dell'app |
| Race condition tra picker e altre istanze | Molto bassa | Basso | Mutex acquisizione già presente + spin di 5s (Incremento 11) |
| Restore di un backup nel profilo sbagliato | Media | Alto (mix dati tra profili) | Il file `medreminder-YYYYMMDD-HHmmss.db` non contiene metadata del profilo. Al restore, dialog conferma: "Verranno sovrascritti i dati del profilo Nonna. Continuare?" |

---

## 14. Decisioni ancora da confermare

Sono le poche zone di ambiguità residua. Rispondi ai punti sotto
prima di partire con l'implementazione.

**A. SMTP credenziali per-profilo o condivise?**
Proposta: per-profilo (§7.2). Alternativa: condivise (un solo
account SMTP per tutti i profili).
→ Consiglio: per-profilo, coerente con `smtp.settings.json`.

**B. Auto-start Windows: unico o multi-profilo?**
Proposta: unico, parte l'ultimo profilo (§7.3). Alternativa:
possibilità di registrare N voci auto-start.
→ Consiglio: unico. La UI resta pulita.

**C. Comportamento del PIN alla `File → Cambia profilo`?**
- **Opzione 1**: PIN richiesto solo se il profilo destinazione ne
  ha uno.
- **Opzione 2**: PIN richiesto anche per uscire dal profilo
  corrente se ne ha uno (previene "esco al volo dal profilo
  protetto e apro un altro").
→ Consiglio: **Opzione 1**. Il PIN è per aprire, non per chiudere.

**D. Cosa mostrare nel picker come "ultimo utilizzo"?**
- Data esatta ("25/09/2026 09:12")
- Data relativa ("3 giorni fa")
- Nessuna data (semplifica UI)
→ Consiglio: data esatta con formato breve `dd/MM HH:mm`,
distingue profili usati oggi da quelli di settimane fa senza
scomodare relativi ("3 giorni fa" è ambiguo con timezone).

**E. First-run wizard obbligatorio o skippabile?**
- Il wizard chiede almeno il nome. Non permettere skip: senza
  profilo l'app non ha dove mettere i dati.
→ Consiglio: obbligatorio, ma "PIN adesso o dopo" resta skippabile.

**F. Backup vecchi in `%LOCALAPPDATA%\MedReminder\backups\
pre-migration-...\` — retention?**
Sono dati critici (l'unico safety net contro migration bugs).
- **Opzione 1**: cancellati manualmente dall'utente.
- **Opzione 2**: cancellati automaticamente dopo N giorni (es. 90)
  dal migrator al boot successivo.
→ Consiglio: **Opzione 1**. Non toccare quei file finché l'utente
non conferma esplicitamente "va tutto bene, cancella".

---

## 15. Piano di implementazione

Cinque sotto-increment sequenziali, ognuno auto-contenuto e
committabile. Ogni sotto-increment lascia l'app **funzionante**
(niente branch death-march).

### 15a — Profile registry + astrazione ICurrentProfile

- Nuovi tipi: `Profile`, `IProfileRegistry`, `ProfileRegistry`,
  `ICurrentProfile`, `CurrentProfile`.
- `AppDataPaths` refactor: rimossi i metodi per-profilo, aggiunti
  helper per `profiles.json` e `profiles/` root.
- Registry solo scrittura file — nessuna integrazione con Program.
  cs ancora.
- Test unit: create/rename/delete, salvataggio atomico, PIN
  set/verify.

**Deliverable:** codice, ma app ancora single-user (registry non
usato). Verifica: `dotnet test`.

### 15b — Migration V1→V2

- `MigrationV1toV2` class in Infrastructure.
- Test integration su directory temporanea.
- Ancora niente boot flow: nessuno la invoca a parte i test.

**Deliverable:** migrator testato ma dormant.

### 15c — Boot flow + ProfilePickerForm + first-run wizard

- `Program.Main` refactored: migrator invocation, picker, wizard,
  scelta profilo, `BuildHost(currentProfile)`.
- `ICurrentProfile` passata come `Singleton` al container DI.
- `AddMedReminderInfrastructure` accetta `ICurrentProfile` e usa i
  path da lì per la connection string SQLite e i JSON di configurazione.
- Tutti i punti che usavano `AppDataPaths.GetDatabasePath` /
  `smtp.settings.json` / `backup.settings.json` / `backup.state.
  json` refactored per iniettare `ICurrentProfile`.

**Deliverable:** multi-utente funzionante end-to-end. Test manuale:
avviare, crea profilo A, aggiungi medicina, cambia profilo B, la
lista è vuota (isolamento verificato).

### 15d — ProfilesManagerForm + `File → Cambia profilo` + `File → Gestisci profili`

- CRUD profili senza uscire dall'app (create, rename, delete con
  doppia conferma, PIN change).
- Switch profilo via `IApplicationRestarter` (riuso Incremento 11).
- StatusStrip mostra nome profilo attivo.

**Deliverable:** UX completa. L'utente può gestire i profili tutto
da dentro l'app.

### 15e — PIN + polish

- PinPromptForm.
- 3-tentativi in-memory.
- Tooltip esplicativi ("il PIN è friction, non security").
- Aggiornamenti a `USER_GUIDE.md` con la sezione multi-profilo.

**Deliverable:** feature completa e documentata.

---

## 16. Effort stimato

Come detto in linea di massima nel piano di evoluzione originale:
**L (grande)**. Nel dettaglio, per sotto-increment:

| Increment | Files nuovi | Files modificati | Effort |
|---|---|---|---|
| 15a | ~5 | ~1 (AppDataPaths) | S/M |
| 15b | 1 | 0 | S |
| 15c | 0 | ~10 | M/L |
| 15d | ~3 | ~2 | M |
| 15e | ~2 | ~3 | S |

Con la separazione in sotto-increment, ogni push è piccolo e
testabile. Se qualcosa va storto in 15c (il più grosso), il diff è
comunque limitato al composition root + refactor DI.

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

### 1.1a Ruoli: admin vs user

Introdotti come da tua indicazione (§14 punto A). Due livelli:

- **admin**: gestisce le impostazioni **globali** dell'app (SMTP,
  cartella e orario del backup automatico, gestione dei profili —
  crea/rinomina/elimina/PIN); ha anche pieno accesso al DB del suo
  profilo come qualsiasi user.
- **user**: gestisce solo il proprio profilo (medicine, scorte,
  terapie, scheda medico, destinatario email personale). NON vede
  la tab Email SMTP nelle Impostazioni, NON vede "Gestisci profili".

**Regole invariabili**:
- Deve sempre esistere **almeno un profilo admin**. L'ultimo admin
  del sistema non è cancellabile né declassabile a user (regola di
  integrità).
- Il **primo profilo** creato dal first-run wizard è **admin per
  default**, senza scelta esplicita — è l'unico che può creare gli
  altri.
- Il ruolo di un profilo è **immutabile dopo la creazione** (in
  questo incremento). Per cambiare ruolo servirebbe un flusso di
  "promuovi/degrada" che aggiunge complessità: chi promuove deve
  essere admin, che se lo declassa perde il diritto a rifarlo se
  è l'unico admin, ecc. Rinviato a un incremento successivo se
  emerge la necessità.

Il ruolo è **soft security** come il PIN — un user con accesso
filesystem può editare `profiles.json` a mano e diventare admin.
La UI lo rispetta, il filesystem no. Tooltip esplicito nella UI
di gestione profili.

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

- **Un admin** crea/rinomina/rimuove profili (utenti) dalla UI.
- Ogni profilo ha il **proprio database** e il **proprio destinatario
  email**; le altre impostazioni (SMTP server, cartella backup,
  orario backup, retention) sono **globali gestite dall'admin**.
- Scelta del profilo all'avvio quando ce n'è più di uno.
- Cambio profilo a runtime senza chiudere manualmente l'app.
- Migrazione automatica e senza perdita dati dall'attuale schema
  single-user (`medreminder.db` in `%LOCALAPPDATA%\MedReminder\`).
- Il profilo migrato dal V1 diventa **admin** (è l'unico esistente).

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
      "Role": "admin",
      "CreatedAt": "2026-09-16T09:00:00Z",
      "LastUsedAt": "2026-09-16T18:30:00Z",
      "PinHash": null,
      "PinSalt": null,
      "PinIterations": 0
    },
    {
      "Id": "3a8c…",
      "DisplayName": "Nonna",
      "Role": "user",
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
- `Role`: `"admin"` o `"user"`. Immutabile dopo la creazione (§1.1a).
  Deserializzazione tollerante: valori sconosciuti → `"user"` (fail-
  safe: mai promuovere per errore).
- `ActiveProfileId`: opzionale hint sull'ultimo profilo caricato,
  usato per l'auto-start (§10). Non è "il" profilo attivo di runtime.
- Il registry vive **fuori** dai profili — è metadata dell'app, non
  di un profilo specifico.
- Scritture atomiche: tmp + File.Move, come già fatto per
  `backup.state.json`.
- Invariante applicata a ogni write: **almeno un profilo con
  `Role == "admin"`**. Il `ProfileRegistry.Delete()` rifiuta se
  cancella l'ultimo admin.

### 2.3 Astrazione lato codice

Nuova interfaccia in `MedReminder.Application.Abstractions`:

```csharp
public enum ProfileRole { User, Admin }

public sealed record Profile(
    string Id,
    string DisplayName,
    ProfileRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool HasPin);

public interface IProfileRegistry
{
    IReadOnlyList<Profile> ListProfiles();
    Profile? GetById(string id);
    string? ActiveProfileIdHint { get; }

    // Il ruolo è deciso alla creazione. Il primo profilo del sistema
    // (registry vuoto) è forzato ad admin — Create ignora il parametro
    // role in quel caso e ritorna un profilo admin. Successivamente,
    // solo un caller con ruolo admin può creare admin (verificato più
    // in alto, non qui — questo layer non conosce il "chi chiama").
    Profile Create(string displayName, ProfileRole role);

    void Rename(string id, string newDisplayName);

    // Rifiuta con InvalidOperationException se id è l'ultimo admin
    // del registry (invariante §2.2). Con deleteData=true rimuove
    // anche la cartella <root>\profiles\<id>\.
    void Delete(string id, bool deleteData);

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
    ProfileRole Role { get; }
    bool IsAdmin => Role == ProfileRole.Admin;

    string DataDirectory { get; }             // %LOCALAPPDATA%\MedReminder\profiles\<id>\
    string DatabasePath { get; }              // <DataDirectory>\medreminder.db
    string NotificationSettingsPath { get; }  // <DataDirectory>\notifications.settings.json (destinatario per-profilo)
}
```

Registrato come `Singleton` nel container DI dopo che l'utente ha
scelto il profilo. Ogni servizio che oggi hardcoda `AppDataPaths.
GetDatabasePath()` va aggiornato per iniettare `ICurrentProfile`.

**Path globali** (non per-profilo, admin-managed):
- `smtp.settings.json`, `smtp.protected` → `%LOCALAPPDATA%\MedReminder\`
- `backup.settings.json`, `backup.state.json` → `%LOCALAPPDATA%\MedReminder\`

Restano esposti da `AppDataPaths` come metodi statici (invariante:
un unico path per macchina + utente Windows). La UI che vi accede
gate-a su `ICurrentProfile.IsAdmin` (§12).

### 2.5 Modifiche a `AppDataPaths`

L'attuale classe statica in `MedReminder.Infrastructure.Storage`
espone metodi hardcoded (`GetDatabasePath`, `GetCredentialsPath`,
ecc.). Va rifattorizzata:

- Metodi che restituiscono **path a livello app** (log dir, SMTP
  credentials DPAPI, `smtp.settings.json`, `backup.settings.json`,
  `backup.state.json`, `profiles.json`) → **restano statici** (path
  globali admin-managed).
- Metodo `GetDatabasePath()` → **rimosso**; sostituito da
  `ICurrentProfile.DatabasePath`.
- Nuovo `NotificationSettingsPath` come membro di `ICurrentProfile`
  (l'unica cosa email per-profilo è il destinatario, §7.1).

Refactor invasivo ma meccanico. Impatto per file:
- `MedReminderDbContext` (connection string): via `ICurrentProfile`.
- `BackupService.DatabasePath`: via `ICurrentProfile`.
- `MailKitEmailNotificationService`: legge `ToAddress` da un nuovo
  `IProfileNotificationSettings` invece che da `SmtpSettings.
  ToAddress` (che non esiste più — vedi §7.1).

---

## 3. Layout su disco

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                    ← registry (admin-managed)
├── smtp.settings.json               ← SMTP GLOBALE (admin-managed)
├── smtp.protected                   ← password DPAPI GLOBALE
├── backup.settings.json             ← config backup GLOBALE (admin-managed)
├── backup.state.json                ← stato ultimo backup GLOBALE
├── logs\
│   ├── medreminder-20260916.log
│   └── …
└── profiles\
    ├── default\                     ← admin (esempio dopo migration V1→V2)
    │   ├── medreminder.db
    │   ├── medreminder.db-wal
    │   ├── medreminder.db-shm
    │   └── notifications.settings.json    ← "ToAddress" del profilo
    └── 3a8c…\                       ← user
        ├── medreminder.db
        └── notifications.settings.json
```

**Ragionamento**:

- **SMTP globale**: un unico account server SMTP configurato
  dall'admin (Gmail App Password unica). Tutti i profili spediscono
  attraverso lo stesso server con lo stesso mittente ma con
  destinatario diverso (§7.1).
- **Backup globale**: cartella e orario decisi dall'admin. Il
  backup automatico giornaliero backuppa **tutti** i DB dei profili
  in un colpo solo (§11).
- **Notifiche per-profilo**: `notifications.settings.json` contiene
  soltanto `ToAddress` (chi riceve le email di questo profilo). È
  la sola cosa che l'utente user può cambiare senza essere admin.
- I file `.log` restano centralizzati.
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
4. `smtp.settings.json`, `smtp.protected`, `backup.settings.json`,
   `backup.state.json`: **NON spostati**. Erano già a livello app
   nel V1 (`%LOCALAPPDATA%\MedReminder\`) e restano tali nel V2 —
   sono impostazioni globali. Nessuna azione da eseguire.
5. Se serve estrarre "ToAddress" dal vecchio `smtp.settings.json`
   e trasferirlo nel `notifications.settings.json` per-profilo:
   - Legge il vecchio JSON, se `Smtp.ToAddress` è valorizzato,
     scrive `profiles\default\notifications.settings.json` con
     `{ "Notifications": { "ToAddress": "<...>" } }`.
   - Rimuove `ToAddress` dal vecchio JSON riscrivendolo (le altre
     chiavi restano; se sono presenti in appsettings.json non
     comportano problemi).
6. Scrive `profiles.json` con un solo entry `default`, `DisplayName`
   = "Utente" (rinominabile subito dal Settings), `Role: "admin"`
   (§1.1a), `ActiveProfileId` = "default".
7. Se qualsiasi passo dai 2 al 6 fallisce: **rollback** — ripristina
   `smtp.settings.json` dal backup preventivo, sposta indietro il
   DB, cancella la cartella `profiles/default` semi-creata, cancella
   `profiles.json`. L'utente riparte al prossimo boot con lo stato
   pre-migrazione intatto.

### 5.3 DPAPI

`smtp.protected` resta a livello app: stessa `DataProtectionScope.
CurrentUser`, nulla da toccare a livello crypto. Nessuna
regressione.

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

## 7. Settings — globali vs per-profilo

### 7.1 SMTP: globale (admin) + destinatario per-profilo

**Split del vecchio `SmtpSettings`:**

- `SmtpSettings` (globale in `%LOCALAPPDATA%\MedReminder\smtp.settings.
  json`, admin-managed): `Host`, `Port`, `UseStartTls`, `Username`,
  `FromAddress`, `FromDisplayName`, `TimeoutSeconds`. **NON contiene
  più `ToAddress`.**
- `NotificationSettings` (per-profilo in `<DataDirectory>\
  notifications.settings.json`): `ToAddress`. Nuovo POCO,
  legato via `IOptionsMonitor<NotificationSettings>`.
- `smtp.protected` (DPAPI, globale in `%LOCALAPPDATA%\MedReminder\`):
  password/App Password del `Username` — unica per tutti i profili.

**Flusso invio email**:
1. `MailKitEmailNotificationService` prende `SmtpSettings` (globali,
   `IOptionsMonitor<SmtpSettings>`) e `NotificationSettings` (per-
   profilo, `IOptionsMonitor<NotificationSettings>`).
2. Costruisce l'email `From` da `FromAddress`, `To` da `ToAddress`
   del profilo attivo.
3. Autentica con `Username` + DPAPI password globali.

**Program.cs** cambia:

```csharp
var appDataDir = AppDataPaths.GetAppDataDirectory();
var current = /* CurrentProfile scelto in boot flow */;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    // Globali admin-managed:
    .AddJsonFile(Path.Combine(appDataDir, "smtp.settings.json"),
                 optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(appDataDir, "backup.settings.json"),
                 optional: true, reloadOnChange: true)
    // Per-profilo (solo il destinatario):
    .AddJsonFile(current.NotificationSettingsPath,
                 optional: true, reloadOnChange: true);
```

### 7.2 Backup: globale (admin)

`backup.settings.json` e `backup.state.json` restano a livello app.
`AutomaticBackupHostedService` legge la config globale e backuppa
**tutti i DB dei profili** ad ogni tick riuscito (§11).

L'admin nella SettingsDialog imposta una sola cartella e un solo
orario, validi per tutti.

### 7.3 Auto-start Windows

**Unico** (come da tua indicazione §14 punto B). La voce
`HKCU\...\Run` è unica per macchina + utente Windows. Al login parte
il profilo `ActiveProfileIdHint`.

### 7.4 Gating admin/user nella SettingsDialog

La tab **Email SMTP**: visibile solo se `ICurrentProfile.IsAdmin`.
User non-admin vedrà solo la voce (nuova) "**Notifiche**" con un
singolo campo `ToAddress` — sufficiente per personalizzare dove
ricevere le proprie email senza toccare il server SMTP condiviso.

La tab **Backup**: visibile solo se `IsAdmin`. User non-admin non
vede backup (né "Esegui adesso" — il backup globale include il suo
DB comunque).

La tab **Avvio automatico**: visibile per tutti, ma per user il
comportamento resta invariato (si abilita/disabilita il flag Windows
`Run` — che è per-account Windows, non per-profilo).

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

### 11.1 Backup GLOBALE che copre tutti i profili

**Modifica sostanziale** vs Incremento 11: `AutomaticBackupHostedService`
non backuppa più solo il DB del profilo attivo — backuppa **tutti**
i DB del sistema in un singolo tick riuscito.

Al tick:
1. Legge `IProfileRegistry.ListProfiles()` per ottenere l'elenco.
2. Per ogni profilo, verifica che il file `medreminder.db` esista
   nella cartella del profilo.
3. Chiama `IBackupService.ExportProfileAsync(profileId, cartella
   globale, ct)` per ognuno.
4. Naming file: `medreminder-<profileId>-YYYYMMDD-HHmmss.db` per
   distinguere i backup dei diversi profili nella stessa cartella.
   Con `profileId == "default"` diventa `medreminder-default-...`.
5. Retention applicata **per profilo** all'interno della stessa
   cartella: il regex del PruneOldBackupsAsync viene esteso per
   catturare il profileId e il filtro "vecchi di N giorni" viene
   applicato ai file di **quel** profileId separatamente. Non
   accade che il backup più recente del profilo A "protegga" i
   vecchi del profilo B.

### 11.2 Impatto su `BackupService`

`IBackupService` cambia signature:

```csharp
// PRIMA
Task<string> ExportAsync(string destinationDirectory, CancellationToken ct);
Task<int> PruneOldBackupsAsync(string dir, int retentionDays, CancellationToken ct);
Task ImportAsync(string sourceFilePath, CancellationToken ct);

// DOPO
Task<string> ExportProfileAsync(string profileId, string destinationDirectory, CancellationToken ct);
Task<int> PruneOldBackupsAsync(string dir, int retentionDays, CancellationToken ct);
Task ImportProfileAsync(string profileId, string sourceFilePath, CancellationToken ct);
```

`ExportProfileAsync` apre `SqliteConnection` sul DB del profilo
specifico usando il path `<root>\profiles\<profileId>\medreminder.
db`. NON usa `ICurrentProfile` — deve poter backuppare anche i
profili inattivi.

`ImportProfileAsync` richiede `profileId` per sapere quale DB
sostituire. Se il profilo importato è quello attivo, va comunque
seguito da `RestartAndExit` (SQLite lock su DB corrente).

### 11.3 Comportamento del restore da UI

Nel `SettingsDialog` (admin only, tab Backup) l'azione "Ripristina
backup…":
1. FileDialog per scegliere il .db.
2. Dropdown "Ripristina nel profilo…" con la lista dei profili
   (default = quello attivo).
3. Doppia conferma.
4. `ImportProfileAsync(scelto)` + `RestartAndExit` se scelto ==
   attivo, altrimenti solo confirm dialog "Fatto".

Per il user non-admin: la voce non appare (§7.4).

### 11.4 Vantaggio del backup globale

Il caso "profilo Nonna non avviato da 3 mesi" (sezione originale)
**non è più un problema**: il backup globale gira nel processo
dell'admin (o di qualsiasi profilo attivo) e copre anche i profili
inattivi. Un solo `AutomaticBackupHostedService` per macchina fa
il lavoro di tutti.

**Con un caveat**: se l'admin non avvia mai l'app e solo lo user
la avvia, chi backuppa? Risposta: **anche il processo di uno user
attiva il backup globale**. Il gating "solo admin vede la tab
Backup" è UI: non impedisce che il servizio backend giri sotto.
Il backup automatico è per tutti, non per admin.

---

## 12. UI

### 12.1 Nuovi elementi

- **ProfilePickerForm** (nuova). Mostrata al boot quando >1 profilo.
  Lista dei profili con nome + badge ruolo (admin/user) + data
  ultimo uso `dd/MM HH:mm`. Bottone unico "Apri" — gestione
  profili è dentro l'app.
- **PinPromptForm** (nuova). Piccola dialog con TextBox
  `UseSystemPasswordChar = true`, contatore tentativi in-memory.
- **ProfilesManagerForm** (nuova, **admin only**). Aperta da
  `Strumenti → Gestisci profili…`. CRUD dei profili (crea come
  user o admin, rinomina, elimina con doppia conferma, imposta/
  cambia/rimuove PIN). Rifiuta la delete dell'ultimo admin (§2.2).
- **`File → Cambia profilo…`** (**tutti**): apre ProfilePickerForm.
- **`Strumenti → Gestisci profili…`** (**solo admin**, hidden per
  user).
- **StatusStrip**: aggiunta label "Profilo: Nonna (admin)" a
  sinistra. Il badge admin è distintivo.
- **Title bar**: `MedReminder — Nonna` (nome profilo appeso, senza
  badge).

### 12.2 Gating dei menu/toolbar in base al ruolo

Da rifattorizzare la costruzione di MenuStrip in MainForm per
prendere in input `ICurrentProfile`:

| Voce menu | admin | user |
|---|---|---|
| File → Cambia profilo… | ✔ | ✔ |
| File → Esci | ✔ | ✔ |
| Terapia → * | ✔ | ✔ |
| Scorte → * | ✔ | ✔ |
| Strumenti → Controlla ora | ✔ | ✔ |
| Strumenti → Gestisci profili… | ✔ | ✘ (nascosto) |
| Strumenti → Impostazioni… | ✔ (tutte le tab) | ✔ (solo Notifiche + Avvio) |
| ? → * | ✔ | ✔ |

La toolbar quick-access non cambia — sono azioni "on the medicine",
tutti i ruoli le hanno.

### 12.3 First-run wizard

Quando la lista profili è vuota (installazione pulita, no migration):

1. Dialog "Benvenuto! Crea il primo profilo (amministratore)."
2. Input: nome (obbligatorio, es. "Mario Rossi").
3. Testo esplicativo: "Il primo profilo è l'amministratore. Potrà
   creare altri profili utente e gestire le impostazioni SMTP e
   Backup globali. Il ruolo non è modificabile dopo la creazione."
4. Opzionale: imposta PIN adesso o dopo (raccomandato per l'admin,
   con avviso testuale non forzato).
5. Crea il profilo con `Role = admin`, salta il picker, entra
   direttamente.

Coordina con §14 punto E — il wizard è obbligatorio (non si può
saltare senza creare almeno il profilo admin), ma la scelta del
PIN al suo interno resta skippabile.

### 12.4 Creazione profili aggiuntivi (admin only)

Dentro ProfilesManagerForm:
1. Bottone "Nuovo profilo".
2. Dialog input: nome + radio "admin"/"user" (default user).
3. Testo esplicativo sul ruolo — "user vede solo le medicine, non
   può cambiare SMTP/Backup".
4. Il PIN si imposta subito dopo o si lascia vuoto.

### 12.5 Feedback visivo

Nella UI del user non-admin, aggiungere una nota permanente in
fondo alla SettingsDialog (tab Notifiche):
*"Per modificare l'account SMTP o la configurazione backup, chiedi
all'amministratore del profilo."*

---

## 13. Rischi e mitigazioni

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| Migration V1→V2 corrompe il DB | Bassa | Alto (perdita dati) | Backup preventivo obbligatorio (§5.2 step 1); rollback su errore (§5.2 step 7); test integration (§5.4) |
| Utente elimina un profilo per sbaglio | Media | Alto | Doppia conferma "Digita il nome del profilo per confermare"; opzione "elimina anche i dati su disco" default OFF (data preservata su disco anche dopo rimozione da registry) |
| Ultimo admin cancellato | Bassa | Molto alto (perdita accesso funzioni globali) | `IProfileRegistry.Delete` rifiuta con eccezione se l'entry è l'unico admin. UI mostra il bottone Elimina disabilitato con tooltip esplicativo. |
| PIN dimenticato | Media | Basso-Medio | Recovery: dal registry si può rimuovere manualmente `PinHash`. Documentato nel USER_GUIDE. Non è security, quindi il recovery non è un bug. |
| Admin senza PIN → user può auto-promuoversi | Media | Medio (perdita gating UI) | Il ruolo è immutabile via API. L'auto-promozione richiede edit manuale di `profiles.json`, come detto in §1.1a. UI raccomanda "imposta un PIN per l'admin" nel wizard first-run. |
| Auto-start apre il profilo sbagliato | Bassa | Medio | `ActiveProfileIdHint` è aggiornato ad ogni switch e ad ogni chiusura pulita dell'app |
| Race condition tra picker e altre istanze | Molto bassa | Basso | Mutex acquisizione già presente + spin di 5s (Incremento 11) |
| Restore di un backup nel profilo sbagliato | Media | Alto (mix dati tra profili) | Il nome del file `medreminder-<profileId>-YYYYMMDD-HHmmss.db` include il profileId originario (§11.1); il restore dialog offre la scelta del profilo di destinazione con default = quello del filename; doppia conferma. |
| Backup globale gira anche quando user è attivo | Certa | Basso (nessuno; è desiderabile) | Documentato: il servizio è per-macchina, non per-ruolo. |

---

## 14. Decisioni confermate (dopo revisione utente)

**A. SMTP: server e credenziali GLOBALI, destinatario per-profilo.**
Un admin configura una volta l'account SMTP; ogni profilo può
personalizzare il proprio `ToAddress`. Introdotto ruolo admin/user
(§1.1a). Impatto: split `SmtpSettings` (globale) +
`NotificationSettings` (per-profilo, contiene solo `ToAddress`).

**B. Auto-start Windows: unico.** Parte `ActiveProfileIdHint`.

**C. PIN alla `Cambia profilo`: solo in ingresso.** Nessun PIN
richiesto per uscire dal profilo corrente.

**D. Formato "ultimo utilizzo" nel picker: `dd/MM HH:mm`.**

**E. First-run wizard obbligatorio.** Non skippabile perché senza
profilo non c'è nulla da mostrare. Il primo profilo è **admin per
default** (§1.1a) — coerente con il modello admin/user. PIN al
suo interno resta skippabile (con avviso "raccomandato per admin").

**F. Backup pre-migration: cancellazione manuale.** Il migrator
non tocca mai `%LOCALAPPDATA%\MedReminder\backups\pre-migration-*\`.
Documentato nel USER_GUIDE.

## 14a. Nuove decisioni aperte (introdotte dal ruolo admin)

Devo chiederti conferma su questi punti prima di partire — sono
tutti direttamente conseguenti alla scelta A.

**G. Immutabilità del ruolo dopo la creazione?**
Ho proposto: **sì, immutabile** (§1.1a). Alternativa: permettere
promote/demote da parte di un admin, con salvaguardia "l'ultimo
admin non può auto-degradarsi".
→ Consiglio: immutabile per Incremento 15. Se emerge la necessità,
si aggiunge in un incremento successivo.

**H. Cosa succede se il DB del profilo attivo contiene medicine ma
il profilo viene eliminato dall'admin?**
- **Opzione 1** (proposta): eliminazione ammessa solo per profili
  diversi da "attualmente caricato in memoria". Se l'admin vuole
  eliminare il proprio profilo, cambio profilo prima e poi elimino.
- **Opzione 2**: eliminazione ammessa sempre, con restart automatico
  post-elimina se era l'attivo.
→ Consiglio: **Opzione 1**. Più semplice, meno magic.

**I. L'admin può registrare l'assunzione di medicine di uno user
non-admin dal proprio profilo?**
Cioè: dall'interno del suo profilo, l'admin vede solo le SUE
medicine, o può switchare vista?
- **Opzione 1** (proposta): l'admin vede solo il suo profilo. Per
  registrare un'assunzione su un altro utente, deve cambiare
  profilo.
- **Opzione 2**: l'admin ha una "vista consolidata" o può switchare
  contesto senza restart.
→ Consiglio: **Opzione 1**. La 2 richiederebbe di ripensare l'IHost
per essere multi-DB, contraddice tutto il design fino a qui.

**J. Password DPAPI: se l'admin cambia la password SMTP, gli user
la vedono cambiare immediatamente?**
`smtp.protected` è globale, `SmtpCredentialStore` è singleton in
DI. Ma `IOptionsMonitor<SmtpSettings>` legge `smtp.settings.json`
con reloadOnChange=true — l'admin modifica il file, il monitor
notifica, gli user (che condividono lo stesso IHost per profilo
diverso, ma stesso appDir) ricevono l'aggiornamento.
Attenzione: **l'IHost è per-processo**. Ogni profilo attivo ha il
suo processo. Se l'admin cambia SMTP e uno user ha l'app aperta
in un'altra sessione Windows... è impossibile (mutex `Local\` è
per-session, ma stesso account Windows == stessa sessione == stesso
mutex). Non ci sono due istanze concorrenti nello stesso account.
Quindi non c'è conflitto reale.
→ **Nessuna decisione richiesta**, solo verifica che il ragionamento
sia corretto. Confermi?

**K. L'user può abilitare/disabilitare i propri canali di notifica
Windows/Email indipendentemente?**
Oggi `Medicine.NotificationChannels` è per medicina (§Domain). Non
per profilo. Cioè: nel profilo di Nonna, l'admin definisce che la
medicina "Cardioaspirina" ha email + toast; Nonna user non ha
motivo di scavalcare. È già così. Non serve gating aggiuntivo.
→ **Nessuna decisione richiesta.**

---

## 15. Piano di implementazione

Cinque sotto-increment sequenziali, ognuno auto-contenuto e
committabile. Ogni sotto-increment lascia l'app **funzionante**
(niente branch death-march). Modifiche dal design originale
evidenziate come **[+admin]**.

### 15a — Profile registry + astrazione ICurrentProfile

- Nuovi tipi: `ProfileRole` **[+admin]**, `Profile` con Role
  **[+admin]**, `IProfileRegistry`, `ProfileRegistry`,
  `ICurrentProfile` con `IsAdmin` **[+admin]**, `CurrentProfile`.
- Nuovo `NotificationSettings` (POCO con solo `ToAddress`) **[+admin]**.
- `AppDataPaths` refactor: rimosso `GetDatabasePath()` (spostato in
  `ICurrentProfile`); il resto resta statico.
- `SmtpSettings` refactor: rimosso `ToAddress` **[+admin]**.
- Registry solo scrittura file — nessuna integrazione con Program.cs
  ancora. Invariante "almeno un admin" attiva.
- Test unit: create/rename/delete, salvataggio atomico, PIN
  set/verify, ruolo immutabile, delete-ultimo-admin rifiutata.

**Deliverable:** codice, ma app ancora single-user (registry non
usato). Verifica: `dotnet test`.

### 15b — Migration V1→V2

- `MigrationV1toV2` class in Infrastructure.
- Estrazione `ToAddress` da vecchio `smtp.settings.json` verso
  nuovo `notifications.settings.json` del profilo `default`
  **[+admin]**.
- Il profilo `default` è creato con `Role = admin` **[+admin]**.
- Test integration su directory temporanea.
- Ancora niente boot flow.

**Deliverable:** migrator testato ma dormant.

### 15c — Boot flow + ProfilePickerForm + first-run wizard (admin only)

- `Program.Main` refactored: migrator invocation, picker, first-run
  wizard che crea admin **[+admin]**, scelta profilo, `BuildHost
  (currentProfile)`.
- `ICurrentProfile` passata come `Singleton` al container DI.
- `AddMedReminderInfrastructure` accetta `ICurrentProfile` e usa
  il suo `DatabasePath` per la connection string SQLite. I path
  globali (`smtp.settings.json`, `backup.settings.json`,
  `backup.state.json`) restano da `AppDataPaths` statici **[+admin]**.
- Refactor `MailKitEmailNotificationService` per usare
  `IOptionsMonitor<NotificationSettings>` per `To` **[+admin]**.
- `AutomaticBackupHostedService` refactored per backuppare **tutti**
  i profili al tick riuscito **[+admin]**.

**Deliverable:** multi-utente funzionante end-to-end con ruolo
admin/user esistente ma UI ancora non-gated.

### 15d — ProfilesManagerForm + gating admin/user della UI + backup restore multi-profile

- ProfilesManagerForm (admin only): CRUD profili con radio ruolo,
  PIN change **[+admin]**.
- `Strumenti → Gestisci profili…` (admin only, hidden per user)
  **[+admin]**.
- SettingsDialog: tab Email/Backup hidden per user; nuova tab
  Notifiche visibile a tutti **[+admin]**.
- StatusStrip mostra "Profilo: Nonna (admin)" con badge ruolo
  **[+admin]**.
- Restore backup dal SettingsDialog offre dropdown "Ripristina nel
  profilo" **[+admin]** (§11.3).
- Switch profilo via `IApplicationRestarter` (riuso Incremento 11).

**Deliverable:** UX completa admin/user.

### 15e — PIN + polish + documentazione

- PinPromptForm.
- 3-tentativi in-memory.
- Tooltip esplicativi ("il PIN è friction, non security", "il
  ruolo è immutabile", "user vede solo il proprio profilo").
- `USER_GUIDE.md` esteso con sezione multi-profilo + admin/user
  **[+admin]**.
- `ANALYSIS-MULTI-USER.md` marcato come "implementato" in fondo.

**Deliverable:** feature completa e documentata.

---

## 16. Effort stimato

Come detto in linea di massima nel piano di evoluzione originale:
**L (grande)**. L'introduzione del ruolo admin (§14 punto A) sposta
la stima **verso l'alto** dai valori iniziali:

| Increment | Files nuovi | Files modificati | Effort (aggiornato) |
|---|---|---|---|
| 15a | ~7 | ~2 (AppDataPaths + SmtpSettings) | M |
| 15b | 1 | 0 | S |
| 15c | 0 | ~13 (MailKit, backup service, program.cs) | L |
| 15d | ~4 | ~4 (SettingsDialog gating, MenuStrip gating, StatusStrip) | M/L |
| 15e | ~2 | ~3 | S |

Con la separazione in sotto-increment, ogni push è piccolo e
testabile. Se qualcosa va storto in 15c (il più grosso), il diff è
comunque limitato al composition root + refactor DI + backup service.

**Non-goal per l'incremento 15**: promote/demote di profili
(§14a punto G), vista consolidata per admin (§14a punto I). Vanno
in Incremento 15+ se emerge la necessità.

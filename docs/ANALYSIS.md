# MedReminder — Analisi tecnica e architettura

Documento prodotto in Fase 1 e Fase 2 come richiesto dalla sezione 31 delle
specifiche. Non contiene codice implementativo: serve a fissare requisiti,
scelte architetturali e piano incrementale prima di procedere.

Classificazione epistemica utilizzata: `[VERIFIED]` (fatto stabilito),
`[INFERRED]` (deduzione da fatti verificati), `[UNCERTAIN]` (dato che non
posso confermare senza test o senza chiarimento dell'utente).

---

## 1. Fase 1 — Analisi

### 1.1 Requisiti ambigui o incompleti

Vengono elencati soltanto i punti che influenzano l'architettura o il
modello dati. Per ciascuno indico la decisione proposta e il motivo. Se una
di queste decisioni non è accettabile, deve essere corretta prima di
procedere alla Fase 3.

1. **Definizione di "consumo giornaliero"**. Non è specificato se il
   consumo giornaliero sia sempre calcolato dallo schema
   (`dose × somministrazioni`) o eventualmente stimato dallo storico delle
   assunzioni.
   Decisione: per l'MVP il consumo giornaliero è **derivato dallo schema
   configurato**. Le assunzioni reali (sezione 6 delle specifiche) restano
   nel modello per una futura stima empirica, ma non influenzano il calcolo
   MVP.

2. **Somministrazioni giornaliere**. La specifica indica "numero" (un
   intero), non un elenco di orari. Decisione: intero
   `AdministrationsPerDay` in MVP; un futuro `AdministrationSchedule` con
   orari è previsto solo come estensione, non necessario ora.

3. **Soglia di avviso — singola o a più livelli**. La sezione 8 mostra
   esempi 10/7/5/3, ma non chiarisce se sono livelli multipli
   configurabili per la stessa medicina o alternative. Decisione MVP:
   **un solo valore intero di soglia (giorni)** per medicina. Il campo può
   essere esteso in futuro a un array di soglie senza rompere lo schema
   (nuova tabella `MedicineThresholds`, oppure JSON).

4. **Reset del ciclo di notifica**. La specifica dice "dopo un nuovo
   rifornimento il ciclo deve poter ripartire". Cosa conta come
   rifornimento? Decisione: **ogni movimento di stock positivo** di tipo
   `NewPackage`, `ManualAdd`, `PositiveCorrection` incrementa un contatore
   `StockEpoch` sulla medicina. Le notifiche sono legate all'epoch
   corrente; una nuova epoch riazzera automaticamente il ciclo.

5. **Sospensione temporanea**. La specifica la menziona (sezione 5) ma non
   definisce la semantica. Decisione: entità `MedicationSuspension`
   con `StartDate` e `EndDate?`. Nei periodi sospesi non viene generato
   consumo automatico; giorni residui e ETA di esaurimento non vengono
   calcolati fintanto che la medicina è sospesa (oppure sono calcolati
   ignorando i giorni sospesi, se il periodo di sospensione è chiuso nel
   passato).

6. **Data di fine terapia**. Se `EndDate` è impostata e cade prima
   dell'esaurimento stimato, ha senso avvisare per "medicina in
   esaurimento"? Decisione: sì, la specifica richiede il promemoria per la
   prescrizione indipendentemente dalla `EndDate`; ma se
   `DataEsaurimentoStimata > EndDate` allora il sistema **non genera
   avviso** (basta il residuo). Regola implementata nel dominio, testata.

7. **Fuso orario e DST**. L'applicazione è desktop personale su Windows 11.
   Decisione: **ora locale via `TimeProvider`**. Le date "logiche" (inizio
   terapia, fine, sospensioni, giorno di consumo) sono `DateOnly`. Le date
   "evento" (movimenti, notifiche, log) sono `DateTimeOffset` con offset
   locale, così da resistere a cambi di DST.

8. **UI "moderna e pulita" in WinForms**. WinForms non offre nativamente lo
   stesso look di WinUI/WPF. Decisione: nessun framework di skin di terze
   parti in MVP. Uso della segoe UI Variable, `HighDpiMode.PerMonitorV2`,
   `DataGridView` doppio-bufferizzato, colori/renderer custom sui
   `ToolStrip`, ownerdrawn dove serve. Se l'utente vuole un look Fluent
   completo va valutato in seguito un porting a WinUI3, che però la
   specifica ha esplicitamente escluso.

9. **Notifiche Windows su app "unpackaged"**. Toast interattive su Windows
   11 richiedono un AUMID e uno shortcut nello Start Menu. `[UNCERTAIN]` la
   compatibilità immediata di `CommunityToolkit.WinUI.Notifications` (o del
   suo predecessore `Microsoft.Toolkit.Uwp.Notifications`) con `net10.0`:
   funzionerà, ma la stringa TFM esatta e l'attivatore COM andranno
   verificati alla prima compilazione. Piano B: `System.Windows.Forms.NotifyIcon.ShowBalloonTip`
   (funziona sempre, meno "moderno" ma senza vincoli di packaging).
   Decisione: implementazione dietro `IWindowsNotificationService`, con
   due adapter selezionabili tramite configurazione. Il primo tentativo
   sarà toast tramite `Microsoft.Toolkit.Uwp.Notifications`, con
   fallback automatico a `NotifyIcon` se l'inizializzazione fallisce.

10. **SMTP client**. `[VERIFIED]` `System.Net.Mail.SmtpClient` è marcato
    come "obsoleted for new development" da Microsoft dal .NET 6.
    Decisione: dipendenza da **MailKit** (`MailKit`/`MimeKit`,
    mantenuti, standard di fatto). Chiuso dietro
    `IEmailNotificationService`, così il provider è sostituibile.

11. **Storage delle credenziali email**. Decisione: la password SMTP viene
    cifrata con **DPAPI** (`System.Security.Cryptography.ProtectedData`,
    scope `CurrentUser`) e salvata come blob base64 nel file di
    configurazione utente. Alternativa (Windows Credential Manager via
    `CredWrite`) più corretta a livello formale ma richiede P/Invoke o
    NuGet aggiuntivo; DPAPI è sufficiente per un'app single-user locale.

12. **Auto-start con Windows**. Decisione: chiave di registro
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (per-utente, non
    richiede privilegi elevati, reversibile). Nessun servizio Windows in
    MVP, conforme alla sezione 11.

13. **Cartella dati**. Decisione:
    `%LOCALAPPDATA%\MedReminder\` per database, log e settings. Motivo:
    `LocalAppData` è appropriato per dati locali non roaming, non richiede
    permessi speciali, non viaggia su rete come `AppData\Roaming`, e non è
    nella directory di installazione (rispetta la sezione 15).

14. **Concurrency**. Un solo processo per utente. Decisione: nessun lock
    distribuito. Al più un mutex nominato all'avvio per impedire istanze
    multiple, e SQLite in modalità WAL per resistere a chiusure improvvise.

### 1.2 Casi limite identificati

Devono essere testati nel dominio (vedi sezione 5 di questo documento):

- `dailyRate == 0` → nessuna ETA, nessun avviso automatico.
- `currentStock < 0` (correzione manuale che porta sotto zero) → clamp a 0,
  log warning, l'app non "recupera" il debito.
- Medicina sospesa → nessun consumo automatico nel periodo sospeso;
  la catch-up di consumo giornaliero salta i giorni sospesi.
- App non aperta per N giorni → catch-up del consumo giornaliero al
  successivo avvio; le notifiche eventualmente perse sono generate una
  sola volta (per epoch), non N volte.
- Cambio dose/frequenza a metà terapia → nuova entry in
  `MedicationScheduleHistory` (versionamento della schedule), il consumo
  giornaliero è ricalcolato in avanti dalla data di cambio.
- Rifornimento durante il periodo di soglia → nuovo `StockEpoch`, la
  notifica successiva è ammessa quando la nuova epoch rientra in soglia.
- Anno bisestile / cambio DST → coperti dall'uso di `DateOnly` per la
  logica di giorno e da `TimeProvider` iniettato nei test.
- Fallimento SMTP transitorio → retry con back-off limitato (max 3
  tentativi, escalating 5s/30s/2m), poi log come `NotificationEvent`
  fallita.
- Database in sola lettura o disco pieno → l'applicazione non si chiude;
  la UI mostra banner di errore, i controlli periodici continuano ma non
  possono scrivere.
- Configurazione SMTP mancante o palesemente errata → il sotto-sistema
  email è marcato "disabilitato"; le notifiche Windows continuano a
  funzionare.

### 1.3 Decisioni architetturali che richiedono conferma

Le decisioni sopra sono proposte tecniche autonome, motivate. Chiedo
conferma esplicita solo sui punti che cambiano il perimetro funzionale:

- **Q1**: Soglia singola per medicina in MVP (§1.1 punto 3), soglie
  multiple in un secondo tempo. Ok?
- **Q2**: `EndDate` che sopprime la notifica quando l'ETA la supera
  (§1.1 punto 6). Ok?
- **Q3**: Toast Windows via `Microsoft.Toolkit.Uwp.Notifications` con
  fallback a `NotifyIcon` (§1.1 punto 9). Ok?
- **Q4**: DPAPI (`CurrentUser`) per la password SMTP, non Credential
  Manager (§1.1 punto 11). Ok?

Se non arriva feedback, procedo con le proposte sopra.

---

## 2. Fase 2 — Architettura

### 2.1 Struttura della solution

```
MedReminder.sln
src/
  MedReminder.Domain/            netstandard2.1 o net10.0
  MedReminder.Application/       net10.0
  MedReminder.Infrastructure/    net10.0-windows10.0.19041.0
  MedReminder.UI/                net10.0-windows10.0.19041.0  (WinForms, output exe)
tests/
  MedReminder.Domain.Tests/      net10.0     xUnit
  MedReminder.Application.Tests/ net10.0     xUnit
  MedReminder.Infrastructure.Tests/ net10.0-windows10.0.19041.0 xUnit
```

Motivazioni:
- `Domain` in `netstandard2.1` (o `net10.0` puro) senza dipendenze Windows,
  100% testabile senza SDK Windows.
- `Application` in `net10.0`, contiene use case, servizi, interfacce
  d'infrastruttura (porte). Non fa riferimento a EF Core, MailKit, Toast.
- `Infrastructure` in `net10.0-windows10.0.19041.0` per poter usare API
  Windows (registry, DPAPI, notifiche, tray). Contiene EF Core, MailKit,
  logger file, adapter DPAPI, registrazione auto-start.
- `UI` è l'unico exe. Referenzia `Application` e `Infrastructure`.
- Nessun progetto "Shared" o "Common": non serve.

### 2.2 Dipendenze NuGet proposte

Numero e motivazione volutamente minimi.

| Pacchetto | Progetto | Motivo |
|---|---|---|
| `Microsoft.Extensions.Hosting` | UI | Generic host: DI, config, logging, hosted services |
| `Microsoft.Extensions.Configuration.Json` | UI | `appsettings.json` + user override |
| `Microsoft.EntityFrameworkCore.Sqlite` | Infrastructure | Persistenza + migrations |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure (tool) | `dotnet ef migrations` |
| `MailKit` | Infrastructure | SMTP moderno (sostituto di `SmtpClient`) |
| `Microsoft.Toolkit.Uwp.Notifications` | Infrastructure | Toast Windows unpackaged |
| `Serilog.Extensions.Hosting` + `Serilog.Sinks.File` | Infrastructure | Log strutturato rolling su file |
| `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` | Tests | Framework di test standard |

Non aggiungerò librerie "skin" WinForms, non aggiungerò AutoMapper, non
aggiungerò MediatR. Se un caso specifico lo giustificherà lo motiverò
prima.

`[INFERRED]` EF Core 10 tag stabile è disponibile e supporta `net10.0`
(release EF Core allineate al major .NET). Se al primo restore risultasse
non ancora sul feed pubblico, ripiegherei su EF Core 9 (compatibile con
`net10.0`) e lo segnalerei.

### 2.3 Modello dati

Nomi in inglese nel codice, coerenti con la specifica sezione 16. Nomi in
italiano nella UI.

**Medicine**
- `Id: Guid`
- `Name: string`  (required)
- `ActiveIngredient: string?`
- `Package: string?`
- `Unit: string`  (codice unità, es. "compresse", "ml", …)
- `DosePerAdministration: decimal`  (unità per singola somministrazione)
- `AdministrationsPerDay: int`
- `StartDate: DateOnly`
- `EndDate: DateOnly?`
- `ThresholdDays: int`
- `DoctorName: string?`
- `Notes: string?`
- `IsActive: bool`
- `StockEpoch: int`  (incrementato ad ogni movimento positivo)
- `NotificationChannels: NotificationChannels`  (flags: Email, Windows)
- `CreatedAt: DateTimeOffset`
- `UpdatedAt: DateTimeOffset`

**StockMovement**
- `Id: Guid`
- `MedicineId: Guid`
- `OccurredAt: DateTimeOffset`
- `Kind: StockMovementKind`  (`InitialLoad`, `NewPackage`, `ManualAdd`,
  `Consumption`, `PositiveCorrection`, `NegativeCorrection`)
- `QuantityDelta: decimal`  (segno coerente con `Kind`)
- `StockEpoch: int`  (l'epoch attivo al momento del movimento)
- `Notes: string?`

**MedicationSuspension**
- `Id: Guid`
- `MedicineId: Guid`
- `StartDate: DateOnly`
- `EndDate: DateOnly?`  (null = sospensione aperta)
- `Reason: string?`

**MedicationScheduleHistory** (per gestire cambi di dose/frequenza a metà
terapia senza distruggere lo storico)
- `Id: Guid`
- `MedicineId: Guid`
- `EffectiveFrom: DateOnly`
- `DosePerAdministration: decimal`
- `AdministrationsPerDay: int`

Nota: `Medicine.Dose*` e `Medicine.AdministrationsPerDay` sono lo stato
"corrente", `MedicationScheduleHistory` è la timeline. Il calcolo del
consumo giornaliero usa la history, non lo stato corrente.

**MedicationIntake**  (previsto ma non usato dall'MVP; presente per non
bloccarne l'implementazione futura, sezione 6)
- `Id: Guid`
- `MedicineId: Guid`
- `ScheduledAt: DateTimeOffset?`
- `ActualAt: DateTimeOffset?`
- `Quantity: decimal`
- `Status: IntakeStatus`  (`Taken`, `Skipped`, `Cancelled`, `ManualCorrection`)
- `Notes: string?`

**NotificationEvent**
- `Id: Guid`
- `MedicineId: Guid`
- `StockEpoch: int`
- `TriggeredAt: DateTimeOffset`
- `Channel: NotificationChannels`
- `DaysRemainingAtSend: int`
- `Success: bool`
- `ErrorMessage: string?`

**ApplicationSetting**  (chiave/valore, per le impostazioni globali)
- `Key: string`  (PK)
- `Value: string`

Le impostazioni SMTP e le preferenze applicative usano `IOptions<T>`
proiettate da questa tabella (o da `appsettings.json` per i valori non
sensibili).

### 2.4 Regole di consistenza

- `CurrentStock(medicineId) = Σ StockMovement.QuantityDelta` per quella
  medicina. Non è persistita, è **funzione**. Se si dovesse memorizzare
  per performance, sarebbe un campo denormalizzato con test di
  consistenza.
- `StockEpoch` corrente della medicina = max epoch presente sui movimenti
  positivi. Il campo su `Medicine` è cache; test di consistenza in
  `NotificationCycleTests`.
- La generazione di `StockMovement.Kind = Consumption` è responsabilità
  del `ConsumptionCatchUpService` (idempotente per `(medicineId, date)`
  grazie a un vincolo unico su `(MedicineId, OccurredAt.Date, Kind)` per
  `Kind = Consumption`).
- La sospensione impedisce la generazione di consumo per giorni contenuti
  nel periodo sospeso.

### 2.5 Interfacce principali (porte)

Dichiarate in `MedReminder.Application` o `MedReminder.Domain` a seconda
del layer. Solo firme, nessuna implementazione — questa è ancora Fase 2.

- `IMedicineRepository`  — CRUD su `Medicine` e sue collezioni.
- `IStockMovementRepository`
- `INotificationEventRepository`
- `IUnitOfWork`  — transazioni.
- `IEmailNotificationService`  — invio email; test di connessione.
- `IWindowsNotificationService`  — toast/tray.
- `IAutoStartService`  — registrazione/rimozione registry Run.
- `ICredentialProtector`  — DPAPI wrapping/unwrapping.
- `IClock`  ⇄ `TimeProvider` (uso `TimeProvider` direttamente, standard
  .NET 8+; niente wrapper custom).
- `IMedicationMonitoringService`  — ciclo di controllo periodico.
- `IConsumptionCatchUpService`  — materializzazione del consumo giornaliero.
- `IStockService`  — API applicativa per aggiungere/correggere stock.
- `IBackupService`  — export/import DB.
- `IEmailComposer`  — costruzione del contenuto email a partire dallo
  stato medicina (separata dal transport per essere testabile).

### 2.6 Scheduler / hosted service

Un unico `IHostedService`: `MedicationMonitorHostedService`.

- Al `StartAsync`: catch-up consumo + primo run del controllo.
- Ciclo periodico configurabile (default 30 minuti in
  `appsettings.json`), realizzato con `PeriodicTimer` + `CancellationToken`.
- Ad ogni run:
  1. Ricarica medicine attive.
  2. Chiede al `IConsumptionCatchUpService` di materializzare eventuali
     giorni di consumo non ancora registrati.
  3. Per ciascuna medicina calcola `DaysRemaining` e verifica la condizione
     di avviso.
  4. Consulta `NotificationEventRepository` con `(MedicineId, StockEpoch)`:
     se non esiste già un evento per l'epoch corrente entro la soglia,
     compone e invia notifica (canali configurati per la medicina) e
     registra `NotificationEvent`.
  5. In caso di errore email, retry con back-off; il fallimento definitivo
     è registrato come `NotificationEvent` fallito (`Success = false`).
- Sezione critica non necessaria: il service è single-threaded e nessun
  altro scrittore agisce concorrentemente sullo stesso DB.
- Alla `StopAsync`: il `CancellationToken` interrompe il ciclo entro un
  paio di secondi. Nessuna scrittura pendente viene abbandonata (le
  transazioni sono per singola operazione).

### 2.7 UI WinForms

Struttura form:

- `MainForm`
  - `DataGridView` medicine (colonne: Nome, Residuo, Consumo/gg, Giorni,
    ETA, Stato).
  - Toolbar: "Nuova", "Modifica", "Aggiungi scorte", "Registra consumo",
    "Controlla ora", "Impostazioni".
  - Stato riga colorato: normale / attenzione (dentro soglia) / esaurita /
    sospesa.
- `MedicineEditDialog` — nuovo/modifica.
- `StockAdjustmentDialog` — carico, correzione ±, consumo manuale.
- `SettingsDialog` — schede: Generali, Notifiche (Email/Windows/Entrambi/
  Nessuna), Email SMTP (host, porta, TLS, user, password), Backup,
  Auto-start.
- `NotifyIcon` + menu (Apri, Controlla ora, Impostazioni, Esci).
- `LogViewerDialog` — coda del file di log corrente.

Il ViewModel è tenuto minimale. Ogni form riceve dai propri costruttori i
servizi applicativi che gli servono (DI via `IServiceProvider` root). Non
introduco MVVM completo su WinForms: sarebbe over-engineering.

### 2.8 Persistenza e migrations

- SQLite file `medreminder.db` in `%LOCALAPPDATA%\MedReminder\`.
- EF Core code-first, migrations versionate nel repository sotto
  `src/MedReminder.Infrastructure/Migrations/`.
- `DbContext` applica `Database.Migrate()` all'avvio (idempotente).
- Modalità WAL, `foreign_keys = ON`.
- Backup: copia del file DB previa `WAL checkpoint TRUNCATE`. Import:
  overwrite del file DB previa conferma e rinomina del file esistente in
  `medreminder.db.bak-yyyyMMddHHmmss`.

### 2.9 Notifiche

- `IEmailNotificationService` (MailKit): host/porta/TLS/user/password/
  timeout/from/to; `SendAsync(subject, body, CancellationToken)`;
  `TestConnectionAsync()`.
- `IEmailComposer`: `Compose(medicine, remaining, daysRemaining, eta)`
  → `EmailMessage`. Testato in isolamento con snapshot del testo.
- `IWindowsNotificationService`: `NotifyAsync(title, body)` con
  implementazione toast + fallback balloon.
- `NotificationChannels` è un `[Flags]` enum su `Medicine` che decide
  quali canali usare. Il monitor deduplica per `(MedicineId, StockEpoch)`,
  non per canale: una volta notificata l'epoch, non si ri-notifica.

### 2.10 Logging

- Serilog: file rolling giornaliero, retention 30 giorni, in
  `%LOCALAPPDATA%\MedReminder\logs\medreminder-.log`.
- Nessun contenuto di email, nessuna password, nessuna nota medica libera
  finisce in log. Solo: `MedicineId`, `Name`, quantità, giorni residui,
  esito operazione.
- Livello configurabile in `appsettings.json`. Default `Information`.

### 2.11 Sicurezza e privacy

- Password SMTP: DPAPI `CurrentUser`, base64, salvata nel file
  `smtp.protected` accanto al DB (non nel repository, non in
  `appsettings.json` versionato).
- `appsettings.json` versionato contiene solo default innocui.
- `.gitignore` deve escludere `bin/`, `obj/`, `*.user`, `.vs/`,
  `*.db*`, `smtp.protected`, `logs/`.
- Email inviata contiene: nome medicina, giorni residui, quantità,
  suggerimento generico di richiedere prescrizione. Nessuna informazione
  clinica libera.

### 2.12 Auto-start

`IAutoStartService` con `IsEnabled`, `Enable()`, `Disable()`. Implementato
scrivendo/rimuovendo il valore
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MedReminder` che
punta all'eseguibile con argomento `--minimized`.

### 2.13 Tray

Nella `MainForm`:
- Alla chiusura, se impostazione "chiudi in tray" attiva, `e.Cancel =
  true` e `Hide()`.
- Doppio click sull'icona → `Show()` + `WindowState = Normal`.
- Voce "Esci" nel menu chiama `Application.Exit()` bypassando il tray.
- Argomento CLI `--minimized` fa partire l'app direttamente in tray.

---

## 3. Piano di implementazione incrementale

Ogni incremento termina con: `dotnet build` OK, `dotnet test` OK,
commit descrittivo, push. Nessuna PR aperta finché l'utente non la
richiede esplicitamente.

**Incremento 0 — Bootstrap solution**
- File solution + 4 progetti + 3 progetti di test.
- `Directory.Build.props` con `Nullable`, `ImplicitUsings`, `TargetFramework`.
- `.gitignore`, `.editorconfig`, `README.md` aggiornato.
- `dotnet build` verde, un test placeholder verde.

**Incremento 1 — Dominio**
- Entità pure: `Medicine`, `StockMovement`, `MedicationSuspension`,
  `MedicationScheduleHistory`, `MedicationIntake`, `NotificationEvent`.
- Enumerazioni.
- `MedicineStock` (value object) con logica di somma dei movimenti,
  clamp a zero, ricalcolo epoch.
- `DailyConsumption` con schedule versionata.
- `RunOutForecast` che calcola `DaysRemaining` ed `EstimatedRunOutDate`
  usando `TimeProvider`.
- `NotificationCycle` che decide "notifica sì/no" a fronte di
  soglia + `NotificationEvent` esistenti per l'epoch.
- Test unitari per tutti i casi limite della sezione 1.2.

**Incremento 2 — Application**
- Interfacce di repository, `IEmailNotificationService`,
  `IWindowsNotificationService`, `IAutoStartService`, `ICredentialProtector`.
- Use case: `AddMedicine`, `UpdateMedicine`, `DeactivateMedicine`,
  `AddStock`, `RegisterConsumption`, `AdjustStock`, `SuspendMedication`,
  `ResumeMedication`, `RunPeriodicCheck`.
- `MedicationMonitor` implementato in Application (senza scheduler).
- `ConsumptionCatchUp` implementato con TimeProvider.
- Test con repository in-memory (fake per gli increment 1-2).

**Incremento 3 — Infrastructure: persistenza**
- `DbContext` EF Core Sqlite, entity configurations, migration iniziale.
- Repositories.
- `IUnitOfWork` con transazione EF Core.
- Test d'integrazione con SQLite in-memory (`:memory:`) o file temporaneo.
- Backup/import service base.

**Incremento 4 — Infrastructure: notifiche + credenziali + auto-start**
- MailKit adapter + `IEmailComposer`.
- Toast adapter + fallback `NotifyIcon`.
- `DpapiCredentialProtector`.
- `RegistryAutoStartService`.
- Test di unità dove sensato; toast non testabile automaticamente, si
  documenta il test manuale.

**Incremento 5 — Hosted service**
- `MedicationMonitorHostedService` + `PeriodicTimer`.
- Composition root in `Program.cs` di `MedReminder.UI` con generic host.
- Test: forcing period corto in test d'integrazione.

**Incremento 6 — UI**
- `MainForm`, dialog di edit, dialog stock, settings, log viewer.
- Tray + argomento `--minimized`.
- Binding UI ↔ services.

**Incremento 7 — Hardening**
- Retry SMTP con back-off.
- Mutex single-instance.
- Errori DB non-fatali (banner UI).
- Verifica DST e cambio giorno con test dedicati.
- Verifica notifiche non duplicate su restart.

**Incremento 8 — Packaging e documentazione**
- Publish `net10.0-windows` x64 self-contained.
- README completo (§28), disclaimer non-dispositivo-medico.
- Documentazione tecnica minima in `docs/` (già iniziata con questo
  file).
- Nessun installer nell'MVP (fuori scope minimo).

---

## 4. Struttura file (esito atteso dopo Incremento 0)

```
MedReminder/
  MedReminder.sln
  Directory.Build.props
  .gitignore
  .editorconfig
  README.md
  LICENSE
  docs/
    ANALYSIS.md            (questo file)
  src/
    MedReminder.Domain/
      MedReminder.Domain.csproj
    MedReminder.Application/
      MedReminder.Application.csproj
    MedReminder.Infrastructure/
      MedReminder.Infrastructure.csproj
    MedReminder.UI/
      MedReminder.UI.csproj
      Program.cs
  tests/
    MedReminder.Domain.Tests/
    MedReminder.Application.Tests/
    MedReminder.Infrastructure.Tests/
```

---

## 5. Che cosa chiedo di approvare prima di procedere

1. Le 4 decisioni marcate Q1–Q4 nella §1.3.
2. La struttura della solution in §2.1 e la lista di dipendenze NuGet in
   §2.2.
3. Il modello dati in §2.3 (in particolare la presenza di
   `MedicationScheduleHistory` e `MedicationSuspension`).
4. Il piano incrementale in §3 e l'ordine dei passi.

Ricevuta approvazione (o correzioni), procedo con l'**Incremento 0** e in
seguito Incremento 1, fermandomi a ogni incremento con build+test verdi e
un breve report di ciò che è cambiato, come richiesto dalla sezione 29
della specifica.

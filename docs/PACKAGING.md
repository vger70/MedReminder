# MedReminder — Pubblicazione e distribuzione

Come produrre l'eseguibile finale da distribuire agli utenti. Copre
i profili di publish inclusi, i vincoli su trimming/AOT, la struttura
attesa nella cartella di output e le note su installazione manuale.

## Prerequisiti

- .NET SDK 10.0 (versione dell'utente aggiornata: `dotnet --version`).
- Repository clonato: `git clone …` seguito da `dotnet restore`.
- La build deve girare su Windows: WinForms + Windows-only APIs
  (`Microsoft.Win32.Registry`, DPAPI, `NotifyIcon`) richiedono un
  ambiente Windows.

## Profili di publish inclusi

I due profili risiedono in
`src/MedReminder.UI/Properties/PublishProfiles/`.

### `win-x64-framework-dependent.pubxml`

Pacchetto leggero (~10 MB). La macchina target deve avere il
**.NET 10 Desktop Runtime** installato — Microsoft lo distribuisce
via Windows Update per gli aggiornamenti feature, oppure come
installer separato da
`https://dotnet.microsoft.com/download/dotnet/10.0`.

```bat
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-framework-dependent
```

Output: `src\MedReminder.UI\bin\Release\net10.0-windows\publish\win-x64-fx\`

Contenuto tipico:
- `MedReminder.exe` — entry point WinForms.
- `MedReminder.dll` + tutte le assembly `MedReminder.*.dll`.
- `MailKit.dll`, `MimeKit.dll`, `Microsoft.EntityFrameworkCore.*.dll`,
  `Microsoft.Data.Sqlite.dll`, `Serilog.dll` e dipendenze.
- `runtimes\win-x64\native\e_sqlite3.dll` — SQLite nativo.
- `appsettings.json` — configurazione default (intervallo monitor,
  sezione Smtp vuota).

### `win-x64-self-contained.pubxml`

Pacchetto autonomo (~85 MB). Include il runtime .NET: gira su
qualsiasi Windows 10/11 x64 senza runtime pre-installato. Emette
un singolo `MedReminder.exe` con estrattore automatico
(`IncludeNativeLibrariesForSelfExtract=true`, essenziale per la DLL
SQLite nativa).

```bat
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-self-contained
```

Output: `src\MedReminder.UI\bin\Release\net10.0-windows\publish\win-x64-sc\MedReminder.exe`

`PublishReadyToRun=true` accelera lo startup a spese di dimensione.
`PublishTrimmed=false` è **obbligatorio**: WinForms ed EF Core
utilizzano reflection estesa (materializzazione entità, binding
DataGridView, source generator del designer). Il trimming
rimuoverebbe codice usato solo via reflection, causando crash a
runtime.

## Distribuzione manuale

Non è previsto un installer nell'MVP. Distribuzione consigliata:

1. Comprimi la cartella di output in un ZIP.
2. L'utente lo scomprime in una cartella a sua scelta (tipicamente
   `%LOCALAPPDATA%\Programs\MedReminder\` per non richiedere UAC,
   oppure `%PROGRAMFILES%\MedReminder\` se dispone di admin).
3. Doppio click su `MedReminder.exe` per il primo avvio.
4. (Opzionale) Impostazioni → **Avvio automatico** per farlo partire
   con Windows in modalità tray.

L'app crea automaticamente `%LOCALAPPDATA%\MedReminder\` alla prima
esecuzione — dati, log e credenziali cifrate non vanno nella
cartella di installazione.

## Creazione di uno shortcut in tray

Per avviare l'app minimizzata (in tray) via uno shortcut manuale:

1. Tasto destro sul Desktop → Nuovo → Collegamento.
2. Percorso: `"C:\Path\To\MedReminder.exe" --minimized`.
3. Nome: `MedReminder`.
4. Proprietà → **Avanzate** → puoi impostare `Esegui: Ridotta a icona`
   se vuoi anche nascondere la finestra all'avvio.

Il flag `--minimized` è gestito da `Program.cs` — la MainForm si
apre con `WindowState=Minimized` e `ShowInTaskbar=false`.

## Aggiornamento di una versione già installata

L'MVP usa `EnsureCreated()` per lo schema SQLite: **non applica
migrazioni** a un DB esistente. Un aggiornamento che cambia lo schema
richiede migration EF Core reali (vedi §"Roadmap sviluppo" sotto).

Nel frattempo, per l'utente:

1. Chiudi MedReminder (menu tray → Esci).
2. Fai un **backup** del DB (Impostazioni → Backup) o copia manuale di
   `%LOCALAPPDATA%\MedReminder\medreminder.db`.
3. Sovrascrivi i file eseguibili con la nuova versione.
4. Se lo schema è compatibile, riavvia l'app normalmente.
5. Se lo schema è cambiato in modo non retro-compatibile,
   cancella `medreminder.db`, `-shm`, `-wal` e ripartite da zero
   (importando eventualmente un backup se rimasto compatibile).

## Firma del codice

Non prevista nell'MVP. Windows SmartScreen potrebbe segnalare l'app
come "sconosciuta" al primo avvio: l'utente deve cliccare **Ulteriori
informazioni → Esegui comunque**. Per una distribuzione più larga
serve un certificato di code-signing (extended-validation o standard).

## Roadmap sviluppo — migration EF Core reali

Attualmente il DB viene creato via `Database.EnsureCreated()` nel
`DatabaseInitializer`. Per adottare le migration:

```bat
cd src\MedReminder.Infrastructure
dotnet ef migrations add InitialCreate ^
  --startup-project ..\MedReminder.UI
dotnet ef database update ^
  --startup-project ..\MedReminder.UI
```

`MedReminderDbContextFactory` è già presente per abilitare
`dotnet ef` da command-line. Dopo la prima migration, sostituire
`EnsureCreatedAsync` con `MigrateAsync` in `DatabaseInitializer`.

Attenzione: se ci sono già DB creati con `EnsureCreated`, occorre una
migrazione manuale (creare una baseline migration + eseguire un
INSERT nella tabella `__EFMigrationsHistory` per allineare lo stato).

## Verifica di una build pubblicata

Dopo publish, controlli minimi da fare sulla macchina di test:

1. Doppio click su `MedReminder.exe`: la finestra deve aprirsi in
   pochi secondi (self-contained un po' più lenta la prima volta).
2. Nell'area di notifica compare l'icona MedReminder.
3. `%LOCALAPPDATA%\MedReminder\medreminder.db` viene creato.
4. `%LOCALAPPDATA%\MedReminder\logs\medreminder-<data>.log`
   contiene una riga `[INF] Application started` e `[INF] Monitor
   scheduler avviato; intervallo 30 minuti`.
5. Creare una medicina di prova e verificare che la griglia si
   aggiorni.
6. Impostazioni → Avvio automatico: attivare, controllare la voce
   in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MedReminder`
   con `regedit`.

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

## Installer MSI (WiX v5)

Dalla versione 1.0.1 la distribuzione supporta un **installer MSI**
generato con WiX Toolset v5. Il progetto vive in
`packaging/wix/MedReminder.wixproj` — build SDK-style, versione
letta da `Directory.Build.props`.

**Caratteristiche dell'MSI**:

- **Scope per-user**: install in `%LOCALAPPDATA%\Programs\MedReminder\`,
  nessun UAC, coerente col modello dati dell'app.
- **Upgrade automatico**: `MajorUpgrade` rimpiazza in place qualunque
  versione precedente della stessa `UpgradeCode`.
- **Shortcut**: Start Menu (obbligatorio) + Desktop (feature separata,
  disattivabile in UI).
- **Auto-start opzionale**: feature separata `AutoStart` (Level=1000,
  disabilitata di default) che scrive `HKCU\...\Run` con
  `--minimized`. L'utente può comunque abilitarlo da Impostazioni.
- **ARP entries**: icona, URL repo, no repair/modify (l'MSI non è
  configurabile a posteriori).
- **License**: RTF minimale con disclaimer "non è un dispositivo medico".

**Prerequisiti**:

- .NET SDK 10 (per `dotnet build`).
- Windows SDK 10.0.22621+ per `signtool.exe` (se firmi).
- Il pacchetto `WixToolset.Sdk 5.x` viene ripristinato automaticamente
  al primo `dotnet restore packaging/wix/MedReminder.wixproj`.

**Build manuale (senza firma)**:

```bat
:: 1. Publish self-contained (l'MSI harvesta questa cartella)
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-self-contained

:: 2. Build MSI (versione da Directory.Build.props)
dotnet build packaging\wix\MedReminder.wixproj -c Release ^
  /p:PublishDir=..\..\src\MedReminder.UI\bin\Release\net10.0-windows10.0.19041.0\publish\win-x64-sc\ ^
  /p:ProductVersion=1.0.1
```

Output: `packaging\wix\bin\Release\MedReminder-1.0.1-x64.msi`.

## Installer MSIX

Anche l'MSIX è supportato da 1.0.1. Il manifest vive in
`packaging/msix/Package.appxmanifest`. Il pacchetto viene assemblato
con `MakeAppx.exe pack` (Windows SDK) usando `MedReminder.mapping.txt`.

**Prerequisiti aggiuntivi rispetto all'MSI**:

- Assets grafici multi-size in `packaging/msix/Assets/` — vedi
  `packaging/msix/Assets/README.md` per le taglie richieste e come
  generarli (Visual Studio Image Asset Generator o ImageMagick).
- **Certificato di firma obbligatorio**: MSIX non installabile senza
  firma valida (né sideloaded né dallo Store).

**Publisher identity — attenzione**:

Il campo `Publisher` in `Package.appxmanifest` DEVE corrispondere
esattamente al Subject del certificato usato per firmare. Default
manifest: `CN=vger70 Development, O=vger70, C=IT` (allineato al
self-signed di sviluppo). Quando passi a un cert vero, cambia il
manifest prima di ripackaging.

**Build (via lo script orchestrator)**:

```powershell
# Solo MSI + MSIX firmati con self-signed di dev
.\packaging\scripts\build-installer.ps1 `
  -CertificateThumbprint <thumbprint del self-signed>

# Solo MSIX (salta MSI)
.\packaging\scripts\build-installer.ps1 -SkipMsi

# Senza firma (test locale della pipeline)
.\packaging\scripts\build-installer.ps1
```

## Firma del codice (code signing)

**Perché firmare**:

- Windows SmartScreen: senza firma segnala "editore sconosciuto" al
  primo avvio; l'utente deve cliccare "Ulteriori informazioni → Esegui
  comunque". Con firma OV/EV la reputazione si costruisce nel tempo
  (EV = fiducia immediata, OV = ~qualche settimana).
- **MSIX**: firma **obbligatoria**. Senza cert valido non installa.
- MSI + exe: firma opzionale ma raccomandata.

**Certificato self-signed (SOLO sviluppo)**:

```powershell
.\packaging\scripts\make-selfsigned-cert.ps1
```

Genera un cert code-signing valido 3 anni nello store CurrentUser\My
+ lo esporta come `.pfx` (per CI) e `.cer` (da importare nel Trusted
Root della macchina di test). SmartScreen resta rosso: **inutilizzabile
per distribuzione pubblica**.

**Certificato di produzione**:

Da CA riconosciute (Sectigo, DigiCert, GlobalSign, SSL.com...):

- **OV Code Signing**: ~200-400 USD/anno. Baseline Requirements
  CA/Browser Forum (nov 2023) impongono HSM/token USB o cloud HSM
  per la custodia della chiave privata. La firma richiede il token
  fisico presente (o la lib del cloud HSM configurata).
- **EV Code Signing**: ~300-700 USD/anno. Sempre su HSM. SmartScreen
  reputation immediata.

Con un cert produttivo, tipicamente installato nello store, la firma
avviene via **thumbprint** senza mai maneggiare la chiave privata:

```powershell
.\packaging\scripts\sign-artifact.ps1 `
  -Files dist\1.0.1\*.msi, dist\1.0.1\*.msix `
  -CertificateThumbprint <thumbprint>
```

**Timestamp**: sempre attivo (parametro `-TimestampUrl`, default
`http://timestamp.digicert.com`). Senza timestamp la firma scade con
il certificato e Windows la rigetta. Alternative gratuite:

- `http://timestamp.sectigo.com`
- `http://timestamp.globalsign.com/tsa/r6advanced1`
- `http://tsa.starfieldtech.com`

**Verifica firma**:

```bat
signtool.exe verify /pa /v MedReminder-1.0.1-x64.msi
```

Deve mostrare "Successfully verified" e una riga con il timestamp.

**MAI committare** file `.pfx`, `.p12`, `.cer`, `.crt`, password.
Il `.gitignore` esclude questi pattern. Per CI usare secrets del
provider (GitHub Actions secrets, Azure Key Vault, ecc.) — mai
metterli in chiaro nel workflow.

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

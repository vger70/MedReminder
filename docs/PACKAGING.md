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

Output: `src\MedReminder.UI\bin\Release\net10.0-windows10.0.19041.0\publish\win-x64-fx\`

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

Output: `src\MedReminder.UI\bin\Release\net10.0-windows10.0.19041.0\publish\win-x64-sc\MedReminder.exe`

`PublishReadyToRun=true` accelera lo startup a spese di dimensione.
`PublishTrimmed=false` è **obbligatorio**: WinForms ed EF Core
utilizzano reflection estesa (materializzazione entità, binding
DataGridView, source generator del designer). Il trimming
rimuoverebbe codice usato solo via reflection, causando crash a
runtime.

## Requisiti di build

Per compilare MedReminder sono necessari:

- Windows;
- .NET 10 SDK;
- accesso ai package NuGet utilizzati dalla soluzione.

Verificare la versione del SDK installato con:

```powershell
dotnet --version
```

e:

```powershell
dotnet --list-sdks
```

La compilazione della soluzione può essere eseguita con:

```powershell
dotnet build MedReminder.sln -c Release
```

---

## 3. Esecuzione dei test

Prima di creare un pacchetto distribuibile è consigliato eseguire tutti i test:

```powershell
dotnet test MedReminder.sln -c Release
```

La distribuzione tramite GitHub Actions viene eseguita solo se i test terminano con successo.

I test attualmente presenti sono:

```text
MedReminder.Domain.Tests
MedReminder.Application.Tests
MedReminder.Infrastructure.Tests
```

---

## 4. Pubblicazione Windows x64

La versione distribuita ufficialmente è una build:

- Windows x64;
- `Release`;
- `net10.0-windows`;
- self-contained.

Il comando di pubblicazione è:

```powershell
dotnet publish `
    src/MedReminder.UI/MedReminder.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish
```

Il risultato viene generato nella directory:

```text
publish/
```

### Self-contained

La pubblicazione usa:

```text
--self-contained true
```

Questo significa che il runtime .NET richiesto dall'applicazione viene incluso nel pacchetto.

L'utente finale non deve quindi installare separatamente il runtime .NET 10 per eseguire MedReminder.

### Runtime

La distribuzione corrente utilizza:

```text
win-x64
```

Questa build è destinata a Windows 64 bit su architettura x64.

---

## 5. Creazione del pacchetto ZIP

Dopo la pubblicazione, il contenuto della directory `publish` può essere distribuito direttamente oppure compresso in un archivio ZIP.

Esempio PowerShell:

```powershell
Compress-Archive `
    -Path publish\* `
    -DestinationPath MedReminder-win-x64.zip
```

Il pacchetto risultante è:

```text
MedReminder-win-x64.zip
```

Il nome del file è volutamente stabile e non contiene il numero di versione.

Questo permette di utilizzare un URL GitHub permanente per scaricare sempre l'ultima versione:

```text
https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip
```

Sostituire `vger70/MedReminder` con il repository GitHub reale.

---

## 6. Pubblicazione locale completa

Per verificare manualmente l'intero processo:

```powershell
dotnet restore MedReminder.sln

dotnet test MedReminder.sln -c Release --no-restore

dotnet publish `
    src/MedReminder.UI/MedReminder.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish

Compress-Archive `
    -Path publish\* `
    -DestinationPath MedReminder-win-x64.zip
```

Il risultato sarà:

```text
MedReminder-win-x64.zip
```

Il contenuto dello ZIP deve essere estratto in una directory locale e verificato avviando:

```text
MedReminder.exe
```

---

# 7. Versionamento

Le versioni distribuite vengono identificate tramite tag Git.

Il formato utilizzato è:

```text
vMAJOR.MINOR.PATCH
```

Esempi:

```text
v1.0.0
v1.1.0
v1.1.1
v2.0.0
```

Il tag rappresenta la versione della release pubblicata.

## Creazione di una release

Dopo aver completato le modifiche:

```powershell
git status
git add .
git commit -m "Prepare release v1.1.0"
git push
```

Creare quindi il tag:

```powershell
git tag v1.1.0
```

e pubblicarlo su GitHub:

```powershell
git push origin v1.1.0
```

Il push del tag avvia automaticamente la GitHub Action di release.

---

# 8. GitHub Actions

Il workflow di distribuzione si trova in:

```text
.github/workflows/release.yml
```

Il workflow viene avviato quando viene pubblicato un tag il cui nome inizia con:

```text
v
```

Ad esempio:

```text
v1.0.0
v1.2.3
v2.0.0
```

Il workflow esegue i seguenti passaggi:

```text
Git tag
   │
   ▼
Checkout repository
   │
   ▼
Installazione .NET 10
   │
   ▼
dotnet restore
   │
   ▼
dotnet test
   │
   ├── FAIL ──► workflow terminato
   │
   ▼
dotnet publish
   │
   ▼
Creazione ZIP
   │
   ▼
Creazione GitHub Release
   │
   ▼
Upload MedReminder-win-x64.zip
```

La Release non viene creata se i test falliscono.

---

# 9. Workflow di release

Il file `.github/workflows/release.yml` deve contenere un workflow equivalente al seguente:

```yaml
name: Build and Release MedReminder

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    name: Build Windows Release
    runs-on: windows-latest

    steps:
      # Scarica il repository.
      - name: Checkout
        uses: actions/checkout@v4

      # Installa .NET 10.
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      # Ripristina i package NuGet.
      - name: Restore
        run: dotnet restore MedReminder.sln

      # Esegue tutti i test.
      # Se un test fallisce, la pipeline si interrompe.
      - name: Test
        run: dotnet test MedReminder.sln -c Release --no-restore

      # Pubblica l'applicazione Windows x64 self-contained.
      - name: Publish
        run: >
          dotnet publish
          src/MedReminder.UI/MedReminder.UI.csproj
          -c Release
          -r win-x64
          --self-contained true
          -o publish

      # Crea il pacchetto ZIP.
      - name: Create ZIP
        shell: pwsh
        run: |
          Compress-Archive `
            -Path publish\* `
            -DestinationPath MedReminder-win-x64.zip

      # Crea la GitHub Release e carica il pacchetto.
      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: MedReminder-win-x64.zip
          generate_release_notes: true
```

---

# 10. GitHub Release

Al termine della pipeline, GitHub crea una release associata al tag.

Esempio:

```text
v1.1.0
```

Gli asset della release saranno:

```text
MedReminder-win-x64.zip
Source code (zip)
Source code (tar.gz)
```

La pagina delle release è disponibile all'indirizzo:

```text
https://github.com/vger70/MedReminder/releases
```

L'ultima release può essere raggiunta tramite:

```text
https://github.com/vger70/MedReminder/releases/latest
```

---

# Download dal README

Il `README.md` deve fornire un accesso diretto alla versione più recente.

Esempio:

```markdown
## Download

**Windows x64**

[⬇️ Download MedReminder](https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip)

[📦 View all releases](https://github.com/vger70/MedReminder/releases)
```

Poiché il nome dell'asset è sempre:

```text
MedReminder-win-x64.zip
```

il link non deve essere modificato ad ogni release.

Per esempio, sia la release `v1.0.0` sia `v1.1.0` utilizzeranno:

```text
MedReminder-win-x64.zip
```

GitHub farà puntare automaticamente:

```text
/releases/latest/download/MedReminder-win-x64.zip
```

all'asset della release più recente.

---

# Procedura consigliata per una nuova release

Prima di pubblicare una nuova versione:

1. completare le modifiche;
2. eseguire i test localmente;
3. verificare l'applicazione in modalità Release;
4. aggiornare la documentazione se necessario;
5. fare commit e push;
6. creare il tag;
7. fare push del tag;
8. verificare la GitHub Action;
9. verificare la GitHub Release;
10. verificare il download del pacchetto.

Esempio:

```powershell
dotnet test MedReminder.sln -c Release

git add .
git commit -m "Prepare release v1.1.0"
git push

git tag v1.1.0
git push origin v1.1.0
```

Dopo il push del tag, non è necessario creare manualmente la Release su GitHub.

---

# Verifica del pacchetto

Prima di considerare una release completata, verificare almeno:

- l'applicazione si avvia;
- il database SQLite viene creato/aperto correttamente;
- le funzionalità principali sono operative;
- l'invio email funziona se configurato;
- la configurazione DPAPI funziona sul computer di destinazione;
- non sono presenti file di sviluppo nel pacchetto;
- il pacchetto contiene tutti i file necessari;
- il download dalla pagina `releases/latest` funziona.

La verifica deve essere eseguita preferibilmente su una macchina Windows separata dall'ambiente di sviluppo.

---

# Dati locali dell'applicazione

Il pacchetto di distribuzione contiene il programma e le sue dipendenze.

I dati generati durante l'utilizzo dell'applicazione non devono essere inclusi nel repository Git e non devono essere inseriti nel pacchetto di release.

In particolare, evitare di distribuire:

- database SQLite dell'ambiente di sviluppo;
- configurazioni contenenti dati personali;
- credenziali;
- password;
- token;
- chiavi o segreti;
- file temporanei;
- log dell'ambiente di sviluppo.

La configurazione necessaria all'utente finale deve essere gestita secondo quanto descritto in `docs/USER_GUIDE.md`.

---

# File da non versionare

I seguenti elementi non devono essere committati nel repository:

```text
bin/
obj/
publish/
*.user
*.suo
*.db
*.sqlite
*.sqlite3
*.log
```

Eventuali file contenenti credenziali o segreti devono essere esclusi dal repository tramite `.gitignore`.

---

# Release e documentazione

Per ogni release significativa è consigliato verificare la coerenza tra:

```text
README.md
docs/USER_GUIDE.md
docs/PACKAGING.md
docs/ANALYSIS.md
```

`README.md` deve fornire informazioni sintetiche sul progetto e il collegamento al download.

`USER_GUIDE.md` deve descrivere l'utilizzo dell'applicazione per l'utente finale.

`PACKAGING.md` descrive build, test, pubblicazione e distribuzione.

`ANALYSIS.md` contiene l'analisi tecnica e l'architettura del progetto.

---

# Evoluzioni future

La distribuzione attuale prevede:

```text
Windows x64
.NET 10
Self-contained
ZIP
```

Eventuali estensioni future possono aggiungere:

- installer Windows (`Setup.exe`);
- build `win-arm64`;
- pacchetto MSIX;
- firma digitale dell'eseguibile;
- checksum SHA-256 degli asset;
- release notes personalizzate;
- aggiornamento automatico dell'applicazione.

Queste funzionalità devono essere introdotte senza modificare il meccanismo di download della versione corrente, quando possibile.

---


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

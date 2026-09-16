# MedReminder

Applicazione desktop Windows che ricorda all'utente di richiedere per
tempo una nuova prescrizione al proprio medico quando la scorta di una
medicina sta per finire. Sviluppata in C# con .NET 10 e WinForms.

> **MedReminder è un promemoria organizzativo, non un dispositivo
> medico.** Non fornisce diagnosi, indicazioni terapeutiche, modifiche
> di terapia o suggerimenti clinici. Ogni decisione sulla terapia va
> presa con il proprio medico.

---

## Scopo

Tenere traccia delle scorte delle medicine assunte regolarmente e
segnalare, con anticipo configurabile, quando è il momento di
richiedere una nuova prescrizione al medico. L'app calcola giorni
residui ed ETA di esaurimento sulla base di dose, frequenza,
sospensioni e cambi di terapia; invia una notifica Windows e/o
un'email quando la scorta scende sotto la soglia.

## Funzionalità

- Elenco medicine con quantità residua, consumo/giorno, giorni
  residui, data prevista di esaurimento, stato colorato.
- Registrazione dose, frequenza, data inizio/fine, soglia di avviso,
  medico di riferimento, note.
- Movimenti di magazzino: carico iniziale, nuova confezione, aggiunta
  manuale, correzione positiva/negativa; storico dei movimenti
  preservato (nessuna modifica distruttiva delle quantità).
- Materializzazione automatica del consumo giornaliero (spec §5).
- Cambio dose/frequenza a metà terapia con schedule versionata
  (preserva la storia).
- Sospensioni temporanee della terapia con date aperte/chiuse.
- Avvisi configurabili per medicina, con canali indipendenti
  (Windows/Email/Entrambi/Nessuno).
- Deduplicazione strutturale delle notifiche via `StockEpoch`:
  dopo un rifornimento il ciclo di avviso riparte.
- Scheduler interno con controllo periodico configurabile (default
  30 minuti); "Controlla ora" per un check on-demand.
- Icona nell'area di notifica con menu Apri/Controlla ora/
  Impostazioni/Esci; chiusura finestra minimizza in tray.
- Avvio automatico opzionale con Windows (per-utente, senza UAC).
- Backup/ripristino del database su file locale.
- Log strutturato consultabile (rolling giornaliero, retention 30 giorni).

## Requisiti

- Windows 10 22H2 o Windows 11.
- Per compilare / eseguire da sorgente: **.NET SDK 10.0 o superiore**.
- Per eseguire una build framework-dependent pubblicata: **.NET 10
  Desktop Runtime** installato.
- Per la build self-contained: nessun runtime esterno richiesto.

## Stack tecnologico

| Layer | Tecnologia |
|---|---|
| UI | WinForms su .NET 10 (`net10.0-windows`) |
| Application / Domain | C# 12+, nullable + implicit usings |
| Persistenza | SQLite via EF Core 10 |
| SMTP | MailKit 4.x |
| Credenziali locali | DPAPI (Windows Data Protection API, scope CurrentUser) |
| Auto-start | Registry `HKCU\...\Run` |
| Notifiche locali | `NotifyIcon.ShowBalloonTip` (tray condivisa) |
| Logging | Serilog + file rolling giornaliero |
| Test | xUnit + FluentAssertions |
| Hosting | `Microsoft.Extensions.Hosting` (generic host + BackgroundService) |

Architettura in 4 progetti + 3 progetti di test — dettagli in
[`docs/ANALYSIS.md`](docs/ANALYSIS.md).

## Struttura del repository

```
MedReminder.sln
src/
  MedReminder.Domain/           entità e calcoli puri  (net10.0)
  MedReminder.Application/      use case e porte       (net10.0)
  MedReminder.Infrastructure/   SQLite/MailKit/DPAPI   (net10.0-windows)
  MedReminder.UI/               WinForms + host        (net10.0-windows)
tests/
  MedReminder.Domain.Tests/
  MedReminder.Application.Tests/
  MedReminder.Infrastructure.Tests/
docs/
  ANALYSIS.md       analisi tecnica + architettura
  USER_GUIDE.it.md  guida rapida per l'utente finale (italiano)
  USER_GUIDE.en.md  user guide (English)
  PACKAGING.md      pubblicazione e distribuzione
```

## Come compilare

```bat
dotnet restore MedReminder.sln
dotnet build   MedReminder.sln -c Release
```

Uscita: `src\MedReminder.UI\bin\Release\net10.0-windows\MedReminder.exe`.

## Come eseguire i test

```bat
dotnet test MedReminder.sln -c Release
```

I test integrazione (`MedReminder.Infrastructure.Tests`) girano solo su
Windows (usano DPAPI e la registry). I test di dominio e applicativi
girano su qualunque piattaforma con .NET 10 SDK.

## Come pubblicare per distribuire

Vedi [`docs/PACKAGING.md`](docs/PACKAGING.md) per dettagli.
Rapidamente:

```bat
:: Framework-dependent (~10 MB, richiede .NET 10 Desktop Runtime)
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-framework-dependent

:: Self-contained (~85 MB, non richiede runtime esterno)
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-self-contained
```

## Dove vengono salvati i dati

Tutto sotto `%LOCALAPPDATA%\MedReminder\`
(`C:\Users\<utente>\AppData\Local\MedReminder\`):

| File | Contenuto |
|---|---|
| `medreminder.db` (+ `-shm`, `-wal`) | Database SQLite: medicine, movimenti, storico schedule, sospensioni, notifiche |
| `smtp.settings.json` | Configurazione SMTP (senza password, sovrascrive appsettings.json) |
| `smtp.protected` | Password SMTP cifrata con DPAPI (CurrentUser) |
| `logs\medreminder-YYYYMMDD.log` | Log giornalieri, retention 30 giorni, 10 MB/file |

Nessun dato viene inviato a servizi esterni salvo l'email quando le
notifiche SMTP sono configurate.

## Come configurare le notifiche

Aprire **Impostazioni → Email SMTP** dal menu principale:

1. Compilare host, porta, StartTLS, username, destinatario.
2. Digitare la password nel campo apposito (viene cifrata via DPAPI
   e salvata in `smtp.protected`; il file di configurazione non
   contiene mai la password in chiaro).
3. "Prova connessione" verifica la raggiungibilità del server.
4. Sulla singola medicina, in **Modifica → Canali di notifica**,
   scegliere Windows/Email/entrambi/nessuno.

Le notifiche Windows non richiedono alcuna configurazione: usano
la stessa icona di tray dell'app.

## Come effettuare il backup

**Impostazioni → Backup / Ripristino**:

- **Esporta backup**: seleziona una cartella; l'app forza un
  `wal_checkpoint(TRUNCATE)` e copia il DB come
  `medreminder-YYYYMMDD-HHMMSS.db`.
- **Ripristina backup**: seleziona un file `.db`; il DB corrente viene
  rinominato in `medreminder.db.bak-YYYYMMDDHHMMSS` prima della
  sostituzione. **Riavviare l'app dopo il ripristino.**

## Log e diagnostica

Percorso log: `%LOCALAPPDATA%\MedReminder\logs\`.
Formato: `medreminder-YYYYMMDD.log` — un file per giorno,
retention 30 giorni, dimensione massima 10 MB per file (con roll
alfabetico automatico oltre quella soglia).

Livello default: `Information`. Contiene i comandi SQL eseguiti
(non parametri sensibili), tick dello scheduler, invii di notifiche,
errori con stack trace. **Password, contenuto delle email e valori
di dose/quantità sensibili non vengono mai scritti nei log.**

## Limitazioni note

- **Non è un dispositivo medico** e non deve essere usato come
  strumento clinico di gestione terapia. Vedi disclaimer in cima.
- L'MVP usa `EnsureCreated()` per lo schema DB, non EF Core migrations:
  un'evoluzione dello schema in futuro richiederà una migration
  d'ingaggio dalla versione MVP. Il `DesignTimeDbContextFactory` è
  già presente per abilitare `dotnet ef migrations add` quando
  necessario.
- La registrazione delle assunzioni singole (spec §6) è presente nel
  modello dati (`MedicationIntake`) ma non è ancora esposta in UI:
  l'MVP calcola il consumo dallo schema configurato.
- L'invio email dipende dalla connessione Internet e dalla
  raggiungibilità del server SMTP; in caso di errori transienti
  l'app fa retry con back-off 5s → 30s → 2m e poi rinuncia,
  loggando l'errore.
- Single-instance è per-utente (una sessione Windows). Un secondo
  utente sulla stessa macchina può avere la propria istanza.
- L'app è pensata per uso single-user desktop: nessun sync tra
  dispositivi.

## Licenza

MIT — vedi [LICENSE](LICENSE).

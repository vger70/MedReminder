# MedReminder

Windows desktop medication stock reminder built with C# and .NET 10.

L'applicazione ricorda all'utente di richiedere per tempo una nuova
prescrizione al medico quando la scorta di una medicina sta per finire.
**Non è un dispositivo medico e non fornisce indicazioni cliniche o
terapeutiche.**

## Stato del progetto

In sviluppo. Analisi tecnica e architettura completate — vedi
[`docs/ANALYSIS.md`](docs/ANALYSIS.md).

Piano di lavoro suddiviso in incrementi (Incremento 0 → Incremento 8);
questa versione contiene l'**Incremento 0 (bootstrap solution)**.

## Requisiti di build

- Windows 10 22H2 o Windows 11
- .NET SDK 10.0 o superiore
- Visual Studio 2022 17.13+ oppure Visual Studio 2026 con carico di
  lavoro ".NET desktop development"

## Struttura

```
MedReminder.sln
src/
  MedReminder.Domain/           entità e regole di business (net10.0)
  MedReminder.Application/      use case e porte (net10.0)
  MedReminder.Infrastructure/   SQLite, MailKit, DPAPI, toast (net10.0-windows)
  MedReminder.UI/               WinForms app entry point   (net10.0-windows)
tests/
  MedReminder.Domain.Tests/
  MedReminder.Application.Tests/
  MedReminder.Infrastructure.Tests/
```

## Come compilare e testare

```
dotnet restore MedReminder.sln
dotnet build   MedReminder.sln -c Release
dotnet test    MedReminder.sln -c Release
```

Il progetto `MedReminder.UI` produce l'eseguibile `MedReminder.exe`.

## Dove vengono salvati i dati

Da MVP: tutti i dati locali sotto `%LOCALAPPDATA%\MedReminder\`
(database SQLite, log e credenziali SMTP cifrate DPAPI). Nessun servizio
cloud viene contattato in assenza di configurazione email.

## Documentazione tecnica

- [`docs/ANALYSIS.md`](docs/ANALYSIS.md) — analisi e architettura.

## Licenza

MIT — vedi [LICENSE](LICENSE).

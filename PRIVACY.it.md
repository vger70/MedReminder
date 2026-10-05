# MedReminder — Informativa sulla privacy

Ultimo aggiornamento: 5 ottobre 2026

Traduzione di [PRIVACY.md](PRIVACY.md). In caso di differenze prevale
la versione inglese.

Questa informativa descrive come l'applicazione desktop MedReminder per
Windows tratta i dati personali. Vale per ogni distribuzione
dell'applicazione: i pacchetti ZIP, gli installer MSI e il Microsoft
Store.

## 1. In sintesi

- MedReminder conserva i tuoi dati sul tuo PC. Lo sviluppatore non
  gestisce alcun server e non riceve nessuno dei tuoi dati.
- Non esiste un account presso lo sviluppatore, né telemetria,
  statistiche d'uso, pubblicità o tracciamento.
- I dati escono dal PC solo tramite funzioni che attivi tu: promemoria
  via email, backup nel cloud, sincronizzazione e controllo degli
  aggiornamenti. Ciascuna invia dati solo al servizio che scegli.

## 2. Chi è responsabile

MedReminder è un software libero e open source (Apache License 2.0)
sviluppato da vger70. Poiché lo sviluppatore non raccoglie né riceve i
tuoi dati, il controllo resta a te: l'applicazione li elabora sul tuo
dispositivo, per tuo conto.

Contatti: info@medreminder26.org, oppure una segnalazione su
https://github.com/vger70/MedReminder/issues (non inserire dati sulla
salute in una segnalazione pubblica).

## 3. Dati conservati sul tuo PC

MedReminder conserva tutto in `%LOCALAPPDATA%\MedReminder\`, nel tuo
account Windows:

- profili: nome, ruolo, PIN facoltativo (salvato come hash);
- farmaci, dosi, posologie, scorte, assunzioni, ricette, scadenze
  amministrative e note, un database per profilo;
- impostazioni email: server SMTP, nome utente e password (la password
  è cifrata con Windows DPAPI), indirizzi del mittente e dei
  destinatari, compreso un eventuale indirizzo del medico o di chi ti
  assiste;
- token di accesso a OneDrive o Google Drive, se li colleghi;
- impostazioni, backup che configuri e file di log.

I dati riguardano la tua salute (farmaci e terapia). I database non
sono cifrati: chiunque possa usare il tuo account Windows può leggerli.
Il PIN del profilo evita di aprire per errore il profilo sbagliato; non
è una protezione. Per tenere separati i dati di altre persone, dai a
ciascuna il proprio account Windows.

I file di log non contengono mai password, testi delle email o note
mediche.

La disinstallazione di MedReminder non cancella questa cartella.
Eliminala per rimuovere tutti i dati.

## 4. Dati che escono dal tuo PC

Solo queste funzioni inviano dati, e solo quando le usi:

| Funzione | Cosa viene inviato | Dove |
|---|---|---|
| Promemoria via email e richieste di ricetta | L'email che vedi nell'app: nomi dei farmaci, scorte, date; una richiesta di ricetta contiene anche il codice del prodotto e il tuo nome. Nessuna posologia né nota | Il server SMTP dell'account email che configuri, poi i destinatari che inserisci |
| Backup nel cloud (facoltativo) | Una copia giornaliera dei profili, cifrata sul tuo PC con una passphrase che non lascia mai il PC | Il tuo OneDrive (cartella dell'app) o Google Drive (cartella MedReminder), oppure una cartella a tua scelta |
| Sincronizzazione tra PC (facoltativa) | Farmaci, scorte, assunzioni, nome del profilo e destinatari, cifrati end-to-end | Il tuo OneDrive, Google Drive o una cartella condivisa |
| Esportazione nel calendario | Un file `.ics`, con titoli generici a meno che tu scelga di includere i nomi dei farmaci | Salvato dove scegli; le email di scorta bassa lo allegano |
| Controllo degli aggiornamenti e aggiornamento del catalogo (attivo per impostazione predefinita, disattivabile) | Una richiesta dell'ultima versione e degli elenchi pubblici dei farmaci; nessun dato personale. GitHub vede il tuo indirizzo IP e la versione dell'app | GitHub (`api.github.com`, `raw.githubusercontent.com`) |

Quando colleghi OneDrive o Google Drive, MedReminder chiede accesso solo
alla propria cartella (OneDrive `Files.ReadWrite.AppFolder`; Google
Drive `drive.file` e `drive.appdata`), non agli altri tuoi file.
Disattivare il backup nel cloud o la sincronizzazione ferma ogni invio;
l'accesso concesso si revoca dalle impostazioni dell'account Microsoft
o Google. Questi servizi trattano i dati secondo le proprie informative
sulla privacy, come il tuo fornitore di posta.

## 5. Webcam

Il lettore di codici a barre può usare la webcam. Le immagini vengono
decodificate sul tuo PC e non vengono mai salvate né inviate. La
fotocamera si spegne quando un codice viene letto, quando chiudi la
finestra di scansione o dopo 30 secondi.

## 6. Link che aprono il browser

Alcuni comandi aprono una pagina web nel browser predefinito: il
foglietto illustrativo o la scheda informativa del farmaco, la guida
utente, la pagina del progetto e, se lo scegli, una pagina di donazione
di Stripe o PayPal. MedReminder non invia dati a questi siti; lo fa il
tuo browser, secondo le informative dei siti stessi. MedReminder non
vede mai i dati di pagamento.

## 7. Minori

MedReminder non è rivolto ai minori e non raccoglie dati da nessuno.

## 8. I tuoi diritti

Tutti i dati sono sul tuo PC, sotto il tuo controllo: puoi consultarli,
correggerli, esportarli (Impostazioni → Backup / Ripristino → Esporta
tutti i dati) e cancellarli in qualsiasi momento. Per i dati nel tuo
account email, OneDrive o Google Drive, rivolgiti a quei fornitori.

## 9. Modifiche

Le modifiche a questa informativa vengono pubblicate in questo file,
con una nuova data di "Ultimo aggiornamento". La cronologia è visibile
nel repository:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.it.md

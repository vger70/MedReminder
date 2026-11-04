# MedReminder — Informativa sulla privacy

Ultimo aggiornamento: 4 novembre 2026

Traduzione di [PRIVACY.md](PRIVACY.md). In caso di differenze prevale
la versione inglese.

Questa informativa descrive come MedReminder tratta i dati personali su
Windows desktop, Android e iOS (iPhone). Si applica alle versioni e alle
funzioni disponibili per ciascuna piattaforma; non tutte le funzioni
sono necessariamente presenti in ogni versione. L'app desktop per
Windows è distribuita come pacchetto ZIP, installer MSI e tramite
Microsoft Store. Android è distribuito tramite Google Play, inclusi i
relativi canali di test. iOS è distribuito tramite l'App Store di Apple,
incluso TestFlight.

## 1. In sintesi

- MedReminder conserva i dati sul tuo dispositivo. Lo sviluppatore non
  gestisce server e non riceve i tuoi dati.
- Non esistono account presso lo sviluppatore, telemetria, statistiche
  d'uso, pubblicità o tracciamento.
- I dati lasciano il dispositivo solo tramite funzioni che usi o attivi,
  come email, esportazione o condivisione, backup cloud,
  sincronizzazione e download dei cataloghi. Ogni funzione invia dati
  solo al servizio o alla destinazione descritti qui sotto. I servizi
  specifici della piattaforma, come gli aggiornamenti tramite Google
  Play e App Store, sono indicati separatamente.

## 2. Chi è responsabile

MedReminder è un software libero e open source (Apache License 2.0)
sviluppato da vger70. Poiché lo sviluppatore non raccoglie né riceve i
tuoi dati, il controllo resta a te: l'applicazione li elabora sul tuo
dispositivo, per tuo conto.

Contatti: info@medreminder26.org, oppure una segnalazione su
https://github.com/vger70/MedReminder/issues (non inserire dati sulla
salute in una segnalazione pubblica).

## 3. Dati conservati sul dispositivo

### Windows desktop

MedReminder conserva i dati in `%LOCALAPPDATA%\MedReminder\`, nel tuo
account Windows. Comprendono:

- profili: nome, ruolo, PIN facoltativo (salvato come hash);
- farmaci, dosi, posologie, scorte, assunzioni, ricette, scadenze
  amministrative e note, con un database per profilo;
- impostazioni email: server SMTP, nome utente e password (la password
  è cifrata con Windows DPAPI), indirizzi del mittente e dei
  destinatari, compreso un eventuale indirizzo del medico o di chi ti
  assiste;
- token di accesso a OneDrive o Google Drive, se li colleghi;
- impostazioni, backup configurati da te e file di log.

I database dei profili su Windows non sono cifrati: chiunque possa usare
il tuo account Windows può leggerli. Il PIN del profilo evita di aprire
per errore il profilo sbagliato; non è una protezione. Per tenere
separati i dati di altre persone, assegna a ciascuna il proprio account
Windows.

I file di log di Windows non contengono password, testi delle email o
note mediche. Disinstallare l'app Windows non elimina la cartella dei
dati; per rimuoverli, elimina la cartella separatamente.

### Android

MedReminder conserva database, impostazioni e file di log nello spazio
privato dell'app sul telefono. Il backup automatico dei dati dell'app di
Android è disattivato, quindi il sistema operativo non copia questi dati
relativi alla salute in un account di backup Android. La passphrase di
backup è conservata tramite l'archiviazione sicura delle chiavi di
Android. L'accesso a Google Drive è gestito dai servizi Google Play, che
conservano l'autorizzazione e forniscono all'app token di accesso di
breve durata. I token di accesso a OneDrive sono conservati dalla
libreria di accesso Microsoft nell'archiviazione privata dell'app, che
le altre app non possono leggere ed è esclusa dal backup Android. I log
Android non contengono password, testi delle email o note mediche.

Cancellare i dati dell'app MedReminder o disinstallarla rimuove la copia
locale. Prima di farlo, usa le funzioni di esportazione o backup cloud
se vuoi conservarne una copia.

### iOS (iPhone)

MedReminder conserva database e impostazioni nello spazio privato
dell'app sull'iPhone, che le altre app non possono leggere. Questo
spazio è escluso dai backup di iCloud e del computer, quindi iOS non
copia questi dati relativi alla salute in un backup iCloud o sul
computer, e un backup ripristinato su un altro iPhone non li contiene. I
segreti locali, come la passphrase di backup, la password dell'email e i
token di accesso a OneDrive e Google Drive, sono cifrati con una chiave
conservata nel Portachiavi di iOS. Solo MedReminder su quell'iPhone può
leggere questa chiave, dopo che l'iPhone è stato sbloccato una prima
volta dall'accensione; la chiave non viene mai sincronizzata con il
Portachiavi iCloud. L'accesso a OneDrive o Google Drive avviene sulla
pagina del fornitore, aperta da iOS; MedReminder non vede mai la
password Microsoft o Google. L'app iOS non scrive file di log.

Eliminare MedReminder dall'iPhone rimuove la copia locale. iOS può
conservare la chiave del Portachiavi dell'app anche dopo l'eliminazione;
solo MedReminder può leggerla e, senza i dati eliminati, non apre nulla.
Prima di eliminare l'app, usa le funzioni di esportazione o backup cloud
se vuoi conservarne una copia.

I promemoria sono notifiche locali pianificate sull'iPhone; non viene
usato alcun servizio di notifiche push. A seconda delle impostazioni
delle notifiche di iOS, il loro testo, ad esempio il nome di un farmaco,
può comparire sulla schermata di blocco e su un Apple Watch abbinato.

## 4. Dati che escono dal dispositivo

Queste funzioni inviano o condividono dati solo quando sono disponibili
e vengono usate o attivate:

| Funzione | Cosa viene inviato | Dove |
|---|---|---|
| Promemoria email e richieste di ricetta (se disponibili e configurati) | L'email mostrata nell'app: nomi dei farmaci, scorte e date; una richiesta di ricetta include anche il codice del prodotto e il tuo nome. Nessuna posologia né nota | Il server SMTP configurato da te, poi i destinatari inseriti |
| Backup cloud (facoltativo) | Una copia giornaliera dei profili, cifrata sul dispositivo con la passphrase di backup. La passphrase non viene inviata al fornitore cloud | Il tuo account OneDrive o Google Drive; su Windows anche una cartella a tua scelta |
| Sincronizzazione (facoltativa) | Farmaci, scorte, assunzioni, nome del profilo e destinatari, cifrati end-to-end | Il tuo account OneDrive o Google Drive; su Windows anche una cartella condivisa |
| Esportazione, esportazione calendario e condivisione | Un archivio profilo `.mrz` cifrato, un file calendario `.ics` o i contenuti che scegli di condividere. I titoli del calendario sono generici, salvo tua scelta di includere i nomi dei farmaci | Il percorso del file o l'app/il destinatario scelto tramite le funzioni di file e condivisione di Windows, Android o iOS |
| Aggiornamenti dei cataloghi e di dati pubblici | Richieste di elenchi pubblici di farmaci e altri dati pubblici correlati; nessun dato personale sulla salute | GitHub (`raw.githubusercontent.com`). GitHub riceve il tuo indirizzo IP e i dettagli della richiesta |
| Controllo aggiornamenti dell'app Windows | Una richiesta dell'ultima versione desktop; nessun dato personale sulla salute. GitHub riceve il tuo indirizzo IP e la versione dell'app | GitHub (`api.github.com`) |
| Aggiornamenti delle app Android e iOS | Google Play e l'App Store distribuiscono gli aggiornamenti e possono trattare informazioni sull'account, sul dispositivo o dati diagnostici secondo le proprie informative e le tue impostazioni | Google Play; App Store di Apple |

Quando colleghi OneDrive o Google Drive, MedReminder richiede l'accesso
ai dati dell'app e ai file di backup, non l'accesso generale agli altri
tuoi file. Disattivare il backup cloud o la sincronizzazione interrompe
i futuri invii di quella funzione; puoi revocare l'accesso concesso dalle
impostazioni del tuo account Microsoft o Google. Disattivare una
funzione non elimina le copie già archiviate presso il fornitore. I
servizi cloud e di posta trattano i dati secondo le proprie informative
sulla privacy.

Su Windows, backup cloud e sincronizzazione possono anche usare cartelle
che selezioni. Tali cartelle potrebbero essere sincronizzate da un
fornitore cloud o condivise con altre persone; accesso e trattamento
dipendono dal fornitore e dalle impostazioni della cartella. Su Android,
backup cloud e sincronizzazione usano l'account OneDrive o Google Drive
collegato. Android o le impostazioni di risparmio energetico del
dispositivo possono ritardare le operazioni pianificate. Su iPhone,
backup cloud e sincronizzazione usano l'account OneDrive o Google Drive
collegato, ed è iOS a decidere quando eseguire le attività in
background: può ritardarle o, per un'app che apri di rado, non
eseguirle.

Microsoft Store, Google Play e App Store possono fornire allo
sviluppatore statistiche aggregate, come i download, e rapporti sugli
arresti anomali, a seconda delle impostazioni di diagnostica e
condivisione del dispositivo (su iPhone, "Condividi con sviluppatori
app"). Non includono i dati conservati in MedReminder.

### Come MedReminder usa i dati utente di Google

Quando colleghi Google Drive, MedReminder richiede due autorizzazioni:
`drive.file` (solo i file e le cartelle creati dall'app) e
`drive.appdata` (una cartella nascosta riservata all'app). Non può
vedere, leggere o modificare nessun altro file del tuo Google Drive. Le
usa solo per creare una cartella visibile MedReminder › backups e
salvarvi file di backup cifrati, per salvare dati di sincronizzazione
cifrati nella cartella nascosta dell'app, e per elencare, scaricare ed
eliminare quei file quando ripristini, sincronizzi o quando i backup
vecchi superano il numero che hai scelto di conservare. Legge inoltre
l'indirizzo email e l'identificativo Drive dell'account collegato e, su
Android e iPhone, il suo nome visualizzato, solo per mostrarti quale
account è collegato e per distinguere gli account. Prima del
caricamento, i file di backup vengono cifrati sul dispositivo con la
passphrase di backup e i dati di sincronizzazione sono cifrati
end-to-end: né Google né lo sviluppatore possono leggerne il contenuto.

### Conservazione, condivisione e uso limitato

I dati dell'account collegato e i token di accesso sono conservati solo
sul tuo dispositivo; la sezione 3 indica dove per ciascuna piattaforma,
e su Windows sono cifrati con Windows DPAPI. I dati utente di Google non
vengono inviati allo sviluppatore né a terzi, non vengono venduti, non
vengono usati per pubblicità e non vengono usati per sviluppare,
migliorare o addestrare modelli di intelligenza artificiale o di machine
learning. L'uso e il trasferimento da parte di MedReminder delle
informazioni ricevute dalle API di Google rispettano le [Google API
Services User Data
Policy](https://developers.google.com/terms/api-services-user-data-policy),
inclusi i requisiti di uso limitato (Limited Use).

### Revoca dell'accesso ed eliminazione dei dati

Puoi revocare in qualsiasi momento l'accesso di MedReminder dal tuo
Account Google, in Sicurezza › I tuoi collegamenti ad app e servizi di
terze parti (https://myaccount.google.com/connections); da quel momento
l'app non può più accedere al tuo Google Drive. Per eliminare ciò che
l'app ha salvato su Google Drive, elimina la cartella MedReminder in Il
mio Drive e, in Impostazioni di Google Drive › Gestisci app ›
MedReminder, scegli Elimina dati app nascosti. Per OneDrive, elimina la
cartella App › MedReminder26 e rimuovi l'accesso dell'app nelle
impostazioni privacy del tuo account Microsoft.

## 5. Fotocamera e blocco dell'app

La scansione dei codici a barre può usare la webcam di Windows o la
fotocamera di Android o iPhone, quando scegli di effettuare una
scansione. Le immagini vengono elaborate sul dispositivo e MedReminder
non le salva né le invia. Su Windows, la fotocamera si spegne quando
viene letto un codice, quando chiudi la finestra di scansione o dopo 30
secondi. Su Android e iPhone la fotocamera è usata solo con
l'autorizzazione che concedi nel sistema operativo, per leggere i codici
a barre dei farmaci o il codice QR mostrato da un altro dispositivo
quando ti unisci a una cerchia; il codice non viene salvato. Su Android
l'accesso alla fotocamera segue anche il ciclo di vita dell'app, e il
codice QR della cerchia è letto solo mentre la pagina di scansione è
aperta. Su iPhone la fotocamera è usata solo mentre una pagina di
scansione è aperta.

Su Android e iPhone, il blocco facoltativo dell'app usa lo sblocco del
telefono: dati biometrici, come l'impronta o Face ID, oppure il PIN, la
sequenza o il codice del blocco schermo. Il controllo è eseguito dal
sistema operativo; MedReminder riceve solo l'esito, mai dati biometrici.

## 6. Link che aprono il browser

Alcuni comandi aprono una pagina web nel browser, ad esempio il foglietto
illustrativo o la scheda informativa del farmaco, la guida utente o la
pagina del progetto. Su Windows, se lo scegli, può aprirsi anche una
pagina per donazioni di Stripe o PayPal. MedReminder non invia dati a
questi siti; lo fa il browser, secondo le informative dei siti stessi.
MedReminder non vede mai i dati di pagamento.

## 7. Minori

MedReminder non è rivolto ai minori. Lo sviluppatore non raccoglie
tramite l'app dati personali di minori.

## 8. I tuoi diritti

Puoi consultare, correggere, esportare e cancellare i dati in
MedReminder. Su Windows, usa Impostazioni → Backup / Ripristino →
Esporta tutti i dati; su Android e iPhone, usa le funzioni di
esportazione e gestione dei dati dell'app. Puoi anche rimuovere i dati
locali dell'app Android dalle impostazioni di Android, oppure eliminare
l'app dall'iPhone. Le copie nelle email, nei file esportati o condivisi
e negli account OneDrive o Google Drive vanno gestite con i relativi
destinatari, app o fornitori.

## 9. Modifiche

Le modifiche a questa informativa vengono pubblicate qui,
con una nuova data di "Ultimo aggiornamento". La cronologia è visibile
nel repository:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.it.md

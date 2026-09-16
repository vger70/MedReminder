namespace MedReminder.Application.Abstractions;

// Preferenze utente per il backup automatico giornaliero.
// Sezione "Backup" del IConfiguration; persistite in
// %LOCALAPPDATA%\MedReminder\backup.settings.json e mergiate sopra
// appsettings.json (reloadOnChange=true → IOptionsMonitor si aggiorna
// senza riavvio dell'app).
//
// PreferredTime è testuale ("HH:mm") per essere robusti al binder di
// IConfiguration: TimeOnly non ha un converter di default, mentre una
// string parsata a mano è a prova di misconfigurazione.
public sealed class BackupSettings
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }

    public string Directory { get; set; } = string.Empty;

    // Formato "HH:mm" (24h, cultura invariant). Vuoto → default 03:00.
    public string PreferredTime { get; set; } = "03:00";

    // Numero di giorni di retention. 0 = illimitato (sconsigliato).
    public int RetentionDays { get; set; } = 30;
}

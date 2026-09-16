using System.Globalization;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Hosting;

// Scheduler del backup automatico giornaliero.
//
// Politica "at-least-once-a-day con orario preferito":
//   - Tick ogni 15 minuti.
//   - Backup se: Enabled && LastSuccessfulBackupAt.Date < today locale
//                && oraLocaleCorrente >= PreferredTime.
//   - ⇒ Se l'app parte tardi rispetto all'orario preferito (es. PC
//     acceso alle 09:00 ma orario impostato a 03:00), catch-up
//     immediato al primo tick.
//   - ⇒ Se l'app non era attiva a orario, il backup dell'ultimo giorno
//     è perso; quello del giorno corrente viene fatto al primo tick
//     dopo l'orario. Nessun giorno con app viva passa senza backup.
//
// Fault-tolerant: un fallimento di export/prune non ferma lo scheduler
// e viene registrato nello state (LastError / LastAttemptAt) oltre che
// nei log.
internal sealed class AutomaticBackupHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeOnly FallbackPreferredTime = new(3, 0);

    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<BackupSettings> _settings;
    private readonly IBackupStateStore _stateStore;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutomaticBackupHostedService> _log;

    public AutomaticBackupHostedService(
        IServiceProvider services,
        IOptionsMonitor<BackupSettings> settings,
        IBackupStateStore stateStore,
        TimeProvider clock,
        ILogger<AutomaticBackupHostedService> log)
    {
        _services = services;
        _settings = settings;
        _stateStore = stateStore;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Backup scheduler avviato; tick ogni {Minutes} minuti.",
            TickInterval.TotalMinutes);

        // Ritardo iniziale: lasciamo al DatabaseInitializer e al monitor
        // il tempo di stabilizzarsi prima di aprire una nuova connessione
        // sul DB per l'eventuale backup.
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRunAsync(stoppingToken);
            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.LogInformation("Backup scheduler fermato.");
    }

    private async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.CurrentValue;
            if (!settings.Enabled) return;
            if (string.IsNullOrWhiteSpace(settings.Directory))
            {
                _log.LogWarning("Backup abilitato ma cartella non configurata; skip.");
                return;
            }

            var preferred = ParsePreferredTime(settings.PreferredTime);
            var state = _stateStore.Load();
            var nowLocal = ConvertToLocal(_clock.GetUtcNow());
            var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
            var timeOfDay = TimeOnly.FromDateTime(nowLocal.DateTime);

            if (state.LastSuccessfulBackupAt is { } last)
            {
                var lastLocalDay = DateOnly.FromDateTime(
                    ConvertToLocal(last).DateTime);
                if (lastLocalDay >= todayLocal)
                {
                    // Backup di oggi già fatto.
                    return;
                }
            }

            if (timeOfDay < preferred)
            {
                // Non è ancora l'ora.
                return;
            }

            _log.LogInformation(
                "Avvio backup automatico giornaliero in {Directory}.",
                settings.Directory);

            await using var scope = _services.CreateAsyncScope();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var file = await backup.ExportAsync(settings.Directory, cancellationToken);

            var pruned = 0;
            if (settings.RetentionDays > 0)
            {
                pruned = await backup.PruneOldBackupsAsync(
                    settings.Directory, settings.RetentionDays, cancellationToken);
            }

            _stateStore.Save(new BackupState(
                LastSuccessfulBackupAt: _clock.GetUtcNow(),
                LastAttemptAt: _clock.GetUtcNow(),
                LastError: null,
                LastBackupFile: file));

            _log.LogInformation(
                "Backup automatico completato: {File} (retention: {Pruned} file rimossi).",
                file, pruned);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown pulito.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Backup automatico fallito.");
            try
            {
                var existing = _stateStore.Load();
                _stateStore.Save(new BackupState(
                    LastSuccessfulBackupAt: existing.LastSuccessfulBackupAt,
                    LastAttemptAt: _clock.GetUtcNow(),
                    LastError: ex.Message,
                    LastBackupFile: existing.LastBackupFile));
            }
            catch
            {
                // Se persino lo state store è rotto, il log resta l'unica
                // fonte di verità per questo ciclo.
            }
        }
    }

    private static TimeOnly ParsePreferredTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return FallbackPreferredTime;
        if (TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t))
        {
            return t;
        }
        if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out t))
        {
            return t;
        }
        return FallbackPreferredTime;
    }

    private DateTimeOffset ConvertToLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _clock.LocalTimeZone);
}

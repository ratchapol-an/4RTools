namespace RagnarokAutomation.Core;

public sealed class MonitoringEngine(ConfigurationStore configurationStore, SocketInspector socketInspector, DiscordBotClient discordBotClient)
{
    private readonly ConfigurationStore _configurationStore = configurationStore;
    private readonly SocketInspector _socketInspector = socketInspector;
    private readonly DiscordBotClient _discordBotClient = discordBotClient;
    private readonly ProcessRegistryService _processRegistryService = new();
    private readonly RagexeCharacterNameReader _characterNameReader = new();
    private readonly Dictionary<string, DateTimeOffset> _lastAlertByKey = [];
    private readonly Dictionary<string, DateTimeOffset> _lastHeartbeatByKey = [];
    private readonly Dictionary<string, MonitoringIncident> _pendingInternetRecoveryAlerts = [];
    private bool? _lastInternetAvailable;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public event EventHandler<IReadOnlyList<MonitoringIncident>>? TickCompleted;
    public event EventHandler<string>? DiagnosticLog;

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        AppConfiguration config = await _configurationStore.LoadConfigurationAsync().ConfigureAwait(false);
        int pollSeconds = Math.Clamp(config.Monitoring.PollIntervalSeconds, 1, 60);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _timer?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    IReadOnlyList<MonitoringIncident> incidents = await PollAndNotifyAsync(cancellationToken).ConfigureAwait(false);
                    TickCompleted?.Invoke(this, incidents);
                }
                catch (Exception ex)
                {
                    DiagnosticLog?.Invoke(this, $"monitoring tick error (loop continues): {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected during stop
        }
    }

    public async Task<IReadOnlyList<MonitoringIncident>> PollAndNotifyAsync(CancellationToken cancellationToken)
    {
        AppConfiguration config = await _configurationStore.LoadConfigurationAsync().ConfigureAwait(false);
        List<MonitoringIncident> incidents = [];
        bool internetAvailable = config.Monitoring.SimulateInternetDown
            ? false
            : await MonitoringClassifier.ProbeInternetAsync().ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.Now;
        if (config.Monitoring.SimulateInternetDown)
        {
            DiagnosticLog?.Invoke(this, "internet simulation is ON: forcing internet probe result to failed");
        }
        LogInternetProbe(internetAvailable);

        if (internetAvailable)
        {
            IReadOnlyList<MonitoringIncident> recoveredIncidents = await FlushQueuedInternetRecoveryAlertsAsync(config, cancellationToken).ConfigureAwait(false);
            incidents.AddRange(recoveredIncidents);
        }

        Dictionary<string, CharacterConfig> byCharacterName = config.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.CharacterName))
            .GroupBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ProcessSnapshot> processes = _processRegistryService.DiscoverRagnarokProcesses(_socketInspector, _characterNameReader);
        foreach (ProcessSnapshot snapshot in processes)
        {
            if (string.IsNullOrWhiteSpace(snapshot.MemoryCharacterName))
            {
                continue;
            }

            if (!byCharacterName.TryGetValue(snapshot.MemoryCharacterName, out CharacterConfig? character))
            {
                continue;
            }

            if (!character.AlertEnabled)
            {
                continue;
            }

            (RuntimeState state, RootCause rootCause, string evidence) = MonitoringClassifier.Classify(snapshot, internetAvailable);

            if (state == RuntimeState.InGame || state == RuntimeState.Unknown)
            {
                continue;
            }

            MonitoringIncident incident = new()
            {
                ProcessId = snapshot.ProcessId,
                CharacterName = snapshot.MemoryCharacterName,
                Username = character.Username,
                State = state,
                RootCause = rootCause,
                Evidence = evidence
            };

            bool shouldNotify = ShouldNotify(config.Monitoring, incident, now);
            if (shouldNotify)
            {
                incident.OccurrenceCount = IncrementOccurrenceCount(incident.DedupKey);
                if (!internetAvailable && incident.RootCause == RootCause.InternetDown)
                {
                    incident.Evidence += " | queued: waiting for internet recovery";
                    incident.DeliveredToDiscord = false;
                    _pendingInternetRecoveryAlerts[incident.DedupKey] = incident;
                    DiagnosticLog?.Invoke(this,
                        $"queued alert char={incident.CharacterName} state={incident.State} cause={incident.RootCause} disconnected={incident.Timestamp:yyyy-MM-dd HH:mm}");
                }
                else
                {
                    incident.DeliveredToDiscord = await TrySendDiscordAsync(config, incident, cancellationToken).ConfigureAwait(false);
                }

                await _configurationStore.AppendIncidentAsync(incident).ConfigureAwait(false);
                incidents.Add(incident);
            }
        }

        return incidents;
    }

    private async Task<bool> TrySendDiscordAsync(AppConfiguration config, MonitoringIncident incident, CancellationToken cancellationToken)
    {
        UserDiscordMap? userMap = config.Users.FirstOrDefault(a =>
            string.Equals(a.Username, incident.Username, StringComparison.OrdinalIgnoreCase));
        if (userMap is null || string.IsNullOrWhiteSpace(userMap.DiscordUserId))
        {
            incident.Evidence += " | unroutable: missing Discord user id";
            DiagnosticLog?.Invoke(this, $"alert send skipped: missing discord user id mapping for username='{incident.Username}'");
            return false;
        }

        if (!TryNormalizeDiscordUserId(userMap.DiscordUserId, out string normalizedDiscordUserId))
        {
            incident.Evidence += " | unroutable: invalid Discord user id";
            DiagnosticLog?.Invoke(this, $"alert send skipped: invalid discord user id '{userMap.DiscordUserId}' for username='{incident.Username}'");
            return false;
        }

        string message = BuildDiscordMessage(incident);
        TimeSpan[] retryWaits = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
        foreach (TimeSpan wait in retryWaits)
        {
            DiscordSendResult sendResult = await _discordBotClient.SendDirectMessageWithDiagnosticsAsync(
                config.Monitoring.DiscordBotToken,
                normalizedDiscordUserId,
                message,
                cancellationToken).ConfigureAwait(false);
            if (sendResult.Success)
            {
                DiagnosticLog?.Invoke(this, $"alert send success: key={incident.DedupKey} char={incident.CharacterName}");
                return true;
            }

            DiagnosticLog?.Invoke(this,
                $"alert send failed: key={incident.DedupKey} char={incident.CharacterName} error={sendResult.ErrorMessage}; retry_in={wait.TotalSeconds}s");
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        DiagnosticLog?.Invoke(this, $"alert send gave up: key={incident.DedupKey} char={incident.CharacterName}");
        return false;
    }

    private async Task<IReadOnlyList<MonitoringIncident>> FlushQueuedInternetRecoveryAlertsAsync(
        AppConfiguration config,
        CancellationToken cancellationToken)
    {
        if (_pendingInternetRecoveryAlerts.Count == 0)
        {
            return [];
        }

        List<string> sentKeys = [];
        List<MonitoringIncident> deliveredIncidents = [];
        DateTimeOffset recoveredAt = DateTimeOffset.Now;
        DiagnosticLog?.Invoke(this, $"internet recovered={recoveredAt:yyyy-MM-dd HH:mm} flushing={_pendingInternetRecoveryAlerts.Count}");
        foreach ((string key, MonitoringIncident queuedIncident) in _pendingInternetRecoveryAlerts)
        {
            queuedIncident.RecoveredAt = recoveredAt;
            bool sent = await TrySendDiscordAsync(config, queuedIncident, cancellationToken).ConfigureAwait(false);
            if (!sent)
            {
                DiagnosticLog?.Invoke(this,
                    $"queued send failed char={queuedIncident.CharacterName} disconnected={queuedIncident.Timestamp:yyyy-MM-dd HH:mm} recovered={recoveredAt:yyyy-MM-dd HH:mm} key={key}");
                continue;
            }

            queuedIncident.DeliveredToDiscord = true;
            queuedIncident.Evidence += " | delivered after internet recovery";
            await _configurationStore.AppendIncidentAsync(queuedIncident).ConfigureAwait(false);
            deliveredIncidents.Add(queuedIncident);
            sentKeys.Add(key);
            DiagnosticLog?.Invoke(this,
                $"queued delivered char={queuedIncident.CharacterName} disconnected={queuedIncident.Timestamp:yyyy-MM-dd HH:mm} recovered={recoveredAt:yyyy-MM-dd HH:mm} key={key}");
        }

        foreach (string key in sentKeys)
        {
            _pendingInternetRecoveryAlerts.Remove(key);
        }

        return deliveredIncidents;
    }

    private void LogInternetProbe(bool internetAvailable)
    {
        if (!internetAvailable)
        {
            if (_lastInternetAvailable != false)
            {
                DiagnosticLog?.Invoke(this, "internet probe 1.1.1.1: failed (internet down detected)");
            }
            else if (_pendingInternetRecoveryAlerts.Count > 0)
            {
                DiagnosticLog?.Invoke(this, "internet probe 1.1.1.1: still failed (waiting to flush queued alerts)");
            }
        }
        else if (_lastInternetAvailable == false)
        {
            DiagnosticLog?.Invoke(this, "internet probe 1.1.1.1: success (internet restored)");
        }

        _lastInternetAvailable = internetAvailable;
    }

    private static bool TryNormalizeDiscordUserId(string input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate = input.Trim();
        if (candidate.StartsWith("<@") && candidate.EndsWith(">"))
        {
            candidate = candidate.Replace("<@", string.Empty).Replace(">", string.Empty).Replace("!", string.Empty);
        }

        if (!ulong.TryParse(candidate, out _))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private bool ShouldNotify(MonitoringSettings settings, MonitoringIncident incident, DateTimeOffset now)
    {
        TimeSpan dedupWindow = TimeSpan.FromMinutes(Math.Clamp(settings.DedupWindowMinutes, 1, 120));
        TimeSpan heartbeatWindow = TimeSpan.FromMinutes(Math.Clamp(settings.IncidentHeartbeatMinutes, 5, 240));
        string key = incident.DedupKey;

        if (!_lastAlertByKey.TryGetValue(key, out DateTimeOffset lastAlert))
        {
            _lastAlertByKey[key] = now;
            _lastHeartbeatByKey[key] = now;
            return true;
        }

        bool dedupExpired = now - lastAlert >= dedupWindow;
        bool heartbeatExpired = !_lastHeartbeatByKey.TryGetValue(key, out DateTimeOffset lastHeartbeat) || now - lastHeartbeat >= heartbeatWindow;
        if (dedupExpired || heartbeatExpired)
        {
            _lastAlertByKey[key] = now;
            _lastHeartbeatByKey[key] = now;
            return true;
        }

        return false;
    }

    private readonly Dictionary<string, int> _occurrenceByKey = [];
    private int IncrementOccurrenceCount(string key)
    {
        if (!_occurrenceByKey.TryGetValue(key, out int count))
        {
            count = 0;
        }

        count++;
        _occurrenceByKey[key] = count;
        return count;
    }

    private static string BuildDiscordMessage(MonitoringIncident incident)
    {
        string characterName = string.IsNullOrWhiteSpace(incident.CharacterName) ? "(unknown)" : incident.CharacterName;
        string status = incident.State == RuntimeState.Dead ? "Died" : "Disconnected";
        if (incident.State == RuntimeState.Disconnected)
        {
            string message = $"Character: {characterName}\nStatus: {status}\nCause: {incident.RootCause}";
            if (incident.RootCause == RootCause.InternetDown)
            {
                message += $"\nDisconnected: {incident.Timestamp:yyyy-MM-dd HH:mm}";
                if (incident.RecoveredAt.HasValue)
                {
                    message += $"\nRecovered: {incident.RecoveredAt.Value:yyyy-MM-dd HH:mm}";
                }
            }

            return message;
        }

        return $"Character: {characterName}\nStatus: {status}";
    }
}

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RagnarokAutomation.Core;
using Serilog;
using Serilog.Extensions.Logging;

namespace RagnarokReconnectWinUI;

public sealed class AppServices
{
    private readonly ConfigurationStore _configurationStore = new();
    private readonly SocketInspector _socketInspector = new();
    private readonly ProcessRegistryService _processRegistryService = new();
    private readonly RagexeCharacterNameReader _characterNameReader = new();
    private readonly SerilogLoggerFactory _loggerFactory;
    private readonly ILogger<AppServices> _logger;

    public AppConfiguration Configuration { get; private set; } = new();
    public string ConfigurationFilePath => _configurationStore.ConfigPath;
    public string StorageDirectoryPath => _configurationStore.BaseDirectory;
    public string LogDirectoryPath => Path.Combine(_configurationStore.BaseDirectory, "logs");
    public ObservableCollection<MonitoringIncident> RecentIncidents { get; } = [];
    public ObservableCollection<ProcessSnapshot> KnownProcesses { get; } = [];
    public ObservableCollection<ProcessMonitoringState> ProcessStates { get; } = [];
    public MonitoringEngine MonitoringEngine { get; }

    public AppServices()
    {
        Directory.CreateDirectory(LogDirectoryPath);
        string logFilePath = Path.Combine(LogDirectoryPath, "app-.log");
        Serilog.ILogger serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _loggerFactory = new SerilogLoggerFactory(serilogLogger, dispose: true);
        _logger = _loggerFactory.CreateLogger<AppServices>();
        MonitoringEngine = new MonitoringEngine(_configurationStore, _socketInspector, new DiscordBotClient(new HttpClient()));
        MonitoringEngine.TickCompleted += async (_, incidents) =>
        {
            await RunOnUiThreadAsync(() =>
            {
                foreach (MonitoringIncident incident in incidents)
                {
                    bool exists = RecentIncidents.Any(i =>
                        i.Timestamp == incident.Timestamp &&
                        i.ProcessId == incident.ProcessId &&
                        string.Equals(i.CharacterName, incident.CharacterName, StringComparison.OrdinalIgnoreCase) &&
                        i.State == incident.State &&
                        i.RootCause == incident.RootCause &&
                        i.DeliveredToDiscord == incident.DeliveredToDiscord &&
                        string.Equals(i.Evidence, incident.Evidence, StringComparison.Ordinal));
                    if (exists)
                    {
                        continue;
                    }

                    RecentIncidents.Insert(0, incident);
                    if (RecentIncidents.Count > 300)
                    {
                        RecentIncidents.RemoveAt(RecentIncidents.Count - 1);
                    }
                }
            });
            _ = SafeRefreshMonitoringStatesAsync();
        };
        MonitoringEngine.DiagnosticLog += (_, message) => AddUiLog(message);
    }

    public async Task InitializeAsync()
    {
        Configuration = await _configurationStore.LoadConfigurationAsync();
        EnsureUserMappings();
        IReadOnlyList<MonitoringIncident> incidents = await _configurationStore.LoadIncidentsAsync();
        foreach (MonitoringIncident incident in incidents.Reverse())
        {
            RecentIncidents.Add(incident);
        }

        await RefreshMonitoringStatesAsync();
    }

    public async Task SaveConfigurationAsync()
    {
        EnsureUserMappings();
        await _configurationStore.SaveConfigurationAsync(Configuration);
    }

    public void RefreshProcesses()
    {
        List<ProcessSnapshot> snapshots = _processRegistryService
            .DiscoverRagnarokProcesses(_socketInspector, _characterNameReader)
            .ToList();
        RunOnUiThread(() =>
        {
            KnownProcesses.Clear();
            foreach (ProcessSnapshot snapshot in snapshots)
            {
                KnownProcesses.Add(snapshot);
            }
        });
    }

    public async Task RefreshMonitoringStatesAsync()
    {
        List<ProcessSnapshot> snapshots = _processRegistryService
            .DiscoverRagnarokProcesses(_socketInspector, _characterNameReader)
            .ToList();
        bool internetAvailable = Configuration.Monitoring.SimulateInternetDown
            ? false
            : await MonitoringClassifier.ProbeInternetAsync();
        Dictionary<string, CharacterConfig> byCharacterName = Configuration.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.CharacterName))
            .GroupBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        List<ProcessMonitoringState> nextStates = [];
        foreach (ProcessSnapshot snapshot in snapshots)
        {
            CharacterConfig? character = null;
            if (!string.IsNullOrWhiteSpace(snapshot.MemoryCharacterName))
            {
                byCharacterName.TryGetValue(snapshot.MemoryCharacterName, out character);
            }
            (RuntimeState state, RootCause rootCause, _) = MonitoringClassifier.Classify(snapshot, internetAvailable);

            nextStates.Add(new ProcessMonitoringState
            {
                ProcessId = snapshot.ProcessId,
                ProcessName = snapshot.ProcessName,
                CharacterName = !string.IsNullOrWhiteSpace(snapshot.MemoryCharacterName)
                    ? snapshot.MemoryCharacterName
                    : "(unbound)",
                Username = character?.Username ?? string.Empty,
                Diagnostic = snapshot.MemoryReadDiagnostic,
                State = state,
                RootCause = rootCause,
                RemotePorts = snapshot.RemotePorts,
                AlertEnabled = character?.AlertEnabled ?? false,
                ReloginEnabled = character?.ReloginEnabled ?? false
            });
        }

        await RunOnUiThreadAsync(() =>
        {
            KnownProcesses.Clear();
            foreach (ProcessSnapshot snapshot in snapshots)
            {
                KnownProcesses.Add(snapshot);
            }

            ProcessStates.Clear();
            foreach (ProcessMonitoringState state in nextStates.OrderBy(s => s.ProcessId))
            {
                ProcessStates.Add(state);
            }
        });
    }

    private async Task SafeRefreshMonitoringStatesAsync()
    {
        try
        {
            await RefreshMonitoringStatesAsync();
        }
        catch (Exception ex)
        {
            AddUiLog($"refresh monitoring states exception: {ex}");
        }
    }

    public async Task UpdateProcessTogglesAsync(string characterName, bool alertEnabled, bool reloginEnabled)
    {
        CharacterConfig? character = Configuration.Characters.FirstOrDefault(c =>
            string.Equals(c.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));
        if (character is null)
        {
            return;
        }

        character.AlertEnabled = alertEnabled;
        character.ReloginEnabled = reloginEnabled;
        await SaveConfigurationAsync();
        await RefreshMonitoringStatesAsync();
    }

    public void AddUiLog(string message)
    {
        _logger.LogInformation("{LogMessage}", message);
    }

    public IReadOnlyList<string> ReadRecentLogLines(int maxLines = 200)
    {
        maxLines = Math.Max(1, maxLines);
        if (!Directory.Exists(LogDirectoryPath))
        {
            return [];
        }

        List<string> allLines = [];
        foreach (string file in Directory.GetFiles(LogDirectoryPath, "*.log")
                     .OrderByDescending(Path.GetFileName))
        {
            List<string> lines = [];
            using (FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new(stream))
            {
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (line is not null)
                    {
                        lines.Add(line);
                    }
                }
            }

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                allLines.Add(lines[i]);
                if (allLines.Count >= maxLines)
                {
                    return allLines;
                }
            }
        }

        return allLines;
    }

    private void EnsureUserMappings()
    {
        HashSet<string> existingUsers = Configuration.Users
            .Select(u => u.Username)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string username in Configuration.Characters
                     .Select(c => c.Username)
                     .Where(u => !string.IsNullOrWhiteSpace(u))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existingUsers.Contains(username))
            {
                continue;
            }

            Configuration.Users.Add(new UserDiscordMap
            {
                Username = username,
                DiscordUserId = string.Empty
            });
            existingUsers.Add(username);
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (App.UiDispatcher is null || App.UiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        App.UiDispatcher.TryEnqueue(() => action());
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (App.UiDispatcher is null || App.UiDispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource tcs = new();
        bool enqueued = App.UiDispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue action on UI dispatcher."));
        }

        return tcs.Task;
    }
}

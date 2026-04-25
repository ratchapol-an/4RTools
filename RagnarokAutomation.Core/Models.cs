using System.Text.Json.Serialization;

namespace RagnarokAutomation.Core;

public sealed class AppConfiguration
{
    public List<UserDiscordMap> Users { get; set; } = [];
    public List<CharacterConfig> Characters { get; set; } = [];
    public MonitoringSettings Monitoring { get; set; } = new();
}

public sealed class UserDiscordMap
{
    public string Username { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;
}

public sealed class CharacterConfig
{
    public string CharacterName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordCipherText { get; set; } = string.Empty;
    public int SlotRow { get; set; }
    public int SlotColumn { get; set; }
    public bool AlertEnabled { get; set; } = true;
    public bool ReloginEnabled { get; set; }
}

public sealed class MonitoringSettings
{
    public string DiscordBotToken { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 5;
    public int DedupWindowMinutes { get; set; } = 5;
    public int IncidentHeartbeatMinutes { get; set; } = 30;
    public bool SimulateInternetDown { get; set; }
}

public enum RuntimeState
{
    Unknown,
    InGame,
    Disconnected,
    Dead
}

public enum RootCause
{
    Unknown,
    InternetDown,
    ServerDown,
    ClientCrashed,
    CharacterDead
}

public sealed class ProcessSnapshot
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public bool IsProcessAlive { get; set; }
    public int EstablishedConnections { get; set; }
    public int ClosingConnections { get; set; }
    public string MemoryCharacterName { get; set; } = string.Empty;
    public string MemoryReadDiagnostic { get; set; } = string.Empty;
    public string RemotePorts { get; set; } = string.Empty;
}

public sealed class ProcessMonitoringState
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = "(unbound)";
    public string Username { get; set; } = string.Empty;
    public string Diagnostic { get; set; } = string.Empty;
    public RuntimeState State { get; set; } = RuntimeState.Unknown;
    public RootCause RootCause { get; set; } = RootCause.Unknown;
    public string RemotePorts { get; set; } = string.Empty;
    public bool AlertEnabled { get; set; }
    public bool ReloginEnabled { get; set; }
}

public sealed class MonitoringIncident
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? RecoveredAt { get; set; }
    public int ProcessId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public RuntimeState State { get; set; }
    public RootCause RootCause { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; } = 1;
    public string HostName { get; set; } = Environment.MachineName;
    public bool DeliveredToDiscord { get; set; }

    [JsonIgnore]
    public string DedupKey => $"{ProcessId}:{CharacterName}:{State}:{RootCause}";
}

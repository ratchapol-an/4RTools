using System.Text.Json;

namespace RagnarokAutomation.Core;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _baseDirectory;
    private readonly string _configPath;
    private readonly string _incidentPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BaseDirectory => _baseDirectory;
    public string ConfigPath => _configPath;
    public string IncidentPath => _incidentPath;

    public ConfigurationStore(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        _configPath = Path.Combine(_baseDirectory, "config.json");
        _incidentPath = Path.Combine(_baseDirectory, "incidents.json");
        Directory.CreateDirectory(_baseDirectory);
    }

    public async Task<AppConfiguration> LoadConfigurationAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_configPath))
            {
                return new AppConfiguration();
            }

            await using FileStream fs = File.OpenRead(_configPath);
            AppConfiguration? config = await JsonSerializer.DeserializeAsync<AppConfiguration>(fs, JsonOptions).ConfigureAwait(false);
            return config ?? new AppConfiguration();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveConfigurationAsync(AppConfiguration configuration)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using FileStream fs = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(fs, configuration, JsonOptions).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<MonitoringIncident>> LoadIncidentsAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_incidentPath))
            {
                return [];
            }

            await using FileStream fs = File.OpenRead(_incidentPath);
            List<MonitoringIncident>? incidents = await JsonSerializer.DeserializeAsync<List<MonitoringIncident>>(fs, JsonOptions).ConfigureAwait(false);
            return incidents ?? [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendIncidentAsync(MonitoringIncident incident, int maxItems = 1000)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<MonitoringIncident> incidents = [];
            if (File.Exists(_incidentPath))
            {
                await using FileStream readStream = File.OpenRead(_incidentPath);
                incidents = await JsonSerializer.DeserializeAsync<List<MonitoringIncident>>(readStream, JsonOptions).ConfigureAwait(false) ?? [];
            }

            incidents.Add(incident);
            if (incidents.Count > maxItems)
            {
                incidents = incidents.TakeLast(maxItems).ToList();
            }

            await using FileStream writeStream = File.Create(_incidentPath);
            await JsonSerializer.SerializeAsync(writeStream, incidents, JsonOptions).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}

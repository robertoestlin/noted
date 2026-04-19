using System.Text.Json;
using Noted.Services;

const string SettingsFileName = "settings.json";
const int DefaultUptimeHeartbeatSeconds = 300;

var runtime = LoadRuntimeSettings();
var audioSnapshotService = new AudioSessionSnapshotService();

var path = UptimeHeartbeatService.GetHeartbeatFilePath(runtime.BackupFolder, DateTimeOffset.Now);
var lastHeartbeatAtLocal = UptimeHeartbeatService.ReadLastHeartbeatTimestamp(path) ?? DateTimeOffset.MinValue;

while (true)
{
    var nextHeartbeatAtLocal = UptimeHeartbeatService.GetNextHeartbeatAtLocal(
        DateTimeOffset.Now,
        runtime.UptimeHeartbeatSeconds,
        lastHeartbeatAtLocal);

    var wait = nextHeartbeatAtLocal - DateTimeOffset.Now;
    if (wait <= TimeSpan.Zero)
        wait = TimeSpan.FromMilliseconds(200);

    await Task.Delay(wait);

    try
    {
        UptimeHeartbeatService.AppendHeartbeatTimestamp(
            runtime.BackupFolder,
            nextHeartbeatAtLocal,
            audioSnapshotService.CaptureOutputAudioSummary,
            "s",
            ref lastHeartbeatAtLocal);
    }
    catch
    {
        // Best-effort activity marker; ignore failures and continue.
    }
}

static RuntimeSettings LoadRuntimeSettings()
{
    var defaultBackupFolder = DefaultBackupFolder();
    var bootstrap = ReadSettings(Path.Combine(defaultBackupFolder, SettingsFileName));
    var configuredBackup = NormalizePathOrFallback(bootstrap?.BackupFolder, defaultBackupFolder);
    var effective = bootstrap;

    var canonicalSettingsPath = Path.Combine(configuredBackup, SettingsFileName);
    if (!string.Equals(Path.GetFullPath(canonicalSettingsPath), Path.GetFullPath(Path.Combine(defaultBackupFolder, SettingsFileName)), StringComparison.OrdinalIgnoreCase))
    {
        var canonical = ReadSettings(canonicalSettingsPath);
        if (canonical != null)
            effective = canonical;
    }

    var interval = DefaultUptimeHeartbeatSeconds;
    if (effective?.UptimeHeartbeatSeconds is int configuredInterval
        && configuredInterval >= 60
        && configuredInterval <= 3600
        && 3600 % configuredInterval == 0)
    {
        interval = configuredInterval;
    }

    return new RuntimeSettings
    {
        BackupFolder = NormalizePathOrFallback(effective?.BackupFolder, configuredBackup),
        UptimeHeartbeatSeconds = interval,
        UseStandaloneHeartbeatApp = effective?.UseStandaloneHeartbeatApp == true
    };
}

static HeartbeatSettingsFile? ReadSettings(string path)
{
    try
    {
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<HeartbeatSettingsFile>(File.ReadAllText(path));
    }
    catch
    {
        return null;
    }
}

static string NormalizePathOrFallback(string? configuredPath, string fallbackPath)
{
    if (string.IsNullOrWhiteSpace(configuredPath))
        return fallbackPath;

    try
    {
        return Path.GetFullPath(configuredPath.Trim());
    }
    catch
    {
        return fallbackPath;
    }
}

static string DefaultBackupFolder() => @"c:\tools\backup\noted";

file sealed class RuntimeSettings
{
    public string BackupFolder { get; set; } = @"c:\tools\backup\noted";
    public int UptimeHeartbeatSeconds { get; set; } = 300;
    public bool UseStandaloneHeartbeatApp { get; set; }
}

file sealed class HeartbeatSettingsFile
{
    public string? BackupFolder { get; set; }
    public int? UptimeHeartbeatSeconds { get; set; }
    public bool UseStandaloneHeartbeatApp { get; set; }
}

using System.IO;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

public sealed class WindowSettingsService
{
    public sealed record LoadResult(
        WindowSettings BootstrapSettings,
        WindowSettings EffectiveSettings,
        string BootstrapBackupFolder,
        string BootstrapCloudBackupFolder,
        string EffectiveSettingsJsonPath);

    /// <summary>Resolved backup paths and <see cref="WindowSettings.LastNotedVersion"/> read from effective <c>settings.json</c> before full load.</summary>
    public sealed record StartupPathsProbe(string EffectiveBackupFolder, string DefaultBackupFolder, string EffectiveSettingsJsonPath, string? LastNotedVersionOnDisk);

    public StartupPathsProbe BuildStartupPathsProbe(
        WindowSettingsStore store,
        string defaultBackupFolder,
        string defaultCloudBackupFolder,
        string settingsFileName)
    {
        var defaultSettingsPath = Path.Combine(defaultBackupFolder, settingsFileName);
        if (!File.Exists(defaultSettingsPath))
        {
            return new StartupPathsProbe(defaultBackupFolder, defaultBackupFolder, defaultSettingsPath, null);
        }

        var boot = store.Load<WindowSettings>(defaultSettingsPath);
        if (boot == null)
            return new StartupPathsProbe(defaultBackupFolder, defaultBackupFolder, defaultSettingsPath, null);

        var bootstrapBackupFolder = NormalizePathOrFallback(boot.BackupFolder, defaultBackupFolder);
        _ = NormalizePathOrFallback(boot.CloudBackupFolder, defaultCloudBackupFolder);

        var canonicalPath = Path.Combine(bootstrapBackupFolder, settingsFileName);
        var effective = boot;
        if (File.Exists(canonicalPath)
            && !string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(defaultSettingsPath), StringComparison.OrdinalIgnoreCase))
        {
            var full = store.Load<WindowSettings>(canonicalPath);
            if (full != null)
                effective = full;
        }

        return new StartupPathsProbe(bootstrapBackupFolder, defaultBackupFolder, canonicalPath, effective.LastNotedVersion);
    }

    public void SaveWithBootstrap(
        WindowSettings state,
        string backupFolder,
        string defaultBackupFolder,
        string settingsFileName,
        JsonSerializerOptions options)
    {
        var primary = Path.Combine(backupFolder, settingsFileName);
        var serialized = JsonSerializer.Serialize(state, options);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(primary, serialized);

        if (string.Equals(Path.GetFullPath(backupFolder), Path.GetFullPath(defaultBackupFolder), StringComparison.OrdinalIgnoreCase))
            return;

        var bootstrap = new WindowSettings { BackupFolder = backupFolder };
        var bootstrapPath = Path.Combine(defaultBackupFolder, settingsFileName);
        var bootstrapJson = JsonSerializer.Serialize(bootstrap, options);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(bootstrapPath, bootstrapJson);
    }

    public LoadResult? LoadWithFallback(
        WindowSettingsStore store,
        string defaultBackupFolder,
        string defaultCloudBackupFolder,
        string settingsFileName)
    {
        var defaultPath = Path.Combine(defaultBackupFolder, settingsFileName);
        var boot = store.Load<WindowSettings>(defaultPath);
        if (boot == null)
            return null;

        var bootstrapBackupFolder = NormalizePathOrFallback(boot.BackupFolder, defaultBackupFolder);
        var bootstrapCloudBackupFolder = NormalizePathOrFallback(boot.CloudBackupFolder, defaultCloudBackupFolder);

        var canonicalPath = Path.Combine(bootstrapBackupFolder, settingsFileName);
        var effective = boot;
        var effectivePath = defaultPath;
        if (File.Exists(canonicalPath)
            && !string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
        {
            var full = store.Load<WindowSettings>(canonicalPath);
            if (full != null)
            {
                effective = full;
                effectivePath = canonicalPath;
            }
        }

        return new LoadResult(boot, effective, bootstrapBackupFolder, bootstrapCloudBackupFolder, effectivePath);
    }

    public string NormalizePathOrFallback(string? configuredPath, string fallbackPath)
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

    public bool TryGetValidCloudHours(int? cloudHours, out int value)
    {
        if (cloudHours is >= 0 and <= 50)
        {
            value = cloudHours.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetValidCloudMinutes(int? cloudMinutes, out int value)
    {
        if (cloudMinutes is >= 0 and <= 55 && cloudMinutes.Value % 5 == 0)
        {
            value = cloudMinutes.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Plain-text folder sync interval minutes: 1–20 (every minute), then 25…55 in steps of 5;
    /// 0 is allowed only when <paramref name="syncHours"/> &gt; 0 (on-the-hour within a multi-hour interval).
    /// </summary>
    public bool TryGetValidPlainTextTabSyncMinutes(int? minutes, int syncHours, out int value)
    {
        if (minutes is not (>= 0 and <= 55))
        {
            value = default;
            return false;
        }

        var m = minutes.Value;
        if (m == 0)
        {
            if (syncHours <= 0)
            {
                value = default;
                return false;
            }

            value = 0;
            return true;
        }

        if ((m >= 1 && m <= 20) || (m >= 25 && m <= 55 && m % 5 == 0))
        {
            value = m;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetValidUptimeHeartbeatSeconds(int? uptimeHeartbeatSeconds, out int value)
    {
        if (uptimeHeartbeatSeconds is >= 60 and <= 3600
            && 3600 % uptimeHeartbeatSeconds.Value == 0)
        {
            value = uptimeHeartbeatSeconds.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetNormalizedUtc(DateTime? input, out DateTime utc)
    {
        if (input is DateTime value && value > DateTime.MinValue)
        {
            utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return true;
        }

        utc = default;
        return false;
    }
}

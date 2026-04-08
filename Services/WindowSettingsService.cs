using System.IO;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

public sealed class WindowSettingsService
{
    public sealed record LoadResult(WindowSettings BootstrapSettings, WindowSettings EffectiveSettings, string BootstrapBackupFolder, string BootstrapCloudBackupFolder);

    public void SaveWithBootstrap(
        WindowSettingsStore store,
        WindowSettings state,
        string backupFolder,
        string defaultBackupFolder,
        string settingsFileName,
        JsonSerializerOptions options)
    {
        var primary = Path.Combine(backupFolder, settingsFileName);
        store.Save(primary, state, options);

        if (string.Equals(Path.GetFullPath(backupFolder), Path.GetFullPath(defaultBackupFolder), StringComparison.OrdinalIgnoreCase))
            return;

        var bootstrap = new WindowSettings { BackupFolder = backupFolder };
        var bootstrapPath = Path.Combine(defaultBackupFolder, settingsFileName);
        store.Save(bootstrapPath, bootstrap, options);
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
        if (File.Exists(canonicalPath)
            && !string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
        {
            var full = store.Load<WindowSettings>(canonicalPath);
            if (full != null)
                effective = full;
        }

        return new LoadResult(boot, effective, bootstrapBackupFolder, bootstrapCloudBackupFolder);
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

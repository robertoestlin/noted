using System.IO;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private void RunStartupVersionProbeSnapshotAndLog()
    {
        try
        {
            var probe = _windowSettingsService.BuildStartupPathsProbe(
                _windowSettingsStore,
                DefaultBackupFolder(),
                DefaultCloudBackupFolder(),
                SettingsFileName);

            var current = NotedAppVersion.Current;
            var prevRaw = probe.LastNotedVersionOnDisk;
            var prevDisplay = string.IsNullOrWhiteSpace(prevRaw) ? "missing" : prevRaw.Trim();
            var slug = string.IsNullOrWhiteSpace(prevRaw)
                ? "missing"
                : PreUpgradeBackupService.SanitizeFolderSegment(prevRaw.Trim());

            var settingsExists = File.Exists(probe.EffectiveSettingsJsonPath);
            var needsSnapshot = settingsExists
                && (string.IsNullOrWhiteSpace(prevRaw)
                    || !string.Equals(prevRaw.Trim(), current, StringComparison.OrdinalIgnoreCase));

            string? snapshotPath = null;
            if (needsSnapshot)
            {
                var destRoot = Path.Combine(
                    probe.EffectiveBackupFolder,
                    PreUpgradeBackupService.PreUpgradeFolderName,
                    PreUpgradeBackupService.BuildSnapshotFolderName(slug, DateTime.Now));
                Directory.CreateDirectory(destRoot);
                PreUpgradeBackupService.CopyBackupTreeExcludingSnapshots(probe.EffectiveBackupFolder, destRoot);
                snapshotPath = destRoot;
            }

            var snapText = string.IsNullOrEmpty(snapshotPath) ? "(none)" : snapshotPath;
            AppLogAppendService.AppendLine(
                probe.EffectiveBackupFolder,
                AppLogFileName,
                $"Noted startup: detectedVersion={current} previousStoredVersion={prevDisplay} snapshot={snapText}");
        }
        catch
        {
            // best-effort
        }
    }

    private void MaybeStampLastNotedVersionAfterLoad()
    {
        try
        {
            var current = NotedAppVersion.Current;
            var prev = _persistedLastNotedVersionForJson?.Trim();
            if (string.IsNullOrEmpty(prev) || !string.Equals(prev, current, StringComparison.OrdinalIgnoreCase))
            {
                _persistedLastNotedVersionForJson = current;
                SaveWindowSettings();
            }
        }
        catch
        {
            // best-effort
        }
    }
}

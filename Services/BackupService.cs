using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Noted.Services;

public sealed class BackupService
{
    public string? GetLatestBackupFilePath(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        return Directory.GetFiles(folder, "noted_*.txt")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.FullName;
    }

    public DateTime GetLatestBackupWriteUtcOrMin(string folder)
    {
        var latest = GetLatestBackupFilePath(folder);
        if (latest == null)
            return DateTime.MinValue;

        try
        {
            return File.GetLastWriteTimeUtc(latest);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    public void PruneBackups(string backupFolder, Regex backupFileNameRegex, int maxBackups)
    {
        if (!Directory.Exists(backupFolder))
            return;

        var files = Directory.GetFiles(backupFolder, "noted_*.txt")
            .Where(path => backupFileNameRegex.IsMatch(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        while (files.Count > maxBackups)
        {
            File.Delete(files[0]);
            files.RemoveAt(0);
        }
    }

    public bool TryCopyBackupToFolder(string sourceBackupPath, string targetFolder)
    {
        if (!File.Exists(sourceBackupPath))
            return false;
        if (string.IsNullOrWhiteSpace(targetFolder))
            return false;

        try
        {
            Directory.CreateDirectory(targetFolder);
            var targetPath = Path.Combine(targetFolder, Path.GetFileName(sourceBackupPath));
            File.Copy(sourceBackupPath, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

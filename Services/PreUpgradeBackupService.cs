using System.IO;
using System.Linq;

namespace Noted.Services;

public static class PreUpgradeBackupService
{
    public const string PreUpgradeFolderName = "pre-upgrade-backups";
    private const string CloudFolderName = "cloud";

    /// <summary>Build folder name <c>yyyyMMdd_HHmmss_oldVersion</c> with filesystem-safe <paramref name="oldVersionSlug"/>.</summary>
    public static string BuildSnapshotFolderName(string oldVersionSlug, DateTime stampLocal)
    {
        var date = stampLocal.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var time = stampLocal.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"{date}_{time}_{SanitizeFolderSegment(oldVersionSlug)}";
    }

    public static string SanitizeFolderSegment(string raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0)
            return "none";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    /// <summary>Recursive copy of <paramref name="sourceRoot"/> into <paramref name="destinationRoot"/>; skips <c>pre-upgrade-backups</c> and <c>cloud</c> under source.</summary>
    public static void CopyBackupTreeExcludingSnapshots(string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destinationRoot))
            return;
        sourceRoot = Path.GetFullPath(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        destinationRoot = Path.GetFullPath(destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(destinationRoot);

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            var name = Path.GetFileName(entry);
            if (name.Length == 0)
                continue;
            if (string.Equals(name, PreUpgradeFolderName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(name, CloudFolderName, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(entry))
                continue;

            var dest = Path.Combine(destinationRoot, name);
            var attrs = File.GetAttributes(entry);
            if (attrs.HasFlag(FileAttributes.Directory))
            {
                CopyBackupTreeExcludingSnapshots(entry, dest);
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(entry, dest, overwrite: true);
                }
                catch
                {
                    // best-effort per file
                }
            }
        }
    }
}

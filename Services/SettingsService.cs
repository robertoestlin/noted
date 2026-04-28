using System.IO;

namespace Noted.Services;

public sealed class SettingsService
{
    public void EnsureFileExists(string folder, string fileName, Action createIfMissing)
    {
        try
        {
            var path = Path.Combine(folder, fileName);
            if (File.Exists(path))
                return;

            createIfMissing();
        }
        catch
        {
            // Non-critical.
        }
    }

    public void CopyFileIfExists(string fromFolder, string toFolder, string fileName)
    {
        var sourcePath = Path.Combine(fromFolder, fileName);
        if (!File.Exists(sourcePath))
            return;

        Directory.CreateDirectory(toFolder);
        var destinationPath = Path.Combine(toFolder, fileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    /// <returns>True if a file was copied (source missing or dest older/different size).</returns>
    public bool CopyFileIfExistsIfNewer(string fromFolder, string toFolder, string fileName)
    {
        var sourcePath = Path.Combine(fromFolder, fileName);
        if (!File.Exists(sourcePath))
            return false;

        Directory.CreateDirectory(toFolder);
        var destinationPath = Path.Combine(toFolder, fileName);
        if (!ShouldCopyByTimestampOrSize(sourcePath, destinationPath))
            return false;

        File.Copy(sourcePath, destinationPath, overwrite: true);
        return true;
    }

    private static bool ShouldCopyByTimestampOrSize(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(destinationPath))
                return true;

            var src = new FileInfo(sourcePath);
            var dst = new FileInfo(destinationPath);
            if (src.Length != dst.Length)
                return true;

            return src.LastWriteTimeUtc > dst.LastWriteTimeUtc;
        }
        catch
        {
            // If we can't compare, best effort: try copy.
            return true;
        }
    }
}

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
}

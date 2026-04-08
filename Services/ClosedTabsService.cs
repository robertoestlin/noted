using System.IO;
using System.Text.Json;

namespace Noted.Services;

public sealed class ClosedTabsService
{
    public string GetHistoryPath(string backupFolder, string fileName)
        => Path.Combine(backupFolder, fileName);

    public void SaveHistory<T>(string backupFolder, string fileName, IReadOnlyCollection<T> entries)
    {
        Directory.CreateDirectory(backupFolder);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var path = GetHistoryPath(backupFolder, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, options));
    }

    public List<T>? LoadHistory<T>(string backupFolder, string fileName)
    {
        var path = GetHistoryPath(backupFolder, fileName);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path));
    }
}

using System.IO;
using System.Text.Json;

namespace Noted.Services;

public sealed class WindowSettingsStore
{
    public void Save<T>(string path, T state, JsonSerializerOptions options)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        File.WriteAllText(path, JsonSerializer.Serialize(state, options));
    }

    public T? Load<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
}

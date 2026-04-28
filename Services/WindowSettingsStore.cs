using System.IO;
using System.Text.Json;

namespace Noted.Services;

public sealed class WindowSettingsStore
{
    /// <summary>Writes UTF-8 text only if content differs from the existing file (after newline normalization).</summary>
    public static void WriteUtf8IfChanged(string path, string newContents)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        if (ExistingTextEquals(path, newContents))
            return;

        File.WriteAllText(path, newContents);
    }

    static bool ExistingTextEquals(string path, string newContents)
    {
        try
        {
            var normalizedNew = NormalizeNewLines(newContents);
            if (!File.Exists(path))
                return false;

            var existing = File.ReadAllText(path);
            return string.Equals(NormalizeNewLines(existing), normalizedNew, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    static string NormalizeNewLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Writes <paramref name="newContents"/> only if it differs semantically from the existing UTF-8 file (property order ignored).</summary>
    public static void WriteUtf8IfSemanticJsonChanged(string path, string newContents)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                if (JsonSemanticEquality.AreEqual(existing, newContents))
                    return;
            }
        }
        catch
        {
            // Corrupt/old file → rewrite below.
        }

        File.WriteAllText(path, newContents);
    }

    public void Save<T>(string path, T state, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(state, options);
        WriteUtf8IfChanged(path, json);
    }

    public T? Load<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
}

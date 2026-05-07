using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Composes the built-in <see cref="ScriptPack"/> from files shipped under
/// <c>{AppContext.BaseDirectory}/Plugins/resources/script-packages/builtin/</c>.
/// The pack is described by <c>metadata.json</c> in that folder; each entry
/// references a sibling script file whose contents become the script body.
/// </summary>
public sealed class BuiltinScriptPackComposer
{
    public const string BuiltinFolderName = "builtin";
    public const string MetadataFileName = "metadata.json";

    public string GetSourceFolder()
        => Path.Combine(AppContext.BaseDirectory, "Plugins", "resources",
            ScriptPackService.SubfolderName, BuiltinFolderName);

    public ScriptPack? Compose()
    {
        var folder = GetSourceFolder();
        var manifestPath = Path.Combine(folder, MetadataFileName);
        if (!File.Exists(manifestPath)) return null;

        BuiltinPackManifest? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<BuiltinPackManifest>(json, JsonReadOptions);
        }
        catch
        {
            return null;
        }
        if (manifest == null) return null;

        var pack = new ScriptPack
        {
            Version = manifest.Version,
            Name = manifest.Name ?? "Built-in Scripts"
        };

        foreach (var entry in manifest.Scripts)
        {
            if (string.IsNullOrWhiteSpace(entry.File)) continue;
            var scriptPath = Path.Combine(folder, entry.File);
            if (!File.Exists(scriptPath)) continue;

            string body;
            try { body = File.ReadAllText(scriptPath); }
            catch { continue; }

            pack.Scripts.Add(new ScriptItem
            {
                Title = entry.Title ?? Path.GetFileNameWithoutExtension(entry.File),
                Language = entry.Language ?? string.Empty,
                Filename = entry.Filename ?? entry.File,
                Description = entry.Description ?? string.Empty,
                Body = body
            });
        }

        return pack;
    }

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class BuiltinPackManifest
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("scripts")] public List<BuiltinPackEntry> Scripts { get; set; } = new();
    }

    private sealed class BuiltinPackEntry
    {
        [JsonPropertyName("file")] public string? File { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}

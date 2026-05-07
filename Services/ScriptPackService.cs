using System.IO;
using System.Text;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Persists script packs to <c>{BackupFolder}/script-packages/</c> as one
/// plain-text file per pack. The file's first line is <c>version: N</c> so callers
/// can compare pack versions without parsing the whole body.
/// </summary>
public sealed class ScriptPackService
{
    public const string SubfolderName = "script-packages";
    public const string PackFileExtension = ".script-pack";
    public const string BuiltinPackFileName = "builtin" + PackFileExtension;

    private const string ScriptDelimiter = "=== script ===";
    private const string BodyDelimiter = "---";

    public string GetSubfolderPath(string backupFolder)
        => Path.Combine(backupFolder, SubfolderName);

    public string GetPackPath(string backupFolder, string packFileName)
        => Path.Combine(GetSubfolderPath(backupFolder), packFileName);

    public string GetBuiltinPackPath(string backupFolder)
        => GetPackPath(backupFolder, BuiltinPackFileName);

    public void EnsureFolderExists(string backupFolder)
    {
        try { Directory.CreateDirectory(GetSubfolderPath(backupFolder)); }
        catch { /* best effort */ }
    }

    public List<(string FileName, ScriptPack Pack)> LoadAllPacks(string backupFolder)
    {
        var result = new List<(string, ScriptPack)>();
        var folder = GetSubfolderPath(backupFolder);
        if (!Directory.Exists(folder))
            return result;

        foreach (var file in Directory.EnumerateFiles(folder, "*" + PackFileExtension))
        {
            try
            {
                var text = File.ReadAllText(file);
                var pack = ParsePack(text);
                if (pack != null)
                    result.Add((Path.GetFileName(file), pack));
            }
            catch
            {
                // Skip unreadable file — best effort.
            }
        }
        return result;
    }

    /// <summary>
    /// Reads only the first line of a pack file to extract its version. Returns -1 if missing
    /// or unparseable. Used by the startup upgrade check to avoid parsing whole files.
    /// </summary>
    public int ReadPackVersion(string filePath)
    {
        if (!File.Exists(filePath)) return -1;
        try
        {
            using var sr = new StreamReader(filePath);
            var first = sr.ReadLine();
            return TryParseVersionLine(first, out var v) ? v : -1;
        }
        catch
        {
            return -1;
        }
    }

    public void WritePack(string filePath, string packText)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, packText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static ScriptPack? ParsePack(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) return null;
        if (!TryParseVersionLine(lines[0], out var version))
            return null;

        var pack = new ScriptPack { Version = version };

        int i = 1;
        // Top-level headers (just "name:" today) until the first "=== script ===".
        while (i < lines.Length && !IsScriptDelimiter(lines[i]))
        {
            var line = lines[i];
            if (TryParseHeader(line, out var key, out var value))
            {
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    pack.Name = value;
            }
            i++;
        }

        while (i < lines.Length)
        {
            if (!IsScriptDelimiter(lines[i])) { i++; continue; }
            i++; // consume delimiter

            var item = new ScriptItem();
            while (i < lines.Length && !IsBodyDelimiter(lines[i]) && !IsScriptDelimiter(lines[i]))
            {
                if (TryParseHeader(lines[i], out var key, out var value))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "title": item.Title = value; break;
                        case "language": item.Language = value; break;
                        case "filename": item.Filename = value; break;
                        case "description": item.Description = value; break;
                    }
                }
                i++;
            }

            if (i < lines.Length && IsBodyDelimiter(lines[i]))
                i++; // consume body delimiter

            var bodyStart = i;
            while (i < lines.Length && !IsScriptDelimiter(lines[i]))
                i++;

            int bodyEnd = i;
            // Trim a single trailing blank line that precedes the next delimiter so the
            // round-trip serializer produces stable output.
            if (bodyEnd > bodyStart && lines[bodyEnd - 1].Length == 0)
                bodyEnd--;

            var body = string.Join('\n', lines, bodyStart, bodyEnd - bodyStart);
            item.Body = body;

            if (!string.IsNullOrEmpty(item.Title) || !string.IsNullOrEmpty(item.Body))
                pack.Scripts.Add(item);
        }

        return pack;
    }

    public static string SerializePack(ScriptPack pack)
    {
        var sb = new StringBuilder();
        sb.Append("version: ").Append(pack.Version).Append('\n');
        if (!string.IsNullOrWhiteSpace(pack.Name))
            sb.Append("name: ").Append(pack.Name).Append('\n');

        foreach (var s in pack.Scripts)
        {
            sb.Append('\n').Append(ScriptDelimiter).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Title))
                sb.Append("title: ").Append(s.Title).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Language))
                sb.Append("language: ").Append(s.Language).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Filename))
                sb.Append("filename: ").Append(s.Filename).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Description))
                sb.Append("description: ").Append(s.Description).Append('\n');
            sb.Append(BodyDelimiter).Append('\n');
            sb.Append(s.Body ?? string.Empty);
            if (s.Body == null || !s.Body.EndsWith('\n'))
                sb.Append('\n');
        }

        return sb.ToString();
    }

    private static bool TryParseVersionLine(string? line, out int version)
    {
        version = 0;
        if (line == null) return false;
        if (!TryParseHeader(line, out var key, out var value)) return false;
        if (!string.Equals(key, "version", StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(value, out version);
    }

    private static bool TryParseHeader(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var idx = line.IndexOf(':');
        if (idx <= 0) return false;
        key = line[..idx].Trim();
        value = line[(idx + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool IsScriptDelimiter(string line)
        => line.Trim().Equals(ScriptDelimiter, StringComparison.Ordinal);

    private static bool IsBodyDelimiter(string line)
        => line.Trim().Equals(BodyDelimiter, StringComparison.Ordinal);
}

using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Noted.Services;

public sealed class BackupBundleService
{
    public sealed record BackupBundleSection(string Header, string Content, string? MetadataPayload);

    public string CreateBackupFilePath(string backupFolder, DateTime localTimestamp)
    {
        var timestamp = localTimestamp.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(backupFolder, $"noted_{timestamp}.txt");
    }

    public string ReadBundleText(string path)
        => File.ReadAllText(path, Encoding.UTF8);

    public DateTime GetLastWriteTime(string path)
        => File.GetLastWriteTime(path);

    public void WriteBundle(
        string path,
        IEnumerable<BackupBundleSection> sections,
        string bundleDivider,
        string metadataPrefix)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        foreach (var section in sections)
        {
            writer.WriteLine($"{bundleDivider}{section.Header}{bundleDivider}");
            if (!string.IsNullOrWhiteSpace(section.MetadataPayload))
                writer.WriteLine($"{metadataPrefix} {section.MetadataPayload}");
            writer.Write(section.Content);
            writer.WriteLine();
        }
    }

    public List<BackupBundleSection> ParseBundle(string text, string metadataPrefix)
    {
        var dividerNew = new Regex(@"^\^---(.*)\^---\r?$", RegexOptions.Multiline);
        var dividerOld = new Regex(@"^====(.*)====\r?$", RegexOptions.Multiline);
        var matches = dividerNew.Matches(text);
        if (matches.Count == 0)
            matches = dividerOld.Matches(text);

        var sections = new List<BackupBundleSection>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            var header = matches[i].Groups[1].Value;
            int contentStart = matches[i].Index + matches[i].Length;

            if (contentStart < text.Length && text[contentStart] == '\r')
                contentStart++;
            if (contentStart < text.Length && text[contentStart] == '\n')
                contentStart++;

            string? metadataPayload = null;
            if (TryReadMetadataPayloadLine(text, contentStart, metadataPrefix, out var parsedPayload, out var contentAfterMetadata))
            {
                metadataPayload = parsedPayload;
                contentStart = contentAfterMetadata;
            }

            int contentEnd = i + 1 < matches.Count
                ? matches[i + 1].Index
                : text.Length;

            var content = text[contentStart..contentEnd];
            if (content.EndsWith("\r\n", StringComparison.Ordinal))
                content = content[..^2];
            else if (content.EndsWith('\n'))
                content = content[..^1];

            sections.Add(new BackupBundleSection(header, content, metadataPayload));
        }

        return sections;
    }

    private static bool TryReadMetadataPayloadLine(
        string text,
        int startIndex,
        string metadataPrefix,
        out string payload,
        out int nextContentStart)
    {
        payload = string.Empty;
        nextContentStart = startIndex;
        if (startIndex >= text.Length)
            return false;

        int lineEnd = text.IndexOf('\n', startIndex);
        if (lineEnd < 0)
            lineEnd = text.Length;

        var lineText = text[startIndex..lineEnd];
        if (lineText.EndsWith('\r'))
            lineText = lineText[..^1];
        if (!lineText.StartsWith(metadataPrefix, StringComparison.Ordinal))
            return false;

        payload = lineText[metadataPrefix.Length..].Trim();
        if (payload.Length == 0 || payload[0] != '{')
            return false;

        // Legacy marker guard: only treat known metadata JSON as metadata.
        if (!payload.Contains("\"HighlightLine\"", StringComparison.Ordinal)
            && !payload.Contains("\"HighlightLines\"", StringComparison.Ordinal)
            && !payload.Contains("\"Assignees\"", StringComparison.Ordinal)
            && !payload.Contains("\"LastSavedUtc\"", StringComparison.Ordinal)
            && !payload.Contains("\"LastChangedUtc\"", StringComparison.Ordinal)
            && !payload.Contains("\"EndsWithNewline\"", StringComparison.Ordinal))
            return false;

        nextContentStart = lineEnd < text.Length ? lineEnd + 1 : lineEnd;
        return true;
    }
}

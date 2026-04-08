using System.IO;
using System.Linq;
using System.Text.Json;
using Noted.Models;

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

    public List<ClosedTabEntry> NormalizeHistory(IEnumerable<ClosedTabEntry> entries, int maxClosedTabs)
    {
        var normalized = new List<ClosedTabEntry>();

        foreach (var entry in entries
                     .Where(entry => entry != null)
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Header))
                     .Take(maxClosedTabs))
        {
            var highlightedLines = entry.Metadata?.HighlightLines?
                .Where(line => line > 0)
                .Distinct()
                .OrderBy(line => line)
                .ToList();

            var assignees = entry.Metadata?.Assignees?
                .Where(assignee => assignee != null && assignee.Line > 0 && !string.IsNullOrWhiteSpace(assignee.Person))
                .Select(assignee => new FileLineAssignee
                {
                    Line = assignee.Line,
                    Person = assignee.Person.Trim()
                })
                .OrderBy(assignee => assignee.Line)
                .ToList();

            normalized.Add(new ClosedTabEntry
            {
                Header = entry.Header.Trim(),
                Content = entry.Content ?? string.Empty,
                IsDirty = entry.IsDirty,
                Metadata = entry.Metadata == null
                    ? null
                    : new FileMetadata
                    {
                        HighlightLine = entry.Metadata.HighlightLine,
                        HighlightLines = highlightedLines != null && highlightedLines.Count > 0 ? highlightedLines : null,
                        Assignees = assignees != null && assignees.Count > 0 ? assignees : null,
                        LastSavedUtc = entry.Metadata.LastSavedUtc,
                        LastChangedUtc = entry.Metadata.LastChangedUtc
                    }
            });
        }

        return normalized;
    }
}

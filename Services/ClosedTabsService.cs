using System.IO;
using System.Linq;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

public sealed class ClosedTabsService
{
    private const int MinClosedTabsCount = 1;
    private const int MaxClosedTabsCount = 500;
    private const int MinRetentionDays = 0;
    private const int MaxRetentionDays = 3650;

    public string GetHistoryPath(string backupFolder, string fileName)
        => Path.Combine(backupFolder, fileName);

    public void SaveHistory<T>(string backupFolder, string fileName, IReadOnlyCollection<T> entries)
    {
        Directory.CreateDirectory(backupFolder);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var path = GetHistoryPath(backupFolder, fileName);
        WindowSettingsStore.WriteUtf8IfChanged(path, JsonSerializer.Serialize(entries, options));
    }

    public List<T>? LoadHistory<T>(string backupFolder, string fileName)
    {
        var path = GetHistoryPath(backupFolder, fileName);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path));
    }

    public List<ClosedTabEntry> NormalizeHistory(IEnumerable<ClosedTabEntry> entries, int maxClosedTabs, int retentionDays)
    {
        var normalizedMaxCount = Math.Clamp(maxClosedTabs, MinClosedTabsCount, MaxClosedTabsCount);
        var normalizedRetentionDays = Math.Clamp(retentionDays, MinRetentionDays, MaxRetentionDays);
        var retentionThresholdUtc = normalizedRetentionDays > 0
            ? DateTime.UtcNow.AddDays(-normalizedRetentionDays)
            : DateTime.MinValue;
        var normalized = new List<ClosedTabEntry>();

        foreach (var normalizedEntry in entries
                     .Where(entry => entry != null)
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Header))
                     .Select(entry =>
                     {
                         var closedAtUtc = entry.ClosedAtUtc?.ToUniversalTime()
                             ?? entry.Metadata?.LastChangedUtc?.ToUniversalTime()
                             ?? entry.Metadata?.LastSavedUtc?.ToUniversalTime()
                             ?? DateTime.UtcNow;
                         return new { Entry = entry, ClosedAtUtc = closedAtUtc };
                     })
                     .Where(item => item.ClosedAtUtc >= retentionThresholdUtc)
                     .Take(normalizedMaxCount))
        {
            var entry = normalizedEntry.Entry;
            var closedAtUtc = normalizedEntry.ClosedAtUtc;
            var highlightedLines = entry.Metadata?.HighlightLines?
                .Where(line => line > 0)
                .Distinct()
                .OrderBy(line => line)
                .ToList();
            var criticalHighlightedLines = entry.Metadata?.CriticalHighlightLines?
                .Where(line => line > 0)
                .Distinct()
                .OrderBy(line => line)
                .ToList();

            var assignees = entry.Metadata?.Assignees?
                .Where(assignee => assignee != null && assignee.Line > 0 && !string.IsNullOrWhiteSpace(assignee.Person))
                .Select(assignee => new FileLineAssignee
                {
                    Line = assignee.Line,
                    Person = assignee.Person.Trim(),
                    CreatedUtc = assignee.CreatedUtc
                })
                .OrderBy(assignee => assignee.Line)
                .ToList();

            normalized.Add(new ClosedTabEntry
            {
                Header = entry.Header.Trim(),
                Content = entry.Content ?? string.Empty,
                IsDirty = entry.IsDirty,
                ClosedAtUtc = closedAtUtc,
                Metadata = entry.Metadata == null
                    ? null
                    : new FileMetadata
                    {
                        HighlightLine = entry.Metadata.HighlightLine,
                        HighlightLines = highlightedLines != null && highlightedLines.Count > 0 ? highlightedLines : null,
                        CriticalHighlightLines = criticalHighlightedLines != null && criticalHighlightedLines.Count > 0
                            ? criticalHighlightedLines
                            : null,
                        Assignees = assignees != null && assignees.Count > 0 ? assignees : null,
                        LastSavedUtc = entry.Metadata.LastSavedUtc,
                        LastChangedUtc = entry.Metadata.LastChangedUtc,
                        TabId = entry.Metadata.TabId
                    }
            });
        }

        return normalized;
    }
}

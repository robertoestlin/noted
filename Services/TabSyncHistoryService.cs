using System.IO;
using System.Linq;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

public sealed class TabSyncHistoryService
{
    public const string FileName = "tab-sync-history.json";
    private const int DefaultMaxEntries = 200;

    private readonly object _gate = new();
    private List<TabSyncHistoryEntry> _entries = [];

    public IReadOnlyList<TabSyncHistoryEntry> GetEntries()
    {
        lock (_gate)
            return _entries.ToList();
    }

    public void Load(string backupFolder)
    {
        var path = Path.Combine(backupFolder, FileName);
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                _entries = [];
                return;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<List<TabSyncHistoryEntry>>(File.ReadAllText(path));
                _entries = loaded ?? [];
                _entries.Sort((a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));
            }
            catch
            {
                _entries = [];
            }
        }
    }

    public void Append(string backupFolder, TabSyncHistoryEntry entry, int maxEntries = DefaultMaxEntries)
    {
        if (entry == null)
            return;

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > maxEntries)
                _entries.RemoveRange(maxEntries, _entries.Count - maxEntries);
            Persist(backupFolder);
        }
    }

    public bool TryMarkResolved(string backupFolder, DateTime entryTimestampUtc, string tabId, string tabHeader, string conflictResolution)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.TimestampUtc == entryTimestampUtc);
            if (entry == null)
                return false;
            var item = entry.Items.FirstOrDefault(i =>
                i.Status == TabSyncItemStatus.Conflict
                && !i.Resolved
                && MatchesConflictItem(i, tabId, tabHeader));
            if (item == null)
                return false;
            item.Resolved = true;
            item.ConflictResolution = conflictResolution;
            Persist(backupFolder);
            return true;
        }
    }

    private static bool MatchesConflictItem(TabSyncItem item, string tabId, string tabHeader)
    {
        if (!string.IsNullOrEmpty(tabId)
            && !string.IsNullOrEmpty(item.TabId)
            && string.Equals(item.TabId, tabId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(tabId) && string.IsNullOrEmpty(item.TabId))
            return string.Equals(item.TabHeader, tabHeader, StringComparison.Ordinal);

        if (string.IsNullOrEmpty(tabId))
            return string.Equals(item.TabHeader, tabHeader, StringComparison.Ordinal);

        return false;
    }

    /// <summary>All instream conflict rows (including resolved), newest entry first.</summary>
    public IReadOnlyList<(TabSyncHistoryEntry Entry, TabSyncItem Item)> GetConflictListEntries()
    {
        lock (_gate)
        {
            return _entries
                .OrderByDescending(e => e.TimestampUtc)
                .SelectMany(e => e.Items
                    .Where(i => i.Status == TabSyncItemStatus.Conflict)
                    .Select(i => (Entry: e, Item: i)))
                .ToList();
        }
    }

    private void Persist(string backupFolder)
    {
        try
        {
            Directory.CreateDirectory(backupFolder);
            var path = Path.Combine(backupFolder, FileName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            WindowSettingsStore.WriteUtf8IfChanged(path, JsonSerializer.Serialize(_entries, options));
        }
        catch
        {
            /* non-critical */
        }
    }
}

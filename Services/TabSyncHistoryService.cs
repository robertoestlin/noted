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

    public bool TryMarkResolved(string backupFolder, DateTime entryTimestampUtc, string tabHeader)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.TimestampUtc == entryTimestampUtc);
            if (entry == null)
                return false;
            var item = entry.Items.FirstOrDefault(i =>
                string.Equals(i.TabHeader, tabHeader, StringComparison.Ordinal)
                && i.Status == TabSyncItemStatus.Conflict
                && !i.Resolved);
            if (item == null)
                return false;
            item.Resolved = true;
            Persist(backupFolder);
            return true;
        }
    }

    public IReadOnlyList<(TabSyncHistoryEntry Entry, TabSyncItem Item)> GetUnresolvedConflicts()
    {
        lock (_gate)
        {
            return _entries
                .SelectMany(e => e.Items
                    .Where(i => i.Status == TabSyncItemStatus.Conflict && !i.Resolved)
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

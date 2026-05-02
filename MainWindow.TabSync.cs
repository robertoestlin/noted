using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private Window? _tabSyncWindow;
    private TabSyncWindowState? _tabSyncWindowState;

    private void RecordTabSyncHistory(TabSyncDirection direction, List<TabSyncItem> items)
    {
        var entry = new TabSyncHistoryEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Direction = direction,
            Items = items
        };
        _tabSyncHistoryService.Append(_backupFolder, entry);
        _tabSyncWindowState?.Refresh();
    }

    private TimeSpan PlainTabsFolderSyncInterval()
        => TimeSpan.FromHours(_cloudSyncTabsPlainTextSyncHours)
           + TimeSpan.FromMinutes(_cloudSyncTabsPlainTextSyncMinutes);

    /// <summary>
    /// Runs instream (if enabled) then outstream (if enabled) on one timer schedule so pull always happens before push.
    /// </summary>
    private void TickCoordinatedPlainTextTabSync()
    {
        var wantInstream = _cloudSyncTabsPlainTextInstreamEnabled
                           && !string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextInFolder);
        var wantOutstream = _cloudSyncTabsPlainTextEnabled
                            && !string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextFolder);
        if (!wantInstream && !wantOutstream)
            return;

        var interval = PlainTabsFolderSyncInterval();
        if (interval <= TimeSpan.Zero)
            return;

        if (_lastPlainTabsFolderSyncPassUtc != DateTime.MinValue
            && DateTime.UtcNow - _lastPlainTabsFolderSyncPassUtc < interval)
            return;

        RunPlainTextTabFolderSyncPass();
    }

    /// <summary>
    /// Pull from folder (if enabled) then push to folder (if enabled). Does not enforce interval.
    /// </summary>
    private void RunPlainTextTabFolderSyncPass(bool advanceSchedulerThrottle = true)
    {
        var wantInstream = _cloudSyncTabsPlainTextInstreamEnabled
                           && !string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextInFolder);
        var wantOutstream = _cloudSyncTabsPlainTextEnabled
                            && !string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextFolder);
        if (!wantInstream && !wantOutstream)
            return;

        try
        {
            SaveSession(updateStatus: false);
        }
        catch
        {
            /* non-critical */
        }

        HashSet<string>? pulledTabIdsThisPass = null;
        if (wantInstream)
            pulledTabIdsThisPass = RunInstreamPlainTextTabSyncOnce();

        if (wantOutstream)
        {
            try
            {
                _ = Path.GetFullPath(_cloudSyncTabsPlainTextFolder.Trim());
                if (TrySyncPlainTextTabsOutstreamCore(_cloudSyncTabsPlainTextFolder, pulledTabIdsThisPass)
                    && pulledTabIdsThisPass is { Count: > 0 })
                {
                    try
                    {
                        SaveSession(updateStatus: false);
                    }
                    catch
                    {
                        /* non-critical */
                    }
                }
            }
            catch
            {
                /* invalid path — skip outstream */
            }
        }

        var completedUtc = DateTime.UtcNow;
        _lastPlainTabsFolderSyncDisplayUtc = completedUtc;
        if (advanceSchedulerThrottle)
            _lastPlainTabsFolderSyncPassUtc = completedUtc;
        RefreshSettingsPlainTabsFolderSyncLabelIfOpen();
    }

    /// <summary>
    /// Polls the plain text tabs folder. When bodies differ, a pull runs when either line-1 <c>lastUpdated=</c> or the file's
    /// last-write time (UTC) is strictly newer than <see cref="TabDocument.LastSavedUtc"/> (fallback
    /// <see cref="TabDocument.LastChangedUtc"/>), so edits that omit updating the header still sync in.
    /// </summary>
    /// <summary>Returns stable tab ids for which external file content was applied (so outstream can refresh push copy + header + mtime).</summary>
    private HashSet<string> RunInstreamPlainTextTabSyncOnce()
    {
        var pulledTabIds = new HashSet<string>(StringComparer.Ordinal);
        if (!_cloudSyncTabsPlainTextInstreamEnabled)
            return pulledTabIds;

        if (string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextInFolder))
            return pulledTabIds;

        string folder;
        try
        {
            folder = Path.GetFullPath(_cloudSyncTabsPlainTextInFolder.Trim());
        }
        catch
        {
            return pulledTabIds;
        }

        if (!Directory.Exists(folder))
            return pulledTabIds;

        var items = new List<TabSyncItem>();
        var anyChange = false;

        var pathByTabId = BuildPlainTabPathIndexById(folder);
        var allocatedPaths = BuildPlainTabAllocatedPaths(folder);
        var consumedPlainPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tabItem in MainTabControl.Items)
        {
            if (tabItem is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                continue;

            if (!allocatedPaths.TryGetValue(doc, out var expectedPath))
                expectedPath = Path.Combine(folder, SanitizeTabHeaderForPlainTabFile(doc.Header) + ".txt");

            var path = ResolvePlainTabInstreamReadPath(folder, doc, pathByTabId, expectedPath);
            try
            {
                consumedPlainPaths.Add(Path.GetFullPath(path));
            }
            catch
            {
                /* ignore invalid path */
            }
            if (!File.Exists(path))
            {
                items.Add(new TabSyncItem
                {
                    TabId = doc.StableTabId,
                    TabHeader = doc.Header,
                    FilePath = path,
                    Status = TabSyncItemStatus.NoFile
                });
                continue;
            }

            string raw;
            try
            {
                raw = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                items.Add(new TabSyncItem
                {
                    TabId = doc.StableTabId,
                    TabHeader = doc.Header,
                    FilePath = path,
                    Status = TabSyncItemStatus.Failed,
                    Detail = ex.Message
                });
                continue;
            }

            var incomingParsed = ParsePlainTabFile(raw);
            var incomingBody = incomingParsed.Body;
            var tFile = incomingParsed.LastUpdatedUtc;
            var currentText = doc.CachedText ?? string.Empty;
            var tabStamp = (doc.LastSavedUtc ?? doc.LastChangedUtc).ToUniversalTime();

            if (NormalizeForCompare(incomingBody) == NormalizeForCompare(currentText))
            {
                items.Add(new TabSyncItem
                {
                    TabId = doc.StableTabId,
                    TabHeader = doc.Header,
                    FilePath = path,
                    LastUpdatedUtc = tFile,
                    Status = TabSyncItemStatus.NoChange
                });
                continue;
            }

            DateTime? fileWriteUtc = null;
            try
            {
                fileWriteUtc = File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                /* ignore */
            }

            DateTime? tFileUtc = tFile?.ToUniversalTime();
            var headerNewer = tFileUtc.HasValue && tFileUtc.Value > tabStamp;
            var fileSystemNewer = fileWriteUtc.HasValue && fileWriteUtc.Value > tabStamp;

            if (!headerNewer && !fileSystemNewer)
            {
                items.Add(new TabSyncItem
                {
                    TabId = doc.StableTabId,
                    TabHeader = doc.Header,
                    FilePath = path,
                    LastUpdatedUtc = tFileUtc ?? fileWriteUtc,
                    Status = TabSyncItemStatus.Skipped,
                    Detail =
                        "tab is newer than file (line 1 lastUpdated and file modification time are not newer than tab Last Updated)"
                });
                continue;
            }

            DateTime appliedUtc;
            if (headerNewer && fileSystemNewer)
                appliedUtc = tFileUtc!.Value >= fileWriteUtc!.Value ? tFileUtc.Value : fileWriteUtc.Value;
            else if (headerNewer)
                appliedUtc = tFileUtc!.Value;
            else
                appliedUtc = fileWriteUtc!.Value;

            ApplyIncomingTextToTab(doc, incomingBody, appliedUtc);
            pulledTabIds.Add(doc.StableTabId);
            items.Add(new TabSyncItem
            {
                TabId = doc.StableTabId,
                TabHeader = doc.Header,
                FilePath = path,
                LastUpdatedUtc = appliedUtc,
                Status = TabSyncItemStatus.AutoApplied
            });
            anyChange = true;
        }

        // Orphan .txt files (no open tab maps to that path): open a new tab to the right, then rewrite the file with the # header line.
        var orphanPaths = new List<string>();
        try
        {
            foreach (var diskPath in Directory.EnumerateFiles(folder, "*.txt", SearchOption.TopDirectoryOnly))
            {
                string full;
                try
                {
                    full = Path.GetFullPath(diskPath);
                }
                catch
                {
                    continue;
                }

                if (consumedPlainPaths.Contains(full))
                    continue;
                orphanPaths.Add(full);
            }
        }
        catch
        {
            /* ignore */
        }

        orphanPaths.Sort(StringComparer.OrdinalIgnoreCase);

        var openTabIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _docs.Values)
            openTabIds.Add(d.StableTabId);

        foreach (var path in orphanPaths)
        {
            string raw;
            try
            {
                raw = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                items.Add(new TabSyncItem
                {
                    TabId = string.Empty,
                    TabHeader = Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    Status = TabSyncItemStatus.Failed,
                    Detail = ex.Message
                });
                continue;
            }

            var parsed = ParsePlainTabFile(raw);
            var idNorm = NormalizeStableTabId(parsed.TabId);
            if (idNorm.Length > 0 && openTabIds.Contains(idNorm))
            {
                items.Add(new TabSyncItem
                {
                    TabId = idNorm,
                    TabHeader = Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    Status = TabSyncItemStatus.Skipped,
                    Detail = "file tab id matches an existing open tab; not imported as a second tab"
                });
                continue;
            }

            var headerFromFile = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(headerFromFile))
                headerFromFile = null;

            DateTime? fileWriteUtc = null;
            try
            {
                fileWriteUtc = File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                /* ignore */
            }

            DateTime appliedUtc;
            var tFileUtc = parsed.LastUpdatedUtc?.ToUniversalTime();
            if (tFileUtc.HasValue && fileWriteUtc.HasValue)
                appliedUtc = tFileUtc.Value >= fileWriteUtc.Value ? tFileUtc.Value : fileWriteUtc.Value;
            else if (tFileUtc.HasValue)
                appliedUtc = tFileUtc.Value;
            else if (fileWriteUtc.HasValue)
                appliedUtc = fileWriteUtc.Value;
            else
                appliedUtc = DateTime.UtcNow;

            var stableForNew = idNorm.Length > 0 ? idNorm : null;
            var doc = CreateTab(headerFromFile, parsed.Body, stableForNew, selectAndFocus: false);
            openTabIds.Add(doc.StableTabId);
            try
            {
                consumedPlainPaths.Add(Path.GetFullPath(path));
            }
            catch
            {
                /* ignore */
            }

            doc.LastSavedUtc = appliedUtc;
            doc.IsDirty = false;

            try
            {
                var plain = BuildCloudPlainTextTabExport(doc.CachedText ?? string.Empty, appliedUtc, doc.StableTabId,
                    DateTime.Now);
                File.WriteAllText(path, plain, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                items.Add(new TabSyncItem
                {
                    TabId = doc.StableTabId,
                    TabHeader = doc.Header,
                    FilePath = path,
                    Status = TabSyncItemStatus.Failed,
                    Detail = ex.Message
                });
                continue;
            }

            pulledTabIds.Add(doc.StableTabId);
            items.Add(new TabSyncItem
            {
                TabId = doc.StableTabId,
                TabHeader = doc.Header,
                FilePath = path,
                LastUpdatedUtc = appliedUtc,
                Status = TabSyncItemStatus.AutoApplied,
                Detail = "Opened new tab from file in sync-from folder; wrote sync header to file"
            });
            anyChange = true;
        }

        if (items.Count > 0)
            RecordTabSyncHistory(TabSyncDirection.Instream, items);

        if (anyChange)
        {
            try { SaveSession(updateStatus: false); }
            catch { /* non-critical */ }
        }

        return pulledTabIds;
    }

    private static Dictionary<string, string> BuildPlainTabPathIndexById(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.txt", SearchOption.TopDirectoryOnly))
            {
                if (TryParsePlainTabIdFromFile(path, out var tid) && tid != null)
                    map[tid] = path;
            }
        }
        catch
        {
            /* ignore */
        }

        return map;
    }

    /// <summary>Same header-based paths as outstream: <c>Name.txt</c>, <c>Name (1).txt</c>, … for duplicate titles.</summary>
    private Dictionary<TabDocument, string> BuildPlainTabAllocatedPaths(string folderPath)
    {
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<TabDocument, string>();
        foreach (var item in MainTabControl.Items)
        {
            if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                continue;
            map[doc] = AllocatePlainTabFilePath(folderPath, doc.Header, usedPaths);
        }

        return map;
    }

    /// <summary>Prefer a file whose header line matches our tab id (e.g. renamed); otherwise the slot from header allocation.</summary>
    private static string ResolvePlainTabInstreamReadPath(
        string folder,
        TabDocument doc,
        IReadOnlyDictionary<string, string> pathByTabId,
        string expectedAllocatedPath)
    {
        if (pathByTabId.TryGetValue(doc.StableTabId, out var byId))
        {
            try
            {
                if (File.Exists(byId))
                    return byId;
            }
            catch
            {
                /* ignore */
            }
        }

        return expectedAllocatedPath;
    }

    private void ApplyIncomingTextToTab(TabDocument doc, string newText, DateTime appliedUtc)
    {
        ReplaceTabEditorTextPreservingLineMetadata(doc, newText);
        doc.IsDirty = false;
        doc.LastSavedUtc = appliedUtc;
        RefreshTabHeader(doc);
    }

    /// <summary>
    /// Replaces editor text while moving highlights, assignees, and bullets using an LCS line diff so unchanged lines
    /// keep their metadata when inserts/removes shift line numbers; metadata on removed lines is dropped.
    /// </summary>
    private void ReplaceTabEditorTextPreservingLineMetadata(TabDocument doc, string newText)
    {
        var oldText = doc.CachedText ?? doc.Editor.Text ?? string.Empty;
        var lineMap = MapOldLineNumbersToNewByLineDiff(oldText, newText);

        var normalHl = GetHighlightedLineNumbers(doc)
            .Where(lineMap.ContainsKey)
            .Select(l => lineMap[l])
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var criticalHl = GetCriticalHighlightedLineNumbers(doc)
            .Where(lineMap.ContainsKey)
            .Select(l => lineMap[l])
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var assigneeList = GetLineAssigneeDetails(doc)
            .Where(kv => lineMap.ContainsKey(kv.Key))
            .Select(kv => new FileLineAssignee
            {
                Line = lineMap[kv.Key],
                Person = kv.Value.Person,
                CreatedUtc = kv.Value.CreatedUtc
            })
            .GroupBy(a => a.Line)
            .Select(g => g.Last())
            .OrderBy(a => a.Line)
            .ToList();

        var bulletList = GetLineBulletDetails(doc)
            .Where(kv => lineMap.ContainsKey(kv.Key))
            .Select(kv => new FileLineBullet
            {
                Line = lineMap[kv.Key],
                Marker = kv.Value.Marker.ToString(),
                CreatedUtc = kv.Value.CreatedUtc
            })
            .GroupBy(b => b.Line)
            .Select(g => g.Last())
            .OrderBy(b => b.Line)
            .ToList();

        var caret = Math.Min(doc.Editor.CaretOffset, newText.Length);
        doc.Editor.Text = newText;
        doc.Editor.CaretOffset = caret;
        doc.CachedText = newText;

        SetHighlightedLines(doc, normalHl.Count > 0 ? normalHl : null, markDirty: false);
        SetCriticalHighlightedLines(doc, criticalHl.Count > 0 ? criticalHl : null, markDirty: false);
        SetLineAssignments(doc, assigneeList.Count > 0 ? assigneeList : null, markDirty: false);
        SetLineBullets(doc, bulletList.Count > 0 ? bulletList : null, markDirty: false);
    }

    private static string NormalizeForCompare(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n");

    // ---------- Tab Sync window ----------------------------------------------------------

    private void ShowTabSyncDialog()
    {
        if (_tabSyncWindow != null)
        {
            try { _tabSyncWindow.Activate(); } catch { /* ignore */ }
            return;
        }

        var window = new Window
        {
            Title = "Tab Sync",
            Width = 900,
            Height = 600,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var state = new TabSyncWindowState(this);
        _tabSyncWindowState = state;
        window.Content = state.BuildRoot();
        window.Closed += (_, _) =>
        {
            _tabSyncWindow = null;
            _tabSyncWindowState = null;
        };
        _tabSyncWindow = window;
        state.Refresh();
        window.Show();
    }

    private void MenuTabSync_Click(object sender, RoutedEventArgs e) => ShowTabSyncDialog();

    private bool AppendLinesToTabById(string? tabId, string tabHeader, IReadOnlyList<string> linesToAppend)
    {
        if (linesToAppend.Count == 0) return false;

        TabDocument? doc = null;
        var idNorm = NormalizeStableTabId(tabId);
        if (idNorm.Length > 0)
        {
            foreach (var item in MainTabControl.Items)
            {
                if (item is not TabItem tab || !_docs.TryGetValue(tab, out var d))
                    continue;
                if (string.Equals(d.StableTabId, idNorm, StringComparison.OrdinalIgnoreCase))
                {
                    doc = d;
                    break;
                }
            }
        }
        else
        {
            foreach (var item in MainTabControl.Items)
            {
                if (item is not TabItem tab || !_docs.TryGetValue(tab, out var d))
                    continue;
                if (string.Equals(d.Header, tabHeader, StringComparison.Ordinal))
                {
                    doc = d;
                    break;
                }
            }
        }

        if (doc == null) return false;

        var current = doc.CachedText ?? string.Empty;
        var newline = current.Contains("\r\n") ? "\r\n" : "\n";
        var sb = new StringBuilder(current);
        if (sb.Length > 0 && !current.EndsWith('\n'))
            sb.Append(newline);
        for (int i = 0; i < linesToAppend.Count; i++)
        {
            sb.Append(linesToAppend[i]);
            sb.Append(newline);
        }
        var combined = sb.ToString();
        ReplaceTabEditorTextPreservingLineMetadata(doc, combined);
        MarkDirty(doc);
        return true;
    }

    // ---------- LCS line diff -----------------------------------------------------------

    internal enum DiffOp { Equal, Removed, Added }

    internal sealed class DiffLine
    {
        public DiffOp Op { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    internal static List<DiffLine> ComputeLineDiff(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        var n = oldLines.Length;
        var m = newLines.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = oldLines[i] == newLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var ops = new List<DiffLine>(n + m);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (oldLines[x] == newLines[y])
            {
                ops.Add(new DiffLine { Op = DiffOp.Equal, Text = oldLines[x] });
                x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(new DiffLine { Op = DiffOp.Removed, Text = oldLines[x] });
                x++;
            }
            else
            {
                ops.Add(new DiffLine { Op = DiffOp.Added, Text = newLines[y] });
                y++;
            }
        }
        while (x < n) { ops.Add(new DiffLine { Op = DiffOp.Removed, Text = oldLines[x++] }); }
        while (y < m) { ops.Add(new DiffLine { Op = DiffOp.Added, Text = newLines[y++] }); }
        return ops;
    }

    /// <summary>1-based old line → 1-based new line for lines matched as equal in <see cref="ComputeLineDiff"/>.</summary>
    internal static Dictionary<int, int> MapOldLineNumbersToNewByLineDiff(string oldText, string newText)
    {
        var ops = ComputeLineDiff(oldText, newText);
        var map = new Dictionary<int, int>();
        var oldLine = 1;
        var newLine = 1;
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case DiffOp.Equal:
                    map[oldLine] = newLine;
                    oldLine++;
                    newLine++;
                    break;
                case DiffOp.Removed:
                    oldLine++;
                    break;
                case DiffOp.Added:
                    newLine++;
                    break;
            }
        }

        return map;
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();
        return text.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>UI state for the modeless Tab Sync window.</summary>
    private sealed class TabSyncWindowState
    {
        private readonly MainWindow _owner;
        private TabControl? _tabControl;
        private ListBox? _historyList;
        private StackPanel? _historyDetail;
        private ListBox? _conflictList;
        private DockPanel? _conflictDetail;

        public TabSyncWindowState(MainWindow owner) => _owner = owner;

        public UIElement BuildRoot()
        {
            _tabControl = new TabControl { Margin = new Thickness(8) };

            // History tab
            var historyRoot = new Grid();
            historyRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            historyRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _historyList = new ListBox { Margin = new Thickness(4) };
            _historyList.SelectionChanged += (_, _) => RefreshHistoryDetail();
            Grid.SetColumn(_historyList, 0);
            historyRoot.Children.Add(_historyList);

            _historyDetail = new StackPanel { Margin = new Thickness(8) };
            var detailScroll = new ScrollViewer
            {
                Content = _historyDetail,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(detailScroll, 1);
            historyRoot.Children.Add(detailScroll);

            _tabControl.Items.Add(new TabItem { Header = "History", Content = historyRoot });

            // Conflicts tab
            var conflictRoot = new Grid();
            conflictRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            conflictRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _conflictList = new ListBox { Margin = new Thickness(4) };
            _conflictList.SelectionChanged += (_, _) => RefreshConflictDetail();
            Grid.SetColumn(_conflictList, 0);
            conflictRoot.Children.Add(_conflictList);

            _conflictDetail = new DockPanel { Margin = new Thickness(8) };
            Grid.SetColumn(_conflictDetail, 1);
            conflictRoot.Children.Add(_conflictDetail);

            _tabControl.Items.Add(new TabItem { Header = "Conflicts", Content = conflictRoot });

            return _tabControl;
        }

        public void Refresh()
        {
            if (_historyList == null || _conflictList == null) return;
            var entries = _owner._tabSyncHistoryService.GetEntries();

            var prevHistory = _historyList.SelectedIndex;
            _historyList.Items.Clear();
            foreach (var entry in entries)
                _historyList.Items.Add(BuildHistoryRow(entry));
            if (_historyList.Items.Count > 0)
                _historyList.SelectedIndex = prevHistory >= 0 && prevHistory < _historyList.Items.Count ? prevHistory : 0;

            var conflicts = _owner._tabSyncHistoryService.GetConflictListEntries();
            var prevConflict = _conflictList.SelectedIndex;
            _conflictList.Items.Clear();
            foreach (var (entry, item) in conflicts)
                _conflictList.Items.Add(BuildConflictRow(entry, item));
            if (_conflictList.Items.Count > 0)
                _conflictList.SelectedIndex = prevConflict >= 0 && prevConflict < _conflictList.Items.Count ? prevConflict : 0;
            else
                RefreshConflictDetail();
        }

        private static ListBoxItem BuildHistoryRow(TabSyncHistoryEntry entry)
        {
            var arrow = entry.Direction == TabSyncDirection.Outstream ? "↑" : "↓";
            var counts = entry.Items
                .GroupBy(i => i.Status)
                .ToDictionary(g => g.Key, g => g.Count());
            var summary = string.Join(" · ", counts
                .OrderBy(p => (int)p.Key)
                .Select(p => $"{p.Value} {p.Key.ToString().ToLowerInvariant()}"));
            var local = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return new ListBoxItem
            {
                Content = $"{arrow}  {local}\n   {summary}",
                Tag = entry,
                Padding = new Thickness(6, 4, 6, 4)
            };
        }

        private void RefreshHistoryDetail()
        {
            if (_historyDetail == null || _historyList == null) return;
            _historyDetail.Children.Clear();
            if (_historyList.SelectedItem is not ListBoxItem sel || sel.Tag is not TabSyncHistoryEntry entry)
                return;

            _historyDetail.Children.Add(new TextBlock
            {
                Text = $"{(entry.Direction == TabSyncDirection.Outstream ? "Outstream (Noted → folder)" : "Instream (folder → Noted)")} — {entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var item in entry.Items)
            {
                var row = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(new TextBlock
                {
                    Inlines =
                    {
                        new Run($"{item.TabHeader}  ") { FontWeight = FontWeights.SemiBold },
                        BuildStatusBadge(item.Status, item.Resolved, item.ConflictResolution)
                    }
                });
                if (!string.IsNullOrEmpty(item.TabId))
                    row.Children.Add(new TextBlock
                    {
                        Text = $"Tab id: {item.TabId}",
                        Foreground = Brushes.DimGray,
                        FontSize = 11
                    });
                if (!string.IsNullOrEmpty(item.FilePath))
                    row.Children.Add(new TextBlock { Text = item.FilePath, Foreground = Brushes.DimGray, FontSize = 11 });
                if (!string.IsNullOrEmpty(item.Detail))
                    row.Children.Add(new TextBlock { Text = item.Detail, Foreground = Brushes.DimGray, FontSize = 11 });

                if (item.Status == TabSyncItemStatus.Conflict && !item.Resolved)
                {
                    var btn = new Button
                    {
                        Content = "Resolve…",
                        Padding = new Thickness(8, 1, 8, 1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    var capturedEntry = entry;
                    var capturedItem = item;
                    btn.Click += (_, _) => SelectConflict(capturedEntry, capturedItem);
                    row.Children.Add(btn);
                }
                _historyDetail.Children.Add(row);
            }
        }

        private static Run BuildStatusBadge(TabSyncItemStatus status, bool resolved, string? conflictResolution = null)
        {
            var (label, color) = status switch
            {
                TabSyncItemStatus.Wrote => ("WROTE", "#2E7D32"),
                TabSyncItemStatus.AutoApplied => ("AUTO-APPLIED", "#1565C0"),
                TabSyncItemStatus.Conflict => resolved
                    ? (string.Equals(conflictResolution, "Appended", StringComparison.Ordinal)
                        ? ("CONFLICT (APPENDED)", "#2E7D32")
                        : ("CONFLICT (RESOLVED)", "#6A1B9A"))
                    : ("CONFLICT", "#C62828"),
                TabSyncItemStatus.NoChange => ("NO CHANGE", "#757575"),
                TabSyncItemStatus.NoFile => ("NO FILE", "#9E9E9E"),
                TabSyncItemStatus.Skipped => ("SKIPPED", "#9E9E9E"),
                TabSyncItemStatus.Failed => ("FAILED", "#C62828"),
                _ => (status.ToString().ToUpperInvariant(), "#000000")
            };
            return new Run($"[{label}]")
            {
                Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
                FontSize = 11
            };
        }

        private static ListBoxItem BuildConflictRow(TabSyncHistoryEntry entry, TabSyncItem item)
        {
            var local = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var suffix = item.Resolved
                ? string.Equals(item.ConflictResolution, "Appended", StringComparison.Ordinal)
                    ? "\n   Appended"
                    : "\n   Resolved"
                : string.Empty;
            var idLine = string.IsNullOrEmpty(item.TabId)
                ? string.Empty
                : $"\n   id {item.TabId}";
            return new ListBoxItem
            {
                Content = $"{item.TabHeader}{idLine}\n   {local}{suffix}",
                Tag = (entry, item),
                Padding = new Thickness(6, 4, 6, 4)
            };
        }

        private void SelectConflict(TabSyncHistoryEntry entry, TabSyncItem item)
        {
            if (_tabControl == null || _conflictList == null) return;
            _tabControl.SelectedIndex = 1;
            for (int i = 0; i < _conflictList.Items.Count; i++)
            {
                if (_conflictList.Items[i] is ListBoxItem li && li.Tag is ValueTuple<TabSyncHistoryEntry, TabSyncItem> tup)
                {
                    if (ReferenceEquals(tup.Item1, entry) && ReferenceEquals(tup.Item2, item))
                    {
                        _conflictList.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void RefreshConflictDetail()
        {
            if (_conflictDetail == null || _conflictList == null) return;
            _conflictDetail.Children.Clear();
            if (_conflictList.SelectedItem is not ListBoxItem sel
                || sel.Tag is not ValueTuple<TabSyncHistoryEntry, TabSyncItem> tup)
            {
                _conflictDetail.Children.Add(new TextBlock
                {
                    Text = "(no conflict entries)",
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(8)
                });
                return;
            }

            var (entry, item) = tup;
            var current = item.CurrentText ?? string.Empty;
            var incoming = item.IncomingText ?? string.Empty;
            var ops = ComputeLineDiff(current, incoming);

            var header = new TextBlock
            {
                Text = item.Resolved
                    ? $"{item.TabHeader} — {item.ConflictResolution ?? "Resolved"} @ {entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                    : $"{item.TabHeader} — conflict @ {entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var topBlock = new StackPanel { Orientation = Orientation.Vertical };
            topBlock.Children.Add(header);
            if (item.Resolved)
            {
                topBlock.Children.Add(new TextBlock
                {
                    Text = string.Equals(item.ConflictResolution, "Appended", StringComparison.Ordinal)
                        ? "Lines from the file were appended to the tab."
                        : "Marked resolved without appending.",
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            DockPanel.SetDock(topBlock, Dock.Top);
            _conflictDetail.Children.Add(topBlock);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // Body: diff on left, candidates on right (unresolved only)
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var diffPanel = new StackPanel { Margin = new Thickness(4) };
            foreach (var op in ops)
            {
                var (bg, fg, prefix) = op.Op switch
                {
                    DiffOp.Added => ("#E6FFED", "#1B5E20", "+ "),
                    DiffOp.Removed => ("#FFEEF0", "#B71C1C", "- "),
                    _ => ("#FFFFFF", "#000000", "  ")
                };
                diffPanel.Children.Add(new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
                    Padding = new Thickness(4, 1, 4, 1),
                    Child = new TextBlock
                    {
                        Text = prefix + op.Text,
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }
            var diffScroll = new ScrollViewer
            {
                Content = diffPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetColumn(diffScroll, 0);
            grid.Children.Add(diffScroll);

            var addedLines = ops.Where(o => o.Op == DiffOp.Added).Select(o => o.Text).ToList();

            if (!item.Resolved)
            {
                var candidatePanel = new StackPanel { Margin = new Thickness(4) };
                candidatePanel.Children.Add(new TextBlock
                {
                    Text = $"Lines present in file but not in tab ({addedLines.Count}):",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                var checkboxes = new List<CheckBox>();
                foreach (var line in addedLines)
                {
                    var cb = new CheckBox
                    {
                        Content = line.Length == 0 ? "(empty line)" : line,
                        IsChecked = true,
                        Margin = new Thickness(0, 1, 0, 1),
                        Tag = line
                    };
                    checkboxes.Add(cb);
                    candidatePanel.Children.Add(cb);
                }
                var candScroll = new ScrollViewer
                {
                    Content = candidatePanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                Grid.SetColumn(candScroll, 1);
                grid.Children.Add(candScroll);

                var btnApply = new Button
                {
                    Content = "Append selected to end of tab",
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 8, 0),
                    IsEnabled = addedLines.Count > 0
                };
                btnApply.Click += (_, _) =>
                {
                    var picks = checkboxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToList();
                    if (picks.Count == 0)
                    {
                        MessageBox.Show("Select at least one line.", "Tab Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    if (!_owner.AppendLinesToTabById(item.TabId, item.TabHeader, picks))
                    {
                        MessageBox.Show(
                            $"Could not find this tab to append to (id/header no longer match an open tab).",
                            "Tab Sync",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _owner._tabSyncHistoryService.TryMarkResolved(_owner._backupFolder, entry.TimestampUtc, item.TabId,
                        item.TabHeader,
                        "Appended");
                    Refresh();
                };

                var btnDismiss = new Button
                {
                    Content = "Mark resolved (no append)",
                    Padding = new Thickness(10, 3, 10, 3)
                };
                btnDismiss.Click += (_, _) =>
                {
                    _owner._tabSyncHistoryService.TryMarkResolved(_owner._backupFolder, entry.TimestampUtc, item.TabId,
                        item.TabHeader,
                        "Resolved");
                    Refresh();
                };

                buttonRow.Children.Add(btnApply);
                buttonRow.Children.Add(btnDismiss);
                DockPanel.SetDock(buttonRow, Dock.Bottom);
                _conflictDetail.Children.Add(buttonRow);
            }

            _conflictDetail.Children.Add(grid);
        }
    }
}

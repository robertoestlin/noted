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

    private TimeSpan InstreamPlainTabsInterval()
        => TimeSpan.FromHours(_cloudSyncTabsPlainTextInstreamHours)
           + TimeSpan.FromMinutes(_cloudSyncTabsPlainTextInstreamMinutes);

    /// <summary>
    /// Polls the plain text tabs folder. Auto-applies an incoming file when:
    ///   1. the file's <c># lastupdated:</c> equals the most recent outstream timestamp recorded for this tab
    ///   2. the matching tab has no unsaved changes
    ///   3. content actually differs.
    /// Anything else with a content difference is logged as a Conflict.
    /// </summary>
    private void TickInstreamPlainTextTabSync()
    {
        if (!_cloudSyncTabsPlainTextEnabled || !_cloudSyncTabsPlainTextInstreamEnabled)
            return;

        if (string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextFolder))
            return;

        var interval = InstreamPlainTabsInterval();
        if (interval <= TimeSpan.Zero)
            return;

        if (_lastInstreamPlainTabsSyncUtc != DateTime.MinValue
            && DateTime.UtcNow - _lastInstreamPlainTabsSyncUtc < interval)
            return;

        string folder;
        try
        {
            folder = Path.GetFullPath(_cloudSyncTabsPlainTextFolder.Trim());
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            _lastInstreamPlainTabsSyncUtc = DateTime.UtcNow;
            return;
        }

        var lastOutstreamByHeader = LookupLastOutstreamPerTab();
        var items = new List<TabSyncItem>();
        var anyChange = false;

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tabItem in MainTabControl.Items)
        {
            if (tabItem is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                continue;

            var path = AllocatePlainTabFilePath(folder, doc.Header, usedPaths);
            if (!File.Exists(path))
            {
                items.Add(new TabSyncItem
                {
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
                    TabHeader = doc.Header,
                    FilePath = path,
                    Status = TabSyncItemStatus.Failed,
                    Detail = ex.Message
                });
                continue;
            }

            var (incomingBody, headerUtc) = ParseCloudPlainTextTabFile(raw);
            var currentText = doc.CachedText ?? string.Empty;
            if (NormalizeForCompare(incomingBody) == NormalizeForCompare(currentText))
            {
                items.Add(new TabSyncItem
                {
                    TabHeader = doc.Header,
                    FilePath = path,
                    LastUpdatedUtc = headerUtc,
                    Status = TabSyncItemStatus.NoChange
                });
                continue;
            }

            lastOutstreamByHeader.TryGetValue(doc.Header, out var lastOutUtc);
            var matchesLastOutstream = headerUtc.HasValue
                && lastOutUtc.HasValue
                && Math.Abs((headerUtc.Value - lastOutUtc.Value).TotalSeconds) < 1.0;

            if (matchesLastOutstream && !doc.IsDirty)
            {
                ApplyIncomingTextToTab(doc, incomingBody, headerUtc ?? DateTime.UtcNow);
                items.Add(new TabSyncItem
                {
                    TabHeader = doc.Header,
                    FilePath = path,
                    LastUpdatedUtc = headerUtc,
                    Status = TabSyncItemStatus.AutoApplied
                });
                anyChange = true;
            }
            else
            {
                var detail = !headerUtc.HasValue
                    ? "no header timestamp in file"
                    : doc.IsDirty
                        ? "tab has unsaved changes"
                        : "incoming timestamp does not match last outstream";
                items.Add(new TabSyncItem
                {
                    TabHeader = doc.Header,
                    FilePath = path,
                    LastUpdatedUtc = headerUtc,
                    Status = TabSyncItemStatus.Conflict,
                    Detail = detail,
                    IncomingText = incomingBody,
                    CurrentText = currentText
                });
            }
        }

        _lastInstreamPlainTabsSyncUtc = DateTime.UtcNow;

        if (items.Count > 0)
            RecordTabSyncHistory(TabSyncDirection.Instream, items);

        if (anyChange)
        {
            try { SaveSession(updateStatus: false); }
            catch { /* non-critical */ }
        }
    }

    private Dictionary<string, DateTime?> LookupLastOutstreamPerTab()
    {
        var result = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        foreach (var entry in _tabSyncHistoryService.GetEntries())
        {
            if (entry.Direction != TabSyncDirection.Outstream) continue;
            foreach (var item in entry.Items)
            {
                if (item.Status != TabSyncItemStatus.Wrote) continue;
                if (string.IsNullOrEmpty(item.TabHeader)) continue;
                if (!result.ContainsKey(item.TabHeader))
                    result[item.TabHeader] = item.LastUpdatedUtc;
            }
        }
        return result;
    }

    private void ApplyIncomingTextToTab(TabDocument doc, string newText, DateTime appliedUtc)
    {
        var caret = Math.Min(doc.Editor.CaretOffset, newText.Length);
        doc.Editor.Text = newText;
        doc.Editor.CaretOffset = caret;
        doc.CachedText = newText;
        doc.IsDirty = false;
        doc.LastSavedUtc = appliedUtc;
        RefreshTabHeader(doc);
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

    private bool AppendLinesToTabByHeader(string tabHeader, IReadOnlyList<string> linesToAppend)
    {
        if (linesToAppend.Count == 0) return false;
        foreach (var item in MainTabControl.Items)
        {
            if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                continue;
            if (!string.Equals(doc.Header, tabHeader, StringComparison.Ordinal))
                continue;

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
            doc.Editor.Text = combined;
            doc.CachedText = combined;
            MarkDirty(doc);
            return true;
        }
        return false;
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

            var conflicts = _owner._tabSyncHistoryService.GetUnresolvedConflicts();
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
                        BuildStatusBadge(item.Status, item.Resolved)
                    }
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

        private static Run BuildStatusBadge(TabSyncItemStatus status, bool resolved)
        {
            var (label, color) = status switch
            {
                TabSyncItemStatus.Wrote => ("WROTE", "#2E7D32"),
                TabSyncItemStatus.AutoApplied => ("AUTO-APPLIED", "#1565C0"),
                TabSyncItemStatus.Conflict => (resolved ? "CONFLICT (RESOLVED)" : "CONFLICT", resolved ? "#6A1B9A" : "#C62828"),
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
            return new ListBoxItem
            {
                Content = $"{item.TabHeader}\n   {local}",
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
                    Text = "(no conflicts)",
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
                Text = $"{item.TabHeader} — conflict @ {entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(header, Dock.Top);
            _conflictDetail.Children.Add(header);

            // Bottom button bar
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(buttonRow, Dock.Bottom);

            // Body: diff on left, candidates on right
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

            _conflictDetail.Children.Add(buttonRow);
            _conflictDetail.Children.Add(grid);

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
                if (!_owner.AppendLinesToTabByHeader(item.TabHeader, picks))
                {
                    MessageBox.Show($"Could not find a tab named '{item.TabHeader}' to append to.", "Tab Sync",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _owner._tabSyncHistoryService.TryMarkResolved(_owner._backupFolder, entry.TimestampUtc, item.TabHeader);
                Refresh();
            };

            var btnDismiss = new Button
            {
                Content = "Mark resolved (no append)",
                Padding = new Thickness(10, 3, 10, 3)
            };
            btnDismiss.Click += (_, _) =>
            {
                _owner._tabSyncHistoryService.TryMarkResolved(_owner._backupFolder, entry.TimestampUtc, item.TabHeader);
                Refresh();
            };

            buttonRow.Children.Add(btnApply);
            buttonRow.Children.Add(btnDismiss);
        }
    }
}

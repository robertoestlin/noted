using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    private const int DefaultSearchFilesHistoryLimit = 20;
    private const int MinSearchFilesHistoryLimit = 1;
    private const int MaxSearchFilesHistoryLimit = 500;
    private const int MaxStoredMatchesPerSearch = 1000;
    private const int MaxMatchCountPerSearch = 5000;
    private List<SearchFilesHistoryEntry> _searchFilesHistory = [];
    private int _searchFilesHistoryLimit = DefaultSearchFilesHistoryLimit;

    private sealed class SearchFilesResultRow
    {
        public string File { get; init; } = string.Empty;
        public string Line { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
    }

    private sealed class SearchFilesHistoryListItem
    {
        public SearchFilesHistoryEntry Entry { get; init; } = new();
        public string DisplayText { get; init; } = string.Empty;

        public override string ToString() => DisplayText;
    }

    private static string BuildSearchFilesHistoryKey(SearchFilesHistoryEntry entry)
    {
        var folder = NormalizeHistoryFolderPath(entry.FolderPath);
        var text = (entry.SearchText ?? string.Empty).Trim().ToLowerInvariant();
        var pattern = (entry.SearchInFiles ?? string.Empty).Trim().ToLowerInvariant();
        return $"{folder}|{text}|{pattern}";
    }

    private static string NormalizeHistoryFolderPath(string? folderPath)
    {
        var raw = (folderPath ?? string.Empty).Trim();
        if (raw.Length == 0)
            return string.Empty;

        try
        {
            var fullPath = Path.GetFullPath(raw);
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
        catch
        {
            return raw.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
    }

    private static string BuildSearchFilesHistoryDisplayText(SearchFilesHistoryEntry entry)
    {
        var createdText = entry.CreatedUtc == default
            ? "(no timestamp)"
            : entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var folderLabel = string.IsNullOrWhiteSpace(entry.FolderPath)
            ? "(no folder)"
            : entry.FolderPath;
        var patternLabel = string.IsNullOrWhiteSpace(entry.SearchInFiles)
            ? "(none)"
            : entry.SearchInFiles;
        var truncatedSuffix = entry.IsResultTruncated ? " (truncated)" : string.Empty;
        var filesMatchedText = string.IsNullOrWhiteSpace(entry.SearchInFiles)
            ? string.Empty
            : $" | Files matched: {entry.MatchedFileCount}";
        return $"{createdText} | {entry.MatchCount} matches{truncatedSuffix}{filesMatchedText} | Files: \"{entry.SearchText}\" | {folderLabel} | Pattern: {patternLabel}";
    }

    private static string BuildSearchFilesSummary(SearchFilesHistoryEntry entry)
    {
        var patternLabel = string.IsNullOrWhiteSpace(entry.SearchInFiles)
            ? "(none)"
            : entry.SearchInFiles;
        var truncatedSuffix = entry.IsResultTruncated ? " (truncated at limit)" : string.Empty;
        var filesMatchedText = string.IsNullOrWhiteSpace(entry.SearchInFiles)
            ? string.Empty
            : $" | Files matched: {entry.MatchedFileCount}";
        return $"Folder: {entry.FolderPath} | File search: \"{entry.SearchText}\" | Pattern: {patternLabel} | Matches: {entry.MatchCount}{truncatedSuffix}{filesMatchedText}";
    }

    private static string NormalizeSearchFilesPreview(string line)
    {
        var text = (line ?? string.Empty).Replace('\t', ' ');
        return text.Length <= 240 ? text : text[..237] + "...";
    }

    private static SearchFilesHistoryEntry CloneSearchFilesHistoryEntry(SearchFilesHistoryEntry source)
    {
        var normalizedMatches = (source.Matches ?? [])
            .Where(match => match != null)
            .Select(match => new SearchFilesHistoryMatch
            {
                RelativePath = (match.RelativePath ?? string.Empty).Trim(),
                LineNumber = Math.Max(0, match.LineNumber),
                LinePreview = NormalizeSearchFilesPreview(match.LinePreview ?? string.Empty)
            })
            .Where(match => match.RelativePath.Length > 0)
            .Take(MaxStoredMatchesPerSearch)
            .ToList();

        return new SearchFilesHistoryEntry
        {
            CreatedUtc = source.CreatedUtc == default ? DateTime.UtcNow : source.CreatedUtc.ToUniversalTime(),
            FolderPath = NormalizeHistoryFolderPath(source.FolderPath),
            SearchText = (source.SearchText ?? string.Empty).Trim(),
            SearchInFiles = (source.SearchInFiles ?? string.Empty).Trim(),
            MatchCount = Math.Max(0, source.MatchCount),
            MatchedFileCount = Math.Max(0, source.MatchedFileCount),
            IsResultTruncated = source.IsResultTruncated,
            Matches = normalizedMatches
        };
    }

    private static int NormalizeSearchFilesHistoryLimit(int? historyLimit)
    {
        var value = historyLimit ?? DefaultSearchFilesHistoryLimit;
        return Math.Clamp(value, MinSearchFilesHistoryLimit, MaxSearchFilesHistoryLimit);
    }

    private static List<SearchFilesHistoryEntry> NormalizeSearchFilesHistory(IEnumerable<SearchFilesHistoryEntry>? entries, int historyLimit)
    {
        var normalizedLimit = NormalizeSearchFilesHistoryLimit(historyLimit);
        return (entries ?? [])
            .Where(entry => entry != null)
            .Select(CloneSearchFilesHistoryEntry)
            .Where(entry => entry.FolderPath.Length > 0 && entry.SearchText.Length > 0)
            .OrderByDescending(entry => entry.CreatedUtc)
            .Take(normalizedLimit)
            .ToList();
    }

    private List<SearchFilesHistoryEntry> BuildSearchFilesHistorySnapshot()
        => NormalizeSearchFilesHistory(_searchFilesHistory, _searchFilesHistoryLimit);

    private void ApplySearchFilesHistorySettings(IEnumerable<SearchFilesHistoryEntry>? history, int? historyLimit = null)
    {
        _searchFilesHistoryLimit = NormalizeSearchFilesHistoryLimit(historyLimit);
        _searchFilesHistory = NormalizeSearchFilesHistory(history, _searchFilesHistoryLimit);
    }

    private static bool AreSearchFilesResultsEqual(SearchFilesHistoryEntry left, SearchFilesHistoryEntry right)
    {
        if (left.MatchCount != right.MatchCount
            || left.MatchedFileCount != right.MatchedFileCount
            || left.IsResultTruncated != right.IsResultTruncated)
        {
            return false;
        }

        var leftMatches = left.Matches ?? [];
        var rightMatches = right.Matches ?? [];
        if (leftMatches.Count != rightMatches.Count)
            return false;

        for (var i = 0; i < leftMatches.Count; i++)
        {
            var leftMatch = leftMatches[i];
            var rightMatch = rightMatches[i];
            if (!string.Equals(leftMatch.RelativePath, rightMatch.RelativePath, StringComparison.OrdinalIgnoreCase)
                || leftMatch.LineNumber != rightMatch.LineNumber
                || !string.Equals(leftMatch.LinePreview, rightMatch.LinePreview, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSearchFilesEntriesEquivalent(SearchFilesHistoryEntry left, SearchFilesHistoryEntry right)
    {
        return string.Equals(BuildSearchFilesHistoryKey(left), BuildSearchFilesHistoryKey(right), StringComparison.Ordinal)
            && AreSearchFilesResultsEqual(left, right);
    }

    private bool AddSearchFilesHistoryEntry(SearchFilesHistoryEntry entry)
    {
        var normalized = CloneSearchFilesHistoryEntry(entry);
        normalized.CreatedUtc = DateTime.UtcNow;
        if (_searchFilesHistory.Count > 0 && AreSearchFilesEntriesEquivalent(_searchFilesHistory[0], normalized))
            return false;

        _searchFilesHistory.Insert(0, normalized);
        _searchFilesHistory = _searchFilesHistory
            .OrderByDescending(item => item.CreatedUtc)
            .Take(_searchFilesHistoryLimit)
            .ToList();
        return true;
    }

    private static Func<string, bool> BuildSearchInFilesMatcher(string searchInFilesRaw)
    {
        if (string.IsNullOrWhiteSpace(searchInFilesRaw))
            return _ => true;

        var tokens = searchInFilesRaw
            .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToList();

        if (tokens.Count == 0)
            return _ => true;

        var wildcardRegexes = new List<Regex>();
        var containsTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (token.Contains('*', StringComparison.Ordinal) || token.Contains('?', StringComparison.Ordinal))
            {
                var regexPattern = "^" + Regex.Escape(token)
                    .Replace(@"\*", ".*", StringComparison.Ordinal)
                    .Replace(@"\?", ".", StringComparison.Ordinal) + "$";
                wildcardRegexes.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            else
            {
                containsTokens.Add(token);
            }
        }

        return relativePath =>
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            var fileName = Path.GetFileName(relativePath);
            foreach (var regex in wildcardRegexes)
            {
                if (regex.IsMatch(normalizedPath) || regex.IsMatch(fileName))
                    return true;
            }

            foreach (var token in containsTokens)
            {
                if (normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        };
    }

    private static SearchFilesHistoryEntry ExecuteSearchFiles(string folderPath, string fileSearchText, string contentPatternRaw)
    {
        var rootFolder = Path.GetFullPath(folderPath);
        var filePathMatcher = BuildSearchInFilesMatcher(fileSearchText);
        var contentPattern = (contentPatternRaw ?? string.Empty).Trim();
        var results = new List<SearchFilesHistoryMatch>();
        var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchCount = 0;
        var isTruncated = false;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootFolder);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();

            try
            {
                foreach (var childDirectory in Directory.GetDirectories(currentDirectory))
                    pendingDirectories.Push(childDirectory);
            }
            catch
            {
                // Ignore directories that cannot be enumerated.
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                string relativePath;
                try
                {
                    relativePath = Path.GetRelativePath(rootFolder, filePath);
                }
                catch
                {
                    relativePath = filePath;
                }

                if (!filePathMatcher(relativePath))
                    continue;

                if (contentPattern.Length == 0)
                {
                    matchCount++;
                    matchedFiles.Add(relativePath);
                    if (results.Count < MaxStoredMatchesPerSearch)
                    {
                        results.Add(new SearchFilesHistoryMatch
                        {
                            RelativePath = relativePath,
                            LineNumber = 0,
                            LinePreview = "(file path match)"
                        });
                    }

                    if (matchCount >= MaxMatchCountPerSearch)
                    {
                        isTruncated = true;
                        break;
                    }
                    continue;
                }

                try
                {
                    using var stream = File.OpenRead(filePath);
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    var lineNumber = 0;
                    var fileMatchedPattern = false;
                    while (reader.ReadLine() is { } line)
                    {
                        lineNumber++;
                        if (line.IndexOf(contentPattern, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (!fileMatchedPattern)
                        {
                            fileMatchedPattern = true;
                            matchedFiles.Add(relativePath);
                        }

                        matchCount++;
                        if (results.Count < MaxStoredMatchesPerSearch)
                        {
                            results.Add(new SearchFilesHistoryMatch
                            {
                                RelativePath = relativePath,
                                LineNumber = lineNumber,
                                LinePreview = NormalizeSearchFilesPreview(line)
                            });
                        }

                        if (matchCount >= MaxMatchCountPerSearch)
                        {
                            isTruncated = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Skip unreadable/binary files.
                }

                if (isTruncated)
                    break;
            }

            if (isTruncated)
                break;
        }

        return new SearchFilesHistoryEntry
        {
            CreatedUtc = DateTime.UtcNow,
            FolderPath = rootFolder,
            SearchText = fileSearchText.Trim(),
            SearchInFiles = contentPattern,
            MatchCount = matchCount,
            MatchedFileCount = matchedFiles.Count,
            IsResultTruncated = isTruncated,
            Matches = results
        };
    }

    private void ShowSearchFilesDialog()
    {
        var dlg = new Window
        {
            Title = "Search Files",
            Width = 1240,
            Height = 760,
            MinWidth = 980,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        dlg.Content = root;

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true
        };
        closeRow.Children.Add(btnClose);
        footer.Children.Add(status);
        footer.Children.Add(closeRow);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var topHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var btnSettings = new Button
        {
            Content = "⚙",
            Width = 34,
            Height = 30,
            FontSize = 16,
            ToolTip = "Search Files settings"
        };
        Grid.SetColumn(btnSettings, 1);
        topHeader.Children.Add(btnSettings);
        DockPanel.SetDock(topHeader, Dock.Top);
        root.Children.Add(topHeader);

        var searchPanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockPanel.SetDock(searchPanel, Dock.Top);
        root.Children.Add(searchPanel);

        void AddLabel(string text, int row)
        {
            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            searchPanel.Children.Add(label);
        }

        AddLabel("Folder", 0);
        AddLabel("Search string", 1);
        AddLabel("Search for pattern in matching files", 2);

        var txtFolder = new TextBox { Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(txtFolder, 0);
        Grid.SetColumn(txtFolder, 1);
        searchPanel.Children.Add(txtFolder);

        var btnBrowseFolder = new Button
        {
            Content = "Browse...",
            Width = 95,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(btnBrowseFolder, 0);
        Grid.SetColumn(btnBrowseFolder, 2);
        searchPanel.Children.Add(btnBrowseFolder);

        var txtSearch = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(txtSearch, 1);
        Grid.SetColumn(txtSearch, 1);
        Grid.SetColumnSpan(txtSearch, 2);
        searchPanel.Children.Add(txtSearch);

        var txtSearchInFiles = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Optional content pattern. Example: class"
        };
        Grid.SetRow(txtSearchInFiles, 2);
        Grid.SetColumn(txtSearchInFiles, 1);
        Grid.SetColumnSpan(txtSearchInFiles, 2);
        searchPanel.Children.Add(txtSearchInFiles);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var btnSearch = new Button
        {
            Content = "Search",
            Width = 110,
            Height = 30,
            IsDefault = true
        };
        buttonRow.Children.Add(btnSearch);
        Grid.SetRow(buttonRow, 3);
        Grid.SetColumn(buttonRow, 1);
        searchPanel.Children.Add(buttonRow);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);

        var historyPanel = new DockPanel();
        Grid.SetColumn(historyPanel, 0);
        body.Children.Add(historyPanel);
        var txtHistoryHeader = new TextBlock
        {
            Text = string.Empty,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        historyPanel.Children.Add(txtHistoryHeader);
        DockPanel.SetDock(txtHistoryHeader, Dock.Top);
        var historyList = new ListBox();
        historyPanel.Children.Add(historyList);

        var resultsPanel = new DockPanel();
        Grid.SetColumn(resultsPanel, 2);
        body.Children.Add(resultsPanel);
        var txtSummary = new TextBlock
        {
            Text = "Run a search to see results.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(txtSummary, Dock.Top);
        resultsPanel.Children.Add(txtSummary);

        var resultsList = new ListView();
        var resultsView = new GridView();
        resultsView.Columns.Add(new GridViewColumn { Header = "File", Width = 300, DisplayMemberBinding = new Binding(nameof(SearchFilesResultRow.File)) });
        resultsView.Columns.Add(new GridViewColumn { Header = "Line", Width = 70, DisplayMemberBinding = new Binding(nameof(SearchFilesResultRow.Line)) });
        resultsView.Columns.Add(new GridViewColumn { Header = "Text", Width = 540, DisplayMemberBinding = new Binding(nameof(SearchFilesResultRow.Text)) });
        resultsList.View = resultsView;
        resultsPanel.Children.Add(resultsList);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        void RenderResults(SearchFilesHistoryEntry entry)
        {
            txtSummary.Text = BuildSearchFilesSummary(entry);
            resultsList.Items.Clear();
            foreach (var match in entry.Matches ?? [])
            {
                resultsList.Items.Add(new SearchFilesResultRow
                {
                    File = match.RelativePath,
                    Line = match.LineNumber <= 0 ? "-" : match.LineNumber.ToString(),
                    Text = match.LinePreview
                });
            }
        }

        void RefreshHistoryList()
        {
            _searchFilesHistory = NormalizeSearchFilesHistory(_searchFilesHistory, _searchFilesHistoryLimit);
            txtHistoryHeader.Text = $"History (last {_searchFilesHistoryLimit} searches)";
            historyList.Items.Clear();
            foreach (var entry in _searchFilesHistory)
            {
                historyList.Items.Add(new SearchFilesHistoryListItem
                {
                    Entry = entry,
                    DisplayText = BuildSearchFilesHistoryDisplayText(entry)
                });
            }
        }

        void ApplyHistorySelection(SearchFilesHistoryEntry entry)
        {
            txtFolder.Text = entry.FolderPath;
            txtSearch.Text = entry.SearchText;
            txtSearchInFiles.Text = entry.SearchInFiles;
            RenderResults(entry);
            SetStatus("Loaded results from history. Search was not run again.");
        }

        void ShowSearchFilesSettingsDialog()
        {
            var settingsDlg = new Window
            {
                Title = "Search Files Settings",
                Width = 420,
                Height = 190,
                MinWidth = 360,
                MinHeight = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg,
                ResizeMode = ResizeMode.NoResize
            };

            var settingsRoot = new DockPanel { Margin = new Thickness(12) };
            settingsDlg.Content = settingsRoot;

            var settingsFooter = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var btnSettingsOk = new Button { Content = "OK", Width = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var btnSettingsCancel = new Button { Content = "Cancel", Width = 85, IsCancel = true };
            settingsFooter.Children.Add(btnSettingsOk);
            settingsFooter.Children.Add(btnSettingsCancel);
            DockPanel.SetDock(settingsFooter, Dock.Bottom);
            settingsRoot.Children.Add(settingsFooter);

            var settingsPanel = new Grid();
            settingsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            settingsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            settingsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            settingsRoot.Children.Add(settingsPanel);

            var lblHistorySize = new TextBlock
            {
                Text = "History size",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            };
            Grid.SetRow(lblHistorySize, 0);
            Grid.SetColumn(lblHistorySize, 0);
            settingsPanel.Children.Add(lblHistorySize);

            var txtSettingsHistorySize = new TextBox
            {
                Text = _searchFilesHistoryLimit.ToString(),
                Width = 90,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = $"How many searches to keep ({MinSearchFilesHistoryLimit}-{MaxSearchFilesHistoryLimit})."
            };
            Grid.SetRow(txtSettingsHistorySize, 0);
            Grid.SetColumn(txtSettingsHistorySize, 1);
            settingsPanel.Children.Add(txtSettingsHistorySize);

            var txtSettingsHelp = new TextBlock
            {
                Text = "This controls how many history entries are saved.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(txtSettingsHelp, 1);
            Grid.SetColumn(txtSettingsHelp, 0);
            Grid.SetColumnSpan(txtSettingsHelp, 2);
            settingsPanel.Children.Add(txtSettingsHelp);

            void CommitSettings()
            {
                var rawText = (txtSettingsHistorySize.Text ?? string.Empty).Trim();
                if (!int.TryParse(rawText, out var parsed))
                {
                    MessageBox.Show(
                        settingsDlg,
                        "Enter a valid number for history size.",
                        "Search Files Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    txtSettingsHistorySize.Focus();
                    txtSettingsHistorySize.SelectAll();
                    return;
                }

                var normalized = NormalizeSearchFilesHistoryLimit(parsed);
                var changed = normalized != _searchFilesHistoryLimit;
                _searchFilesHistoryLimit = normalized;
                _searchFilesHistory = NormalizeSearchFilesHistory(_searchFilesHistory, _searchFilesHistoryLimit);
                RefreshHistoryList();
                if (changed)
                    SaveWindowSettings();

                settingsDlg.DialogResult = true;
            }

            btnSettingsOk.Click += (_, _) => CommitSettings();
            txtSettingsHistorySize.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    CommitSettings();
                }
            };

            settingsDlg.Loaded += (_, _) =>
            {
                txtSettingsHistorySize.Focus();
                txtSettingsHistorySize.SelectAll();
            };

            settingsDlg.ShowDialog();
        }

        void ExecuteSearch()
        {
            var rawFolder = (txtFolder.Text ?? string.Empty).Trim();
            var searchText = (txtSearch.Text ?? string.Empty).Trim();
            var searchInFiles = (txtSearchInFiles.Text ?? string.Empty).Trim();

            if (rawFolder.Length == 0)
            {
                SetStatus("Choose a folder first.", Brushes.IndianRed);
                txtFolder.Focus();
                return;
            }

            string fullFolderPath;
            try
            {
                fullFolderPath = Path.GetFullPath(rawFolder);
            }
            catch
            {
                SetStatus("Folder path is not valid.", Brushes.IndianRed);
                txtFolder.Focus();
                return;
            }

            if (!Directory.Exists(fullFolderPath))
            {
                SetStatus("Folder does not exist.", Brushes.IndianRed);
                txtFolder.Focus();
                return;
            }

            if (searchText.Length == 0)
            {
                SetStatus("Enter a search string.", Brushes.IndianRed);
                txtSearch.Focus();
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = ExecuteSearchFiles(fullFolderPath, searchText, searchInFiles);
                var wasAddedToHistory = AddSearchFilesHistoryEntry(result);
                if (wasAddedToHistory)
                    SaveWindowSettings();
                RefreshHistoryList();
                if (historyList.Items.Count > 0)
                {
                    historyList.SelectedIndex = 0;
                }
                else
                {
                    RenderResults(result);
                }

                SetStatus(result.MatchCount == 0
                    ? "Search complete. No matches found."
                    : $"Search complete. Found {result.MatchCount} matches.");
                if (!wasAddedToHistory)
                    SetStatus("Search complete. Same input/output as previous search, not added to history.");
            }
            catch (Exception ex)
            {
                SetStatus($"Search failed: {ex.Message}", Brushes.IndianRed);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        btnBrowseFolder.Click += (_, _) =>
        {
            var picker = new VistaFolderBrowserDialog
            {
                Description = "Select folder to search",
                UseDescriptionForTitle = true
            };

            var currentText = (txtFolder.Text ?? string.Empty).Trim();
            try
            {
                if (currentText.Length > 0)
                {
                    var fullPath = Path.GetFullPath(currentText);
                    if (Directory.Exists(fullPath))
                        picker.SelectedPath = fullPath;
                }
            }
            catch
            {
                // Ignore invalid pre-filled path.
            }

            if (picker.ShowDialog(dlg) == true)
                txtFolder.Text = picker.SelectedPath;
        };

        btnSearch.Click += (_, _) => ExecuteSearch();
        btnSettings.Click += (_, _) => ShowSearchFilesSettingsDialog();
        txtSearch.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                ExecuteSearch();
            }
        };
        historyList.SelectionChanged += (_, _) =>
        {
            if (historyList.SelectedItem is SearchFilesHistoryListItem selected)
                ApplyHistorySelection(selected.Entry);
        };
        btnClose.Click += (_, _) => dlg.Close();

        dlg.Loaded += (_, _) =>
        {
            RefreshHistoryList();
            if (historyList.Items.Count > 0)
                historyList.SelectedIndex = 0;

            if (string.IsNullOrWhiteSpace(txtFolder.Text))
            {
                var initialFolder = _backupFolder;
                if (Directory.Exists(initialFolder))
                    txtFolder.Text = initialFolder;
            }

            txtSearch.Focus();
            SetStatus("Search string matches files. Optional pattern searches inside those matching files.");
        };

        dlg.ShowDialog();
    }
}

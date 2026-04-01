using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Noted.Models;

namespace Noted;

public partial class MainWindow : Window
{
    // --- State -------------------------------------------------------------------------
    private readonly Dictionary<TabItem, TabDocument> _docs = new();
    private readonly DispatcherTimer _autoSaveTimer;
    private Point _tabDragStartPoint;
    private TabItem? _dragSourceTab;
    private bool _startMaximized = false;
    private bool _sessionSaved = false;

    private const string BackupFolder = @"c:\tools\backup2\noted";
    private const string SettingsFile = @"c:\tools\backup2\noted\settings.json";
    private const int MaxBackups = 100;
    private const int DefaultAutoSaveSeconds = 30;
    private const int DefaultInitialLines = 50;
    private const string DefaultFontFamily = "Consolas, Courier New";
    private const double DefaultFontSize = 13;
    private const int DefaultFontWeight = 400;
    private const string BundleDivider = "^---";
    private int _initialLines = DefaultInitialLines;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    private int _fontWeight = DefaultFontWeight;

    // --- Constructor -------------------------------------------------------------
    public MainWindow()
    {
        InitializeComponent();

        // Routed commands -> our handlers
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewTab()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => SaveCurrentAs()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => CloseCurrentTab()));
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        MainTabControl.AllowDrop = true;
        MainTabControl.DragOver += MainTabControl_DragOver;
        MainTabControl.Drop += MainTabControl_Drop;

        // Auto-save timer
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DefaultAutoSaveSeconds) };
        _autoSaveTimer.Tick += (_, _) => SaveSession();
        _autoSaveTimer.Start();

        // Restore window position/size, then session
        LoadWindowSettings();
        Loaded += (_, _) => { if (_startMaximized) WindowState = WindowState.Maximized; };

        // Restore previous session; if nothing to restore, open a blank tab
        LoadSession();
        if (_docs.Count == 0)
            NewTab();
    }

    // --- Tab management ----------------------------------------------------------

    private int NextFileNumber()
    {
        var used = new HashSet<int>();
        foreach (var doc in _docs.Values)
        {
            if (doc.Header.StartsWith("new ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(doc.Header[4..], out int n))
                used.Add(n);
        }
        for (int i = 1; ; i++)
            if (!used.Contains(i)) return i;
    }

    private TabDocument CreateTab(string? header = null, string? content = null)
    {
        var name = header ?? $"new {NextFileNumber()}";

        var editor = CreateEditor();
        if (content != null)
            editor.Text = content;
        else
            editor.Text = new string('\n', _initialLines - 1);

        var doc = new TabDocument
        {
            Header = name,
            Editor = editor,
            CachedText = editor.Text,
            IsDirty = false
        };

        // Wire events
        editor.TextChanged += (_, _) => { doc.CachedText = editor.Text; MarkDirty(doc); };
        editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatusBar(doc);
        editor.PreviewMouseWheel += Editor_PreviewMouseWheel;

        // Build tab header
        var headerLabel = new TextBlock
        {
            Text = doc.DisplayHeader,
            Margin = new Thickness(2, 0, 2, 0)
        };

        var tab = new TabItem
        {
            Header = headerLabel,
            Content = editor,
            Tag = doc
        };
        headerLabel.Tag = tab;
        headerLabel.PreviewMouseLeftButtonDown += TabHeader_PreviewMouseLeftButtonDown;
        headerLabel.PreviewMouseMove += TabHeader_PreviewMouseMove;

        var tabMenu = new ContextMenu();
        var renameItem = new MenuItem { Header = "Rename..." };
        renameItem.Click += (_, _) => RenameTab(tab);
        tabMenu.Items.Add(renameItem);
        tab.ContextMenu = tabMenu;

        _docs[tab] = doc;
        MainTabControl.Items.Add(tab);
        MainTabControl.SelectedItem = tab;

        editor.Focus();
        return doc;
    }

    private TextEditor CreateEditor()
    {
        var editor = new TextEditor
        {
            FontFamily = new FontFamily(_fontFamily),
            FontSize = _fontSize,
            FontWeight = FontWeight.FromOpenTypeWeight(_fontWeight),
            ShowLineNumbers = true,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4)
        };
        editor.Options.HighlightCurrentLine = true;
        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(225, 240, 255));
        return editor;
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;

        double delta = e.Delta > 0 ? 1 : -1;
        double newSize = _fontSize + delta;
        if (newSize < 6) newSize = 6;
        if (newSize > 72) newSize = 72;
        _fontSize = newSize;

        var family = new FontFamily(_fontFamily);
        foreach (var doc in _docs.Values)
        {
            doc.Editor.FontFamily = family;
            doc.Editor.FontSize = _fontSize;
            doc.Editor.FontWeight = FontWeight.FromOpenTypeWeight(_fontWeight);
        }
    }

    private void MarkDirty(TabDocument doc)
    {
        if (doc.IsDirty) return;
        doc.IsDirty = true;
        RefreshTabHeader(doc);
    }

    private void RefreshTabHeader(TabDocument doc)
    {
        var tab = GetTab(doc);
        if (tab?.Header is TextBlock tb)
            tb.Text = doc.DisplayHeader;

        RefreshGlobalDirtyStatus();
    }

    private void RefreshGlobalDirtyStatus()
    {
        bool anyDirty = _docs.Values.Any(d => d.IsDirty);
        if (StatusUnsavedDot != null)
            StatusUnsavedDot.Text = anyDirty ? "U" : "S";
    }

    private TabItem? GetTab(TabDocument doc)
        => _docs.FirstOrDefault(kv => kv.Value == doc).Key;

    private TabDocument? CurrentDoc()
    {
        if (MainTabControl.SelectedItem is TabItem tab && _docs.TryGetValue(tab, out var doc))
            return doc;
        return null;
    }

    /// <summary>Returns true if the text is empty or whitespace-only (all blank lines).</summary>
    private static bool IsEffectivelyEmpty(string text)
        => string.IsNullOrWhiteSpace(text);

    // --- File operations -----------------------------------------------------

    private void NewTab()
    {
        // If the current tab is already empty, just stay on it
        var cur = CurrentDoc();
        if (cur != null && IsEffectivelyEmpty(cur.CachedText))
            return;
        CreateTab(null, null);
    }

    private bool SaveCurrentAs()
    {
        var doc = CurrentDoc();
        if (doc == null) return false;

        var dlg = new SaveFileDialog
        {
            FileName = doc.Header,
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return false;

        doc.FilePath = dlg.FileName;
        doc.Header = Path.GetFileName(dlg.FileName);
        return WriteFile(doc, dlg.FileName);
    }

    private static string RemoveTrailingWhitespaces(string text)
        => string.IsNullOrEmpty(text) ? text : Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);

    private bool WriteFile(TabDocument doc, string path)
    {
        try
        {
            var textToSave = RemoveTrailingWhitespaces(doc.Editor.Text);
            if (doc.Editor.Text != textToSave)
            {
                var currentCaret = doc.Editor.CaretOffset;
                doc.Editor.Document.Text = textToSave;
                doc.Editor.CaretOffset = Math.Min(currentCaret, doc.Editor.Document.TextLength);
            }

            File.WriteAllText(path, textToSave, System.Text.Encoding.UTF8);
            doc.IsDirty = false;
            RefreshTabHeader(doc);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file:\n{ex.Message}", "Noted",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }


    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            if (MainTabControl.SelectedItem is TabItem tab)
            {
                e.Handled = true;
                RenameTab(tab);
            }
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var doc = CurrentDoc();
            if (doc != null)
            {
                e.Handled = true;
                doc.Editor.Document.Insert(doc.Editor.Document.TextLength, new string('\n', 10));
                doc.Editor.ScrollToEnd();
            }
        }
    }

    private void TabHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not TabItem tab) return;
        _tabDragStartPoint = e.GetPosition(this);
        _dragSourceTab = tab;
    }

    private void TabHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSourceTab == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _tabDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _tabDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var dragTab = _dragSourceTab;
        _dragSourceTab = null;
        DragDrop.DoDragDrop((DependencyObject)sender, dragTab, DragDropEffects.Move);
    }

    private void MainTabControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TabItem)) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void MainTabControl_Drop(object sender, DragEventArgs e)
    {
        var sourceTab = e.Data.GetData(typeof(TabItem)) as TabItem;
        var targetTab = TryGetTabFromHeaderSource(e.OriginalSource as DependencyObject);
        if (sourceTab == null || targetTab == null || ReferenceEquals(sourceTab, targetTab)) return;

        int sourceIndex = MainTabControl.Items.IndexOf(sourceTab);
        int targetIndex = MainTabControl.Items.IndexOf(targetTab);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        MainTabControl.Items.Remove(sourceTab);
        if (sourceIndex < targetIndex) targetIndex--;
        MainTabControl.Items.Insert(targetIndex, sourceTab);
        MainTabControl.SelectedItem = sourceTab;
        SaveSession(updateStatus: true);
    }

    private TabItem? TryGetTabFromHeaderSource(DependencyObject? source)
    {
        if (source == null) return null;

        var fromGenerator = ItemsControl.ContainerFromElement(MainTabControl, source) as TabItem;
        if (fromGenerator != null) return fromGenerator;

        if (source is FrameworkElement fe && fe.Tag is TabItem taggedTab)
            return taggedTab;

        DependencyObject? current = source;

        while (current != null)
        {
            if (current is TabItem tab)
                return tab;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private void RenameTab(TabItem tab)
    {
        if (tab.Tag is not TabDocument doc) return;
        var newName = ShowRenameDialog(doc.Header);

        if (string.IsNullOrWhiteSpace(newName)) return;

        doc.Header = newName.Trim();
        RefreshTabHeader(doc);
    }

    private string? ShowRenameDialog(string currentName)
    {
        var dlg = new Window
        {
            Title = "Rename Tab",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Tab name:", Margin = new Thickness(0, 0, 0, 6) });

        var input = new TextBox { Text = currentName, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(input);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                MessageBox.Show("Tab name cannot be empty.", "Rename Tab", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dlg.DialogResult = true;
        };

        dlg.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
            Keyboard.Focus(input);
        };

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        dlg.Content = root;
        var result = dlg.ShowDialog();
        return result == true ? input.Text : null;
    }

    private void CloseCurrentTab()
    {
        if (MainTabControl.SelectedItem is TabItem tab)
            CloseTab(tab);
    }

    private bool CloseTab(TabItem tab)
    {
        if (!_docs.TryGetValue(tab, out var doc)) return true;

        if (doc.IsDirty && !string.IsNullOrEmpty(doc.Editor.Text))
        {
            // Ensure the tab with unsaved changes is in view
            MainTabControl.SelectedItem = tab;

            var result = MessageBox.Show(
                $"Save \"{doc.Header}\" before closing?", "Noted",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes && !SaveCurrentAs()) return false;
        }

        _docs.Remove(tab);
        MainTabControl.Items.Remove(tab);

        RefreshGlobalDirtyStatus();

        // Always keep at least one tab open
        if (MainTabControl.Items.Count == 0)
            NewTab();

        return true;
    }

    // -- Session serialisation ----------------------------------------------------

    /// <summary>
    /// Writes all open tabs into a single timestamped backup file.
    /// Format:
    ///   ===filename===
    ///   ...content...
    ///   ===filename2===
    ///   ...
    /// </summary>
    private void SaveSession(bool updateStatus = true)
    {
        try
        {
            foreach (var doc in _docs.Values)
                doc.CachedText = RemoveTrailingWhitespaces(doc.Editor.Text);

            Directory.CreateDirectory(BackupFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(BackupFolder, $"noted_{timestamp}.txt");

            using var sw = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
            foreach (var item in MainTabControl.Items)
            {
                if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                    continue;

                var text = doc.CachedText;
                // Skip empty/whitespace-only tabs - never store them in the backup
                if (IsEffectivelyEmpty(text)) continue;

                // Divider format: ^---name^---
                // Content format: plain text (verbatim)
                sw.WriteLine($"{BundleDivider}{doc.Header}{BundleDivider}");
                sw.Write(text);

                // Guarantee next divider starts on its own line
                if (text.Length > 0 && !text.EndsWith('\n'))
                    sw.WriteLine();
            }

            PruneBackups();

            // Bundle save is the "real" save - clear dirty flags
            foreach (var doc in _docs.Values)
            {
                doc.IsDirty = false;
                RefreshTabHeader(doc);
            }
            if (updateStatus && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.Invoke(() => StatusAutoSave.Text = $"Last saved: {DateTime.Now:yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex)
        {
            if (updateStatus && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.Invoke(() => StatusAutoSave.Text = $"Save failed: {ex.Message}");
        }
    }

    /// <summary>Deletes the oldest backups when more than MaxBackups files exist.</summary>
    private static void PruneBackups()
    {
        var files = Directory.GetFiles(BackupFolder, "noted_*.txt")
                            .OrderBy(f => f)            // lexicographical = chronological
                            .ToList();

        while (files.Count > MaxBackups)
        {
            File.Delete(files[0]);
            files.RemoveAt(0);
        }
    }

    /// <summary>Restores all tabs from the most recent backup file, if one exists.</summary>
    private void LoadSession()
    {
        if (!Directory.Exists(BackupFolder)) return;

        var latest = Directory.GetFiles(BackupFolder, "noted_*.txt")
                            .OrderByDescending(f => f)
                            .FirstOrDefault();

        if (latest == null) return;

        try
        {
            var text = File.ReadAllText(latest, System.Text.Encoding.UTF8);
            var dividerNew = new Regex(@"^\^---(.*)\^---\r?$", RegexOptions.Multiline);
            var dividerOld = new Regex(@"^====(.*)====\r?$", RegexOptions.Multiline);
            var matches = dividerNew.Matches(text);
            var useNewFormat = matches.Count > 0;
            if (!useNewFormat)
                matches = dividerOld.Matches(text);

            for (int i = 0; i < matches.Count; i++)
            {
                var name = matches[i].Groups[1].Value;
                int contentStart = matches[i].Index + matches[i].Length;

                // Skip the line-ending after the divider
                if (contentStart < text.Length && text[contentStart] == '\r') contentStart++;
                if (contentStart < text.Length && text[contentStart] == '\n') contentStart++;

                int contentEnd = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : text.Length;

                // Strip the trailing newline that SaveSession adds
                var content = text[contentStart..contentEnd];
                if (content.EndsWith("\r\n")) content = content[..^2];
                else if (content.EndsWith('\n')) content = content[..^1];

                // New format keeps content verbatim; no escaping/decoding

                var doc = CreateTab(name, content);
                doc.IsDirty = false;
                RefreshTabHeader(doc);
            }

            var lastSaved = File.GetLastWriteTime(latest);
            StatusAutoSave.Text = $"Last saved: {lastSaved:yyyy-MM-dd HH:mm}";
        }
        catch
        {
            // If the backup file is corrupt, just start fresh - no crash
        }
    }

    // --- Status bar -----------------------------------------------------------

    private void UpdateStatusBar(TabDocument doc)
    {
        if (CurrentDoc() != doc) return;
        var caret = doc.Editor.TextArea.Caret;
        StatusLine.Text = $"Ln {caret.Line}";
        StatusColumn.Text = $"Col {caret.Column}";
    }

    // --- Event handlers -------------------------------------------------------

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var doc = CurrentDoc();
        if (doc != null) UpdateStatusBar(doc);
    }

    private void MenuNew_Click(object sender, RoutedEventArgs e) => NewTab();
    private void MenuSaveAs_Click(object sender, RoutedEventArgs e) => SaveCurrentAs();

    private void MenuOpenLastBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(BackupFolder)) return;
        var latest = Directory.GetFiles(BackupFolder, "noted_*.txt").OrderByDescending(f => f).FirstOrDefault();
        if (latest != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{latest}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open VS Code:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void MenuCloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();
    private void MenuSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsDialog();

    private void MenuUndo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Undo();
    private void MenuRedo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Redo();
    private void MenuCut_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Cut();
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Copy();
    private void MenuPaste_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Paste();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        SaveWindowSettings();
        if (!_sessionSaved)
            SaveSession(updateStatus: false);
    }

    // -- Window settings ------------------------------------------------------------------

    private void SaveWindowSettings()
    {
        try
        {
            Directory.CreateDirectory(BackupFolder);
            var state = new WindowSettings
            {
                Left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
                Top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
                Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
                Maximized = WindowState == WindowState.Maximized,
                AutoSaveSeconds = (int)_autoSaveTimer.Interval.TotalSeconds,
                InitialLines = _initialLines,
                FontFamily = _fontFamily,
                FontSize = _fontSize,
                FontWeight = _fontWeight
            };
            File.WriteAllText(SettingsFile,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-critical */ }
    }

    private void LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            var state = JsonSerializer.Deserialize<WindowSettings>(
                File.ReadAllText(SettingsFile));
            if (state == null) return;

            Left = state.Left;
            Top = state.Top;
            Width = state.Width;
            Height = state.Height;
            // Apply auto-save interval
            if (state.AutoSaveSeconds > 0)
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(state.AutoSaveSeconds);
            if (state.InitialLines >= 1)
                _initialLines = state.InitialLines;
            if (!string.IsNullOrWhiteSpace(state.FontFamily))
                _fontFamily = state.FontFamily;
            if (state.FontSize >= 6)
                _fontSize = state.FontSize;
            if (state.FontWeight >= 100 && state.FontWeight <= 900)
                _fontWeight = state.FontWeight;
            // Don't maximize here - defer to Loaded so the window is on the right monitor first
            _startMaximized = state.Maximized;
        }
        catch { /* ignore corrupt settings */ }
    }

    private sealed class WindowSettings
    {
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 1100;
        public double Height { get; set; } = 700;
        public bool Maximized { get; set; } = false;
        public int AutoSaveSeconds { get; set; } = DefaultAutoSaveSeconds;
        public int InitialLines { get; set; } = DefaultInitialLines;
        public string FontFamily { get; set; } = DefaultFontFamily;
        public double FontSize { get; set; } = DefaultFontSize;
        public int FontWeight { get; set; } = DefaultFontWeight;
    }

    // --- Settings dialog ----------------------------------------------------

    private void ShowSettingsDialog()
    {
        var dlg = new Window
        {
            Title = "Settings",
            Width = 380, Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblAutoSave = new TextBlock { Text = "Auto-save interval (seconds):" };
        Grid.SetRow(lblAutoSave, 0);
        Grid.SetColumn(lblAutoSave, 0);
        var txtAutoSave = new TextBox
        {
            Text = ((int)_autoSaveTimer.Interval.TotalSeconds).ToString(),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(txtAutoSave, 0);
        Grid.SetColumn(txtAutoSave, 1);

        var lblLines = new TextBlock { Text = "Initial lines per new tab:" };
        Grid.SetRow(lblLines, 1);
        Grid.SetColumn(lblLines, 0);
        var txtLines = new TextBox
        {
            Text = _initialLines.ToString(),
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(txtLines, 1);
        Grid.SetColumn(txtLines, 1);

        var lblFont = new TextBlock { Text = "Font family:", Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblFont, 2);
        Grid.SetColumn(lblFont, 0);
        var cmbFont = new ComboBox
        {
            IsEditable = true,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        string[] popularFonts =
        {
            "Consolas", "Courier New", "Source Code Pro", "Cascadia Code", "Cascadia Mono", "Fira Code",
            "JetBrains Mono", "Lucida Console", "Menlo", "Monaco", "Roboto Mono", "Ubuntu Mono"
        };
        foreach (var f in popularFonts)
            cmbFont.Items.Add(f);
        cmbFont.Text = _fontFamily;
        Grid.SetRow(cmbFont, 2);
        Grid.SetColumn(cmbFont, 1);

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblFontSize = new TextBlock { Text = "Font size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(lblFontSize, 3);
        Grid.SetColumn(lblFontSize, 0);

        var txtFontSize = new TextBox { Text = _fontSize.ToString(), Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(txtFontSize, 3);
        Grid.SetColumn(txtFontSize, 1);

        var lblFontWeight = new TextBlock { Text = "Font weight:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(lblFontWeight, 4);
        Grid.SetColumn(lblFontWeight, 0);

        var cmbFontWeight = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        var weights = new (string Name, int Value)[]
        {
            ("Thin (100)", 100),
            ("ExtraLight (200)", 200),
            ("Light (300)", 300),
            ("Normal (400)", 400),
            ("Medium (500)", 500),
            ("SemiBold (600)", 600),
            ("Bold (700)", 700),
            ("ExtraBold (800)", 800),
            ("Black (900)", 900)
        };
        int selectedIdx = 3; // default Normal
        for (int i = 0; i < weights.Length; i++)
        {
            cmbFontWeight.Items.Add(weights[i].Name);
            if (weights[i].Value == _fontWeight) selectedIdx = i;
        }
        cmbFontWeight.SelectedIndex = selectedIdx;
        Grid.SetRow(cmbFontWeight, 4);
        Grid.SetColumn(cmbFontWeight, 1);

        var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 8, 8, 0), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 8, 0, 0), IsCancel = true };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonPanel.Children.Add(btnOk);
        buttonPanel.Children.Add(btnCancel);
        Grid.SetRow(buttonPanel, 6);
        Grid.SetColumnSpan(buttonPanel, 2);

        grid.Children.Add(lblAutoSave);
        grid.Children.Add(txtAutoSave);
        grid.Children.Add(lblLines);
        grid.Children.Add(txtLines);
        grid.Children.Add(lblFont);
        grid.Children.Add(cmbFont);
        grid.Children.Add(lblFontSize);
        grid.Children.Add(txtFontSize);
        grid.Children.Add(lblFontWeight);
        grid.Children.Add(cmbFontWeight);
        grid.Children.Add(buttonPanel);

        dlg.Content = grid;

        btnOk.Click += (_, _) =>
        {
            if (int.TryParse(txtAutoSave.Text, out int secs) && secs >= 5
                && int.TryParse(txtLines.Text, out int lines) && lines >= 1
                && double.TryParse(txtFontSize.Text, out double fsize) && fsize >= 6
                && !string.IsNullOrWhiteSpace(cmbFont.Text))
            {
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(secs);
                _initialLines = lines;
                _fontFamily = cmbFont.Text.Trim();
                _fontSize = fsize;
                _fontWeight = weights[cmbFontWeight.SelectedIndex].Value;

                // Apply font to all open editors
                var family = new FontFamily(_fontFamily);
                var weight = FontWeight.FromOpenTypeWeight(_fontWeight);
                foreach (var doc in _docs.Values)
                {
                    doc.Editor.FontFamily = family;
                    doc.Editor.FontSize = _fontSize;
                    doc.Editor.FontWeight = weight;
                }

                SaveWindowSettings();
                dlg.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Auto-save must be \u2265 5 seconds.\nInitial lines must be \u2265 1.\nFont size must be \u2265 6.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        dlg.ShowDialog();
    }
}
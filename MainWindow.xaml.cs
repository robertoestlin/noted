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
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using Noted.Models;
using Ookii.Dialogs.Wpf;

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
    private bool _lastSaveIncludedCloudCopy = false;

    private const string SettingsFileName = "settings.json";

    private static string DefaultBackupFolder() => @"c:\tools\backup\noted";
    private static string DefaultCloudBackupFolder() => Path.Combine(DefaultBackupFolder(), "cloud");

    private string _backupFolder = DefaultBackupFolder();
    private string _cloudBackupFolder = DefaultCloudBackupFolder();
    private int _cloudSaveIntervalHours = 1;
    private int _cloudSaveIntervalMinutes = 0;
    private DateTime _lastCloudSaveUtc = DateTime.MinValue;
    private const int MaxBackups = 100;

    /// <summary>Filenames written by <see cref="SaveSession"/> (<c>noted_yyyyMMdd_HHmmss.txt</c>).</summary>
    private static readonly Regex NotedBackupFileNameRegex =
        new(@"^noted_\d{8}_\d{6}\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int DefaultAutoSaveSeconds = 30;
    private const int DefaultInitialLines = 50;
    private const string DefaultFontFamily = "Consolas, Courier New";
    private const double DefaultFontSize = 13;
    private const int DefaultFontWeight = 400;
    private const string DefaultShortcutNewPrimary = "Ctrl+N";
    private const string DefaultShortcutNewSecondary = "Ctrl+T";
    private const string DefaultShortcutCloseTab = "Ctrl+W";
    private const string DefaultShortcutRenameTab = "F2";
    private const string DefaultShortcutAddBlankLines = "Ctrl+Space";
    private const string DefaultShortcutToggleHighlight = "Ctrl+J";
    private static readonly Color DefaultSelectedLineColor = Color.FromRgb(225, 240, 255);
    private static readonly Color DefaultHighlightedLineColor = Color.FromRgb(255, 196, 128);
    private static readonly Color DefaultSelectedHighlightedLineColor = Color.FromRgb(255, 160, 96);
    private const string BundleDivider = "^---";
    private const string MetadataPrefix = "^meta^";
    private static readonly int[] CloudMinuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];
    private int _initialLines = DefaultInitialLines;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    private int _fontWeight = DefaultFontWeight;
    private string _shortcutNewPrimary = DefaultShortcutNewPrimary;
    private string _shortcutNewSecondary = DefaultShortcutNewSecondary;
    private string _shortcutCloseTab = DefaultShortcutCloseTab;
    private string _shortcutRenameTab = DefaultShortcutRenameTab;
    private string _shortcutAddBlankLines = DefaultShortcutAddBlankLines;
    private string _shortcutToggleHighlight = DefaultShortcutToggleHighlight;
    private Color _selectedLineColor = DefaultSelectedLineColor;
    private Color _highlightedLineColor = DefaultHighlightedLineColor;
    private Color _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
    private Brush _selectedLineBrush = CreateFrozenBrush(DefaultSelectedLineColor);
    private Brush _highlightedLineBrush = CreateFrozenBrush(DefaultHighlightedLineColor);
    private Brush _selectedHighlightedLineBrush = CreateFrozenBrush(DefaultSelectedHighlightedLineColor);
    private readonly List<KeyBinding> _shortcutBindings = [];

    private static readonly RoutedUICommand RenameTabCommand = new("Rename Tab", nameof(RenameTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand AddBlankLinesCommand = new("Add Blank Lines", nameof(AddBlankLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleHighlightCommand = new("Toggle Highlight", nameof(ToggleHighlightCommand), typeof(MainWindow));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed class FileMetadata
    {
        // Backward-compatible legacy field (single highlight).
        public int? HighlightLine { get; set; }

        // Current format supports multiple highlighted lines.
        public List<int>? HighlightLines { get; set; }
    }

    private sealed class HighlightLineRenderer : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyCollection<int>> _lineProvider;
        private readonly Func<int, bool> _lineSelectionProvider;
        private readonly Func<Brush> _normalBrushProvider;
        private readonly Func<Brush> _selectedBrushProvider;

        public HighlightLineRenderer(
            Func<IReadOnlyCollection<int>> lineProvider,
            Func<int, bool> lineSelectionProvider,
            Func<Brush> normalBrushProvider,
            Func<Brush> selectedBrushProvider)
        {
            _lineProvider = lineProvider;
            _lineSelectionProvider = lineSelectionProvider;
            _normalBrushProvider = normalBrushProvider;
            _selectedBrushProvider = selectedBrushProvider;
        }

        // Draw on selection layer so highlight remains visible even when selected.
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            var highlightedLines = _lineProvider();
            if (highlightedLines.Count == 0)
                return;

            foreach (var lineNumber in highlightedLines)
            {
                if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                    continue;

                var line = textView.Document.GetLineByNumber(lineNumber);
                var segment = new TextSegment
                {
                    StartOffset = line.Offset,
                    EndOffset = line.Offset + line.Length
                };

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    var fullWidthRect = new Rect(0, rect.Top, textView.ActualWidth, rect.Height);
                    var brush = _lineSelectionProvider(lineNumber) ? _selectedBrushProvider() : _normalBrushProvider();
                    drawingContext.DrawRectangle(brush, null, fullWidthRect);
                }
            }
        }
    }

    // --- Constructor -------------------------------------------------------------
    public MainWindow()
    {
        InitializeComponent();

        // Routed commands -> our handlers
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewTab()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => CloseCurrentTab()));
        CommandBindings.Add(new CommandBinding(RenameTabCommand, (_, _) => ExecuteRenameCurrentTab()));
        CommandBindings.Add(new CommandBinding(AddBlankLinesCommand, (_, _) => ExecuteAddBlankLines()));
        CommandBindings.Add(new CommandBinding(ToggleHighlightCommand, (_, _) => ExecuteToggleHighlight()));

        MainTabControl.AllowDrop = true;
        MainTabControl.DragOver += MainTabControl_DragOver;
        MainTabControl.Drop += MainTabControl_Drop;

        // Auto-save timer
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DefaultAutoSaveSeconds) };
        _autoSaveTimer.Tick += (_, _) => SaveSession();
        _autoSaveTimer.Start();

        // Restore window position/size, then session
        LoadWindowSettings();
        ApplyShortcutBindings();
        EnsureSettingsFileExists();
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

        var highlightRenderer = new HighlightLineRenderer(
            () => GetHighlightedLineNumbers(doc),
            line => IsLineSelected(doc.Editor, line),
            () => _highlightedLineBrush,
            () => _selectedHighlightedLineBrush);
        doc.HighlightRenderer = highlightRenderer;
        editor.TextArea.TextView.BackgroundRenderers.Add(highlightRenderer);

        // Wire events
        editor.TextChanged += (_, _) =>
        {
            doc.CachedText = editor.Text;
            MarkDirty(doc);
            RedrawHighlight(doc);
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            UpdateStatusBar(doc);
            RedrawHighlight(doc);
        };
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
        editor.TextArea.TextView.CurrentLineBackground = _selectedLineBrush;
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
        _lastSaveIncludedCloudCopy = false;
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
            StatusUnsavedDot.Text = _lastSaveIncludedCloudCopy ? "C" : (anyDirty ? "U" : "S");
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

    /// <summary>Writes the current tab to a path chosen by the user; does not rename the tab, bind a path, or clear dirty state.</summary>
    private bool ExportCurrentTabToFile()
    {
        var doc = CurrentDoc();
        if (doc == null) return false;

        var dlg = new SaveFileDialog
        {
            FileName = doc.Header,
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            var textToSave = RemoveTrailingWhitespaces(doc.Editor.Text);
            File.WriteAllText(dlg.FileName, textToSave, System.Text.Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export file:\n{ex.Message}", "Noted",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string RemoveTrailingWhitespaces(string text)
        => string.IsNullOrEmpty(text) ? text : Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);

    private static bool IsLineSelected(TextEditor editor, int lineNumber)
    {
        // Treat the caret line as selected/active too.
        if (editor.TextArea.Caret.Line == lineNumber)
            return true;

        var selection = editor.TextArea.Selection;
        if (selection == null || selection.IsEmpty)
            return false;

        int startLine = selection.StartPosition.Line;
        int endLine = selection.EndPosition.Line;
        if (startLine <= 0 || endLine <= 0)
            return false;

        int firstLine = Math.Min(startLine, endLine);
        int lastLine = Math.Max(startLine, endLine);
        return lineNumber >= firstLine && lineNumber <= lastLine;
    }

    private void ApplyColorThemeToOpenEditors()
    {
        _selectedLineBrush = CreateFrozenBrush(_selectedLineColor);
        _highlightedLineBrush = CreateFrozenBrush(_highlightedLineColor);
        _selectedHighlightedLineBrush = CreateFrozenBrush(_selectedHighlightedLineColor);

        foreach (var doc in _docs.Values)
        {
            doc.Editor.TextArea.TextView.CurrentLineBackground = _selectedLineBrush;
            RedrawHighlight(doc);
        }
    }

    private static bool TryParseColor(string? input, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            var parsed = ColorConverter.ConvertFromString(input.Trim());
            if (parsed is Color c)
            {
                color = c;
                return true;
            }
        }
        catch
        {
            // handled by caller
        }

        return false;
    }

    private static string ColorToHex(Color color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private IReadOnlyCollection<int> GetHighlightedLineNumbers(TabDocument doc)
    {
        if (doc.HighlightAnchors.Count == 0)
            return [];

        var lines = new HashSet<int>();
        for (int i = doc.HighlightAnchors.Count - 1; i >= 0; i--)
        {
            var anchor = doc.HighlightAnchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.HighlightAnchors.RemoveAt(i);
                continue;
            }

            lines.Add(anchor.Line);
        }

        return lines;
    }

    private bool IsLineHighlighted(TabDocument doc, int lineNumber)
    {
        for (int i = doc.HighlightAnchors.Count - 1; i >= 0; i--)
        {
            var anchor = doc.HighlightAnchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.HighlightAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line == lineNumber)
                return true;
        }

        return false;
    }

    private bool AddHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
    {
        int lineCount = doc.Editor.Document.LineCount;
        if (lineCount <= 0)
            lineCount = 1;

        int line = Math.Max(1, Math.Min(lineNumber, lineCount));
        if (IsLineHighlighted(doc, line))
            return false;

        var docLine = doc.Editor.Document.GetLineByNumber(line);
        var anchor = doc.Editor.Document.CreateAnchor(docLine.Offset);
        anchor.MovementType = AnchorMovementType.BeforeInsertion;
        doc.HighlightAnchors.Add(anchor);

        if (markDirty)
            MarkDirty(doc);
        if (redraw)
            RedrawHighlight(doc);
        return true;
    }

    private bool RemoveHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
    {
        bool removed = false;
        for (int i = doc.HighlightAnchors.Count - 1; i >= 0; i--)
        {
            var anchor = doc.HighlightAnchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.HighlightAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line == lineNumber)
            {
                doc.HighlightAnchors.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && markDirty)
            MarkDirty(doc);
        if (removed && redraw)
            RedrawHighlight(doc);
        return removed;
    }

    private void SetHighlightedLines(TabDocument doc, IEnumerable<int>? lineNumbers, bool markDirty = true)
    {
        doc.HighlightAnchors.Clear();

        if (lineNumbers != null)
        {
            foreach (var line in lineNumbers.Where(line => line > 0).Distinct())
                AddHighlightedLine(doc, line, markDirty: false, redraw: false);
        }

        if (markDirty)
            MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void ToggleHighlightedCaretLine(TabDocument doc)
    {
        var selectedLines = GetSelectedLineNumbers(doc);
        if (selectedLines.Count > 0)
        {
            bool allHighlighted = selectedLines.All(line => IsLineHighlighted(doc, line));
            bool changed = false;
            foreach (var line in selectedLines)
            {
                changed |= allHighlighted
                    ? RemoveHighlightedLine(doc, line, markDirty: false, redraw: false)
                    : AddHighlightedLine(doc, line, markDirty: false, redraw: false);
            }

            if (changed)
            {
                MarkDirty(doc);
                RedrawHighlight(doc);
            }
            return;
        }

        int caretLine = Math.Max(1, doc.Editor.TextArea.Caret.Line);
        if (IsLineHighlighted(doc, caretLine))
            RemoveHighlightedLine(doc, caretLine);
        else
            AddHighlightedLine(doc, caretLine);
    }

    private static List<int> GetSelectedLineNumbers(TabDocument doc)
    {
        var selection = doc.Editor.TextArea.Selection;
        if (selection == null || selection.IsEmpty)
            return [];

        int startLine = selection.StartPosition.Line;
        int endLine = selection.EndPosition.Line;
        if (startLine <= 0 || endLine <= 0)
            return [];

        int firstLine = Math.Min(startLine, endLine);
        int lastLine = Math.Max(startLine, endLine);
        return Enumerable.Range(firstLine, lastLine - firstLine + 1).ToList();
    }

    private static string BuildMetadataLine(IReadOnlyCollection<int> highlightedLines)
    {
        var metadata = new FileMetadata { HighlightLines = highlightedLines.OrderBy(n => n).ToList() };
        return $"{MetadataPrefix}{JsonSerializer.Serialize(metadata)}";
    }

    private static bool TryReadMetadataLine(string text, int startIndex, out FileMetadata metadata, out int nextContentStart)
    {
        metadata = new FileMetadata();
        nextContentStart = startIndex;
        if (startIndex >= text.Length)
            return false;

        int lineEnd = text.IndexOf('\n', startIndex);
        if (lineEnd < 0)
            lineEnd = text.Length;

        var lineText = text[startIndex..lineEnd];
        if (lineText.EndsWith('\r'))
            lineText = lineText[..^1];

        if (!lineText.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            return false;

        var payload = lineText[MetadataPrefix.Length..].Trim();
        if (payload.Length > 0)
        {
            try
            {
                metadata = JsonSerializer.Deserialize<FileMetadata>(payload) ?? new FileMetadata();
            }
            catch
            {
                metadata = new FileMetadata();
            }
        }

        nextContentStart = lineEnd < text.Length ? lineEnd + 1 : lineEnd;
        return true;
    }

    private static void RedrawHighlight(TabDocument doc)
        => doc.Editor.TextArea.TextView.Redraw();


    private static bool TryParseKeyGesture(string? input, out KeyGesture gesture)
    {
        gesture = null!;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            var converter = new KeyGestureConverter();
            var parsed = converter.ConvertFromInvariantString(input.Trim());
            if (parsed is KeyGesture keyGesture && keyGesture.Key != Key.None)
            {
                gesture = keyGesture;
                return true;
            }
        }
        catch
        {
            // Invalid gesture text.
        }

        return false;
    }

    private void ApplyShortcutBindings()
    {
        foreach (var binding in _shortcutBindings)
            InputBindings.Remove(binding);
        _shortcutBindings.Clear();

        AddShortcutBinding(_shortcutNewPrimary, ApplicationCommands.New);
        AddShortcutBinding(_shortcutNewSecondary, ApplicationCommands.New);
        AddShortcutBinding(_shortcutCloseTab, ApplicationCommands.Close);
        AddShortcutBinding(_shortcutRenameTab, RenameTabCommand);
        AddShortcutBinding(_shortcutAddBlankLines, AddBlankLinesCommand);
        AddShortcutBinding(_shortcutToggleHighlight, ToggleHighlightCommand);
        UpdateMenuShortcutTexts();
    }

    private void AddShortcutBinding(string? gestureText, ICommand command)
    {
        if (!TryParseKeyGesture(gestureText, out var gesture))
            return;

        var binding = new KeyBinding(command, gesture);
        _shortcutBindings.Add(binding);
        InputBindings.Add(binding);
    }

    private void ExecuteRenameCurrentTab()
    {
        if (MainTabControl.SelectedItem is TabItem tab)
            RenameTab(tab);
    }

    private void ExecuteAddBlankLines()
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        doc.Editor.Document.Insert(doc.Editor.Document.TextLength, new string('\n', 10));
        doc.Editor.ScrollToEnd();
    }

    private void ExecuteToggleHighlight()
    {
        var doc = CurrentDoc();
        if (doc != null)
            ToggleHighlightedCaretLine(doc);
    }

    private static string GestureDisplayText(string? gestureText)
        => string.IsNullOrWhiteSpace(gestureText) ? "None" : gestureText.Trim();

    private static string CombinedGestureDisplayText(string? first, string? second)
    {
        var one = GestureDisplayText(first);
        var two = GestureDisplayText(second);
        return two == "None" ? one : $"{one} / {two}";
    }

    private void UpdateMenuShortcutTexts()
    {
        if (MenuItemNewTab != null)
            MenuItemNewTab.InputGestureText = CombinedGestureDisplayText(_shortcutNewPrimary, _shortcutNewSecondary);
        if (MenuItemCloseTab != null)
            MenuItemCloseTab.InputGestureText = GestureDisplayText(_shortcutCloseTab);
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
            if (result == MessageBoxResult.Yes && !ExportCurrentTabToFile()) return false;
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

    /// <summary>Most recently written <c>noted_*.txt</c> in the folder (by last write time).</summary>
    private static string? GetLatestBackupFilePath(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        return Directory.GetFiles(folder, "noted_*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ThenByDescending(fi => fi.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Resolves a GUI editor executable so we do not spawn <c>code.cmd</c> (which leaves <c>cmd.exe</c> running).
    /// </summary>
    private static string? TryFindVsCodeOrCursorExecutable()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        [
            Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(pf, "Microsoft VS Code", "Code.exe"),
            Path.Combine(pfx86, "Microsoft VS Code", "Code.exe"),
            Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
            Path.Combine(local, "Programs", "cursor", "Cursor.exe"),
        ];
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

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
            _lastSaveIncludedCloudCopy = false;
            foreach (var doc in _docs.Values)
                doc.CachedText = RemoveTrailingWhitespaces(doc.Editor.Text);

            Directory.CreateDirectory(_backupFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(_backupFolder, $"noted_{timestamp}.txt");

            {
                using var sw = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
                foreach (var item in MainTabControl.Items)
                {
                    if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                        continue;

                    var text = doc.CachedText;
                    // Skip empty/whitespace-only tabs - never store them in the backup
                    if (IsEffectivelyEmpty(text)) continue;

                    // Divider format: ^---name^---
                    // Metadata format: ^meta^{"highlightLine":N}
                    // Content format: plain text (verbatim)
                    sw.WriteLine($"{BundleDivider}{doc.Header}{BundleDivider}");
                    sw.WriteLine(BuildMetadataLine(GetHighlightedLineNumbers(doc)));
                    sw.Write(text);

                    // Guarantee next divider starts on its own line
                    if (text.Length > 0 && !text.EndsWith('\n'))
                        sw.WriteLine();
                }
            }

            PruneBackups();
            TrySaveCloudBackup(path);

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

    private TimeSpan CloudSaveInterval()
        => TimeSpan.FromHours(_cloudSaveIntervalHours) + TimeSpan.FromMinutes(_cloudSaveIntervalMinutes);

    private static DateTime GetLatestBackupWriteUtcOrMin(string folder)
    {
        var latest = GetLatestBackupFilePath(folder);
        if (latest == null) return DateTime.MinValue;
        try
        {
            return File.GetLastWriteTimeUtc(latest);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string FormatCloudCopyTimestamp(DateTime utcTimestamp)
        => utcTimestamp == DateTime.MinValue
            ? "Never"
            : utcTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private bool ShouldSaveCloudBackup()
    {
        if (string.IsNullOrWhiteSpace(_cloudBackupFolder)) return false;
        if (_cloudSaveIntervalHours < 0 || _cloudSaveIntervalHours > 50) return false;
        if (_cloudSaveIntervalMinutes < 0 || _cloudSaveIntervalMinutes > 55 || _cloudSaveIntervalMinutes % 5 != 0) return false;
        if (_cloudSaveIntervalHours == 0 && _cloudSaveIntervalMinutes == 0) return false;

        if (_lastCloudSaveUtc == DateTime.MinValue)
            _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);

        if (_lastCloudSaveUtc == DateTime.MinValue)
            return true;

        return (DateTime.UtcNow - _lastCloudSaveUtc) >= CloudSaveInterval();
    }

    private void TrySaveCloudBackup(string justSavedBackupPath)
    {
        if (!ShouldSaveCloudBackup()) return;
        if (!File.Exists(justSavedBackupPath)) return;

        try
        {
            Directory.CreateDirectory(_cloudBackupFolder);
            var targetPath = Path.Combine(_cloudBackupFolder, Path.GetFileName(justSavedBackupPath));
            File.Copy(justSavedBackupPath, targetPath, overwrite: true);
            _lastCloudSaveUtc = DateTime.UtcNow;
            _lastSaveIncludedCloudCopy = true;
            SaveWindowSettings();
        }
        catch
        {
            // Cloud copy is best-effort. The regular backup already succeeded.
        }
    }

    /// <summary>Deletes the oldest backups when more than MaxBackups files exist.</summary>
    private void PruneBackups()
    {
        if (!Directory.Exists(_backupFolder)) return;

        var files = Directory.GetFiles(_backupFolder, "noted_*.txt")
            .Where(f => NotedBackupFileNameRegex.IsMatch(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase) // yyyyMMdd_HHmmss sorts chronologically
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
        if (!Directory.Exists(_backupFolder)) return;

        var latest = GetLatestBackupFilePath(_backupFolder);
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

                FileMetadata metadata = new();
                if (TryReadMetadataLine(text, contentStart, out var parsedMetadata, out var contentAfterMetadata))
                {
                    metadata = parsedMetadata;
                    contentStart = contentAfterMetadata;
                }

                int contentEnd = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : text.Length;

                // Strip the trailing newline that SaveSession adds
                var content = text[contentStart..contentEnd];
                if (content.EndsWith("\r\n")) content = content[..^2];
                else if (content.EndsWith('\n')) content = content[..^1];

                // New format keeps content verbatim; no escaping/decoding

                var doc = CreateTab(name, content);
                var linesToRestore = metadata.HighlightLines != null && metadata.HighlightLines.Count > 0
                    ? metadata.HighlightLines
                    : (metadata.HighlightLine is int legacyLine ? [legacyLine] : []);
                SetHighlightedLines(doc, linesToRestore, markDirty: false);
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
    private void MenuExportToFile_Click(object sender, RoutedEventArgs e) => ExportCurrentTabToFile();

    private void MenuOpenLastBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_backupFolder)) return;
        var latest = GetLatestBackupFilePath(_backupFolder);
        if (latest != null)
        {
            try
            {
                var editor = TryFindVsCodeOrCursorExecutable();
                if (editor != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = editor,
                        Arguments = $"\"{latest}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    // PATH shim is often code.cmd → cmd.exe; hide console if it appears.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "code",
                        Arguments = $"\"{latest}\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open editor:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var opts = new JsonSerializerOptions { WriteIndented = true };
            Directory.CreateDirectory(_backupFolder);
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
                FontWeight = _fontWeight,
                ShortcutNewPrimary = _shortcutNewPrimary,
                ShortcutNewSecondary = _shortcutNewSecondary,
                ShortcutCloseTab = _shortcutCloseTab,
                ShortcutRenameTab = _shortcutRenameTab,
                ShortcutAddBlankLines = _shortcutAddBlankLines,
                ShortcutToggleHighlight = _shortcutToggleHighlight,
                SelectedLineColor = ColorToHex(_selectedLineColor),
                HighlightedLineColor = ColorToHex(_highlightedLineColor),
                SelectedHighlightedLineColor = ColorToHex(_selectedHighlightedLineColor),
                BackupFolder = _backupFolder,
                CloudBackupFolder = _cloudBackupFolder,
                CloudSaveHours = _cloudSaveIntervalHours,
                CloudSaveMinutes = _cloudSaveIntervalMinutes,
                LastCloudCopyUtc = _lastCloudSaveUtc == DateTime.MinValue ? null : _lastCloudSaveUtc
            };
            var primary = Path.Combine(_backupFolder, SettingsFileName);
            File.WriteAllText(primary, JsonSerializer.Serialize(state, opts));

            var def = DefaultBackupFolder();
            if (!string.Equals(Path.GetFullPath(_backupFolder), Path.GetFullPath(def), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(def);
                var bootstrap = new WindowSettings { BackupFolder = _backupFolder };
                File.WriteAllText(Path.Combine(def, SettingsFileName), JsonSerializer.Serialize(bootstrap, opts));
            }
        }
        catch { /* non-critical */ }
    }

    private void LoadWindowSettings()
    {
        try
        {
            _backupFolder = DefaultBackupFolder();
            _cloudBackupFolder = DefaultCloudBackupFolder();
            _selectedLineColor = DefaultSelectedLineColor;
            _highlightedLineColor = DefaultHighlightedLineColor;
            _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
            _shortcutNewPrimary = DefaultShortcutNewPrimary;
            _shortcutNewSecondary = DefaultShortcutNewSecondary;
            _shortcutCloseTab = DefaultShortcutCloseTab;
            _shortcutRenameTab = DefaultShortcutRenameTab;
            _shortcutAddBlankLines = DefaultShortcutAddBlankLines;
            _shortcutToggleHighlight = DefaultShortcutToggleHighlight;
            var defaultPath = Path.Combine(DefaultBackupFolder(), SettingsFileName);
            if (!File.Exists(defaultPath))
                return;

            var boot = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(defaultPath));
            if (boot == null) return;

            if (!string.IsNullOrWhiteSpace(boot.BackupFolder))
            {
                try
                {
                    _backupFolder = Path.GetFullPath(boot.BackupFolder.Trim());
                }
                catch
                {
                    _backupFolder = DefaultBackupFolder();
                }
            }

            if (!string.IsNullOrWhiteSpace(boot.CloudBackupFolder))
            {
                try
                {
                    _cloudBackupFolder = Path.GetFullPath(boot.CloudBackupFolder.Trim());
                }
                catch
                {
                    _cloudBackupFolder = DefaultCloudBackupFolder();
                }
            }

            if (boot.CloudSaveHours is >= 0 and <= 50)
                _cloudSaveIntervalHours = boot.CloudSaveHours.Value;
            if (boot.CloudSaveMinutes is >= 0 and <= 55 && boot.CloudSaveMinutes.Value % 5 == 0)
                _cloudSaveIntervalMinutes = boot.CloudSaveMinutes.Value;
            if (boot.LastCloudCopyUtc is DateTime bootCloudCopyUtc && bootCloudCopyUtc > DateTime.MinValue)
                _lastCloudSaveUtc = bootCloudCopyUtc.Kind == DateTimeKind.Utc ? bootCloudCopyUtc : bootCloudCopyUtc.ToUniversalTime();

            var canonicalPath = Path.Combine(_backupFolder, SettingsFileName);
            WindowSettings? state = boot;
            if (File.Exists(canonicalPath)
                && !string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(defaultPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                var full = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(canonicalPath));
                if (full != null) state = full;
            }

            if (state == null) return;

            Left = state.Left;
            Top = state.Top;
            Width = state.Width;
            Height = state.Height;
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
            if (TryParseKeyGesture(state.ShortcutNewPrimary, out _))
                _shortcutNewPrimary = state.ShortcutNewPrimary!.Trim();
            if (string.IsNullOrWhiteSpace(state.ShortcutNewSecondary))
                _shortcutNewSecondary = string.Empty;
            else if (TryParseKeyGesture(state.ShortcutNewSecondary, out _))
                _shortcutNewSecondary = state.ShortcutNewSecondary.Trim();
            if (TryParseKeyGesture(state.ShortcutCloseTab, out _))
                _shortcutCloseTab = state.ShortcutCloseTab!.Trim();
            if (TryParseKeyGesture(state.ShortcutRenameTab, out _))
                _shortcutRenameTab = state.ShortcutRenameTab!.Trim();
            if (TryParseKeyGesture(state.ShortcutAddBlankLines, out _))
                _shortcutAddBlankLines = state.ShortcutAddBlankLines!.Trim();
            if (TryParseKeyGesture(state.ShortcutToggleHighlight, out _))
                _shortcutToggleHighlight = state.ShortcutToggleHighlight!.Trim();
            if (!string.IsNullOrWhiteSpace(state.BackupFolder))
            {
                try
                {
                    _backupFolder = Path.GetFullPath(state.BackupFolder.Trim());
                }
                catch
                {
                    /* keep prior */
                }
            }
            if (!string.IsNullOrWhiteSpace(state.CloudBackupFolder))
            {
                try
                {
                    _cloudBackupFolder = Path.GetFullPath(state.CloudBackupFolder.Trim());
                }
                catch
                {
                    /* keep prior */
                }
            }
            if (state.CloudSaveHours is >= 0 and <= 50)
                _cloudSaveIntervalHours = state.CloudSaveHours.Value;
            if (state.CloudSaveMinutes is >= 0 and <= 55 && state.CloudSaveMinutes.Value % 5 == 0)
                _cloudSaveIntervalMinutes = state.CloudSaveMinutes.Value;
            if (state.LastCloudCopyUtc is DateTime cloudCopyUtc && cloudCopyUtc > DateTime.MinValue)
                _lastCloudSaveUtc = cloudCopyUtc.Kind == DateTimeKind.Utc ? cloudCopyUtc : cloudCopyUtc.ToUniversalTime();
            if (TryParseColor(state.SelectedLineColor, out var selectedLineColor))
                _selectedLineColor = selectedLineColor;
            if (TryParseColor(state.HighlightedLineColor, out var highlightedLineColor))
                _highlightedLineColor = highlightedLineColor;
            if (TryParseColor(state.SelectedHighlightedLineColor, out var selectedHighlightedLineColor))
                _selectedHighlightedLineColor = selectedHighlightedLineColor;

            _startMaximized = state.Maximized;
            if (_lastCloudSaveUtc == DateTime.MinValue)
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
            ApplyColorThemeToOpenEditors();
        }
        catch { /* ignore corrupt settings */ }
    }

    /// <summary>Creates <see cref="SettingsFileName"/> under the current backup folder when missing.</summary>
    private void EnsureSettingsFileExists()
    {
        try
        {
            if (File.Exists(Path.Combine(_backupFolder, SettingsFileName)))
                return;
            SaveWindowSettings();
        }
        catch
        {
            /* non-critical */
        }
    }

    private static void CopySettingsFileToBackupFolder(string fromFolder, string toFolder)
    {
        var src = Path.Combine(fromFolder, SettingsFileName);
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(toFolder);
        var dst = Path.Combine(toFolder, SettingsFileName);
        File.Copy(src, dst, overwrite: true);
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
        public string ShortcutNewPrimary { get; set; } = DefaultShortcutNewPrimary;
        public string ShortcutNewSecondary { get; set; } = DefaultShortcutNewSecondary;
        public string ShortcutCloseTab { get; set; } = DefaultShortcutCloseTab;
        public string ShortcutRenameTab { get; set; } = DefaultShortcutRenameTab;
        public string ShortcutAddBlankLines { get; set; } = DefaultShortcutAddBlankLines;
        public string ShortcutToggleHighlight { get; set; } = DefaultShortcutToggleHighlight;
        public string? SelectedLineColor { get; set; }
        public string? HighlightedLineColor { get; set; }
        public string? SelectedHighlightedLineColor { get; set; }
        public string? BackupFolder { get; set; }
        public string? CloudBackupFolder { get; set; }
        public int? CloudSaveHours { get; set; }
        public int? CloudSaveMinutes { get; set; }
        public DateTime? LastCloudCopyUtc { get; set; }
    }

    // --- Settings dialog ----------------------------------------------------

    private void ShowSettingsDialog()
    {
        var dlg = new Window
        {
            Title = "Settings",
            Width = 840,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var tabControl = new TabControl { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(tabControl, Dock.Top);

        var backupPanel = new StackPanel { Margin = new Thickness(12) };
        backupPanel.Children.Add(new TextBlock { Text = "Backup folder:" });
        var backupRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        backupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        backupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtBackup = new TextBox { Text = _backupFolder, Margin = new Thickness(0, 0, 8, 0) };
        var btnBrowseBackup = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2) };
        Grid.SetColumn(txtBackup, 0);
        Grid.SetColumn(btnBrowseBackup, 1);
        backupRow.Children.Add(txtBackup);
        backupRow.Children.Add(btnBrowseBackup);
        backupPanel.Children.Add(backupRow);

        backupPanel.Children.Add(new TextBlock { Text = "Cloud storage folder:" });
        var cloudRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        cloudRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cloudRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtCloudBackup = new TextBox { Text = _cloudBackupFolder, Margin = new Thickness(0, 0, 8, 0) };
        var btnBrowseCloudBackup = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2) };
        Grid.SetColumn(txtCloudBackup, 0);
        Grid.SetColumn(btnBrowseCloudBackup, 1);
        cloudRow.Children.Add(txtCloudBackup);
        cloudRow.Children.Add(btnBrowseCloudBackup);
        backupPanel.Children.Add(cloudRow);

        backupPanel.Children.Add(new TextBlock { Text = "Cloud save interval:" });
        var cloudIntervalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var cmbCloudHours = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        for (int h = 0; h <= 50; h++) cmbCloudHours.Items.Add(h);
        cmbCloudHours.SelectedItem = _cloudSaveIntervalHours;
        if (cmbCloudHours.SelectedItem == null) cmbCloudHours.SelectedItem = 0;
        var cmbCloudMinutes = new ComboBox { Width = 80, Margin = new Thickness(8, 0, 8, 0) };
        foreach (var m in CloudMinuteOptions) cmbCloudMinutes.Items.Add(m);
        cmbCloudMinutes.SelectedItem = _cloudSaveIntervalMinutes;
        if (cmbCloudMinutes.SelectedItem == null) cmbCloudMinutes.SelectedItem = 0;
        cloudIntervalRow.Children.Add(cmbCloudHours);
        cloudIntervalRow.Children.Add(new TextBlock { Text = "hours", VerticalAlignment = VerticalAlignment.Center });
        cloudIntervalRow.Children.Add(cmbCloudMinutes);
        cloudIntervalRow.Children.Add(new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center });
        backupPanel.Children.Add(cloudIntervalRow);
        backupPanel.Children.Add(new TextBlock
        {
            Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        });

        backupPanel.Children.Add(new TextBlock { Text = "Auto-save interval (seconds):" });
        var txtAutoSave = new TextBox { Text = ((int)_autoSaveTimer.Interval.TotalSeconds).ToString(), Margin = new Thickness(0, 4, 0, 10) };
        backupPanel.Children.Add(txtAutoSave);
        backupPanel.Children.Add(new TextBlock { Text = "Initial lines per new tab:" });
        var txtLines = new TextBox { Text = _initialLines.ToString(), Margin = new Thickness(0, 4, 0, 0) };
        backupPanel.Children.Add(txtLines);
        tabControl.Items.Add(new TabItem { Header = "Backup", Content = new ScrollViewer { Content = backupPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        var fontPanel = new StackPanel { Margin = new Thickness(12) };
        fontPanel.Children.Add(new TextBlock { Text = "Font family:" });
        var cmbFont = new ComboBox { IsEditable = true, Margin = new Thickness(0, 4, 0, 10) };
        string[] popularFonts =
        {
            "Consolas", "Courier New", "Source Code Pro", "Cascadia Code", "Cascadia Mono", "Fira Code",
            "JetBrains Mono", "Lucida Console", "Menlo", "Monaco", "Roboto Mono", "Ubuntu Mono"
        };
        foreach (var f in popularFonts)
            cmbFont.Items.Add(f);
        cmbFont.Text = _fontFamily;
        fontPanel.Children.Add(cmbFont);

        fontPanel.Children.Add(new TextBlock { Text = "Font size:" });
        var txtFontSize = new TextBox { Text = _fontSize.ToString(), Margin = new Thickness(0, 4, 0, 10) };
        fontPanel.Children.Add(txtFontSize);

        fontPanel.Children.Add(new TextBlock { Text = "Font weight:" });
        var cmbFontWeight = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
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
        int selectedIdx = 3;
        for (int i = 0; i < weights.Length; i++)
        {
            cmbFontWeight.Items.Add(weights[i].Name);
            if (weights[i].Value == _fontWeight) selectedIdx = i;
        }
        cmbFontWeight.SelectedIndex = selectedIdx;
        fontPanel.Children.Add(cmbFontWeight);
        tabControl.Items.Add(new TabItem { Header = "Fonts", Content = fontPanel });

        var colorsPanel = new Grid { Margin = new Thickness(12) };
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] colorOptions =
        [
            "Orange",
            "Goldenrod",
            "Tomato",
            "SkyBlue",
            "LightSkyBlue",
            "Khaki",
            "LightGreen",
            "PaleVioletRed",
            "#FFE1F0FF",
            "#FFFFC480",
            "#FFFFA060"
        ];

        (ComboBox combo, Border preview) CreateColorPicker(Color initial)
        {
            var combo = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 8) };
            foreach (var option in colorOptions)
                combo.Items.Add(option);
            combo.Text = ColorToHex(initial);

            var preview = new Border
            {
                Width = 54,
                Height = 22,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };

            void RefreshPreview()
            {
                if (TryParseColor(combo.Text, out var color))
                    preview.Background = new SolidColorBrush(color);
            }

            combo.LostFocus += (_, _) => RefreshPreview();
            combo.SelectionChanged += (_, _) => RefreshPreview();
            RefreshPreview();
            return (combo, preview);
        }

        var (cmbSelectedLineColor, selectedLinePreview) = CreateColorPicker(_selectedLineColor);
        var (cmbHighlightedLineColor, highlightedLinePreview) = CreateColorPicker(_highlightedLineColor);
        var (cmbSelectedHighlightedLineColor, selectedHighlightedLinePreview) = CreateColorPicker(_selectedHighlightedLineColor);

        var lblSelectedLineColor = new TextBlock { Text = "Selected line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblSelectedLineColor, 0);
        colorsPanel.Children.Add(lblSelectedLineColor);
        Grid.SetRow(cmbSelectedLineColor, 0);
        Grid.SetColumn(cmbSelectedLineColor, 1);
        colorsPanel.Children.Add(cmbSelectedLineColor);
        Grid.SetRow(selectedLinePreview, 0);
        Grid.SetColumn(selectedLinePreview, 2);
        colorsPanel.Children.Add(selectedLinePreview);

        var lblHighlightedLineColor = new TextBlock { Text = "Highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblHighlightedLineColor, 1);
        colorsPanel.Children.Add(lblHighlightedLineColor);
        Grid.SetRow(cmbHighlightedLineColor, 1);
        Grid.SetColumn(cmbHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbHighlightedLineColor);
        Grid.SetRow(highlightedLinePreview, 1);
        Grid.SetColumn(highlightedLinePreview, 2);
        colorsPanel.Children.Add(highlightedLinePreview);

        var lblSelectedHighlightedLineColor = new TextBlock { Text = "Selected highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblSelectedHighlightedLineColor, 2);
        colorsPanel.Children.Add(lblSelectedHighlightedLineColor);
        Grid.SetRow(cmbSelectedHighlightedLineColor, 2);
        Grid.SetColumn(cmbSelectedHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbSelectedHighlightedLineColor);
        Grid.SetRow(selectedHighlightedLinePreview, 2);
        Grid.SetColumn(selectedHighlightedLinePreview, 2);
        colorsPanel.Children.Add(selectedHighlightedLinePreview);

        tabControl.Items.Add(new TabItem { Header = "Colors", Content = colorsPanel });

        var shortkeysPanel = new StackPanel { Margin = new Thickness(12) };
        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Set shortcuts (format examples: Ctrl+N, Ctrl+Shift+N, F2):",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        shortkeysPanel.Children.Add(new TextBlock { Text = "New tab (primary):" });
        var txtShortcutNewPrimary = new TextBox { Text = _shortcutNewPrimary, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutNewPrimary);

        shortkeysPanel.Children.Add(new TextBlock { Text = "New tab (secondary, optional):" });
        var txtShortcutNewSecondary = new TextBox { Text = _shortcutNewSecondary, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutNewSecondary);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Close current tab:" });
        var txtShortcutClose = new TextBox { Text = _shortcutCloseTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutClose);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Rename current tab:" });
        var txtShortcutRename = new TextBox { Text = _shortcutRenameTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutRename);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Add 10 blank lines at end:" });
        var txtShortcutAddBlankLines = new TextBox { Text = _shortcutAddBlankLines, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutAddBlankLines);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Toggle highlight on current/selected lines:" });
        var txtShortcutToggleHighlight = new TextBox { Text = _shortcutToggleHighlight, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutToggleHighlight);

        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+MouseWheel changes font size and is not configurable.",
            Foreground = Brushes.DimGray
        });
        tabControl.Items.Add(new TabItem { Header = "Shortkeys", Content = shortkeysPanel });

        var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonPanel.Children.Add(btnOk);
        buttonPanel.Children.Add(btnCancel);
        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        root.Children.Add(buttonPanel);
        root.Children.Add(tabControl);
        dlg.Content = root;

        btnBrowseBackup.Click += (_, _) =>
        {
            var fbd = new VistaFolderBrowserDialog
            {
                Description = "Choose folder for session backups",
                UseDescriptionForTitle = true
            };
            var cur = txtBackup.Text.Trim();
            try
            {
                if (!string.IsNullOrEmpty(cur))
                {
                    var full = Path.GetFullPath(cur);
                    if (Directory.Exists(full))
                        fbd.SelectedPath = full;
                    else
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                            fbd.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (fbd.ShowDialog(dlg) == true)
                txtBackup.Text = fbd.SelectedPath;
        };

        btnBrowseCloudBackup.Click += (_, _) =>
        {
            var fbd = new VistaFolderBrowserDialog
            {
                Description = "Choose folder for cloud backup copies",
                UseDescriptionForTitle = true
            };
            var cur = txtCloudBackup.Text.Trim();
            try
            {
                if (!string.IsNullOrEmpty(cur))
                {
                    var full = Path.GetFullPath(cur);
                    if (Directory.Exists(full))
                        fbd.SelectedPath = full;
                    else
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                            fbd.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (fbd.ShowDialog(dlg) == true)
                txtCloudBackup.Text = fbd.SelectedPath;
        };

        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtBackup.Text))
            {
                MessageBox.Show("Backup folder cannot be empty.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtCloudBackup.Text))
            {
                MessageBox.Show("Cloud storage folder cannot be empty.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string backupPath;
            try
            {
                backupPath = Path.GetFullPath(txtBackup.Text.Trim());
            }
            catch
            {
                MessageBox.Show("Backup folder path is not valid.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string cloudBackupPath;
            try
            {
                cloudBackupPath = Path.GetFullPath(txtCloudBackup.Text.Trim());
            }
            catch
            {
                MessageBox.Show("Cloud storage folder path is not valid.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var shortcutNewPrimary = txtShortcutNewPrimary.Text.Trim();
            var shortcutNewSecondary = txtShortcutNewSecondary.Text.Trim();
            var shortcutClose = txtShortcutClose.Text.Trim();
            var shortcutRename = txtShortcutRename.Text.Trim();
            var shortcutAddBlankLines = txtShortcutAddBlankLines.Text.Trim();
            var shortcutToggleHighlight = txtShortcutToggleHighlight.Text.Trim();

            if (string.IsNullOrWhiteSpace(shortcutNewPrimary)
                || string.IsNullOrWhiteSpace(shortcutClose)
                || string.IsNullOrWhiteSpace(shortcutRename)
                || string.IsNullOrWhiteSpace(shortcutAddBlankLines)
                || string.IsNullOrWhiteSpace(shortcutToggleHighlight))
            {
                MessageBox.Show("Shortcut fields cannot be empty (except secondary New tab shortcut).",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseKeyGesture(shortcutNewPrimary, out _)
                || (!string.IsNullOrWhiteSpace(shortcutNewSecondary) && !TryParseKeyGesture(shortcutNewSecondary, out _))
                || !TryParseKeyGesture(shortcutClose, out _)
                || !TryParseKeyGesture(shortcutRename, out _)
                || !TryParseKeyGesture(shortcutAddBlankLines, out _)
                || !TryParseKeyGesture(shortcutToggleHighlight, out _))
            {
                MessageBox.Show("One or more shortcuts have an invalid format.\nUse values like Ctrl+N, Ctrl+Shift+N, F2.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var shortcutList = new List<string>
            {
                shortcutNewPrimary,
                shortcutClose,
                shortcutRename,
                shortcutAddBlankLines,
                shortcutToggleHighlight
            };
            if (!string.IsNullOrWhiteSpace(shortcutNewSecondary))
                shortcutList.Add(shortcutNewSecondary);
            if (shortcutList.Count != shortcutList.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                MessageBox.Show("Shortcut keys must be unique across actions.", "Invalid settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (int.TryParse(txtAutoSave.Text, out int secs) && secs >= 5
                && int.TryParse(txtLines.Text, out int lines) && lines >= 1
                && double.TryParse(txtFontSize.Text, out double fsize) && fsize >= 6
                && !string.IsNullOrWhiteSpace(cmbFont.Text)
                && cmbCloudHours.SelectedItem is int cloudHours && cloudHours >= 0 && cloudHours <= 50
                && cmbCloudMinutes.SelectedItem is int cloudMinutes && cloudMinutes >= 0
                && cloudMinutes <= 55 && cloudMinutes % 5 == 0
                && (cloudHours > 0 || cloudMinutes > 0)
                && TryParseColor(cmbSelectedLineColor.Text, out var selectedLineColor)
                && TryParseColor(cmbHighlightedLineColor.Text, out var highlightedLineColor)
                && TryParseColor(cmbSelectedHighlightedLineColor.Text, out var selectedHighlightedLineColor))
            {
                var previousBackupFolder = _backupFolder;
                if (!string.Equals(Path.GetFullPath(previousBackupFolder), Path.GetFullPath(backupPath),
                        StringComparison.OrdinalIgnoreCase))
                    CopySettingsFileToBackupFolder(previousBackupFolder, backupPath);

                _backupFolder = backupPath;
                _cloudBackupFolder = cloudBackupPath;
                _cloudSaveIntervalHours = cloudHours;
                _cloudSaveIntervalMinutes = cloudMinutes;
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(secs);
                _initialLines = lines;
                _fontFamily = cmbFont.Text.Trim();
                _fontSize = fsize;
                _fontWeight = weights[cmbFontWeight.SelectedIndex].Value;
                _shortcutNewPrimary = shortcutNewPrimary;
                _shortcutNewSecondary = shortcutNewSecondary;
                _shortcutCloseTab = shortcutClose;
                _shortcutRenameTab = shortcutRename;
                _shortcutAddBlankLines = shortcutAddBlankLines;
                _shortcutToggleHighlight = shortcutToggleHighlight;
                _selectedLineColor = selectedLineColor;
                _highlightedLineColor = highlightedLineColor;
                _selectedHighlightedLineColor = selectedHighlightedLineColor;

                // Apply font to all open editors
                var family = new FontFamily(_fontFamily);
                var weight = FontWeight.FromOpenTypeWeight(_fontWeight);
                foreach (var doc in _docs.Values)
                {
                    doc.Editor.FontFamily = family;
                    doc.Editor.FontSize = _fontSize;
                    doc.Editor.FontWeight = weight;
                }
                ApplyShortcutBindings();
                ApplyColorThemeToOpenEditors();

                SaveWindowSettings();
                dlg.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Auto-save must be >= 5 seconds.\nInitial lines must be >= 1.\nFont size must be >= 6.\nCloud interval must be 0-50 hours and minutes in 5-minute steps (not 0h 0m).\nColor values must be valid WPF colors (name or #AARRGGBB).\nShortcuts must be valid key gestures.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        dlg.ShowDialog();
    }
}
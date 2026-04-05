using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
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
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
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
    private int _activeTabIndex = 0;
    private readonly List<ClosedTabEntry> _closedTabHistory = [];
    private List<UserProfile> _users = [];

    private const string SettingsFileName = "settings.json";
    private const string ClosedTabsFileName = "closed-tabs.json";
    private const int MaxClosedTabs = 10;

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
    private const string DefaultShortcutReopenClosedTab = "Ctrl+Shift+T";
    private const string DefaultShortcutRenameTab = "F2";
    private const string DefaultShortcutAddBlankLines = "Ctrl+Space";
    private const string DefaultShortcutToggleHighlight = "Ctrl+J";
    private const string DefaultShortcutGoToLine = "Ctrl+G";
    private static readonly string[] FridayBackgroundImageUris =
    [
        "pack://application:,,,/Noted;component/logo/friday.png",
        "pack://application:,,,/logo/friday.png",
        "logo/friday.png"
    ];
    private static readonly Color DefaultSelectedLineColor = Color.FromRgb(225, 240, 255);
    private static readonly Color DefaultHighlightedLineColor = Color.FromRgb(255, 244, 179);
    private static readonly Color DefaultSelectedHighlightedLineColor = Color.FromRgb(255, 234, 128);
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
    private string _shortcutGoToLine = DefaultShortcutGoToLine;
    private Color _selectedLineColor = DefaultSelectedLineColor;
    private Color _highlightedLineColor = DefaultHighlightedLineColor;
    private Color _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
    private Brush _selectedLineBrush = CreateFrozenBrush(DefaultSelectedLineColor);
    private Brush _highlightedLineBrush = CreateFrozenBrush(DefaultHighlightedLineColor);
    private Brush _selectedHighlightedLineBrush = CreateFrozenBrush(DefaultSelectedHighlightedLineColor);
    private bool _isFridayFeelingEnabled = true;
    private bool _isFredagspartySessionEnabled = false;
    private ImageBrush? _fridayBackgroundBrush;
    private readonly List<KeyBinding> _shortcutBindings = [];

    private static readonly RoutedUICommand RenameTabCommand = new("Rename Tab", nameof(RenameTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ReopenClosedTabCommand = new("Reopen Closed Tab", nameof(ReopenClosedTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand AddBlankLinesCommand = new("Add Blank Lines", nameof(AddBlankLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleHighlightCommand = new("Toggle Highlight", nameof(ToggleHighlightCommand), typeof(MainWindow));
    private static readonly RoutedUICommand GoToLineCommand = new("Go To Line", nameof(GoToLineCommand), typeof(MainWindow));
    private static readonly Lazy<IHighlightingDefinition> JsonSyntaxHighlighting = new(CreateJsonSyntaxHighlighting);

    private static IHighlightingDefinition CreateJsonSyntaxHighlighting()
    {
        const string jsonXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""JSON"" extensions="".json"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""PropertyName"" foreground=""#0451A5"" />
  <Color name=""String"" foreground=""#A31515"" />
  <Color name=""Number"" foreground=""#098658"" />
  <Color name=""Keyword"" foreground=""#0000FF"" fontWeight=""bold"" />
  <Color name=""Punctuation"" foreground=""#7A7A7A"" />
  <RuleSet ignoreCase=""false"">
    <Rule color=""PropertyName"">""(?:\\.|[^""\\])*""(?=\s*:)</Rule>
    <Rule color=""String"">""(?:\\.|[^""\\])*""</Rule>
    <Rule color=""Number"">-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?</Rule>
    <Rule color=""Keyword"">\b(?:true|false|null)\b</Rule>
    <Rule color=""Punctuation"">[\{\}\[\],:]</Rule>
  </RuleSet>
</SyntaxDefinition>";

        using var stringReader = new StringReader(jsonXshd);
        using var xmlReader = XmlReader.Create(stringReader);
        return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
    }

    private static void EnableJsonSyntaxHighlighting(TextEditor editor)
        => editor.SyntaxHighlighting = JsonSyntaxHighlighting.Value;

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

        // Optional line ownership metadata.
        public List<FileLineAssignee>? Assignees { get; set; }
    }

    private sealed class FileLineAssignee
    {
        public int Line { get; set; }
        public string Person { get; set; } = string.Empty;
    }

    private sealed class UserProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    private sealed class ClosedTabEntry
    {
        public string Header { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public FileMetadata? Metadata { get; set; }
    }

    private sealed class HighlightLineRenderer : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyCollection<int>> _lineProvider;
        private readonly Func<IReadOnlyDictionary<int, string>> _assigneeProvider;
        private readonly Func<string, Color> _assigneeColorProvider;
        private readonly Func<int, bool> _lineSelectionProvider;
        private readonly Func<Brush> _normalBrushProvider;
        private readonly Func<Brush> _selectedBrushProvider;

        public HighlightLineRenderer(
            Func<IReadOnlyCollection<int>> lineProvider,
            Func<IReadOnlyDictionary<int, string>> assigneeProvider,
            Func<string, Color> assigneeColorProvider,
            Func<int, bool> lineSelectionProvider,
            Func<Brush> normalBrushProvider,
            Func<Brush> selectedBrushProvider)
        {
            _lineProvider = lineProvider;
            _assigneeProvider = assigneeProvider;
            _assigneeColorProvider = assigneeColorProvider;
            _lineSelectionProvider = lineSelectionProvider;
            _normalBrushProvider = normalBrushProvider;
            _selectedBrushProvider = selectedBrushProvider;
        }

        private static Color ContrastTextColor(Color background)
        {
            // W3C-ish luminance threshold.
            var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
            return luminance >= 150 ? Color.FromRgb(25, 35, 30) : Colors.White;
        }

        private static Color Darken(Color color, double factor)
        {
            var f = Math.Max(0, Math.Min(1, factor));
            return Color.FromRgb(
                (byte)Math.Round(color.R * f),
                (byte)Math.Round(color.G * f),
                (byte)Math.Round(color.B * f));
        }

        // Draw on selection layer so highlight remains visible even when selected.
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            var highlightedLines = _lineProvider();
            if (highlightedLines.Count > 0)
            {
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

            var assignments = _assigneeProvider();
            if (assignments.Count == 0)
                return;

            double pixelsPerDip = VisualTreeHelper.GetDpi(textView).PixelsPerDip;
            var typeface = new Typeface("Segoe UI");
            var styleCache = new Dictionary<string, (Brush Background, Brush Foreground, Pen Border)>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in assignments)
            {
                int lineNumber = pair.Key;
                if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                    continue;

                var person = pair.Value?.Trim();
                if (string.IsNullOrWhiteSpace(person))
                    continue;

                if (!styleCache.TryGetValue(person, out var style))
                {
                    var userColor = _assigneeColorProvider(person);
                    var bgBrush = new SolidColorBrush(userColor);
                    var fgBrush = new SolidColorBrush(ContrastTextColor(userColor));
                    var borderPen = new Pen(new SolidColorBrush(Darken(userColor, 0.7)), 0.9);
                    bgBrush.Freeze();
                    fgBrush.Freeze();
                    borderPen.Freeze();
                    style = (bgBrush, fgBrush, borderPen);
                    styleCache[person] = style;
                }

                var line = textView.Document.GetLineByNumber(lineNumber);
                var segment = new TextSegment
                {
                    StartOffset = line.Offset,
                    EndOffset = line.Offset + line.Length
                };

                var lineRects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();
                if (lineRects.Count == 0)
                    continue;

                var label = person;
                var formattedText = new FormattedText(
                    label,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    style.Foreground,
                    pixelsPerDip);

                double paddingX = 6;
                double badgeHeight = Math.Max(16, lineRects[0].Height - 2);
                double badgeWidth = formattedText.WidthIncludingTrailingWhitespace + (paddingX * 2);
                double lineEndX = lineRects.Max(rect => rect.Right);
                double x = Math.Max(0, Math.Min(textView.ActualWidth - badgeWidth - 4, lineEndX + 14));
                double y = lineRects[0].Top + Math.Max(0, (lineRects[0].Height - badgeHeight) / 2);

                var badgeRect = new Rect(x, y, badgeWidth, badgeHeight);
                drawingContext.DrawRoundedRectangle(style.Background, style.Border, badgeRect, 4, 4);
                drawingContext.DrawText(formattedText, new Point(x + paddingX, y + Math.Max(0, (badgeHeight - formattedText.Height) / 2)));
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
        CommandBindings.Add(new CommandBinding(ReopenClosedTabCommand, (_, _) => ReopenLastClosedTab()));
        CommandBindings.Add(new CommandBinding(RenameTabCommand, (_, _) => ExecuteRenameCurrentTab()));
        CommandBindings.Add(new CommandBinding(AddBlankLinesCommand, (_, _) => ExecuteAddBlankLines()));
        CommandBindings.Add(new CommandBinding(ToggleHighlightCommand, (_, _) => ExecuteToggleHighlight()));
        CommandBindings.Add(new CommandBinding(GoToLineCommand, (_, _) => ExecuteGoToLine()));

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
        LoadClosedTabHistory();
        Loaded += (_, _) => { if (_startMaximized) WindowState = WindowState.Maximized; };

        // Restore previous session; if nothing to restore, open a blank tab
        LoadSession();
        RestoreActiveTabSelection();
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
            () => GetLineAssignments(doc),
            person => GetUserColor(person),
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
        editor.PreviewKeyDown += (_, e) => HandleEditorPreviewKeyDown(doc, e);

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
        EnableJsonSyntaxHighlighting(editor);
        editor.ContextMenu = BuildEditorContextMenu(editor);
        ApplyFridayBackgroundToEditor(editor);
        return editor;
    }

    private ContextMenu BuildEditorContextMenu(TextEditor editor)
    {
        var menu = new ContextMenu();

        var formatJsonItem = new MenuItem { Header = "Format selection as pretty JSON" };
        formatJsonItem.Click += (_, _) => FormatSelectedJson(editor);
        var copySelectionItem = new MenuItem { Header = "Copy Selection To" };
        copySelectionItem.Items.Add(new MenuItem { Header = "(Loading...)", IsEnabled = false });
        copySelectionItem.SubmenuOpened += (_, _) => PopulateTransferMenu(copySelectionItem, editor, moveSelection: false);

        var moveSelectionItem = new MenuItem { Header = "Move Selection To" };
        moveSelectionItem.Items.Add(new MenuItem { Header = "(Loading...)", IsEnabled = false });
        moveSelectionItem.SubmenuOpened += (_, _) => PopulateTransferMenu(moveSelectionItem, editor, moveSelection: true);
        var assignLineOwnerItem = new MenuItem { Header = "Assign Selected Line(s)..." };
        assignLineOwnerItem.Click += (_, _) => AssignSelectedLines(editor);
        var clearLineOwnerItem = new MenuItem { Header = "Clear Selected Line Assignment(s)" };
        clearLineOwnerItem.Click += (_, _) => ClearSelectedLineAssignments(editor);

        menu.Items.Add(formatJsonItem);
        menu.Items.Add(copySelectionItem);
        menu.Items.Add(moveSelectionItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(assignLineOwnerItem);
        menu.Items.Add(clearLineOwnerItem);

        menu.Opened += (_, _) =>
        {
            bool hasSelection = !string.IsNullOrEmpty(editor.SelectedText);
            formatJsonItem.IsEnabled = hasSelection;
            copySelectionItem.IsEnabled = hasSelection;
            moveSelectionItem.IsEnabled = hasSelection;

            var doc = FindDocByEditor(editor);
            bool canAssign = doc != null && _users.Count > 0;
            assignLineOwnerItem.IsEnabled = canAssign;
            clearLineOwnerItem.IsEnabled = doc != null;
        };

        return menu;
    }

    private void FormatSelectedJson(TextEditor editor)
    {
        var selectedText = editor.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText))
            return;

        try
        {
            using var jsonDocument = JsonDocument.Parse(selectedText, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var formatted = JsonSerializer.Serialize(jsonDocument.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var selectionStart = editor.SelectionStart;
            var selectionLength = editor.SelectionLength;
            editor.Document.Replace(selectionStart, selectionLength, formatted);
            editor.Select(selectionStart, formatted.Length);
            EnableJsonSyntaxHighlighting(editor);
        }
        catch (JsonException)
        {
            MessageBox.Show(
                "Selected text is not valid JSON.",
                "Pretty JSON",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private TabDocument? FindDocByEditor(TextEditor editor)
        => _docs.Values.FirstOrDefault(doc => ReferenceEquals(doc.Editor, editor));

    private void PopulateTransferMenu(MenuItem menu, TextEditor? sourceEditor, bool moveSelection)
    {
        menu.Items.Clear();

        var sourceDoc = sourceEditor == null ? CurrentDoc() : FindDocByEditor(sourceEditor);
        sourceEditor ??= sourceDoc?.Editor;
        bool hasSelection = sourceEditor != null && !string.IsNullOrEmpty(sourceEditor.SelectedText);

        var sendToNewTabItem = new MenuItem
        {
            Header = "New Tab",
            IsEnabled = hasSelection
        };
        sendToNewTabItem.Click += (_, _) =>
        {
            if (sourceEditor != null)
                TransferSelectionToNewTab(sourceEditor, moveSelection);
        };
        menu.Items.Add(sendToNewTabItem);
        menu.Items.Add(new Separator());

        var destinationDocs = _docs.Values
            .Where(doc => doc != sourceDoc)
            .OrderBy(doc => doc.Header, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (destinationDocs.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "(No other tabs)",
                IsEnabled = false
            });
            return;
        }

        foreach (var destinationDoc in destinationDocs)
        {
            var target = destinationDoc;
            var tabItem = new MenuItem
            {
                Header = target.Header,
                IsEnabled = hasSelection
            };
            tabItem.Click += (_, _) =>
            {
                if (sourceEditor != null)
                    TransferSelectionToTab(sourceEditor, target, moveSelection);
            };
            menu.Items.Add(tabItem);
        }
    }

    private bool TryGetSelectionToSend(TextEditor sourceEditor, out int selectionStart, out int selectionLength, out string selectedText)
    {
        selectionStart = sourceEditor.SelectionStart;
        selectionLength = sourceEditor.SelectionLength;
        selectedText = sourceEditor.SelectedText;
        return selectionLength > 0 && !string.IsNullOrEmpty(selectedText);
    }

    private void TransferSelectionToNewTab(TextEditor sourceEditor, bool moveSelection)
    {
        if (!TryGetSelectionToSend(sourceEditor, out int sourceSelectionStart, out int sourceSelectionLength, out var selectedText))
            return;

        string trailingBlankLines = new string('\n', Math.Max(0, _initialLines - 1));
        string contentWithPadding = selectedText + trailingBlankLines;

        var newDoc = CreateTab(content: contentWithPadding);
        newDoc.Editor.Select(0, selectedText.Length);
        newDoc.Editor.Focus();

        if (moveSelection)
        {
            sourceEditor.Document.Replace(sourceSelectionStart, sourceSelectionLength, string.Empty);
            sourceEditor.Select(sourceSelectionStart, 0);
        }
    }

    private void TransferSelectionToTab(TextEditor sourceEditor, TabDocument destinationDoc, bool moveSelection)
    {
        if (!TryGetSelectionToSend(sourceEditor, out int sourceSelectionStart, out int sourceSelectionLength, out var selectedText))
            return;

        if (ReferenceEquals(sourceEditor, destinationDoc.Editor))
            return;

        var destinationEditor = destinationDoc.Editor;
        int insertOffset = destinationEditor.Document.TextLength;
        string separator = string.Empty;
        if (insertOffset > 0)
        {
            // Always add one visible blank line before incoming selection.
            string currentText = destinationEditor.Text;
            bool endsWithNewLine = currentText.EndsWith("\n", StringComparison.Ordinal) ||
                                   currentText.EndsWith("\r", StringComparison.Ordinal);
            separator = endsWithNewLine
                ? Environment.NewLine
                : Environment.NewLine + Environment.NewLine;
        }

        destinationEditor.Document.Insert(insertOffset, separator + selectedText);
        int destinationSelectionStart = insertOffset + separator.Length;
        destinationEditor.Select(destinationSelectionStart, selectedText.Length);

        if (moveSelection)
        {
            sourceEditor.Document.Replace(sourceSelectionStart, sourceSelectionLength, string.Empty);
            sourceEditor.Select(sourceSelectionStart, 0);
        }

        var destinationTab = GetTab(destinationDoc);
        if (destinationTab != null)
            MainTabControl.SelectedItem = destinationTab;
        destinationEditor.Focus();
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

    private bool ShouldUseFridayBackground()
        => _isFredagspartySessionEnabled || (_isFridayFeelingEnabled && DateTime.Now.DayOfWeek == DayOfWeek.Friday);

    private ImageBrush? GetFridayBackgroundBrush()
    {
        if (_fridayBackgroundBrush != null)
            return _fridayBackgroundBrush;

        foreach (var uriText in FridayBackgroundImageUris)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(uriText, uriText.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
                    ? UriKind.Absolute
                    : UriKind.Relative);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                var brush = new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Top,
                    Opacity = 0.32
                };
                brush.Freeze();
                _fridayBackgroundBrush = brush;
                return _fridayBackgroundBrush;
            }
            catch
            {
                // Try next lower-case friday.png URI candidate.
            }
        }

        _fridayBackgroundBrush = null;
        return null;
    }

    private void ApplyFridayBackgroundToEditor(TextEditor editor)
    {
        var background = ShouldUseFridayBackground() && GetFridayBackgroundBrush() is Brush fridayBrush
            ? fridayBrush
            : Brushes.White;

        editor.Background = background;
        editor.TextArea.Background = background;
        editor.TextArea.TextView.InvalidateVisual();
    }

    private void ApplyFridayFeelingToOpenEditors()
    {
        foreach (var doc in _docs.Values)
            ApplyFridayBackgroundToEditor(doc.Editor);
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

    private static List<int> GetSelectedOrCaretLineNumbers(TabDocument doc)
    {
        var selectedLines = GetSelectedLineNumbers(doc);
        if (selectedLines.Count > 0)
            return selectedLines.Distinct().ToList();

        return [Math.Max(1, doc.Editor.TextArea.Caret.Line)];
    }

    private void HandleEditorPreviewKeyDown(TabDocument doc, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (e.Key == Key.F || e.Key == Key.H))
        {
            ShowFindReplaceDialog();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete || (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        bool changed = false;
        foreach (var line in GetSelectedOrCaretLineNumbers(doc))
            changed |= RemoveLineAssignee(doc, line, markDirty: false, redraw: false);

        if (changed)
            RedrawHighlight(doc);
    }

    private IReadOnlyDictionary<int, string> GetLineAssignments(TabDocument doc)
    {
        if (doc.LineAssigneeAnchors.Count == 0)
            return new Dictionary<int, string>();

        var result = new Dictionary<int, string>();
        for (int i = doc.LineAssigneeAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineAssigneeAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                continue;
            }

            var person = entry.Person?.Trim();
            if (string.IsNullOrWhiteSpace(person))
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                continue;
            }

            result[anchor.Line] = person;
        }

        return result;
    }

    private bool TryGetLineAssignee(TabDocument doc, int lineNumber, out string person)
    {
        person = string.Empty;
        for (int i = doc.LineAssigneeAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineAssigneeAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line != lineNumber)
                continue;

            person = entry.Person?.Trim() ?? string.Empty;
            if (person.Length == 0)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                return false;
            }

            return true;
        }

        return false;
    }

    private bool SetLineAssignee(TabDocument doc, int lineNumber, string person, bool markDirty = true, bool redraw = true)
    {
        var cleaned = person.Trim();
        if (cleaned.Length == 0)
            return false;

        int lineCount = doc.Editor.Document.LineCount;
        if (lineCount <= 0)
            lineCount = 1;

        int line = Math.Max(1, Math.Min(lineNumber, lineCount));
        bool changed = false;
        bool foundExisting = false;

        for (int i = doc.LineAssigneeAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineAssigneeAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line != line)
                continue;

            if (!foundExisting)
            {
                foundExisting = true;
                if (!string.Equals(entry.Person, cleaned, StringComparison.Ordinal))
                {
                    entry.Person = cleaned;
                    changed = true;
                }
            }
            else
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                changed = true;
            }
        }

        if (!foundExisting)
        {
            var docLine = doc.Editor.Document.GetLineByNumber(line);
            var anchor = doc.Editor.Document.CreateAnchor(docLine.Offset);
            anchor.MovementType = AnchorMovementType.BeforeInsertion;
            anchor.SurviveDeletion = false;
            doc.LineAssigneeAnchors.Add(new TabDocument.LineAssigneeAnchor
            {
                Anchor = anchor,
                Person = cleaned
            });
            changed = true;
        }

        if (changed && markDirty)
            MarkDirty(doc);
        if (changed && redraw)
            RedrawHighlight(doc);
        return changed;
    }

    private bool RemoveLineAssignee(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
    {
        bool removed = false;
        for (int i = doc.LineAssigneeAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineAssigneeAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line == lineNumber)
            {
                doc.LineAssigneeAnchors.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && markDirty)
            MarkDirty(doc);
        if (removed && redraw)
            RedrawHighlight(doc);
        return removed;
    }

    private void SetLineAssignments(TabDocument doc, IEnumerable<FileLineAssignee>? assignees, bool markDirty = true)
    {
        doc.LineAssigneeAnchors.Clear();

        if (assignees != null)
        {
            foreach (var assignee in assignees)
            {
                if (assignee == null || assignee.Line <= 0 || string.IsNullOrWhiteSpace(assignee.Person))
                    continue;

                SetLineAssignee(doc, assignee.Line, assignee.Person, markDirty: false, redraw: false);
            }
        }

        if (markDirty)
            MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void AssignSelectedLines(TextEditor editor)
    {
        var doc = FindDocByEditor(editor);
        if (doc == null)
            return;

        if (_users.Count == 0)
        {
            MessageBox.Show(
                "No users found. Add users from Tools > Users first.",
                "Assign Line",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var lines = GetSelectedOrCaretLineNumbers(doc);
        if (lines.Count == 0)
            return;

        var existingOwners = new List<string>();
        foreach (var line in lines)
        {
            if (TryGetLineAssignee(doc, line, out var owner) && !string.IsNullOrWhiteSpace(owner))
                existingOwners.Add(owner);
        }
        existingOwners = existingOwners
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string initialOwner = existingOwners.Count == 1 ? existingOwners[0] : string.Empty;
        var selectedOwner = ShowLineOwnerDialog(initialOwner);
        if (selectedOwner == null)
            return;

        var cleaned = selectedOwner.Trim();
        if (cleaned.Length == 0)
        {
            MessageBox.Show(
                "Please enter a person name.",
                "Assign Line",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool changed = false;
        foreach (var line in lines)
            changed |= SetLineAssignee(doc, line, cleaned, markDirty: false, redraw: false);

        if (changed)
        {
            MarkDirty(doc);
            RedrawHighlight(doc);
        }
    }

    private void ClearSelectedLineAssignments(TextEditor editor)
    {
        var doc = FindDocByEditor(editor);
        if (doc == null)
            return;

        var lines = GetSelectedOrCaretLineNumbers(doc);
        bool changed = false;
        foreach (var line in lines)
            changed |= RemoveLineAssignee(doc, line, markDirty: false, redraw: false);

        if (changed)
        {
            MarkDirty(doc);
            RedrawHighlight(doc);
        }
    }

    private string? ShowLineOwnerDialog(string initialOwner)
    {
        var dlg = new Window
        {
            Title = "Assign Line Owner",
            Width = 390,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = "Assign to user:",
            Margin = new Thickness(0, 0, 0, 6)
        });
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 10), IsEditable = false };
        foreach (var user in _users)
            combo.Items.Add(user.Name);
        if (!string.IsNullOrWhiteSpace(initialOwner))
        {
            var selected = _users
                .Select(user => user.Name)
                .FirstOrDefault(userName => string.Equals(userName, initialOwner, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selected))
                combo.SelectedItem = selected;
        }
        if (combo.SelectedItem == null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
        root.Children.Add(combo);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        ok.Click += (_, _) =>
        {
            if (combo.SelectedItem is not string)
                return;
            dlg.DialogResult = true;
        };
        dlg.Loaded += (_, _) =>
        {
            combo.Focus();
            Keyboard.Focus(combo);
        };

        combo.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        dlg.Content = root;
        var result = dlg.ShowDialog();
        return result == true ? combo.SelectedItem as string : null;
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
        if (payload.Length == 0 || payload[0] != '{')
            return false;

        // Legacy metadata line marker from older backups only.
        if (!payload.Contains("\"HighlightLine\"", StringComparison.Ordinal)
            && !payload.Contains("\"HighlightLines\"", StringComparison.Ordinal)
            && !payload.Contains("\"Assignees\"", StringComparison.Ordinal)
            && !payload.Contains("\"EndsWithNewline\"", StringComparison.Ordinal))
            return false;

        try
        {
            metadata = JsonSerializer.Deserialize<FileMetadata>(payload) ?? new FileMetadata();
        }
        catch
        {
            return false;
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
        AddShortcutBinding(DefaultShortcutReopenClosedTab, ReopenClosedTabCommand);
        AddShortcutBinding(_shortcutRenameTab, RenameTabCommand);
        AddShortcutBinding(_shortcutAddBlankLines, AddBlankLinesCommand);
        AddShortcutBinding(_shortcutToggleHighlight, ToggleHighlightCommand);
        AddShortcutBinding(_shortcutGoToLine, GoToLineCommand);
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

    private void ExecuteGoToLine()
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        int maxLine = Math.Max(1, doc.Editor.Document.LineCount);
        int currentLine = Math.Clamp(doc.Editor.TextArea.Caret.Line, 1, maxLine);
        var originalLocation = doc.Editor.TextArea.Caret.Location;
        var requestedLine = ShowGoToLineDialog(doc.Editor, currentLine, maxLine);
        if (requestedLine == null)
        {
            doc.Editor.TextArea.Caret.Location = originalLocation;
            doc.Editor.Select(doc.Editor.CaretOffset, 0);
            CenterCaretLine(doc.Editor, originalLocation.Line);
            doc.Editor.Focus();
            UpdateStatusBar(doc);
            return;
        }

        int targetLine = Math.Clamp(requestedLine.Value, 1, maxLine);
        doc.Editor.TextArea.Caret.Location = new TextLocation(targetLine, 1);
        doc.Editor.Select(doc.Editor.CaretOffset, 0);
        CenterCaretLine(doc.Editor, targetLine);
        doc.Editor.Focus();
        UpdateStatusBar(doc);
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWholeWordMatch(string text, int start, int length)
    {
        bool leftOk = start <= 0 || !IsWordChar(text[start - 1]);
        int rightIndex = start + length;
        bool rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static bool TryFindNextIndex(
        string text,
        string findText,
        int startIndex,
        StringComparison comparison,
        bool wholeWord,
        out int index)
    {
        index = -1;
        if (string.IsNullOrEmpty(findText))
            return false;

        int probe = Math.Clamp(startIndex, 0, text.Length);
        while (probe <= text.Length)
        {
            int found = text.IndexOf(findText, probe, comparison);
            if (found < 0)
                return false;
            if (!wholeWord || IsWholeWordMatch(text, found, findText.Length))
            {
                index = found;
                return true;
            }
            probe = found + 1;
        }

        return false;
    }

    private static int CountMatches(
        string text,
        string findText,
        StringComparison comparison,
        bool wholeWord)
    {
        if (string.IsNullOrEmpty(findText))
            return 0;

        int count = 0;
        int scanIndex = 0;
        while (TryFindNextIndex(text, findText, scanIndex, comparison, wholeWord, out var found))
        {
            count++;
            scanIndex = found + findText.Length;
        }

        return count;
    }

    private static bool SelectMatchInEditor(TextEditor editor, int start, int length)
    {
        if (start < 0 || length <= 0 || start + length > editor.Document.TextLength)
            return false;

        editor.Select(start, length);
        editor.TextArea.Caret.Offset = start + length;
        var line = editor.Document.GetLineByOffset(start);
        editor.ScrollToLine(line.LineNumber);
        editor.Focus();
        return true;
    }

    private void ShowFindReplaceDialog()
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        var editor = doc.Editor;
        var dlg = new Window
        {
            Title = "Find + Replace",
            Width = 520,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lblFind = new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
        Grid.SetRow(lblFind, 0);
        Grid.SetColumn(lblFind, 0);
        root.Children.Add(lblFind);

        var txtFind = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        if (!string.IsNullOrEmpty(editor.SelectedText))
            txtFind.Text = editor.SelectedText;
        Grid.SetRow(txtFind, 0);
        Grid.SetColumn(txtFind, 1);
        root.Children.Add(txtFind);

        var lblReplace = new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
        Grid.SetRow(lblReplace, 1);
        Grid.SetColumn(lblReplace, 0);
        root.Children.Add(lblReplace);

        var txtReplace = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(txtReplace, 1);
        Grid.SetColumn(txtReplace, 1);
        root.Children.Add(txtReplace);

        var optionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var chkMatchCase = new CheckBox { Content = "Match case", Margin = new Thickness(0, 0, 16, 0) };
        var chkWholeWord = new CheckBox { Content = "Whole word" };
        var chkAllTabs = new CheckBox { Content = "Find in all tabs", Margin = new Thickness(16, 0, 0, 0) };
        optionsPanel.Children.Add(chkMatchCase);
        optionsPanel.Children.Add(chkWholeWord);
        optionsPanel.Children.Add(chkAllTabs);
        Grid.SetRow(optionsPanel, 2);
        Grid.SetColumn(optionsPanel, 1);
        root.Children.Add(optionsPanel);

        var status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
            Text = "Use Find Next to jump between matches."
        };
        Grid.SetRow(status, 3);
        Grid.SetColumn(status, 1);
        root.Children.Add(status);

        var tabMatchesWrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        var tabMatchesScroll = new ScrollViewer
        {
            Content = tabMatchesWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 62,
            MaxHeight = 90
        };
        var tabMatchesBorder = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.WhiteSmoke,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
            Child = tabMatchesScroll
        };
        Grid.SetRow(tabMatchesBorder, 4);
        Grid.SetColumn(tabMatchesBorder, 1);
        root.Children.Add(tabMatchesBorder);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnFindNext = new Button { Content = "Find Next", Width = 95, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var btnReplace = new Button { Content = "Replace", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var btnReplaceAll = new Button { Content = "Replace All", Width = 95, Margin = new Thickness(0, 0, 8, 0) };
        var btnClose = new Button { Content = "Close", Width = 80, IsCancel = true };
        buttons.Children.Add(btnFindNext);
        buttons.Children.Add(btnReplace);
        buttons.Children.Add(btnReplaceAll);
        buttons.Children.Add(btnClose);
        Grid.SetRow(buttons, 5);
        Grid.SetColumn(buttons, 1);
        root.Children.Add(buttons);

        StringComparison GetComparison()
            => chkMatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        bool JumpToMatch(TabItem tab, int foundOffset, int length)
        {
            if (!_docs.TryGetValue(tab, out var foundDoc))
                return false;

            MainTabControl.SelectedItem = tab;
            if (!SelectMatchInEditor(foundDoc.Editor, foundOffset, length))
                return false;

            status.Text = $"Match found in \"{foundDoc.DisplayHeader}\".";
            return true;
        }

        void RefreshTabMatchButtons()
        {
            tabMatchesWrap.Children.Clear();

            var needle = txtFind.Text ?? string.Empty;
            if (chkAllTabs.IsChecked != true || string.IsNullOrEmpty(needle))
            {
                tabMatchesBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var comparison = GetComparison();
            bool wholeWord = chkWholeWord.IsChecked == true;
            var matches = new List<(TabItem Tab, TabDocument Doc, int Count)>();
            foreach (var tab in MainTabControl.Items.OfType<TabItem>())
            {
                if (!_docs.TryGetValue(tab, out var tabDoc))
                    continue;

                int count = CountMatches(tabDoc.Editor.Text, needle, comparison, wholeWord);
                if (count > 0)
                    matches.Add((tab, tabDoc, count));
            }

            if (matches.Count == 0)
            {
                tabMatchesBorder.Visibility = Visibility.Collapsed;
                return;
            }

            tabMatchesBorder.Visibility = Visibility.Visible;
            foreach (var item in matches)
            {
                var targetTab = item.Tab;
                var targetDoc = item.Doc;
                var targetButton = new Button
                {
                    Content = $"{targetDoc.DisplayHeader} ({item.Count})",
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(8, 2, 8, 2),
                    MinHeight = 24
                };
                targetButton.Click += (_, _) =>
                {
                    var targetComparison = GetComparison();
                    bool targetWholeWord = chkWholeWord.IsChecked == true;
                    if (!_docs.TryGetValue(targetTab, out var currentDoc))
                        return;

                    if (TryFindNextIndex(currentDoc.Editor.Text, needle, 0, targetComparison, targetWholeWord, out var foundAt))
                        JumpToMatch(targetTab, foundAt, needle.Length);
                };

                tabMatchesWrap.Children.Add(targetButton);
            }
        }

        bool FindNext(bool wrap)
        {
            var needle = txtFind.Text ?? string.Empty;
            if (string.IsNullOrEmpty(needle))
            {
                status.Text = "Enter text to find.";
                return false;
            }

            var comparison = GetComparison();
            bool wholeWord = chkWholeWord.IsChecked == true;
            if (chkAllTabs.IsChecked != true)
            {
                var text = editor.Text;
                int start = editor.SelectionStart + editor.SelectionLength;

                if (TryFindNextIndex(text, needle, start, comparison, wholeWord, out var found))
                {
                    SelectMatchInEditor(editor, found, needle.Length);
                    status.Text = "Match found.";
                    return true;
                }

                if (wrap && TryFindNextIndex(text, needle, 0, comparison, wholeWord, out found))
                {
                    SelectMatchInEditor(editor, found, needle.Length);
                    status.Text = "Wrapped to top.";
                    return true;
                }

                status.Text = "No matches.";
                return false;
            }

            var orderedTabs = MainTabControl.Items.OfType<TabItem>().Where(tab => _docs.ContainsKey(tab)).ToList();
            if (orderedTabs.Count == 0)
            {
                status.Text = "No open tabs.";
                return false;
            }

            var currentTab = MainTabControl.SelectedItem as TabItem;
            int currentIndex = currentTab == null ? 0 : orderedTabs.IndexOf(currentTab);
            if (currentIndex < 0)
                currentIndex = 0;

            int startOffset = editor.SelectionStart + editor.SelectionLength;
            for (int i = currentIndex; i < orderedTabs.Count; i++)
            {
                var tab = orderedTabs[i];
                if (!_docs.TryGetValue(tab, out var tabDoc))
                    continue;

                int searchStart = i == currentIndex ? startOffset : 0;
                if (TryFindNextIndex(tabDoc.Editor.Text, needle, searchStart, comparison, wholeWord, out var found))
                    return JumpToMatch(tab, found, needle.Length);
            }

            if (!wrap)
            {
                status.Text = "No more matches.";
                return false;
            }

            for (int i = 0; i <= currentIndex; i++)
            {
                var tab = orderedTabs[i];
                if (!_docs.TryGetValue(tab, out var tabDoc))
                    continue;

                string textToSearch = tabDoc.Editor.Text;
                int limit = i == currentIndex ? Math.Clamp(startOffset, 0, textToSearch.Length) : textToSearch.Length;
                if (limit <= 0)
                    continue;

                string segment = limit == textToSearch.Length ? textToSearch : textToSearch[..limit];
                if (TryFindNextIndex(segment, needle, 0, comparison, wholeWord, out var found))
                    return JumpToMatch(tab, found, needle.Length);
            }

            status.Text = "No matches in any tab.";
            return false;
        }

        bool SelectionMatchesFindText()
        {
            var needle = txtFind.Text ?? string.Empty;
            if (string.IsNullOrEmpty(needle) || editor.SelectionLength != needle.Length)
                return false;

            var selected = editor.SelectedText;
            var comparison = GetComparison();
            if (!string.Equals(selected, needle, comparison))
                return false;
            if (chkWholeWord.IsChecked != true)
                return true;

            return IsWholeWordMatch(editor.Text, editor.SelectionStart, editor.SelectionLength);
        }

        void ReplaceCurrentSelection()
        {
            if (!SelectionMatchesFindText())
            {
                if (!FindNext(wrap: true))
                    return;
            }

            int start = editor.SelectionStart;
            int length = editor.SelectionLength;
            editor.Document.Replace(start, length, txtReplace.Text ?? string.Empty);
            editor.Select(start, (txtReplace.Text ?? string.Empty).Length);
            editor.TextArea.Caret.Offset = start + (txtReplace.Text ?? string.Empty).Length;
            status.Text = "Replaced current match.";
        }

        void ReplaceAll()
        {
            var needle = txtFind.Text ?? string.Empty;
            if (string.IsNullOrEmpty(needle))
            {
                status.Text = "Enter text to find.";
                return;
            }

            var replacement = txtReplace.Text ?? string.Empty;
            var source = editor.Text;
            var comparison = GetComparison();

            int scanIndex = 0;
            int cursor = 0;
            int count = 0;
            var builder = new StringBuilder(source.Length);

            while (TryFindNextIndex(source, needle, scanIndex, comparison, chkWholeWord.IsChecked == true, out var found))
            {
                builder.Append(source, cursor, found - cursor);
                builder.Append(replacement);
                cursor = found + needle.Length;
                scanIndex = cursor;
                count++;
            }

            if (count == 0)
            {
                status.Text = "No matches to replace.";
                return;
            }

            builder.Append(source, cursor, source.Length - cursor);
            editor.Text = builder.ToString();
            status.Text = $"Replaced {count} occurrence(s).";
            editor.Focus();
        }

        btnFindNext.Click += (_, _) => FindNext(wrap: true);
        btnReplace.Click += (_, _) => ReplaceCurrentSelection();
        btnReplaceAll.Click += (_, _) => ReplaceAll();
        chkAllTabs.Checked += (_, _) => RefreshTabMatchButtons();
        chkAllTabs.Unchecked += (_, _) => RefreshTabMatchButtons();
        chkMatchCase.Checked += (_, _) => RefreshTabMatchButtons();
        chkMatchCase.Unchecked += (_, _) => RefreshTabMatchButtons();
        chkWholeWord.Checked += (_, _) => RefreshTabMatchButtons();
        chkWholeWord.Unchecked += (_, _) => RefreshTabMatchButtons();
        txtFind.TextChanged += (_, _) => RefreshTabMatchButtons();
        txtFind.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                FindNext(wrap: true);
                e.Handled = true;
            }
        };

        dlg.Content = root;
        dlg.Loaded += (_, _) =>
        {
            txtFind.Focus();
            txtFind.SelectAll();
            RefreshTabMatchButtons();
        };
        dlg.ShowDialog();
    }

    private int? ShowGoToLineDialog(TextEditor editor, int currentLine, int maxLine)
    {
        var dlg = new Window
        {
            Title = "Go To Line",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = $"Line number (1-{maxLine}):",
            Margin = new Thickness(0, 0, 0, 6)
        });

        var input = new TextBox { Text = currentLine.ToString(), Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(input);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        int parsedLine = currentLine;
        void PreviewLine(int line)
        {
            int targetLine = Math.Clamp(line, 1, maxLine);
            editor.TextArea.Caret.Location = new TextLocation(targetLine, 1);
            editor.Select(editor.CaretOffset, 0);
            CenterCaretLine(editor, targetLine);
        }

        input.TextChanged += (_, _) =>
        {
            if (int.TryParse(input.Text.Trim(), out int liveLine) && liveLine >= 1)
                PreviewLine(liveLine);
        };

        ok.Click += (_, _) =>
        {
            if (!int.TryParse(input.Text.Trim(), out parsedLine))
            {
                MessageBox.Show("Enter a valid line number.", "Go To Line", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (parsedLine < 1 || parsedLine > maxLine)
            {
                MessageBox.Show($"Line number must be between 1 and {maxLine}.", "Go To Line", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PreviewLine(parsedLine);
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
        return result == true ? parsedLine : null;
    }

    private static void CenterCaretLine(TextEditor editor, int line)
    {
        editor.ScrollTo(line, 1);

        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();
        if (textView.ActualHeight <= 0)
            return;

        double visualTop = textView.GetVisualTopByDocumentLine(line);
        double targetOffset = Math.Max(0, visualTop - (textView.ActualHeight / 2) + (textView.DefaultLineHeight / 2));
        editor.ScrollToVerticalOffset(targetOffset);
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
        if (MenuItemGoToLine != null)
            MenuItemGoToLine.InputGestureText = GestureDisplayText(_shortcutGoToLine);
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

    private string ClosedTabsHistoryPath() => Path.Combine(_backupFolder, ClosedTabsFileName);

    private void AddClosedTabToHistory(TabDocument doc)
    {
        _closedTabHistory.Insert(0, new ClosedTabEntry
        {
            Header = doc.Header,
            Content = doc.CachedText,
            IsDirty = doc.IsDirty,
            Metadata = CreateFileMetadata(doc)
        });

        if (_closedTabHistory.Count > MaxClosedTabs)
            _closedTabHistory.RemoveRange(MaxClosedTabs, _closedTabHistory.Count - MaxClosedTabs);

        SaveClosedTabHistory();
    }

    private void SaveClosedTabHistory()
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ClosedTabsHistoryPath(), JsonSerializer.Serialize(_closedTabHistory, options));
        }
        catch
        {
            // Non-critical: tab close still succeeds.
        }
    }

    private void LoadClosedTabHistory()
    {
        _closedTabHistory.Clear();

        try
        {
            var path = ClosedTabsHistoryPath();
            if (!File.Exists(path))
                return;

            var parsed = JsonSerializer.Deserialize<List<ClosedTabEntry>>(File.ReadAllText(path));
            if (parsed == null)
                return;

            foreach (var entry in parsed
                .Where(e => e != null)
                .Where(e => !string.IsNullOrWhiteSpace(e.Header))
                .Take(MaxClosedTabs))
            {
                var highlightedLines = entry.Metadata?.HighlightLines?
                    .Where(line => line > 0)
                    .Distinct()
                    .OrderBy(line => line)
                    .ToList();

                var assignees = entry.Metadata?.Assignees?
                    .Where(a => a != null && a.Line > 0 && !string.IsNullOrWhiteSpace(a.Person))
                    .Select(a => new FileLineAssignee
                    {
                        Line = a.Line,
                        Person = a.Person.Trim()
                    })
                    .OrderBy(a => a.Line)
                    .ToList();

                _closedTabHistory.Add(new ClosedTabEntry
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
                            Assignees = assignees != null && assignees.Count > 0 ? assignees : null
                        }
                });
            }
        }
        catch
        {
            // Ignore corrupt file and start with empty history.
        }
    }

    private void ReopenLastClosedTab()
    {
        if (_closedTabHistory.Count == 0)
            return;

        var entry = _closedTabHistory[0];
        _closedTabHistory.RemoveAt(0);
        SaveClosedTabHistory();

        var doc = CreateTab(entry.Header, entry.Content);
        var highlightedLines = entry.Metadata?.HighlightLines ?? [];
        if (highlightedLines.Count == 0 && entry.Metadata?.HighlightLine is int legacyHighlightLine && legacyHighlightLine > 0)
            highlightedLines = [legacyHighlightLine];
        SetHighlightedLines(doc, highlightedLines, markDirty: false);
        SetLineAssignments(doc, entry.Metadata?.Assignees, markDirty: false);
        doc.IsDirty = entry.IsDirty;
        RefreshTabHeader(doc);
    }

    private bool ConfirmDiscardUnsavedTab(string tabName)
    {
        var dlg = new Window
        {
            Title = "Noted",
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var root = new DockPanel { Margin = new Thickness(14) };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = "!",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.DarkGoldenrod,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var message = new TextBlock
        {
            Text = $"\"{tabName}\" has unsaved data.\nClosing it will remove the entire tab and all of its content.\n\nDelete this tab?",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(icon);
        content.Children.Add(message);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var btnDelete = new Button
        {
            Content = "Delete tab",
            Width = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnKeep = new Button
        {
            Content = "Keep tab",
            Width = 90,
            IsCancel = true
        };

        btnDelete.Click += (_, _) => dlg.DialogResult = true;
        btnKeep.Click += (_, _) => dlg.DialogResult = false;

        buttonPanel.Children.Add(btnDelete);
        buttonPanel.Children.Add(btnKeep);

        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        root.Children.Add(buttonPanel);
        root.Children.Add(content);
        dlg.Content = root;

        return dlg.ShowDialog() == true;
    }

    private bool CloseTab(TabItem tab)
    {
        if (!_docs.TryGetValue(tab, out var doc)) return true;

        if (doc.IsDirty && !string.IsNullOrEmpty(doc.Editor.Text))
        {
            // Ensure the tab with unsaved changes is in view
            MainTabControl.SelectedItem = tab;

            if (!ConfirmDiscardUnsavedTab(doc.Header))
                return false;
        }

        doc.CachedText = doc.Editor.Text;
        AddClosedTabToHistory(doc);
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
    private FileMetadata? CreateFileMetadata(TabDocument doc)
    {
        var highlighted = GetHighlightedLineNumbers(doc).OrderBy(line => line).ToList();
        var assignees = GetLineAssignments(doc)
            .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new FileLineAssignee { Line = pair.Key, Person = pair.Value })
            .OrderBy(entry => entry.Line)
            .ToList();

        if (highlighted.Count == 0 && assignees.Count == 0)
            return null;

        return new FileMetadata
        {
            HighlightLines = highlighted.Count > 0 ? highlighted : null,
            Assignees = assignees.Count > 0 ? assignees : null
        };
    }

    private void SaveSession(
        bool updateStatus = true,
        bool forceCloudBackup = false,
        string? cloudBackupFolderOverride = null,
        bool persistCloudMetadata = true)
    {
        try
        {
            _lastSaveIncludedCloudCopy = false;
            foreach (var doc in _docs.Values)
                doc.CachedText = doc.Editor.Text;

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
                    var metadata = CreateFileMetadata(doc);

                    // Divider format: ^---name^---
                    // Content format: plain text (verbatim, no injected metadata line)
                    sw.WriteLine($"{BundleDivider}{doc.Header}{BundleDivider}");
                    if (metadata != null)
                        sw.WriteLine($"{MetadataPrefix} {JsonSerializer.Serialize(metadata)}");
                    sw.Write(text);
                    sw.WriteLine();
                }
            }

            PruneBackups();
            TrySaveCloudBackup(path, forceCloudBackup, cloudBackupFolderOverride, persistCloudMetadata);

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

    private void TrySaveCloudBackup(
        string justSavedBackupPath,
        bool forceCloudBackup = false,
        string? cloudBackupFolderOverride = null,
        bool persistCloudMetadata = true)
    {
        var cloudFolder = string.IsNullOrWhiteSpace(cloudBackupFolderOverride)
            ? _cloudBackupFolder
            : cloudBackupFolderOverride.Trim();

        if (string.IsNullOrWhiteSpace(cloudFolder)) return;
        if (!forceCloudBackup && !ShouldSaveCloudBackup()) return;
        if (!File.Exists(justSavedBackupPath)) return;

        try
        {
            Directory.CreateDirectory(cloudFolder);
            var targetPath = Path.Combine(cloudFolder, Path.GetFileName(justSavedBackupPath));
            File.Copy(justSavedBackupPath, targetPath, overwrite: true);
            _lastCloudSaveUtc = DateTime.UtcNow;
            _lastSaveIncludedCloudCopy = true;
            if (persistCloudMetadata)
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

                // Backward compatibility: older backups inserted a metadata line.
                FileMetadata metadata = new();
                if (TryReadMetadataLine(text, contentStart, out var parsedMetadata, out var contentAfterMetadata))
                {
                    contentStart = contentAfterMetadata;
                    metadata = parsedMetadata;
                }

                int contentEnd = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : text.Length;

                // Strip exactly one separator newline that SaveSession adds.
                var content = text[contentStart..contentEnd];
                if (content.EndsWith("\r\n")) content = content[..^2];
                else if (content.EndsWith('\n')) content = content[..^1];

                // New format keeps content verbatim; no escaping/decoding

                var doc = CreateTab(name, content);
                var highlightedLines = metadata.HighlightLines ?? [];
                if (highlightedLines.Count == 0 && metadata.HighlightLine.HasValue && metadata.HighlightLine.Value > 0)
                    highlightedLines = [metadata.HighlightLine.Value];
                SetHighlightedLines(doc, highlightedLines, markDirty: false);
                SetLineAssignments(doc, metadata.Assignees, markDirty: false);
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

    private void RestoreActiveTabSelection()
    {
        if (MainTabControl.Items.Count == 0) return;
        if (_activeTabIndex < 0 || _activeTabIndex >= MainTabControl.Items.Count) return;
        MainTabControl.SelectedIndex = _activeTabIndex;
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
    private void MenuReopenClosedTab_Click(object sender, RoutedEventArgs e) => ReopenLastClosedTab();
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();
    private void MenuSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsDialog();
    private void MenuUsers_Click(object sender, RoutedEventArgs e) => ShowUsersDialog();

    private void MenuUndo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Undo();
    private void MenuRedo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Redo();
    private void MenuCut_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Cut();
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Copy();
    private void MenuPaste_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Paste();
    private void MenuFindReplace_Click(object sender, RoutedEventArgs e) => ShowFindReplaceDialog();
    private void MenuGoToLine_Click(object sender, RoutedEventArgs e) => ExecuteGoToLine();
    private void MenuCopySelectionTo_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;
        PopulateTransferMenu(menu, CurrentDoc()?.Editor, moveSelection: false);
    }

    private void MenuMoveSelectionTo_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;
        PopulateTransferMenu(menu, CurrentDoc()?.Editor, moveSelection: true);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        SaveWindowSettings();
        if (!_sessionSaved)
            SaveSession(updateStatus: false);
    }

    private static List<UserProfile> NormalizeUsers(IEnumerable<UserProfile>? users)
    {
        if (users == null)
            return [];

        var byName = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Name))
                continue;

            var name = user.Name.Trim();
            var color = NormalizeUserColor(user.Color, fallbackSeed: name);
            byName[name] = new UserProfile { Name = name, Color = color };
        }

        return byName.Values
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<UserProfile> BuildUsersFromLegacyNames(IEnumerable<string>? userNames)
    {
        if (userNames == null)
            return [];

        return NormalizeUsers(userNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new UserProfile
            {
                Name = name.Trim(),
                Color = ColorToHex(DeterministicUserColor(name.Trim()))
            }));
    }

    private static string NormalizeUserColor(string? input, string fallbackSeed)
    {
        if (TryParseColor(input, out var parsed))
            return ColorToHex(parsed);

        return ColorToHex(DeterministicUserColor(fallbackSeed));
    }

    private static Color DeterministicUserColor(string seed)
    {
        int hash = Math.Abs((seed ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase));
        double hue = hash % 360;
        return ColorFromHsv(hue, 0.50, 0.88);
    }

    private static Color RandomUserColor()
    {
        double hue = Random.Shared.NextDouble() * 360.0;
        double saturation = 0.45 + (Random.Shared.NextDouble() * 0.30);
        double value = 0.78 + (Random.Shared.NextDouble() * 0.18);
        return ColorFromHsv(hue, saturation, value);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double c = value * saturation;
        double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        double m = value - c;

        double r = 0, g = 0, b = 0;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private Color GetUserColor(string person)
    {
        var user = _users.FirstOrDefault(u => string.Equals(u.Name, person, StringComparison.OrdinalIgnoreCase));
        if (user != null && TryParseColor(user.Color, out var parsed))
            return parsed;

        return DeterministicUserColor(person);
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
                ShortcutGoToLine = _shortcutGoToLine,
                SelectedLineColor = ColorToHex(_selectedLineColor),
                HighlightedLineColor = ColorToHex(_highlightedLineColor),
                SelectedHighlightedLineColor = ColorToHex(_selectedHighlightedLineColor),
                BackupFolder = _backupFolder,
                CloudBackupFolder = _cloudBackupFolder,
                CloudSaveHours = _cloudSaveIntervalHours,
                CloudSaveMinutes = _cloudSaveIntervalMinutes,
                LastCloudCopyUtc = _lastCloudSaveUtc == DateTime.MinValue ? null : _lastCloudSaveUtc,
                ActiveTabIndex = MainTabControl.SelectedIndex,
                FridayFeelingEnabled = _isFridayFeelingEnabled,
                Users = _users.Select(user => user.Name).ToList(),
                UserProfiles = NormalizeUsers(_users)
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
            _shortcutGoToLine = DefaultShortcutGoToLine;
            _isFridayFeelingEnabled = true;
            _isFredagspartySessionEnabled = false;
            _users = [];
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
            if (TryParseKeyGesture(state.ShortcutGoToLine, out _))
                _shortcutGoToLine = state.ShortcutGoToLine!.Trim();
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
            if (state.ActiveTabIndex >= 0)
                _activeTabIndex = state.ActiveTabIndex;
            _isFridayFeelingEnabled = state.FridayFeelingEnabled;
            var loadedUsers = NormalizeUsers(state.UserProfiles);
            if (loadedUsers.Count == 0)
                loadedUsers = BuildUsersFromLegacyNames(state.Users);
            _users = loadedUsers;
            if (TryParseColor(state.SelectedLineColor, out var selectedLineColor))
                _selectedLineColor = selectedLineColor;
            if (TryParseColor(state.HighlightedLineColor, out var highlightedLineColor))
            {
                // Migrate prior built-in defaults to the current highlight palette.
                if (highlightedLineColor == Color.FromRgb(255, 196, 128))
                    highlightedLineColor = DefaultHighlightedLineColor;
                if (highlightedLineColor == Color.FromRgb(255, 105, 180))
                    highlightedLineColor = DefaultHighlightedLineColor;
                if (highlightedLineColor == Color.FromRgb(255, 182, 193))
                    highlightedLineColor = DefaultHighlightedLineColor;
                _highlightedLineColor = highlightedLineColor;
            }
            if (TryParseColor(state.SelectedHighlightedLineColor, out var selectedHighlightedLineColor))
            {
                // Migrate prior built-in defaults to the current highlight palette.
                if (selectedHighlightedLineColor == Color.FromRgb(255, 160, 96))
                    selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
                if (selectedHighlightedLineColor == Color.FromRgb(255, 105, 180))
                    selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
                if (selectedHighlightedLineColor == Color.FromRgb(255, 182, 193))
                    selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
                _selectedHighlightedLineColor = selectedHighlightedLineColor;
            }

            _startMaximized = state.Maximized;
            if (_lastCloudSaveUtc == DateTime.MinValue)
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
            ApplyColorThemeToOpenEditors();
            ApplyFridayFeelingToOpenEditors();
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

    private static void CopyClosedTabsFileToBackupFolder(string fromFolder, string toFolder)
    {
        var src = Path.Combine(fromFolder, ClosedTabsFileName);
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(toFolder);
        var dst = Path.Combine(toFolder, ClosedTabsFileName);
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
        public string ShortcutGoToLine { get; set; } = DefaultShortcutGoToLine;
        public string? SelectedLineColor { get; set; }
        public string? HighlightedLineColor { get; set; }
        public string? SelectedHighlightedLineColor { get; set; }
        public string? BackupFolder { get; set; }
        public string? CloudBackupFolder { get; set; }
        public int? CloudSaveHours { get; set; }
        public int? CloudSaveMinutes { get; set; }
        public DateTime? LastCloudCopyUtc { get; set; }
        public int ActiveTabIndex { get; set; } = 0;
        public bool FridayFeelingEnabled { get; set; } = true;
        public List<string>? Users { get; set; }
        public List<UserProfile>? UserProfiles { get; set; }
    }

    // --- Settings dialog ----------------------------------------------------

    private void ShowUsersDialog()
    {
        var users = NormalizeUsers(_users);
        var dlg = new Window
        {
            Title = "Users",
            Width = 520,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Users available for line assignment:",
            Margin = new Thickness(0, 0, 0, 8)
        });

        var list = new ListBox
        {
            Height = 200,
            Margin = new Thickness(0, 0, 0, 10)
        };
        foreach (var user in users)
            list.Items.Add(user);
        content.Children.Add(list);

        var addRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtUser = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        var btnAdd = new Button { Content = "Add", Width = 80 };
        Grid.SetColumn(txtUser, 0);
        Grid.SetColumn(btnAdd, 1);
        addRow.Children.Add(txtUser);
        addRow.Children.Add(btnAdd);
        content.Children.Add(addRow);

        content.Children.Add(new TextBlock
        {
            Text = "Selected user's color:",
            Margin = new Thickness(0, 0, 0, 6)
        });

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string[] userColorOptions =
        [
            "LightSkyBlue",
            "LightGreen",
            "Khaki",
            "LightSalmon",
            "Plum",
            "PaleTurquoise",
            "MistyRose",
            "PeachPuff",
            "Lavender",
            "#FF8BD3DD",
            "#FFE0B3FF",
            "#FFFFD58A",
            "#FFC6E8A8"
        ];

        var cmbUserColor = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 200 };
        foreach (var option in userColorOptions)
            cmbUserColor.Items.Add(option);

        var btnApplyColor = new Button { Content = "Apply", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var btnRandomColor = new Button { Content = "Randomize", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var colorPreview = new Border
        {
            Width = 24,
            Height = 24,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent
        };

        Grid.SetColumn(cmbUserColor, 0);
        Grid.SetColumn(btnApplyColor, 1);
        Grid.SetColumn(btnRandomColor, 2);
        Grid.SetColumn(colorPreview, 3);
        colorRow.Children.Add(cmbUserColor);
        colorRow.Children.Add(btnApplyColor);
        colorRow.Children.Add(btnRandomColor);
        colorRow.Children.Add(colorPreview);
        content.Children.Add(colorRow);

        var btnRemove = new Button
        {
            Content = "Remove Selected",
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(btnRemove);

        void RefreshUsers(string? selectedUserName = null)
        {
            users = NormalizeUsers(users);
            list.Items.Clear();
            foreach (var user in users)
                list.Items.Add(user);

            if (!string.IsNullOrWhiteSpace(selectedUserName))
            {
                var selected = users.FirstOrDefault(user => string.Equals(user.Name, selectedUserName, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    list.SelectedItem = selected;
            }
        }

        void UpdateSelectedColorEditor()
        {
            bool hasSelection = list.SelectedItem is UserProfile;
            btnApplyColor.IsEnabled = hasSelection;
            btnRandomColor.IsEnabled = hasSelection;
            if (!hasSelection)
            {
                cmbUserColor.Text = string.Empty;
                colorPreview.Background = Brushes.Transparent;
                return;
            }

            var selected = (UserProfile)list.SelectedItem;
            cmbUserColor.Text = selected.Color;
            if (TryParseColor(selected.Color, out var selectedColor))
                colorPreview.Background = new SolidColorBrush(selectedColor);
            else
                colorPreview.Background = Brushes.Transparent;
        }

        void ApplyColorToSelectedUser(bool randomize)
        {
            if (list.SelectedItem is not UserProfile selectedUser)
                return;

            Color color;
            if (randomize)
            {
                color = RandomUserColor();
            }
            else if (!TryParseColor(cmbUserColor.Text, out color))
            {
                MessageBox.Show("Please enter a valid color name or hex value.", "Users",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            selectedUser.Color = ColorToHex(color);
            RefreshUsers(selectedUser.Name);
            UpdateSelectedColorEditor();
        }

        btnAdd.Click += (_, _) =>
        {
            var name = (txtUser.Text ?? string.Empty).Trim();
            if (name.Length == 0)
                return;

            var existing = users.FirstOrDefault(user => string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                users.Add(new UserProfile { Name = name, Color = ColorToHex(RandomUserColor()) });

            txtUser.SelectAll();
            RefreshUsers(name);
            UpdateSelectedColorEditor();
        };

        txtUser.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btnAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        btnRemove.Click += (_, _) =>
        {
            if (list.SelectedItem is UserProfile selectedUser)
            {
                users.RemoveAll(user => string.Equals(user.Name, selectedUser.Name, StringComparison.OrdinalIgnoreCase));
                RefreshUsers();
                UpdateSelectedColorEditor();
            }
        };

        btnApplyColor.Click += (_, _) => ApplyColorToSelectedUser(randomize: false);
        btnRandomColor.Click += (_, _) => ApplyColorToSelectedUser(randomize: true);
        cmbUserColor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btnApplyColor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is UserProfile selected)
                txtUser.Text = selected.Name;
            UpdateSelectedColorEditor();
        };

        ok.Click += (_, _) =>
        {
            users = NormalizeUsers(users);
            dlg.DialogResult = true;
        };

        root.Children.Add(content);
        dlg.Content = root;
        dlg.Loaded += (_, _) =>
        {
            txtUser.Focus();
            Keyboard.Focus(txtUser);
            UpdateSelectedColorEditor();
        };

        if (dlg.ShowDialog() != true)
            return;

        _users = NormalizeUsers(users);
        foreach (var doc in _docs.Values)
            RedrawHighlight(doc);
        SaveWindowSettings();
    }

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
        var txtLastCloudCopy = new TextBlock
        {
            Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        };
        backupPanel.Children.Add(txtLastCloudCopy);

        var btnCloudSaveNow = new Button
        {
            Content = "Save now",
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        btnCloudSaveNow.Click += (_, _) =>
        {
            var cloudBackupPath = (txtCloudBackup.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cloudBackupPath))
            {
                MessageBox.Show("Set a cloud storage folder first.", "Cloud save",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveSession(
                updateStatus: true,
                forceCloudBackup: true,
                cloudBackupFolderOverride: cloudBackupPath,
                persistCloudMetadata: false);
            txtLastCloudCopy.Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}";
            RefreshGlobalDirtyStatus();
        };
        backupPanel.Children.Add(btnCloudSaveNow);

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
            "#FFFFF4B3",
            "#FFFFEA80"
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

        shortkeysPanel.Children.Add(new TextBlock { Text = "Go to line:" });
        var txtShortcutGoToLine = new TextBox { Text = _shortcutGoToLine, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutGoToLine);

        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+Shift+T reopens the most recently closed tab.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+MouseWheel changes font size.",
            Foreground = Brushes.DimGray
        });
        tabControl.Items.Add(new TabItem { Header = "Shortkeys", Content = shortkeysPanel });

        var fridayPanel = new StackPanel { Margin = new Thickness(12) };
        var chkFridayFeeling = new CheckBox
        {
            Content = "Enable Fredagsparty background automatically on Fridays",
            IsChecked = _isFridayFeelingEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkFredagsparty = new CheckBox
        {
            Content = "Enable Fredagsparty until app closes",
            IsChecked = _isFredagspartySessionEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        fridayPanel.Children.Add(chkFridayFeeling);
        fridayPanel.Children.Add(chkFredagsparty);
        tabControl.Items.Add(new TabItem
        {
            Header = "Friday",
            Content = fridayPanel
        });

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
            var shortcutGoToLine = txtShortcutGoToLine.Text.Trim();

            if (string.IsNullOrWhiteSpace(shortcutNewPrimary)
                || string.IsNullOrWhiteSpace(shortcutClose)
                || string.IsNullOrWhiteSpace(shortcutRename)
                || string.IsNullOrWhiteSpace(shortcutAddBlankLines)
                || string.IsNullOrWhiteSpace(shortcutToggleHighlight)
                || string.IsNullOrWhiteSpace(shortcutGoToLine))
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
                || !TryParseKeyGesture(shortcutToggleHighlight, out _)
                || !TryParseKeyGesture(shortcutGoToLine, out _))
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
                shortcutToggleHighlight,
                shortcutGoToLine
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
                {
                    CopySettingsFileToBackupFolder(previousBackupFolder, backupPath);
                    CopyClosedTabsFileToBackupFolder(previousBackupFolder, backupPath);
                }

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
                _shortcutGoToLine = shortcutGoToLine;
                _selectedLineColor = selectedLineColor;
                _highlightedLineColor = highlightedLineColor;
                _selectedHighlightedLineColor = selectedHighlightedLineColor;
                _isFridayFeelingEnabled = chkFridayFeeling.IsChecked == true;
                _isFredagspartySessionEnabled = chkFredagsparty.IsChecked == true;
                SaveClosedTabHistory();

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
                ApplyFridayFeelingToOpenEditors();

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
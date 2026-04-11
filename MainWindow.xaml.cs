using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Net.Http.Headers;
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
using Noted.Services;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow : Window
{
    // --- State -------------------------------------------------------------------------
    private readonly SettingsService _settingsService = new();
    private readonly BackupService _backupService = new();
    private readonly ClosedTabsService _closedTabsService = new();
    private readonly WindowSettingsStore _windowSettingsStore = new();
    private readonly WindowSettingsService _windowSettingsService = new();
    private readonly BackupBundleService _backupBundleService = new();
    private readonly ShortcutService _shortcutService = new();
    private readonly ColorThemeService _colorThemeService = new();
    private readonly UserProfileService _userProfileService = new();
    private readonly TimeReportSettingsService _timeReportSettingsService = new();
    private readonly Dictionary<TabItem, TabDocument> _docs = new();
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _pluginAlarmTimer;
    private Point _tabDragStartPoint;
    private TabItem? _dragSourceTab;
    private bool _startMaximized = false;
    private bool _sessionSaved = false;
    private bool _lastSaveIncludedCloudCopy = false;
    private int _activeTabIndex = 0;
    private readonly List<ClosedTabEntry> _closedTabHistory = [];
    private List<UserProfile> _users = [];
    private readonly Dictionary<string, TimeReportMonthState> _timeReports = new(StringComparer.OrdinalIgnoreCase);
    private List<PluginAlarmSettings> _pluginAlarms = [];
    private bool _pluginAlarmsEnabled = true;
    private readonly HashSet<string> _triggeredPluginAlarmKeysForMinute = new(StringComparer.OrdinalIgnoreCase);
    private string _triggeredPluginAlarmMinuteKey = string.Empty;
    private double? _alarmPopupLeft;
    private double? _alarmPopupTop;

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
    private const int DefaultTabCleanupStaleDays = 30;
    private const string DefaultFontFamily = "Consolas, Courier New";
    private const double DefaultFontSize = 13;
    private const int DefaultFontWeight = 400;
    private const string DefaultShortcutNewPrimary = "Ctrl+N";
    private const string DefaultShortcutNewSecondary = "Ctrl+T";
    private const string DefaultShortcutCloseTab = "Ctrl+W";
    private const string DefaultShortcutReopenClosedTab = "Ctrl+Shift+T";
    private const string DefaultShortcutRenameTab = "F2";
    private const string DefaultShortcutAddBlankLines = "Ctrl+Space";
    private const string DefaultShortcutTrimTrailingEmptyLines = "Ctrl+Shift+Space";
    private const string DefaultShortcutToggleHighlight = "Ctrl+J";
    private const string DefaultShortcutGoToLine = "Ctrl+G";
    private const string DefaultShortcutGoToTab = "Ctrl+P";
    private const string DefaultShortcutFakeSave = "Ctrl+S";
    private static readonly string[] FakeSaveStatusMessages =
    [
        "No need to buy me nice stuff. - Nothing was saved.",
        "You don't have to grease me up like that. - Nothing was saved.",
        "I would do anything for love... but I won't do that. - Nothing was saved.",
        "You don't have to try that hard to impress me like that. - Nothing was saved.",
        "I'm low maintenance, I promise. - Nothing was saved.",
        "I'm not that hard to win over, you know. - Nothing was saved.",
        "No need to go that far for me like that. - Nothing was saved."
    ];
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
    private const string BackupImagesFolderName = "images";
    private const string DeletedImagesFolderName = "deleted";
    private const int MaxDeletedInlineImages = 100;
    private static readonly Regex ImageLineMarkerRegex =
        new(@"^\^<(?<file>[A-Za-z0-9][A-Za-z0-9._-]*\.png)(?:,(?<scale>[1-9][0-9]{0,2}))?>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly int[] CloudMinuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];
    private int _initialLines = DefaultInitialLines;
    private int _tabCleanupStaleDays = DefaultTabCleanupStaleDays;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    /// <summary>Session-only offset for Ctrl+wheel zoom; not saved to settings.</summary>
    private double _sessionEditorFontZoomDelta;
    private int _fontWeight = DefaultFontWeight;
    private string _shortcutNewPrimary = DefaultShortcutNewPrimary;
    private string _shortcutNewSecondary = DefaultShortcutNewSecondary;
    private string _shortcutCloseTab = DefaultShortcutCloseTab;
    private string _shortcutRenameTab = DefaultShortcutRenameTab;
    private string _shortcutAddBlankLines = DefaultShortcutAddBlankLines;
    private string _shortcutTrimTrailingEmptyLines = DefaultShortcutTrimTrailingEmptyLines;
    private string _shortcutToggleHighlight = DefaultShortcutToggleHighlight;
    private string _shortcutGoToLine = DefaultShortcutGoToLine;
    private string _shortcutGoToTab = DefaultShortcutGoToTab;
    private Color _selectedLineColor = DefaultSelectedLineColor;
    private Color _highlightedLineColor = DefaultHighlightedLineColor;
    private Color _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
    private Brush _selectedLineBrush = CreateFrozenBrush(DefaultSelectedLineColor);
    private Brush _highlightedLineBrush = CreateFrozenBrush(DefaultHighlightedLineColor);
    private Brush _selectedHighlightedLineBrush = CreateFrozenBrush(DefaultSelectedHighlightedLineColor);
    private bool _isFridayFeelingEnabled = true;
    private bool _isFredagspartySessionEnabled = false;
    private bool _isFredagspartyTemporarilyDisabled;
    private ImageBrush? _fridayBackgroundBrush;
    private readonly List<KeyBinding> _shortcutBindings = [];
    private readonly Dictionary<string, BitmapSource> _inlineImageCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _referencedInlineImagesSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInlineImageResizeActive;
    private TextEditor? _inlineImageResizeEditor;
    private int _inlineImageResizeLineNumber;
    private string _inlineImageResizeFileName = string.Empty;
    private double _inlineImageResizeStartX;
    private int _inlineImageResizeStartScalePercent;
    private int _inlineImageResizeCurrentScalePercent;

    private static readonly RoutedUICommand RenameTabCommand = new("Rename Tab", nameof(RenameTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ReopenClosedTabCommand = new("Reopen Closed Tab", nameof(ReopenClosedTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand AddBlankLinesCommand = new("Add Blank Lines", nameof(AddBlankLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand TrimTrailingEmptyLinesCommand = new("Trim Trailing Empty Lines", nameof(TrimTrailingEmptyLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleHighlightCommand = new("Toggle Highlight", nameof(ToggleHighlightCommand), typeof(MainWindow));
    private static readonly RoutedUICommand GoToLineCommand = new("Go To Line", nameof(GoToLineCommand), typeof(MainWindow));
    private static readonly RoutedUICommand GoToTabCommand = new("Go To Tab", nameof(GoToTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand FakeSaveCommand = new("Fake Save", nameof(FakeSaveCommand), typeof(MainWindow));
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

    private sealed class GoToTabOption
    {
        public required string Header { get; init; }
        public required TabItem Tab { get; init; }
    }

    private readonly record struct InlineImageMarker(string FileName, int ScalePercent)
    {
        public string ToMarkerText()
            => ScalePercent == 100
                ? $"^<{FileName}>"
                : $"^<{FileName},{ScalePercent}>";
    }

    private sealed class ImageLineElementGenerator : VisualLineElementGenerator
    {
        private readonly Func<DocumentLine, InlineImageMarker, UIElement?> _elementFactory;
        private readonly Func<InlineImageMarker, bool> _canRenderImageLine;

        public ImageLineElementGenerator(
            Func<DocumentLine, InlineImageMarker, UIElement?> elementFactory,
            Func<InlineImageMarker, bool> canRenderImageLine)
        {
            _elementFactory = elementFactory;
            _canRenderImageLine = canRenderImageLine;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            try
            {
                var document = CurrentContext.Document;
                if (document == null || document.TextLength == 0)
                    return -1;

                int safeOffset = Math.Max(0, Math.Min(startOffset, document.TextLength - 1));
                var line = document.GetLineByOffset(safeOffset);
                while (line != null)
                {
                    var lineText = document.GetText(line.Offset, line.Length);
                    if (TryGetInlineImageMarker(lineText, out _)
                        && line.Offset >= startOffset)
                    {
                        if (TryGetInlineImageMarker(lineText, out var marker)
                            && _canRenderImageLine(marker))
                            return line.Offset;
                    }
                    line = line.NextLine;
                }

                return -1;
            }
            catch
            {
                return -1;
            }
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            try
            {
                var document = CurrentContext.Document;
                if (document == null || document.TextLength == 0 || offset < 0 || offset >= document.TextLength)
                    return null;

                var line = document.GetLineByOffset(offset);
                if (line.Offset != offset || line.Length <= 0)
                    return null;

                var lineText = document.GetText(line.Offset, line.Length);
                if (!TryGetInlineImageMarker(lineText, out var marker))
                    return null;

                var element = _elementFactory(line, marker);
                if (element == null)
                    return null;

                return new InlineObjectElement(line.Length, element);
            }
            catch
            {
                return null;
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
        CommandBindings.Add(new CommandBinding(TrimTrailingEmptyLinesCommand, (_, _) => ExecuteTrimTrailingEmptyLines()));
        CommandBindings.Add(new CommandBinding(ToggleHighlightCommand, (_, _) => ExecuteToggleHighlight()));
        CommandBindings.Add(new CommandBinding(GoToLineCommand, (_, _) => ExecuteGoToLine()));
        CommandBindings.Add(new CommandBinding(GoToTabCommand, (_, _) => ExecuteGoToTab()));
        CommandBindings.Add(new CommandBinding(FakeSaveCommand, (_, _) => ExecuteFakeSaveShortcut()));

        MainTabControl.AllowDrop = true;
        MainTabControl.DragOver += MainTabControl_DragOver;
        MainTabControl.Drop += MainTabControl_Drop;

        // Auto-save timer
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DefaultAutoSaveSeconds) };
        _autoSaveTimer.Tick += (_, _) => SaveSession();
        _autoSaveTimer.Start();
        _pluginAlarmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _pluginAlarmTimer.Tick += (_, _) => CheckPluginAlarms();

        // Restore window position/size, then session
        LoadWindowSettings();
        _pluginAlarmTimer.Start();
        ApplyShortcutBindings();
        EnsureSettingsFileExists();
        EnsureBackupImagesFolderExists();
        LoadClosedTabHistory();
        Loaded += (_, _) => { if (_startMaximized) WindowState = WindowState.Maximized; };

        // Restore previous session; if nothing to restore, open a blank tab
        LoadSession();
        RestoreActiveTabSelection();
        if (_docs.Count == 0)
            NewTab();
        RefreshInlineImageReferenceSnapshot();
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
            IsDirty = false,
            LastChangedUtc = DateTime.UtcNow
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
            doc.LastChangedUtc = DateTime.UtcNow;
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
        editor.PreviewMouseRightButtonDown += (_, e) => MoveCaretToMousePosition(editor, e);

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
            FontSize = ClampedEditorDisplayFontSize(),
            FontWeight = FontWeight.FromOpenTypeWeight(_fontWeight),
            ShowLineNumbers = true,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4)
        };
        editor.TextArea.TextView.Margin = new Thickness(8, 0, 0, 0);
        editor.Options.HighlightCurrentLine = true;
        editor.TextArea.TextView.CurrentLineBackground = _selectedLineBrush;
        editor.TextArea.TextView.ElementGenerators.Add(new ImageLineElementGenerator(
            (line, marker) => CreateInlineImageElement(editor, line.LineNumber, marker),
            CanRenderInlineImageLine));
        EnableJsonSyntaxHighlighting(editor);
        editor.ContextMenu = BuildEditorContextMenu(editor);
        ApplyFridayBackgroundToEditor(editor);
        return editor;
    }

    private string GetBackupImagesFolderPath()
        => Path.Combine(_backupFolder, BackupImagesFolderName);

    private string GetDeletedImagesFolderPath()
        => Path.Combine(GetBackupImagesFolderPath(), DeletedImagesFolderName);

    private void EnsureBackupImagesFolderExists()
    {
        try
        {
            Directory.CreateDirectory(GetBackupImagesFolderPath());
        }
        catch
        {
            // Best-effort folder bootstrap.
        }
    }

    private static HashSet<string> CollectReferencedInlineImageFiles(IEnumerable<string> texts)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
                continue;

            using var reader = new StringReader(text);
            while (reader.ReadLine() is string line)
            {
                if (TryGetInlineImageMarker(line, out var marker))
                    referenced.Add(marker.FileName);
            }
        }

        return referenced;
    }

    private HashSet<string> GetReferencedInlineImageFilesFromDocs()
        => CollectReferencedInlineImageFiles(_docs.Values.Select(doc => doc.CachedText));

    private void RefreshInlineImageReferenceSnapshot()
        => _referencedInlineImagesSnapshot = GetReferencedInlineImageFilesFromDocs();

    private void MoveDeletedInlineImagesToArchive(HashSet<string> removedReferences)
    {
        if (removedReferences.Count == 0)
            return;

        var imagesFolder = GetBackupImagesFolderPath();
        if (!Directory.Exists(imagesFolder))
            return;

        var deletedFolder = GetDeletedImagesFolderPath();
        Directory.CreateDirectory(deletedFolder);

        foreach (var fileName in removedReferences)
        {
            var filePath = Path.Combine(imagesFolder, fileName);
            if (!File.Exists(filePath))
                continue;

            var destinationPath = EnsureUniqueImageFilePath(deletedFolder, fileName);
            try
            {
                File.Move(filePath, destinationPath);
                _inlineImageCache.Remove(fileName);
            }
            catch
            {
                // Best-effort cleanup; keep the file in place on failure.
            }
        }

        PruneDeletedInlineImages();
    }

    private void PruneDeletedInlineImages()
    {
        var deletedFolder = GetDeletedImagesFolderPath();
        if (!Directory.Exists(deletedFolder))
            return;

        var files = Directory.EnumerateFiles(deletedFolder, "*.png", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.LastWriteTimeUtc)
            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int deleteCount = files.Count - MaxDeletedInlineImages;
        if (deleteCount <= 0)
            return;

        foreach (var file in files.Take(deleteCount))
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Best-effort pruning; ignore files that cannot be deleted.
            }
        }
    }

    private static bool TryGetInlineImageMarker(string lineText, out InlineImageMarker marker)
    {
        marker = default;
        if (string.IsNullOrEmpty(lineText))
            return false;

        var match = ImageLineMarkerRegex.Match(lineText.Trim());
        if (!match.Success)
            return false;

        var fileName = match.Groups["file"].Value;
        if (fileName.Length == 0)
            return false;

        var scaleGroup = match.Groups["scale"];
        int scalePercent = 100;
        if (scaleGroup.Success && !int.TryParse(scaleGroup.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out scalePercent))
            return false;

        if (scalePercent < 1 || scalePercent > 999)
            return false;

        marker = new InlineImageMarker(fileName, scalePercent);
        return true;
    }

    private bool TryGetInlineImageSource(string fileName, out BitmapSource imageSource)
    {
        imageSource = null!;
        if (_inlineImageCache.TryGetValue(fileName, out var cached))
        {
            imageSource = cached;
            return true;
        }

        var imagePath = Path.Combine(GetBackupImagesFolderPath(), fileName);
        if (!File.Exists(imagePath))
        {
            if (!TryRestoreInlineImageFromDeleted(fileName))
                return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            _inlineImageCache[fileName] = bitmap;
            imageSource = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryRestoreInlineImageFromDeleted(string fileName)
    {
        var imagePath = Path.Combine(GetBackupImagesFolderPath(), fileName);
        if (File.Exists(imagePath))
            return true;

        var deletedPath = Path.Combine(GetDeletedImagesFolderPath(), fileName);
        if (!File.Exists(deletedPath))
            return false;

        try
        {
            Directory.CreateDirectory(GetBackupImagesFolderPath());
            File.Move(deletedPath, imagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool CanRenderInlineImageLine(InlineImageMarker marker)
    {
        if (_inlineImageCache.ContainsKey(marker.FileName))
            return true;
        var imagePath = Path.Combine(GetBackupImagesFolderPath(), marker.FileName);
        if (File.Exists(imagePath))
            return true;

        return TryRestoreInlineImageFromDeleted(marker.FileName);
    }

    private UIElement? CreateInlineImageElement(TextEditor editor, int lineNumber, InlineImageMarker marker)
    {
        if (!TryGetInlineImageSource(marker.FileName, out var imageSource))
            return null;

        var baseWidth = Math.Max(1.0, imageSource.Width);
        var baseHeight = Math.Max(1.0, imageSource.Height);
        int currentScalePercent = marker.ScalePercent;
        if (_isInlineImageResizeActive
            && ReferenceEquals(_inlineImageResizeEditor, editor)
            && _inlineImageResizeLineNumber == lineNumber
            && string.Equals(_inlineImageResizeFileName, marker.FileName, StringComparison.OrdinalIgnoreCase))
        {
            currentScalePercent = _inlineImageResizeCurrentScalePercent;
        }

        var image = new Image
        {
            Source = imageSource,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(2)
        };

        void ApplyScale(int scalePercent)
        {
            image.Width = baseWidth * scalePercent / 100.0;
            image.Height = baseHeight * scalePercent / 100.0;
        }

        ApplyScale(currentScalePercent);

        var border = new Border
        {
            Margin = new Thickness(2, 1, 2, 1),
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.LightGray,
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Arrow,
            ToolTip = "Drag lower-right corner to resize image"
        };

        var containerGrid = new Grid();
        containerGrid.Children.Add(image);

        var resizeHandle = new Border
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Cursor = Cursors.SizeNWSE
        };
        containerGrid.Children.Add(resizeHandle);
        border.Child = containerGrid;

        void EndResizeSession(bool commit)
        {
            if (!_isInlineImageResizeActive)
                return;

            editor.PreviewMouseMove -= EditorOnPreviewMouseMove;
            editor.PreviewMouseLeftButtonUp -= EditorOnPreviewMouseLeftButtonUp;
            editor.LostMouseCapture -= EditorOnLostMouseCapture;
            Mouse.Capture(null);

            if (commit)
                UpdateInlineImageMarkerScale(editor, _inlineImageResizeLineNumber, _inlineImageResizeFileName, _inlineImageResizeCurrentScalePercent);

            _isInlineImageResizeActive = false;
            _inlineImageResizeEditor = null;
            _inlineImageResizeLineNumber = 0;
            _inlineImageResizeFileName = string.Empty;
            _inlineImageResizeStartX = 0;
            _inlineImageResizeStartScalePercent = 100;
            _inlineImageResizeCurrentScalePercent = 100;
            editor.TextArea.TextView.Redraw();
        }

        void EditorOnPreviewMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isInlineImageResizeActive
                || !ReferenceEquals(_inlineImageResizeEditor, editor)
                || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            double currentX = e.GetPosition(editor).X;
            double deltaX = currentX - _inlineImageResizeStartX;
            int nextScalePercent = (int)Math.Round(_inlineImageResizeStartScalePercent + deltaX, MidpointRounding.AwayFromZero);
            nextScalePercent = Math.Max(1, Math.Min(400, nextScalePercent));
            if (nextScalePercent == _inlineImageResizeCurrentScalePercent)
                return;

            _inlineImageResizeCurrentScalePercent = nextScalePercent;
            editor.TextArea.TextView.Redraw();
            e.Handled = true;
        }

        void EditorOnPreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (!_isInlineImageResizeActive || !ReferenceEquals(_inlineImageResizeEditor, editor))
                return;

            EndResizeSession(commit: true);
            e.Handled = true;
        }

        void EditorOnLostMouseCapture(object? sender, MouseEventArgs e)
        {
            if (!_isInlineImageResizeActive || !ReferenceEquals(_inlineImageResizeEditor, editor))
                return;

            EndResizeSession(commit: true);
        }

        resizeHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (_isInlineImageResizeActive)
                EndResizeSession(commit: true);

            _isInlineImageResizeActive = true;
            _inlineImageResizeEditor = editor;
            _inlineImageResizeLineNumber = lineNumber;
            _inlineImageResizeFileName = marker.FileName;
            _inlineImageResizeStartX = e.GetPosition(editor).X;
            _inlineImageResizeStartScalePercent = currentScalePercent;
            _inlineImageResizeCurrentScalePercent = currentScalePercent;

            editor.PreviewMouseMove += EditorOnPreviewMouseMove;
            editor.PreviewMouseLeftButtonUp += EditorOnPreviewMouseLeftButtonUp;
            editor.LostMouseCapture += EditorOnLostMouseCapture;

            Mouse.Capture(editor, CaptureMode.SubTree);
            e.Handled = true;
        };

        return border;
    }

    private static int ClampScalePercent(int scalePercent)
        => Math.Max(1, Math.Min(999, scalePercent));

    private static string BuildInlineImageMarkerText(string fileName, int scalePercent)
        => new InlineImageMarker(fileName, ClampScalePercent(scalePercent)).ToMarkerText();

    private static void UpdateInlineImageMarkerScale(TextEditor editor, int lineNumber, string fileName, int scalePercent)
    {
        if (editor.Document == null)
            return;

        if (lineNumber < 1 || lineNumber > editor.Document.LineCount)
            return;

        var line = editor.Document.GetLineByNumber(lineNumber);
        var currentText = editor.Document.GetText(line.Offset, line.Length);
        if (!TryGetInlineImageMarker(currentText, out var currentMarker))
            return;

        if (!string.Equals(currentMarker.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            return;

        var clampedScalePercent = ClampScalePercent(scalePercent);
        if (currentMarker.ScalePercent == clampedScalePercent)
            return;

        var replacementText = BuildInlineImageMarkerText(fileName, clampedScalePercent);
        editor.Document.Replace(line.Offset, line.Length, replacementText);
        editor.TextArea.TextView.Redraw();
    }

    private static string BuildImageFileName(DateTime timestamp)
        => $"screenshot-{timestamp:yyyy-MM-dd-HHmmss}.png";

    private static string EnsureUniqueImageFilePath(string folder, string desiredFileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(desiredFileName);
        string extension = Path.GetExtension(desiredFileName);
        string candidatePath = Path.Combine(folder, desiredFileName);
        int suffix = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(folder, $"{baseName}-{suffix:00}{extension}");
            suffix++;
        }

        return candidatePath;
    }

    private static void InsertImageMarkerLine(TextEditor editor, string marker)
    {
        if (editor.Document == null)
            return;

        int replaceStart = editor.SelectionStart;
        int replaceLength = editor.SelectionLength;
        int anchorOffset = Math.Max(0, Math.Min(replaceStart, editor.Document.TextLength));
        var anchorLine = editor.Document.GetLineByOffset(anchorOffset);

        bool needsLeadingNewLine = anchorOffset > anchorLine.Offset;
        bool needsTrailingNewLine = anchorOffset < anchorLine.EndOffset;
        string replacement =
            $"{(needsLeadingNewLine ? Environment.NewLine : string.Empty)}{marker}{(needsTrailingNewLine ? Environment.NewLine : string.Empty)}";

        editor.Document.Replace(replaceStart, replaceLength, replacement);
        editor.TextArea.Caret.Offset = replaceStart + replacement.Length;
        editor.Select(editor.TextArea.Caret.Offset, 0);
    }

    private bool TryPasteClipboardImage(TabDocument doc)
    {
        BitmapSource? clipboardImage;
        try
        {
            if (!Clipboard.ContainsImage())
                return false;
            clipboardImage = Clipboard.GetImage();
        }
        catch
        {
            return false;
        }

        if (clipboardImage == null)
            return false;

        try
        {
            var imageFolder = GetBackupImagesFolderPath();
            Directory.CreateDirectory(imageFolder);

            var desiredFileName = BuildImageFileName(DateTime.Now);
            var imagePath = EnsureUniqueImageFilePath(imageFolder, desiredFileName);
            var imageFileName = Path.GetFileName(imagePath);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(clipboardImage));

            using (var stream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                encoder.Save(stream);

            _inlineImageCache.Remove(imageFileName);
            InsertImageMarkerLine(doc.Editor, BuildInlineImageMarkerText(imageFileName, 100));
            doc.Editor.TextArea.TextView.Redraw();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save image from clipboard:\n{ex.Message}", "Paste image",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
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
        var resetImageSizeItem = new MenuItem { Header = "Reset Image Size to Original" };
        resetImageSizeItem.Click += (_, _) => ResetInlineImageSizeToOriginal(editor);
        var openImageFolderItem = new MenuItem { Header = "Show Image in Folder" };
        openImageFolderItem.Click += (_, _) => ShowInlineImageInFolder(editor);

        menu.Items.Add(formatJsonItem);
        menu.Items.Add(copySelectionItem);
        menu.Items.Add(moveSelectionItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetImageSizeItem);
        menu.Items.Add(openImageFolderItem);
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
            resetImageSizeItem.IsEnabled = CanResetInlineImageSizeAtCaret(editor);
            openImageFolderItem.IsEnabled = CanShowInlineImageInFolderAtCaret(editor);
        };

        return menu;
    }

    private static void MoveCaretToMousePosition(TextEditor editor, MouseButtonEventArgs e)
    {
        var position = editor.GetPositionFromPoint(e.GetPosition(editor));
        if (!position.HasValue)
            return;

        editor.TextArea.Caret.Location = position.Value.Location;
        editor.Select(editor.CaretOffset, 0);
    }

    private bool CanResetInlineImageSizeAtCaret(TextEditor editor)
    {
        if (!TryGetInlineImageMarkerAtCaret(editor, out _, out var marker))
            return false;
        return marker.ScalePercent != 100;
    }

    private void ResetInlineImageSizeToOriginal(TextEditor editor)
    {
        if (!TryGetInlineImageMarkerAtCaret(editor, out var lineNumber, out var marker))
            return;

        UpdateInlineImageMarkerScale(editor, lineNumber, marker.FileName, 100);
    }

    private bool CanShowInlineImageInFolderAtCaret(TextEditor editor)
    {
        if (!TryGetInlineImageMarkerAtCaret(editor, out _, out var marker))
            return false;

        var imagePath = Path.Combine(GetBackupImagesFolderPath(), marker.FileName);
        return File.Exists(imagePath);
    }

    private void ShowInlineImageInFolder(TextEditor editor)
    {
        if (!TryGetInlineImageMarkerAtCaret(editor, out _, out var marker))
            return;

        var imagePath = Path.Combine(GetBackupImagesFolderPath(), marker.FileName);
        if (!File.Exists(imagePath))
            return;

        try
        {
            string fullPath = Path.GetFullPath(imagePath).Replace('/', '\\');
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select, \"{fullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open image in folder:\n{ex.Message}", "Open image folder",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryGetInlineImageMarkerAtCaret(TextEditor editor, out int lineNumber, out InlineImageMarker marker)
    {
        lineNumber = 0;
        marker = default;
        if (editor.Document == null || editor.Document.LineCount == 0)
            return false;

        lineNumber = Math.Max(1, Math.Min(editor.TextArea.Caret.Line, editor.Document.LineCount));
        var line = editor.Document.GetLineByNumber(lineNumber);
        var lineText = editor.Document.GetText(line.Offset, line.Length);
        return TryGetInlineImageMarker(lineText, out marker);
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

    private double ClampedEditorDisplayFontSize()
        => Math.Max(6, Math.Min(72, _fontSize + _sessionEditorFontZoomDelta));

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;

        double step = e.Delta > 0 ? 1 : -1;
        double next = Math.Max(6, Math.Min(72, _fontSize + _sessionEditorFontZoomDelta + step));
        _sessionEditorFontZoomDelta = next - _fontSize;

        var family = new FontFamily(_fontFamily);
        foreach (var doc in _docs.Values)
        {
            doc.Editor.FontFamily = family;
            doc.Editor.FontSize = next;
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
        => !_isFredagspartyTemporarilyDisabled
            && (_isFredagspartySessionEnabled || (_isFridayFeelingEnabled && DateTime.Now.DayOfWeek == DayOfWeek.Friday));

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
        => new ColorThemeService().TryParseColor(input, out color);

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
            && e.Key == Key.V
            && TryPasteClipboardImage(doc))
        {
            e.Handled = true;
            return;
        }

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

    private static void RedrawHighlight(TabDocument doc)
        => doc.Editor.TextArea.TextView.Redraw();


    private bool TryParseKeyGesture(string? input, out KeyGesture gesture)
        => _shortcutService.TryParseKeyGesture(input, out gesture);

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
        AddShortcutBinding(_shortcutTrimTrailingEmptyLines, TrimTrailingEmptyLinesCommand);
        AddShortcutBinding(_shortcutToggleHighlight, ToggleHighlightCommand);
        AddShortcutBinding(_shortcutGoToLine, GoToLineCommand);
        AddShortcutBinding(_shortcutGoToTab, GoToTabCommand);
        AddShortcutBinding(DefaultShortcutFakeSave, FakeSaveCommand);
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

    private void ExecuteTrimTrailingEmptyLines()
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        var text = doc.Editor.Text;
        var preferredNewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trimmed = Regex.Replace(text, @"(?:\r?\n[ \t]*)+$", string.Empty);
        var updated = trimmed + preferredNewLine;
        if (string.Equals(text, updated, StringComparison.Ordinal))
            return;

        int caretOffset = Math.Min(doc.Editor.CaretOffset, updated.Length);
        doc.Editor.Text = updated;
        doc.Editor.CaretOffset = caretOffset;
    }

    private void ExecuteFakeSaveShortcut()
    {
        if (FakeSaveStatusMessages.Length == 0)
            return;

        var index = Random.Shared.Next(FakeSaveStatusMessages.Length);
        StatusAutoSave.Text = FakeSaveStatusMessages[index];
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

    private void ExecuteGoToTab()
    {
        var availableTabs = MainTabControl.Items
            .OfType<TabItem>()
            .Where(tab => tab.Tag is TabDocument)
            .ToList();
        if (availableTabs.Count == 0)
            return;

        var selectedTab = MainTabControl.SelectedItem as TabItem;
        var targetTab = ShowGoToTabDialog(availableTabs, selectedTab);
        if (targetTab == null)
            return;

        MainTabControl.SelectedItem = targetTab;
        if (targetTab.Tag is TabDocument targetDoc)
            targetDoc.Editor.Focus();
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

    private TabItem? ShowGoToTabDialog(IReadOnlyList<TabItem> tabs, TabItem? currentTab)
    {
        var dlg = new Window
        {
            Title = "Go To Tab",
            Width = 420,
            Height = 245,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.CanResize,
            MinWidth = 340,
            MinHeight = 220
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var input = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(input, Dock.Top);
        root.Children.Add(input);

        var list = new ListBox
        {
            DisplayMemberPath = nameof(GoToTabOption.Header),
            Height = 130,
            MinHeight = 120
        };
        root.Children.Add(list);

        TabItem? chosenTab = null;

        void MoveSelection(int delta)
        {
            if (list.Items.Count == 0)
                return;

            int nextIndex = list.SelectedIndex;
            if (nextIndex < 0)
                nextIndex = 0;
            else
                nextIndex = Math.Clamp(nextIndex + delta, 0, list.Items.Count - 1);

            list.SelectedIndex = nextIndex;
            list.ScrollIntoView(list.SelectedItem);
        }

        void ConfirmSelection()
        {
            if (list.SelectedItem is not GoToTabOption selected)
                return;

            chosenTab = selected.Tab;
            dlg.DialogResult = true;
        }

        void RefreshList()
        {
            var filter = (input.Text ?? string.Empty).Trim();
            var filtered = tabs
                .Where(tab =>
                {
                    var header = (tab.Tag as TabDocument)?.Header ?? tab.Header?.ToString() ?? string.Empty;
                    return filter.Length == 0
                        || header.Contains(filter, StringComparison.OrdinalIgnoreCase);
                })
                .Select(tab => new GoToTabOption
                {
                    Header = (tab.Tag as TabDocument)?.Header ?? tab.Header?.ToString() ?? string.Empty,
                    Tab = tab
                })
                .ToList();

            list.ItemsSource = filtered;
            if (filtered.Count == 0)
            {
                list.SelectedItem = null;
                return;
            }

            if (list.SelectedItem is not GoToTabOption selected || !filtered.Any(item => ReferenceEquals(item.Tab, selected.Tab)))
            {
                list.SelectedItem = currentTab != null
                    ? filtered.FirstOrDefault(item => ReferenceEquals(item.Tab, currentTab)) ?? filtered[0]
                    : filtered[0];
            }
        }

        input.TextChanged += (_, _) => RefreshList();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.Items.Count > 0)
            {
                e.Handled = true;
                MoveSelection(1);
            }
            else if (e.Key == Key.Up && list.Items.Count > 0)
            {
                e.Handled = true;
                MoveSelection(-1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmSelection();
            }
        };

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is GoToTabOption)
                ConfirmSelection();
        };

        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveSelection(1);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveSelection(-1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmSelection();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dlg.DialogResult = false;
            }
        };

        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dlg.DialogResult = false;
            }
        };

        dlg.Loaded += (_, _) =>
        {
            input.Focus();
            Keyboard.Focus(input);
            RefreshList();
        };

        dlg.Content = root;
        var result = dlg.ShowDialog();
        return result == true ? chosenTab : null;
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
        if (MenuItemGoToTab != null)
            MenuItemGoToTab.InputGestureText = GestureDisplayText(_shortcutGoToTab);
        if (MenuItemTrimTrailingEmptyLines != null)
            MenuItemTrimTrailingEmptyLines.InputGestureText = GestureDisplayText(_shortcutTrimTrailingEmptyLines);
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
            _closedTabsService.SaveHistory(_backupFolder, ClosedTabsFileName, _closedTabHistory);
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
            var parsed = _closedTabsService.LoadHistory<ClosedTabEntry>(_backupFolder, ClosedTabsFileName);
            if (parsed == null)
                return;

            _closedTabHistory.AddRange(_closedTabsService.NormalizeHistory(parsed, MaxClosedTabs));
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
        RestoreCaretPosition(doc, entry.Metadata);
        doc.LastSavedUtc = entry.Metadata?.LastSavedUtc?.ToUniversalTime();
        doc.LastChangedUtc = entry.Metadata?.LastChangedUtc?.ToUniversalTime()
            ?? entry.Metadata?.LastSavedUtc?.ToUniversalTime()
            ?? DateTime.UtcNow;
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
    private string? GetLatestBackupFilePath(string folder)
        => _backupService.GetLatestBackupFilePath(folder);

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
    private FileMetadata CreateFileMetadata(TabDocument doc)
    {
        var highlighted = GetHighlightedLineNumbers(doc).OrderBy(line => line).ToList();
        var assignees = GetLineAssignments(doc)
            .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new FileLineAssignee { Line = pair.Key, Person = pair.Value })
            .OrderBy(entry => entry.Line)
            .ToList();

        return new FileMetadata
        {
            HighlightLines = highlighted.Count > 0 ? highlighted : null,
            Assignees = assignees.Count > 0 ? assignees : null,
            LastSavedUtc = doc.LastSavedUtc,
            LastChangedUtc = doc.LastChangedUtc,
            CaretOffset = doc.Editor.CaretOffset
        };
    }

    private static void RestoreCaretPosition(TabDocument doc, FileMetadata? metadata)
    {
        if (metadata?.CaretOffset is not int savedOffset)
            return;

        int targetOffset = Math.Max(0, Math.Min(savedOffset, doc.Editor.Document.TextLength));
        doc.Editor.CaretOffset = targetOffset;
        doc.Editor.Select(targetOffset, 0);
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
            var referencedNow = GetReferencedInlineImageFilesFromDocs();
            var removedReferences = _referencedInlineImagesSnapshot
                .Except(referencedNow, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            MoveDeletedInlineImagesToArchive(removedReferences);
            _referencedInlineImagesSnapshot = referencedNow;

            var path = _backupBundleService.CreateBackupFilePath(_backupFolder, DateTime.Now);
            var sections = new List<BackupBundleService.BackupBundleSection>();
            foreach (var item in MainTabControl.Items)
            {
                if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc))
                    continue;

                if (doc.IsDirty)
                    doc.LastSavedUtc = DateTime.UtcNow;

                var text = doc.CachedText;
                var metadata = CreateFileMetadata(doc);
                var metadataPayload = metadata == null ? null : JsonSerializer.Serialize(metadata);
                sections.Add(new BackupBundleService.BackupBundleSection(doc.Header, text, metadataPayload));
            }
            _backupBundleService.WriteBundle(path, sections, BundleDivider, MetadataPrefix);

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

    private DateTime GetLatestBackupWriteUtcOrMin(string folder)
        => _backupService.GetLatestBackupWriteUtcOrMin(folder);

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

        if (!_backupService.TryCopyBackupToFolder(justSavedBackupPath, cloudFolder))
            return;

        _lastCloudSaveUtc = DateTime.UtcNow;
        _lastSaveIncludedCloudCopy = true;
        if (persistCloudMetadata)
            SaveWindowSettings();
    }

    /// <summary>Deletes the oldest backups when more than MaxBackups files exist.</summary>
    private void PruneBackups()
        => _backupService.PruneBackups(_backupFolder, NotedBackupFileNameRegex, MaxBackups);

    /// <summary>Restores all tabs from the most recent backup file, if one exists.</summary>
    private void LoadSession()
    {
        if (!Directory.Exists(_backupFolder)) return;

        var latest = GetLatestBackupFilePath(_backupFolder);
        if (latest == null) return;

        try
        {
            var text = _backupBundleService.ReadBundleText(latest);
            var sections = _backupBundleService.ParseBundle(text, MetadataPrefix);

            foreach (var section in sections)
            {
                FileMetadata metadata = new();
                if (!string.IsNullOrWhiteSpace(section.MetadataPayload))
                {
                    try
                    {
                        metadata = JsonSerializer.Deserialize<FileMetadata>(section.MetadataPayload) ?? new FileMetadata();
                    }
                    catch
                    {
                        metadata = new FileMetadata();
                    }
                }

                var doc = CreateTab(section.Header, section.Content);
                var highlightedLines = metadata.HighlightLines ?? [];
                if (highlightedLines.Count == 0 && metadata.HighlightLine.HasValue && metadata.HighlightLine.Value > 0)
                    highlightedLines = [metadata.HighlightLine.Value];
                SetHighlightedLines(doc, highlightedLines, markDirty: false);
                SetLineAssignments(doc, metadata.Assignees, markDirty: false);
                RestoreCaretPosition(doc, metadata);
                doc.LastSavedUtc = metadata.LastSavedUtc?.ToUniversalTime();
                doc.LastChangedUtc = metadata.LastChangedUtc?.ToUniversalTime()
                    ?? metadata.LastSavedUtc?.ToUniversalTime()
                    ?? DateTime.UtcNow;
                doc.IsDirty = false;
                RefreshTabHeader(doc);
            }

            var lastSaved = _backupBundleService.GetLastWriteTime(latest);
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
    private void MenuOpenFile_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "Sorry, you can't open a file, you think this is a text editor?",
            "Open file",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
    private void MenuAlarms_Click(object sender, RoutedEventArgs e) => ShowAlarmsDialog();
    private void MenuUsers_Click(object sender, RoutedEventArgs e) => ShowUsersDialog();
    private void MenuTabCleanup_Click(object sender, RoutedEventArgs e) => ShowTabCleanupDialog();
    private void MenuTimeReport_Click(object sender, RoutedEventArgs e) => ShowTimeReportDialog();
    private void MenuBase64_Click(object sender, RoutedEventArgs e) => ShowBase64Dialog();
    private void MenuQuickMessageOverlay_Click(object sender, RoutedEventArgs e) => ShowQuickMessageOverlayDialog();
    private void MenuCidrConverter_Click(object sender, RoutedEventArgs e) => ShowCidrConverterDialog();
    private void MenuPasswordGenerator_Click(object sender, RoutedEventArgs e) => ShowPasswordGeneratorDialog();
    private void MenuJwtDecoder_Click(object sender, RoutedEventArgs e) => ShowJwtDecoderDialog();
    private void MenuTextSplitter_Click(object sender, RoutedEventArgs e) => ShowTextSplitterDialog();
    private void MenuTxtLookup_Click(object sender, RoutedEventArgs e) => ShowTxtLookupDialog();
    private void MenuTimeConverter_Click(object sender, RoutedEventArgs e) => ShowTimeConverterDialog();
    private void MenuMongoObjectIdTimestampConverter_Click(object sender, RoutedEventArgs e) => ShowMongoObjectIdTimestampConverterDialog();
    private void MenuMongoDbApiGetToken_Click(object sender, RoutedEventArgs e) => ShowMongoDbApiGetTokenDialog();
    private void MenuMongoSrvLookup_Click(object sender, RoutedEventArgs e) => ShowMongoSrvLookupDialog();

    private void MenuUndo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Undo();
    private void MenuRedo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Redo();
    private void MenuCut_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Cut();
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Copy();
    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;
        if (!TryPasteClipboardImage(doc))
            doc.Editor.Paste();
    }
    private void MenuFindReplace_Click(object sender, RoutedEventArgs e) => ShowFindReplaceDialog();
    private void MenuGoToLine_Click(object sender, RoutedEventArgs e) => ExecuteGoToLine();
    private void MenuGoToTab_Click(object sender, RoutedEventArgs e) => ExecuteGoToTab();
    private void MenuTrimTrailingEmptyLines_Click(object sender, RoutedEventArgs e) => ExecuteTrimTrailingEmptyLines();
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

    private void PruneTimeReportMonth(string monthKey)
    {
        if (!_timeReports.TryGetValue(monthKey, out var monthState))
            return;

        var normalizedDayValues = monthState.DayValues
            .Where(entry => entry.Key >= 1 && entry.Key <= 31)
            .Select(entry => new { Day = entry.Key, Raw = entry.Value })
            .Where(entry => TryNormalizeTimeReportDayValue(entry.Raw, out _))
            .ToDictionary(
                entry => entry.Day,
                entry =>
                {
                    TryNormalizeTimeReportDayValue(entry.Raw, out var normalized);
                    return normalized;
                });
        monthState.DayValues.Clear();
        foreach (var pair in normalizedDayValues)
            monthState.DayValues[pair.Key] = pair.Value;

        var normalizedComments = monthState.WeekComments
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key.Trim(), entry => entry.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        monthState.WeekComments.Clear();
        foreach (var pair in normalizedComments)
            monthState.WeekComments[pair.Key] = pair.Value;

        if (monthState.DayValues.Count == 0 && monthState.WeekComments.Count == 0)
            _timeReports.Remove(monthKey);
    }

    private List<TimeReportMonthRecord> BuildTimeReportSettings()
    {
        var monthKeys = _timeReports.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var monthKey in monthKeys)
            PruneTimeReportMonth(monthKey);

        return _timeReportSettingsService.BuildRecords(_timeReports);
    }

    private void LoadTimeReportSettings(IEnumerable<TimeReportMonthRecord>? records)
    {
        _timeReports.Clear();
        foreach (var entry in _timeReportSettingsService.LoadStates(records))
            _timeReports[entry.Key] = entry.Value;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        _pluginAlarmTimer.Stop();
        SaveWindowSettings();
        if (!_sessionSaved)
            SaveSession(updateStatus: false);
    }

}
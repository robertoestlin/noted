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
using System.Runtime.InteropServices;
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
using System.Threading;
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
    private enum FancyBulletStyle
    {
        Dot,
        HollowCircle,
        Square,
        Diamond,
        Dash
    }

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
    private readonly AudioSessionSnapshotService _audioSessionSnapshotService = new();
    private readonly Dictionary<TabItem, TabDocument> _docs = new();
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _pluginAlarmTimer;
    private readonly DispatcherTimer _backupHeartbeatTimer;
    private DateTimeOffset _nextBackupHeartbeatAtLocal = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBackupHeartbeatAtLocal = DateTimeOffset.MinValue;
    private bool _uptimeHeartbeatStartupBeatWritten;
    private int _uptimeHeartbeatShutdownBeatWritten;
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
    private string _taskPanelTitle = DefaultTaskPanelTitle;
    private List<TaskAreaState> _taskAreas = [];
    private string _currentTaskAreaId = DefaultTaskAreaId;
    private bool _pluginAlarmsEnabled = true;
    private DateTime? _pluginAlarmsSnoozedUntilLocal;
    private List<ProjectLineCounterProject> _projectLineCounterProjects = [];
    private List<ProjectLineCounterType> _projectLineCounterTypes = [];
    private List<string> _projectLineCounterAutoDetectedFileTypes = [];
    private List<string> _projectLineCounterIgnoredFileTypes = [];
    private List<string> _projectLineCounterIgnoredFolders = [];
    private readonly HashSet<string> _triggeredPluginAlarmKeysForMinute = new(StringComparer.OrdinalIgnoreCase);
    private string _triggeredPluginAlarmMinuteKey = string.Empty;
    private double? _alarmPopupLeft;
    private double? _alarmPopupTop;

    private const string SettingsFileName = "settings.json";
    private const string ClosedTabsFileName = "closed-tabs.json";
    private const string SearchFilesHistoryFileName = "plugin-search-files-history.json";
    private const string TimeReportsFileName = "plugin-time-reports.json";
    private const string TodoItemsFileName = "todo-items.json";
    private const string StateConfigFileName = "state-config.json";
    private const string AppLogFileName = "noted.log";
    private const string DefaultTaskPanelTitle = "Task Panel";
    private const string DefaultTaskAreaId = "main";
    private const string DefaultTaskAreaName = "Main";
    private static readonly (string Id, string Name, string ShortcutKey, int CompletedRetentionDays, int CompletedRetentionHours)[] DefaultTaskGroups =
    [
        ("today", "Today", "+", 1, 0),
        ("this-week", "This Week", "W", 7, 0),
        ("this-month", "This Month", "M", 30, 0)
    ];
    private const int DefaultCompletedRetentionDays = 7;
    private const int DefaultCompletedRetentionHours = 0;
    private const int MinCompletedRetentionDays = 0;
    private const int MaxCompletedRetentionDays = 3650;
    private const int MinCompletedRetentionHours = 0;
    private const int MaxCompletedRetentionHours = 23;
    private const int MinUndoneMarkDays = 0;
    private const int MaxUndoneMarkDays = 3650;
    private const int MinUndoneMarkHours = 0;
    private const int MaxUndoneMarkHours = 23;
    private const int DefaultClosedTabsMaxCount = 10;
    private const int MinClosedTabsMaxCount = 1;
    private const int MaxClosedTabsMaxCount = 500;
    private const int DefaultClosedTabsRetentionDays = 0;
    private const int MinClosedTabsRetentionDays = 0;
    private const int MaxClosedTabsRetentionDays = 3650;

    private static string DefaultBackupFolder() => @"c:\tools\backup\noted";
    private static string DefaultCloudBackupFolder() => Path.Combine(DefaultBackupFolder(), "cloud");

    private string _backupFolder = DefaultBackupFolder();
    private string _cloudBackupFolder = DefaultCloudBackupFolder();
    private int _cloudSaveIntervalHours = 1;
    private int _cloudSaveIntervalMinutes = 0;
    private DateTime _lastCloudSaveUtc = DateTime.MinValue;
    private bool _backupAdditionalIncludeSettingsFile = true;
    private bool _backupAdditionalIncludeAppLog = true;
    private bool _backupAdditionalIncludeHeartbeatLogs = true;
    private bool _backupAdditionalIncludeTodoItems = true;
    private bool _backupAdditionalIncludeStateConfig = true;
    private bool _backupAdditionalIncludeSafePaste;
    private bool _backupAdditionalIncludeTimeReports = true;
    private bool _backupAdditionalIncludeMidiCustomSongs;
    private bool _backupAdditionalIncludeImages = true;
    private const int MaxBackups = 100;

    /// <summary>Filenames written by <see cref="SaveSession"/> (<c>noted_yyyyMMdd_HHmmss.txt</c>).</summary>
    private static readonly Regex NotedBackupFileNameRegex =
        new(@"^noted_\d{8}_\d{6}\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineAssigneeSuffixRegex =
        new(@"^(?<content>.*?)(?:\s*)\[(?<person>[^\[\]\r\n]+)\]\s*$", RegexOptions.Compiled);
    private const int DefaultAutoSaveSeconds = 30;
    private const int DefaultUptimeHeartbeatSeconds = 300;
    private const int DefaultInitialLines = 50;
    private const int DefaultVisualLineWrapColumn = 150;
    private const int MinVisualLineWrapColumn = 60;
    private const int MaxVisualLineWrapColumn = 400;
    private const int ControlArrowLineJump = 10;
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
    private const string DefaultShortcutToggleCriticalHighlight = "Ctrl+K";
    private const string DefaultShortcutGoToLine = "Ctrl+G";
    private const string DefaultShortcutGoToTab = "Ctrl+P";
    private const string DefaultShortcutMidiPlayer = "Ctrl+M";
    private const int DefaultMidiPlayerVolumePercent = 100;
    private const string DefaultShortcutSwitchToPreviousTab = "Ctrl+Q";
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
    private static readonly Color DefaultCriticalHighlightedLineColor = Color.FromRgb(255, 205, 210);
    private static readonly Color DefaultSelectedCriticalHighlightedLineColor = Color.FromRgb(255, 171, 177);
    private const string BundleDivider = "^---";
    private const string MetadataPrefix = "^meta^";
    private const string BackupImagesFolderName = "images";
    private const string DeletedImagesFolderName = "deleted";
    private const int MaxDeletedInlineImages = 100;
    private static readonly Regex ImageLineMarkerRegex =
        new(@"^\^<(?<file>[A-Za-z0-9][A-Za-z0-9._-]*\.png)(?:,(?<scale>[1-9][0-9]{0,2}))?>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HorizontalRuleLineRegex =
        new(@"^---$", RegexOptions.Compiled);
    private static readonly Regex FancyBulletPrefixRegex =
        new(@"^(?:-|\*)\s", RegexOptions.Compiled);
    private static readonly Regex SmileyTokenRegex =
        new(@":\)|;\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly int[] CloudMinuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];
    private int _initialLines = DefaultInitialLines;
    private int _uptimeHeartbeatSeconds = DefaultUptimeHeartbeatSeconds;
    private bool _writeUptimeHeartbeatInNoted = true;
    private bool _useStandaloneHeartbeatApp = false;
    private int _tabCleanupStaleDays = DefaultTabCleanupStaleDays;
    private int _closedTabsMaxCount = DefaultClosedTabsMaxCount;
    private int _closedTabsRetentionDays = DefaultClosedTabsRetentionDays;
    private char _saveBulletsAsMarker = '-';
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
    private string _shortcutToggleCriticalHighlight = DefaultShortcutToggleCriticalHighlight;
    private string _shortcutGoToLine = DefaultShortcutGoToLine;
    private string _shortcutGoToTab = DefaultShortcutGoToTab;
    private string _shortcutMidiPlayer = DefaultShortcutMidiPlayer;
    private int _midiPlayerVolumePercent = DefaultMidiPlayerVolumePercent;
    private Color _selectedLineColor = DefaultSelectedLineColor;
    private Color _highlightedLineColor = DefaultHighlightedLineColor;
    private Color _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
    private Color _criticalHighlightedLineColor = DefaultCriticalHighlightedLineColor;
    private Color _selectedCriticalHighlightedLineColor = DefaultSelectedCriticalHighlightedLineColor;
    private Brush _selectedLineBrush = CreateFrozenBrush(DefaultSelectedLineColor);
    private Brush _highlightedLineBrush = CreateFrozenBrush(DefaultHighlightedLineColor);
    private Brush _selectedHighlightedLineBrush = CreateFrozenBrush(DefaultSelectedHighlightedLineColor);
    private Brush _criticalHighlightedLineBrush = CreateFrozenBrush(DefaultCriticalHighlightedLineColor);
    private Brush _selectedCriticalHighlightedLineBrush = CreateFrozenBrush(DefaultSelectedCriticalHighlightedLineColor);
    private bool _isFridayFeelingEnabled = true;
    private bool _fancyBulletsEnabled;
    private bool _wrapLongLinesVisually = true;
    private int _visualLineWrapColumn = DefaultVisualLineWrapColumn;
    private bool _showSmileys = true;
    private bool _renderStyledTags = true;
    private bool _showLineAssignments = true;
    private bool _showBulletHoverTooltips = true;
    private bool _showHorizontalRuler = true;
    private bool _showInlineImages = true;
    private FancyBulletStyle _fancyBulletStyle = FancyBulletStyle.Dot;
    private bool _isFredagspartySessionEnabled = false;
    private bool _isFredagspartyTemporarilyDisabled;
    private ImageBrush? _fridayBackgroundBrush;
    private readonly List<KeyBinding> _shortcutBindings = [];
    private TabItem? _previousSelectedTab;
    private readonly Dictionary<string, BitmapSource> _inlineImageCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _referencedInlineImagesSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TabDocument, Stack<LineAssigneeUndoRecord>> _pendingShiftDeleteAssigneeUndo = [];
    private readonly Dictionary<TabDocument, Stack<HighlightLineUndoRecord>> _pendingShiftDeleteHighlightUndo = [];
    private readonly Dictionary<TabDocument, Stack<LineAssigneeChangeRecord>> _lineAssigneeUndoHistory = [];
    private readonly Dictionary<TabDocument, Stack<LineAssigneeChangeRecord>> _lineAssigneeRedoHistory = [];
    private CutLineMetadataTransfer? _pendingCutLineMetadataTransfer;
    private ToolTip? _assigneeHoverTooltip;
    private TabDocument? _assigneeHoverTooltipDoc;
    private string _assigneeHoverTooltipKey = string.Empty;

    private ToolTip? _bulletHoverTooltip;
    private TabDocument? _bulletHoverTooltipDoc;
    private string _bulletHoverTooltipKey = string.Empty;
    private bool _isInlineImageResizeActive;
    private TextEditor? _inlineImageResizeEditor;
    private int _inlineImageResizeLineNumber;
    private string _inlineImageResizeFileName = string.Empty;
    private double _inlineImageResizeStartX;
    private int _inlineImageResizeStartScalePercent;
    private int _inlineImageResizeCurrentScalePercent;
    private bool _isSelectionCursorClipActive;
    private TextEditor? _selectionCursorClipEditor;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorClipRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class LineAssigneeUndoRecord
    {
        public required IReadOnlyList<LineAssigneeUndoEntry> Entries { get; init; }
    }

    private sealed class LineAssigneeUndoEntry
    {
        public required int LineNumber { get; init; }
        public required string Person { get; init; }
        public required string LineText { get; init; }
        public DateTime? CreatedUtc { get; init; }
    }

    private sealed class LineAssigneeChangeRecord
    {
        public required IReadOnlyList<LineAssigneeChangeEntry> Entries { get; init; }
    }

    private sealed class LineAssigneeChangeEntry
    {
        public required int LineNumber { get; init; }
        public string? BeforePerson { get; init; }
        public string? AfterPerson { get; init; }
        public DateTime? BeforeCreatedUtc { get; init; }
        public DateTime? AfterCreatedUtc { get; init; }
    }

    private sealed class HighlightLineUndoRecord
    {
        public required IReadOnlyList<HighlightLineUndoEntry> Entries { get; init; }
    }

    private sealed class HighlightLineUndoEntry
    {
        public required int LineNumber { get; init; }
        public bool IsCritical { get; init; }
        public required string LineText { get; init; }
    }

    private sealed class CutLineMetadataTransfer
    {
        public required string ClipboardText { get; init; }
        public required IReadOnlyList<CutLineMetadataEntry> Entries { get; init; }
    }

    private sealed class CutLineMetadataEntry
    {
        public required int RelativeLineOffset { get; init; }
        public HighlightKind? Highlight { get; init; }
        public string? Assignee { get; init; }
        public DateTime? AssigneeCreatedUtc { get; init; }
    }

    private sealed class CutLineMetadataCapture
    {
        public required CutLineMetadataTransfer Transfer { get; init; }
        public required IReadOnlyList<int> FullyCutLines { get; init; }
    }

    private enum HighlightKind
    {
        Normal = 0,
        Critical = 1
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClipCursor(ref CursorClipRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClipCursor(IntPtr rect);

    private static readonly RoutedUICommand RenameTabCommand = new("Rename Tab", nameof(RenameTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ReopenClosedTabCommand = new("Reopen Closed Tab", nameof(ReopenClosedTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand AddBlankLinesCommand = new("Add Blank Lines", nameof(AddBlankLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand TrimTrailingEmptyLinesCommand = new("Trim Trailing Empty Lines", nameof(TrimTrailingEmptyLinesCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleHighlightCommand = new("Toggle Highlight", nameof(ToggleHighlightCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleCriticalHighlightCommand = new("Toggle Critical Highlight", nameof(ToggleCriticalHighlightCommand), typeof(MainWindow));
    private static readonly RoutedUICommand GoToLineCommand = new("Go To Line", nameof(GoToLineCommand), typeof(MainWindow));
    private static readonly RoutedUICommand GoToTabCommand = new("Go To Tab", nameof(GoToTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand SwitchToPreviousTabCommand = new("Switch To Previous Tab", nameof(SwitchToPreviousTabCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleTodoPanelCommand = new("Toggle Todo Panel", nameof(ToggleTodoPanelCommand), typeof(MainWindow));
    private static readonly RoutedUICommand ToggleMidiPlayerCommand = new("Toggle MIDI Player", nameof(ToggleMidiPlayerCommand), typeof(MainWindow));
    private static readonly RoutedUICommand FakeSaveCommand = new("Fake Save", nameof(FakeSaveCommand), typeof(MainWindow));
    private static readonly (FancyBulletStyle Style, string Label)[] FancyBulletStyleOptions =
    [
        (FancyBulletStyle.Dot, "Filled dot"),
        (FancyBulletStyle.HollowCircle, "Hollow circle"),
        (FancyBulletStyle.Square, "Square"),
        (FancyBulletStyle.Diamond, "Diamond"),
        (FancyBulletStyle.Dash, "Dash")
    ];
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
        private readonly Func<IReadOnlyCollection<int>> _criticalLineProvider;
        private readonly Func<IReadOnlyDictionary<int, (string Person, DateTime? CreatedUtc)>> _assigneeProvider;
        private readonly Func<string, Color> _assigneeColorProvider;
        private readonly Action<TabDocument.AssigneeBadgeBounds>? _badgeRecorder;
        private readonly Action? _badgeRecorderReset;
        private readonly Func<int, bool> _lineSelectionProvider;
        private readonly Func<int> _caretLineProvider;
        private readonly Func<Brush> _selectedLineBrushProvider;
        private readonly Func<Brush> _normalBrushProvider;
        private readonly Func<Brush> _selectedBrushProvider;
        private readonly Func<Brush> _criticalBrushProvider;
        private readonly Func<Brush> _selectedCriticalBrushProvider;
        private readonly Func<bool> _showHorizontalRuleProvider;
        private readonly Func<bool> _fancyBulletsEnabledProvider;
        private readonly Func<bool> _showSmileysProvider;
        private readonly Func<FancyBulletStyle> _fancyBulletStyleProvider;
        private readonly Func<bool> _showStyledTagsProvider;
        private readonly Func<bool> _showLineAssignmentsProvider;
        private static readonly Brush HorizontalRuleSelectedBandBrush = CreateFrozenBrush(Color.FromArgb(96, 198, 235, 255));
        private static readonly Pen CaretLineBorderPen = CreateFrozenPen(Color.FromArgb(70, 60, 160, 80), 1.5);
        private static readonly Pen HorizontalRulePen = CreateFrozenPen(Color.FromRgb(184, 193, 204), 1.2);
        private static readonly Pen HorizontalRuleAccentPen = CreateFrozenPen(Color.FromRgb(229, 233, 240), 0.8);
        private static readonly Pen HorizontalRuleSelectedPen = CreateFrozenPen(Color.FromRgb(63, 154, 214), 1.8);
        private static readonly Brush SmileyFaceBrush = CreateFrozenBrush(Color.FromRgb(255, 213, 79));
        private static readonly Pen SmileyFaceOutlinePen = CreateFrozenPen(Color.FromRgb(191, 142, 43), 1.1);
        private static readonly Brush SmileyFeatureBrush = CreateFrozenBrush(Color.FromRgb(74, 55, 21));
        private static readonly Pen SmileyFeaturePen = CreateFrozenPen(Color.FromRgb(74, 55, 21), 1.1);
        private static readonly Pen SmileyMouthPen = CreateFrozenPen(Color.FromRgb(74, 55, 21), 1.25);
        private static readonly Brush SmileyHighlightBrush = CreateFrozenBrush(Color.FromArgb(145, 255, 244, 196));
        private static readonly Brush SmileyBlushBrush = CreateFrozenBrush(Color.FromArgb(120, 255, 153, 153));
        private static readonly Brush SmileyGrinFillBrush = CreateFrozenBrush(Color.FromRgb(120, 68, 47));
        private static readonly Pen SmileyGrinPen = CreateFrozenPen(Color.FromRgb(74, 55, 21), 0.9);
        private static readonly Brush SmileyTeethBrush = CreateFrozenBrush(Color.FromRgb(250, 250, 246));
        private static readonly Brush SmileyTearBrush = CreateFrozenBrush(Color.FromRgb(76, 182, 255));
        private static readonly Pen SmileyTearPen = CreateFrozenPen(Color.FromRgb(41, 132, 196), 0.85);
        private static readonly Brush TagPillFillBrush = CreateTagPillFillBrush();
        private static readonly Pen TagPillBorderPen = CreateFrozenPen(Color.FromRgb(92, 148, 206), 1.05);
        private static readonly Brush TagPillHashBrush = CreateFrozenBrush(Color.FromRgb(88, 118, 158));
        private static readonly Brush TagPillNameBrush = CreateFrozenBrush(Color.FromRgb(18, 58, 102));

        private static Brush CreateTagPillFillBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(252, 254, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(232, 244, 255), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(214, 232, 252), 1));
            brush.Freeze();
            return brush;
        }

        public HighlightLineRenderer(
            Func<IReadOnlyCollection<int>> lineProvider,
            Func<IReadOnlyCollection<int>> criticalLineProvider,
            Func<IReadOnlyDictionary<int, (string Person, DateTime? CreatedUtc)>> assigneeProvider,
            Func<string, Color> assigneeColorProvider,
            Func<int, bool> lineSelectionProvider,
            Func<int> caretLineProvider,
            Func<Brush> selectedLineBrushProvider,
            Func<Brush> normalBrushProvider,
            Func<Brush> selectedBrushProvider,
            Func<Brush> criticalBrushProvider,
            Func<Brush> selectedCriticalBrushProvider,
            Func<bool> showHorizontalRuleProvider,
            Func<bool> fancyBulletsEnabledProvider,
            Func<bool> showSmileysProvider,
            Func<FancyBulletStyle> fancyBulletStyleProvider,
            Func<bool> showStyledTagsProvider,
            Func<bool> showLineAssignmentsProvider,
            Action? badgeRecorderReset = null,
            Action<TabDocument.AssigneeBadgeBounds>? badgeRecorder = null)
        {
            _lineProvider = lineProvider;
            _criticalLineProvider = criticalLineProvider;
            _assigneeProvider = assigneeProvider;
            _assigneeColorProvider = assigneeColorProvider;
            _lineSelectionProvider = lineSelectionProvider;
            _caretLineProvider = caretLineProvider;
            _selectedLineBrushProvider = selectedLineBrushProvider;
            _normalBrushProvider = normalBrushProvider;
            _selectedBrushProvider = selectedBrushProvider;
            _criticalBrushProvider = criticalBrushProvider;
            _selectedCriticalBrushProvider = selectedCriticalBrushProvider;
            _showHorizontalRuleProvider = showHorizontalRuleProvider;
            _fancyBulletsEnabledProvider = fancyBulletsEnabledProvider;
            _showSmileysProvider = showSmileysProvider;
            _fancyBulletStyleProvider = fancyBulletStyleProvider;
            _showStyledTagsProvider = showStyledTagsProvider;
            _showLineAssignmentsProvider = showLineAssignmentsProvider;
            _badgeRecorderReset = badgeRecorderReset;
            _badgeRecorder = badgeRecorder;
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

        private static Pen CreateFrozenPen(Color color, double thickness)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        // Draw on selection layer so highlight remains visible even when selected.
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            // textView.ActualWidth is the document content width (sized to the longest line),
            // NOT the visible viewport width. Use IScrollInfo to get the real viewport width
            // and cover from x=0 to max(content, viewport) so every line spans the window.
            double drawWidth = textView.ActualWidth;
            if (textView is IScrollInfo si)
                drawWidth = Math.Max(drawWidth, si.HorizontalOffset + si.ViewportWidth);

            var highlightedLines = _lineProvider();
            var highlightedLineSet = highlightedLines.Count > 0
                ? new HashSet<int>(highlightedLines)
                : [];
            var criticalHighlightedLines = _criticalLineProvider();
            var criticalHighlightedLineSet = criticalHighlightedLines.Count > 0
                ? new HashSet<int>(criticalHighlightedLines)
                : [];

            // Paint selected/caret and highlighted rows using full editor width so the
            // background remains consistent even when line content is short.
            foreach (var visualLine in textView.VisualLines)
            {
                var line = visualLine.FirstDocumentLine;
                if (line == null)
                    continue;

                bool isHighlighted = highlightedLineSet.Contains(line.LineNumber);
                bool isCriticalHighlighted = criticalHighlightedLineSet.Contains(line.LineNumber);
                bool isSelected = _lineSelectionProvider(line.LineNumber);
                if (!isHighlighted && !isCriticalHighlighted && !isSelected)
                    continue;

                var segment = new TextSegment
                {
                    StartOffset = line.Offset,
                    EndOffset = line.Offset + line.Length
                };

                var lineRects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();
                if (lineRects.Count == 0)
                    continue;

                var brush = isCriticalHighlighted
                    ? (isSelected ? _selectedCriticalBrushProvider() : _criticalBrushProvider())
                    : isHighlighted
                        ? (isSelected ? _selectedBrushProvider() : _normalBrushProvider())
                        : _selectedLineBrushProvider();

                bool isCaretLine = line.LineNumber == _caretLineProvider();

                foreach (var rect in lineRects)
                {
                    var fullWidthRect = new Rect(0, rect.Top, drawWidth, rect.Height);
                    drawingContext.DrawRectangle(brush, null, fullWidthRect);

                    if (isCaretLine)
                    {
                        // Inset top/bottom by half the pen thickness so the border stays
                        // inside the rect vertically; span the full draw width horizontally.
                        const double inset = 0.75;
                        var borderRect = new Rect(0, rect.Top + inset, drawWidth, rect.Height - inset * 2);
                        drawingContext.DrawRectangle(null, CaretLineBorderPen, borderRect);
                    }
                }
            }

            bool showHorizontalRule = _showHorizontalRuleProvider();
            bool showFancyBullets = _fancyBulletsEnabledProvider();
            var fancyBulletColor = SystemColors.ControlTextColor;
            var fancyBulletFillBrush = CreateFrozenBrush(fancyBulletColor);
            var fancyBulletOutlinePen = CreateFrozenPen(fancyBulletColor, 1.25);
            var fancyBulletDashPen = CreateFrozenPen(fancyBulletColor, 1.5);
            if (showHorizontalRule || showFancyBullets)
            {
                foreach (var visualLine in textView.VisualLines)
                {
                    var line = visualLine.FirstDocumentLine;
                    if (line == null || line.Length <= 0)
                        continue;

                    var lineText = textView.Document.GetText(line.Offset, line.Length);
                    if (showHorizontalRule && IsHorizontalRuleLine(lineText))
                    {
                        var lineSegment = new TextSegment
                        {
                            StartOffset = line.Offset,
                            EndOffset = line.Offset + line.Length
                        };

                        var lineRects = BackgroundGeometryBuilder.GetRectsForSegment(textView, lineSegment).ToList();
                        if (lineRects.Count == 0)
                            continue;

                        var lineRect = lineRects[0];
                        bool isSelected = _lineSelectionProvider(line.LineNumber);
                        if (isSelected)
                        {
                            var bandRect = new Rect(0, lineRect.Top, drawWidth, lineRect.Height);
                            drawingContext.DrawRectangle(HorizontalRuleSelectedBandBrush, null, bandRect);
                        }

                        double y = lineRect.Top + (lineRect.Height / 2);
                        double xStart = lineRects.Min(rect => rect.Left);
                        double xEnd = Math.Max(xStart + 24, drawWidth - 10);
                        drawingContext.DrawLine(isSelected ? HorizontalRuleSelectedPen : HorizontalRulePen, new Point(xStart, y), new Point(xEnd, y));
                        if (!isSelected)
                            drawingContext.DrawLine(HorizontalRuleAccentPen, new Point(xStart, y - 1), new Point(xEnd, y - 1));
                    }

                    if (!showFancyBullets)
                        continue;

                    if (!TryGetFancyBulletPrefixLength(lineText, out int prefixLength))
                        continue;

                    var bulletSegment = new TextSegment
                    {
                        StartOffset = line.Offset,
                        EndOffset = line.Offset + prefixLength
                    };
                    var bulletRects = BackgroundGeometryBuilder.GetRectsForSegment(textView, bulletSegment).ToList();
                    if (bulletRects.Count == 0)
                        continue;

                    var bulletRect = bulletRects[0];
                    double bulletCenterY = bulletRect.Top + (bulletRect.Height / 2.0);
                    double radius = Math.Max(2.0, Math.Min(3.5, bulletRect.Height * 0.18));
                    double centerX = bulletRect.Left + (radius * 2.2);
                    DrawFancyBullet(
                        drawingContext,
                        _fancyBulletStyleProvider(),
                        centerX,
                        bulletCenterY,
                        radius,
                        fancyBulletFillBrush,
                        fancyBulletOutlinePen,
                        fancyBulletDashPen);
                }
            }

            if (_showSmileysProvider())
            {
                foreach (var visualLine in textView.VisualLines)
                {
                    var line = visualLine.FirstDocumentLine;
                    if (line == null || line.Length <= 0)
                        continue;

                    var lineText = textView.Document.GetText(line.Offset, line.Length);
                    foreach (var token in EnumerateSmileyTokens(lineText))
                    {
                        var segment = new TextSegment
                        {
                            StartOffset = line.Offset + token.StartIndex,
                            EndOffset = line.Offset + token.StartIndex + token.Length
                        };
                        var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();
                        if (rects.Count == 0)
                            continue;

                        DrawSmileyIcon(drawingContext, rects[0], token.Glyph);
                    }
                }
            }

            if (_showStyledTagsProvider())
            {
                double tagPixelsPerDip = VisualTreeHelper.GetDpi(textView).PixelsPerDip;
                foreach (var visualLine in textView.VisualLines)
                {
                    var line = visualLine.FirstDocumentLine;
                    if (line == null || line.Length <= 0)
                        continue;

                    var lineText = textView.Document.GetText(line.Offset, line.Length);
                    foreach (var token in EnumerateTagTokens(lineText))
                    {
                        var segment = new TextSegment
                        {
                            StartOffset = line.Offset + token.StartIndex,
                            EndOffset = line.Offset + token.StartIndex + token.Length
                        };
                        var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();
                        if (rects.Count == 0)
                            continue;

                        DrawTagPill(drawingContext, rects[0], token.Name, tagPixelsPerDip);
                    }
                }
            }

            // Always reset the recorder cache (even when assignments are hidden), so a stale
            // hover from a prior render doesn't fire a tooltip after the badges disappear.
            _badgeRecorderReset?.Invoke();

            if (!_showLineAssignmentsProvider())
                return;

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

                var person = pair.Value.Person?.Trim();
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

                // For wrapped/multiline content, keep the assignee badge on the last visual line.
                var targetRect = lineRects
                    .OrderBy(rect => rect.Top)
                    .ThenBy(rect => rect.Left)
                    .Last();

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
                double badgeHeight = Math.Max(16, targetRect.Height - 2);
                double badgeWidth = formattedText.WidthIncludingTrailingWhitespace + (paddingX * 2);
                double lineEndX = targetRect.Right;
                double x = Math.Max(0, Math.Min(drawWidth - badgeWidth - 4, lineEndX + 14));
                double y = targetRect.Top + Math.Max(0, (targetRect.Height - badgeHeight) / 2);

                var badgeRect = new Rect(x, y, badgeWidth, badgeHeight);
                drawingContext.DrawRoundedRectangle(style.Background, style.Border, badgeRect, 4, 4);
                drawingContext.DrawText(formattedText, new Point(x + paddingX, y + Math.Max(0, (badgeHeight - formattedText.Height) / 2)));

                _badgeRecorder?.Invoke(new TabDocument.AssigneeBadgeBounds
                {
                    Bounds = badgeRect,
                    LineNumber = lineNumber,
                    Person = person,
                    CreatedUtc = pair.Value.CreatedUtc
                });
            }
        }

        private static void DrawFancyBullet(
            DrawingContext drawingContext,
            FancyBulletStyle style,
            double centerX,
            double centerY,
            double radius,
            Brush bulletFillBrush,
            Pen bulletOutlinePen,
            Pen bulletDashPen)
        {
            switch (style)
            {
                case FancyBulletStyle.HollowCircle:
                    drawingContext.DrawEllipse(null, bulletOutlinePen, new Point(centerX, centerY), radius, radius);
                    break;
                case FancyBulletStyle.Square:
                {
                    var square = new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2);
                    drawingContext.DrawRectangle(bulletFillBrush, null, square);
                    break;
                }
                case FancyBulletStyle.Diamond:
                {
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(centerX, centerY - radius), true, true);
                        ctx.LineTo(new Point(centerX + radius, centerY), true, false);
                        ctx.LineTo(new Point(centerX, centerY + radius), true, false);
                        ctx.LineTo(new Point(centerX - radius, centerY), true, false);
                    }
                    geometry.Freeze();
                    drawingContext.DrawGeometry(bulletFillBrush, null, geometry);
                    break;
                }
                case FancyBulletStyle.Dash:
                    drawingContext.DrawLine(
                        bulletDashPen,
                        new Point(centerX - (radius * 1.7), centerY),
                        new Point(centerX + (radius * 1.7), centerY));
                    break;
                case FancyBulletStyle.Dot:
                default:
                    drawingContext.DrawEllipse(bulletFillBrush, null, new Point(centerX, centerY), radius, radius);
                    break;
            }
        }

        private static void DrawTagPill(DrawingContext drawingContext, Rect tokenRect, string tagName, double pixelsPerDip)
        {
            string label = FormatTagLabelForDisplay(tagName);
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.SemiBold,
                FontStretches.Normal);

            // Match assignee badges: stay within the visual line (line height minus small inset).
            double lineH = Math.Max(1, tokenRect.Height);
            double pillHeight = Math.Max(2, lineH - 2);
            double yLine = tokenRect.Top + Math.Max(0, (lineH - pillHeight) / 2);

            const double padX = 6;
            string namePortion = label.Length > 1 ? label[1..] : string.Empty;

            double fontSize = Math.Min(11, Math.Max(6.5, pillHeight * 0.72));
            FormattedText ftHash;
            FormattedText ftName;
            double textHeight;
            while (true)
            {
                ftHash = new FormattedText(
                    "#",
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    TagPillHashBrush,
                    pixelsPerDip);
                ftName = new FormattedText(
                    namePortion,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    TagPillNameBrush,
                    pixelsPerDip);
                textHeight = Math.Max(ftHash.Height, ftName.Height);
                if (textHeight <= pillHeight - 1 || fontSize <= 6)
                    break;
                fontSize = Math.Max(6, fontSize - 0.35);
            }

            double textWidth = ftHash.WidthIncludingTrailingWhitespace + ftName.WidthIncludingTrailingWhitespace;
            double width = textWidth + (padX * 2);
            double x = tokenRect.Left;
            var pill = new Rect(x, yLine, width, pillHeight);
            double corner = Math.Min(4, Math.Max(2, pillHeight * 0.22));
            drawingContext.DrawRoundedRectangle(TagPillFillBrush, TagPillBorderPen, pill, corner, corner);

            double textY = yLine + Math.Max(0, (pillHeight - textHeight) / 2);
            drawingContext.DrawText(ftHash, new Point(x + padX, textY));
            drawingContext.DrawText(ftName, new Point(x + padX + ftHash.WidthIncludingTrailingWhitespace, textY));
        }

        private static void DrawSmileyIcon(DrawingContext drawingContext, Rect tokenRect, string glyph)
        {
            double size = Math.Min(tokenRect.Width, tokenRect.Height) * 0.9;
            size = Math.Max(10, Math.Min(16, size));
            double radius = size / 2.0;
            double centerX = tokenRect.Left + (tokenRect.Width / 2.0);
            double centerY = tokenRect.Top + (tokenRect.Height / 2.0);

            drawingContext.DrawEllipse(SmileyFaceBrush, SmileyFaceOutlinePen, new Point(centerX, centerY), radius, radius);
            drawingContext.DrawEllipse(
                SmileyHighlightBrush,
                null,
                new Point(centerX - (radius * 0.3), centerY - (radius * 0.35)),
                radius * 0.33,
                radius * 0.25);

            double eyeOffsetX = radius * 0.42;
            double eyeOffsetY = radius * 0.28;
            double eyeRadius = Math.Max(0.85, radius * 0.1);
            var leftEye = new Point(centerX - eyeOffsetX, centerY - eyeOffsetY);
            var rightEye = new Point(centerX + eyeOffsetX, centerY - eyeOffsetY);

            if (glyph == "😉")
            {
                DrawWinkEye(drawingContext, leftEye, radius * 0.26, angleDegrees: -14);
                drawingContext.DrawEllipse(SmileyFeatureBrush, null, rightEye, eyeRadius * 1.18, eyeRadius * 1.18);
            }
            else if (glyph == "😂")
            {
                DrawXEye(drawingContext, leftEye, radius * 0.2);
                DrawXEye(drawingContext, rightEye, radius * 0.2);
            }
            else
            {
                drawingContext.DrawEllipse(SmileyFeatureBrush, null, leftEye, eyeRadius, eyeRadius);
                drawingContext.DrawEllipse(SmileyFeatureBrush, null, rightEye, eyeRadius, eyeRadius);
            }

            if (glyph == "😄")
            {
                DrawGrinMouth(drawingContext, centerX, centerY + (radius * 0.28), radius * 0.56, radius * 0.32);
            }
            else if (glyph == "😂")
            {
                DrawGrinMouth(drawingContext, centerX, centerY + (radius * 0.28), radius * 0.58, radius * 0.36);
                DrawTearDrop(drawingContext, new Point(centerX - (radius * 0.62), centerY + (radius * 0.22)), radius * 0.2);
                DrawTearDrop(drawingContext, new Point(centerX + (radius * 0.62), centerY + (radius * 0.22)), radius * 0.2);
            }
            else
            {
                DrawSmileMouth(drawingContext, centerX, centerY + (radius * 0.28), radius * 0.56, radius * 0.34);
            }

            drawingContext.DrawEllipse(SmileyBlushBrush, null, new Point(centerX - (radius * 0.46), centerY + (radius * 0.24)), radius * 0.17, radius * 0.12);
            drawingContext.DrawEllipse(SmileyBlushBrush, null, new Point(centerX + (radius * 0.46), centerY + (radius * 0.24)), radius * 0.17, radius * 0.12);
        }

        private static void DrawWinkEye(DrawingContext drawingContext, Point center, double halfWidth, double angleDegrees)
        {
            double radians = angleDegrees * (Math.PI / 180.0);
            double dx = Math.Cos(radians) * halfWidth;
            double dy = Math.Sin(radians) * halfWidth;
            drawingContext.DrawLine(SmileyFeaturePen, new Point(center.X - dx, center.Y - dy), new Point(center.X + dx, center.Y + dy));
        }

        private static void DrawXEye(DrawingContext drawingContext, Point center, double halfSize)
        {
            drawingContext.DrawLine(SmileyFeaturePen, new Point(center.X - halfSize, center.Y - halfSize), new Point(center.X + halfSize, center.Y + halfSize));
            drawingContext.DrawLine(SmileyFeaturePen, new Point(center.X - halfSize, center.Y + halfSize), new Point(center.X + halfSize, center.Y - halfSize));
        }

        private static void DrawSmileMouth(DrawingContext drawingContext, double centerX, double centerY, double halfWidth, double height)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var start = new Point(centerX - halfWidth, centerY);
                var end = new Point(centerX + halfWidth, centerY);
                var control = new Point(centerX, centerY + height);
                ctx.BeginFigure(start, false, false);
                ctx.QuadraticBezierTo(control, end, true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(null, SmileyMouthPen, geometry);
        }

        private static void DrawGrinMouth(DrawingContext drawingContext, double centerX, double centerY, double halfWidth, double height)
        {
            var mouthRect = new Rect(centerX - halfWidth, centerY - (height * 0.55), halfWidth * 2, height * 1.45);
            drawingContext.DrawRoundedRectangle(SmileyGrinFillBrush, SmileyGrinPen, mouthRect, height * 0.55, height * 0.55);

            var teethRect = new Rect(
                mouthRect.Left + (mouthRect.Width * 0.12),
                mouthRect.Top + (mouthRect.Height * 0.14),
                mouthRect.Width * 0.76,
                mouthRect.Height * 0.28);
            drawingContext.DrawRoundedRectangle(SmileyTeethBrush, null, teethRect, height * 0.2, height * 0.2);
        }

        private static void DrawTearDrop(DrawingContext drawingContext, Point center, double size)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var top = new Point(center.X, center.Y - size);
                var bottom = new Point(center.X, center.Y + size);
                var left = new Point(center.X - (size * 0.72), center.Y);
                var right = new Point(center.X + (size * 0.72), center.Y);
                ctx.BeginFigure(top, true, true);
                ctx.BezierTo(new Point(top.X - (size * 0.52), top.Y + (size * 0.2)), left, bottom, true, false);
                ctx.BezierTo(right, new Point(top.X + (size * 0.52), top.Y + (size * 0.2)), top, true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(SmileyTearBrush, SmileyTearPen, geometry);
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

    private readonly record struct SmileyToken(int StartIndex, int Length, string Glyph);

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

    private sealed class HorizontalRuleTextMaskingTransformer : DocumentColorizingTransformer
    {
        private readonly Func<bool> _enabledProvider;

        public HorizontalRuleTextMaskingTransformer(Func<bool> enabledProvider)
            => _enabledProvider = enabledProvider;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_enabledProvider())
                return;

            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var lineText = CurrentContext.Document.GetText(line.Offset, line.Length);
            if (!IsHorizontalRuleLine(lineText))
                return;

            ChangeLinePart(line.Offset, line.EndOffset, visualElement =>
            {
                visualElement.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
            });
        }
    }

    private sealed class FancyBulletTextMaskingTransformer : DocumentColorizingTransformer
    {
        private readonly Func<bool> _enabledProvider;

        public FancyBulletTextMaskingTransformer(Func<bool> enabledProvider)
            => _enabledProvider = enabledProvider;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_enabledProvider())
                return;

            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var lineText = CurrentContext.Document.GetText(line.Offset, line.Length);
            if (!TryGetFancyBulletPrefixLength(lineText, out int prefixLength))
                return;

            ChangeLinePart(line.Offset, line.Offset + prefixLength, visualElement =>
            {
                visualElement.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
            });
        }
    }

    private sealed class SmileyTextMaskingTransformer : DocumentColorizingTransformer
    {
        private readonly Func<bool> _enabledProvider;

        public SmileyTextMaskingTransformer(Func<bool> enabledProvider)
            => _enabledProvider = enabledProvider;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_enabledProvider())
                return;

            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var lineText = CurrentContext.Document.GetText(line.Offset, line.Length);
            foreach (var token in EnumerateSmileyTokens(lineText))
            {
                ChangeLinePart(line.Offset + token.StartIndex, line.Offset + token.StartIndex + token.Length, visualElement =>
                {
                    visualElement.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                });
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
        CommandBindings.Add(new CommandBinding(ToggleCriticalHighlightCommand, (_, _) => ExecuteToggleCriticalHighlight()));
        CommandBindings.Add(new CommandBinding(GoToLineCommand, (_, _) => ExecuteGoToLine()));
        CommandBindings.Add(new CommandBinding(GoToTabCommand, (_, _) => ExecuteGoToTab()));
        CommandBindings.Add(new CommandBinding(SwitchToPreviousTabCommand, (_, _) => ExecuteSwitchToPreviousTab()));
        CommandBindings.Add(new CommandBinding(ToggleTodoPanelCommand, (_, _) => ExecuteToggleTodoPanel()));
        CommandBindings.Add(new CommandBinding(ToggleMidiPlayerCommand, (_, _) => ToggleMidiPlayer()));
        CommandBindings.Add(new CommandBinding(FakeSaveCommand, (_, _) => ExecuteFakeSaveShortcut()));

        MainTabControl.AllowDrop = true;
        MainTabControl.DragOver += MainTabControl_DragOver;
        MainTabControl.Drop += MainTabControl_Drop;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Deactivated += (_, _) => EndSelectionCursorClip();
        LocationChanged += (_, _) => RefreshSelectionCursorClipBounds();
        SizeChanged += (_, _) => RefreshSelectionCursorClipBounds();
        StateChanged += (_, _) => RefreshSelectionCursorClipBounds();

        // Auto-save timer
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DefaultAutoSaveSeconds) };
        _autoSaveTimer.Tick += (_, _) => SaveSession();
        _autoSaveTimer.Start();
        _pluginAlarmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _pluginAlarmTimer.Tick += (_, _) => CheckPluginAlarms();
        _backupHeartbeatTimer = new DispatcherTimer();
        _backupHeartbeatTimer.Tick += (_, _) => HandleBackupHeartbeatTick();

        // Restore window position/size, then session
        LoadWindowSettings();
        UpdateAlarmSnoozeStatus();
        InitializeTodoPanel();
        UpdateViewMenuChecks();
        _pluginAlarmTimer.Start();
        StartBackupHeartbeatTimer();
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

        // Discard any "previous tab" captured from startup-triggered selection
        // changes so Ctrl+Q does nothing until the user actually switches tabs.
        _previousSelectedTab = null;
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
            () => GetCriticalHighlightedLineNumbers(doc),
            () => GetLineAssigneeDetails(doc),
            person => GetUserColor(person),
            line => IsLineSelected(doc.Editor, line),
            () => doc.Editor.TextArea.Caret.Line,
            () => _selectedLineBrush,
            () => _highlightedLineBrush,
            () => _selectedHighlightedLineBrush,
            () => _criticalHighlightedLineBrush,
            () => _selectedCriticalHighlightedLineBrush,
            () => _showHorizontalRuler,
            () => _fancyBulletsEnabled,
            () => _showSmileys,
            () => _fancyBulletStyle,
            () => _renderStyledTags,
            () => _showLineAssignments,
            badgeRecorderReset: () => doc.AssigneeBadgeBoundsCache.Clear(),
            badgeRecorder: bounds => doc.AssigneeBadgeBoundsCache.Add(bounds));
        doc.HighlightRenderer = highlightRenderer;
        editor.TextArea.TextView.BackgroundRenderers.Add(highlightRenderer);

        // Wire events
        editor.TextChanged += (_, _) =>
        {
            doc.CachedText = editor.Text;
            doc.LastChangedUtc = DateTime.UtcNow;
            MarkDirty(doc);
            RedrawHighlight(doc);
            HideAssigneeHoverTooltip();
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            UpdateStatusBar(doc);
            RedrawHighlight(doc);
        };
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => HideAssigneeHoverTooltip();
        editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
        editor.PreviewKeyDown += (_, e) => HandleEditorPreviewKeyDown(doc, e);
        editor.PreviewMouseRightButtonDown += (_, e) => MoveCaretToMousePosition(editor, e);
        editor.PreviewMouseLeftButtonDown += (_, e) => BeginSelectionCursorClip(editor, e);
        editor.PreviewMouseMove += (_, e) =>
        {
            UpdateSelectionCursorClip(editor, e);
            UpdateAssigneeHoverTooltip(doc, e);
            UpdateBulletHoverTooltip(doc, e);
        };
        editor.PreviewMouseLeftButtonUp += (_, _) => EndSelectionCursorClip(editor);
        editor.LostMouseCapture += (_, _) => EndSelectionCursorClip(editor);
        editor.MouseLeave += (_, _) =>
        {
            HideAssigneeHoverTooltip();
            HideBulletHoverTooltip();
        };
        editor.TextArea.TextEntered += (_, e) => HandleTagHashTextEntered(doc, e);
        editor.TextArea.PreviewTextInput += (_, e) => HandleTagWhitespaceInputAsHyphen(doc, e);

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
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4)
        };
        editor.TextArea.TextView.Margin = new Thickness(8, 0, 0, 0);
        editor.Options.HighlightCurrentLine = false;
        editor.TextArea.TextView.ElementGenerators.Add(new ImageLineElementGenerator(
            (line, marker) => CreateInlineImageElement(editor, line.LineNumber, marker),
            marker => _showInlineImages && CanRenderInlineImageLine(marker)));
        editor.TextArea.TextView.LineTransformers.Add(new HorizontalRuleTextMaskingTransformer(() => _showHorizontalRuler));
        editor.TextArea.TextView.LineTransformers.Add(new FancyBulletTextMaskingTransformer(() => _fancyBulletsEnabled));
        editor.TextArea.TextView.LineTransformers.Add(new SmileyTextMaskingTransformer(() => _showSmileys));
        editor.TextArea.TextView.LineTransformers.Add(new TagTextMaskingTransformer(() => _renderStyledTags));
        // AvalonEdit enables hyperlinks by default (Ctrl+click). Intercept before it uses Process.Start with an arbitrary URI.
        editor.AddHandler(Hyperlink.RequestNavigateEvent, (RequestNavigateEventHandler)((_, e) =>
        {
            e.Handled = true;
            SafeHttpUriLauncher.TryOpenHyperlinkUri(e.Uri);
        }));
        EnableJsonSyntaxHighlighting(editor);
        editor.ContextMenu = BuildEditorContextMenu(editor);
        ApplyFridayBackgroundToEditor(editor);
        ApplyVisualLineWrapSettings(editor);
        return editor;
    }

    private void BeginSelectionCursorClip(TextEditor editor, MouseButtonEventArgs e)
    {
        if (_isInlineImageResizeActive || e.ChangedButton != MouseButton.Left)
            return;

        if (e.OriginalSource is not DependencyObject source || !IsWithinTextArea(editor, source))
            return;

        _selectionCursorClipEditor = editor;
        _isSelectionCursorClipActive = true;
        RefreshSelectionCursorClipBounds();
    }

    private void UpdateSelectionCursorClip(TextEditor editor, MouseEventArgs e)
    {
        if (!_isSelectionCursorClipActive || !ReferenceEquals(_selectionCursorClipEditor, editor))
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndSelectionCursorClip(editor);
            return;
        }

        RefreshSelectionCursorClipBounds();
    }

    private static bool IsWithinTextArea(TextEditor editor, DependencyObject source)
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, editor.TextArea))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void RefreshSelectionCursorClipBounds()
    {
        if (!_isSelectionCursorClipActive)
            return;

        if (!TryGetWindowScreenBounds(out var clipRect))
        {
            EndSelectionCursorClip();
            return;
        }

        ClipCursor(ref clipRect);
    }

    private bool TryGetWindowScreenBounds(out CursorClipRect clipRect)
    {
        clipRect = default;
        if (!IsLoaded || WindowState == WindowState.Minimized || ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        int left = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
        int top = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
        int right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X));
        int bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y));
        if (right <= left || bottom <= top)
            return false;

        clipRect = new CursorClipRect
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        return true;
    }

    private void EndSelectionCursorClip(TextEditor? editor = null)
    {
        if (!_isSelectionCursorClipActive)
            return;

        if (editor != null && _selectionCursorClipEditor != null && !ReferenceEquals(editor, _selectionCursorClipEditor))
            return;

        ClipCursor(IntPtr.Zero);
        _isSelectionCursorClipActive = false;
        _selectionCursorClipEditor = null;
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

    private static bool IsHorizontalRuleLine(string lineText)
    {
        if (string.IsNullOrWhiteSpace(lineText))
            return false;

        return HorizontalRuleLineRegex.IsMatch(lineText);
    }

    private static bool TryGetFancyBulletPrefixLength(string lineText, out int prefixLength)
    {
        prefixLength = 0;
        if (string.IsNullOrEmpty(lineText))
            return false;

        var match = FancyBulletPrefixRegex.Match(lineText);
        if (!match.Success)
            return false;

        prefixLength = match.Length;
        return prefixLength > 0;
    }

    private static IEnumerable<SmileyToken> EnumerateSmileyTokens(string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
            yield break;

        foreach (Match match in SmileyTokenRegex.Matches(lineText))
        {
            if (!TryResolveSmileyGlyph(match.Value, out var glyph))
                continue;

            yield return new SmileyToken(match.Index, match.Length, glyph);
        }
    }

    private static bool TryResolveSmileyGlyph(string token, out string glyph)
    {
        glyph = token switch
        {
            ":)" => "🙂",
            ";)" => "😉",
            _ => string.Empty
        };

        return glyph.Length > 0;
    }

    private static string FancyBulletStyleToSetting(FancyBulletStyle style)
        => style switch
        {
            FancyBulletStyle.HollowCircle => "hollow-circle",
            FancyBulletStyle.Square => "square",
            FancyBulletStyle.Diamond => "diamond",
            FancyBulletStyle.Dash => "dash",
            _ => "dot"
        };

    private static FancyBulletStyle ParseFancyBulletStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FancyBulletStyle.Dot;

        return value.Trim().ToLowerInvariant() switch
        {
            "hollow-circle" => FancyBulletStyle.HollowCircle,
            "square" => FancyBulletStyle.Square,
            "diamond" => FancyBulletStyle.Diamond,
            "dash" => FancyBulletStyle.Dash,
            _ => FancyBulletStyle.Dot
        };
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

        var formatJsonItem = new MenuItem { Header = "Format JSON" };
        formatJsonItem.Click += (_, _) => FormatJson(editor, keepOriginal: false);
        var formatJsonKeepOriginalItem = new MenuItem { Header = "Format JSON (keep original)" };
        formatJsonKeepOriginalItem.Click += (_, _) => FormatJson(editor, keepOriginal: true);
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
        var bulletInfoItem = new MenuItem { Header = "Bullet stats" };
        bulletInfoItem.Click += (_, _) => ShowBulletInfoAtCaret(editor);
        var resetImageSizeItem = new MenuItem { Header = "Reset Image Size to Original" };
        resetImageSizeItem.Click += (_, _) => ResetInlineImageSizeToOriginal(editor);
        var openImageFolderItem = new MenuItem { Header = "Show Image in Folder" };
        openImageFolderItem.Click += (_, _) => ShowInlineImageInFolder(editor);

        menu.Items.Add(formatJsonItem);
        menu.Items.Add(formatJsonKeepOriginalItem);
        menu.Items.Add(copySelectionItem);
        menu.Items.Add(moveSelectionItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetImageSizeItem);
        menu.Items.Add(openImageFolderItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(assignLineOwnerItem);
        menu.Items.Add(clearLineOwnerItem);
        menu.Items.Add(bulletInfoItem);

        menu.Opened += (_, _) =>
        {
            bool hasSelection = !string.IsNullOrEmpty(editor.SelectedText);
            bool canFormatJson = editor.Document != null && editor.Document.LineCount > 0;
            formatJsonItem.IsEnabled = canFormatJson;
            formatJsonKeepOriginalItem.IsEnabled = canFormatJson;
            copySelectionItem.IsEnabled = hasSelection;
            moveSelectionItem.IsEnabled = hasSelection;

            var doc = FindDocByEditor(editor);
            bool canAssign = doc != null && _users.Count > 0;
            assignLineOwnerItem.IsEnabled = canAssign;
            clearLineOwnerItem.IsEnabled = doc != null;
            resetImageSizeItem.IsEnabled = CanResetInlineImageSizeAtCaret(editor);
            openImageFolderItem.IsEnabled = CanShowInlineImageInFolderAtCaret(editor);

            var isBulletLine = doc != null && TryGetBulletLineAtCaret(editor, out _, out _, out _);
            bulletInfoItem.IsEnabled = isBulletLine;
            bulletInfoItem.Visibility = isBulletLine ? Visibility.Visible : Visibility.Collapsed;
        };

        return menu;
    }

    private static bool TryGetBulletLineAtCaret(TextEditor editor, out int lineNumber, out char marker, out string lineText)
    {
        lineNumber = 0;
        marker = default;
        lineText = string.Empty;
        if (editor.Document == null || editor.Document.LineCount == 0)
            return false;

        lineNumber = Math.Max(1, Math.Min(editor.TextArea.Caret.Line, editor.Document.LineCount));
        var line = editor.Document.GetLineByNumber(lineNumber);
        lineText = editor.Document.GetText(line.Offset, line.Length);
        return TryGetBulletLineMarker(lineText, out marker);
    }

    private static bool TryGetBulletLineMarker(string lineText, out char marker)
    {
        marker = default;
        if (string.IsNullOrEmpty(lineText) || lineText.Length < 2)
            return false;
        if (lineText[1] != ' ')
            return false;
        if (lineText[0] is '-' or '*')
        {
            marker = lineText[0];
            return true;
        }
        return false;
    }

    private bool TryGetLineBullet(TabDocument doc, int lineNumber, out char marker, out DateTime? createdUtc)
    {
        marker = default;
        createdUtc = null;
        for (int i = doc.LineBulletAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineBulletAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line != lineNumber)
                continue;

            marker = entry.Marker;
            createdUtc = entry.CreatedUtc;
            return true;
        }
        return false;
    }

    private bool SetLineBullet(TabDocument doc, int lineNumber, char marker, DateTime? createdUtc = null)
    {
        if (marker is not ('-' or '*'))
            return false;

        int lineCount = doc.Editor.Document.LineCount;
        if (lineCount <= 0)
            lineCount = 1;
        int line = Math.Max(1, Math.Min(lineNumber, lineCount));

        bool changed = false;
        bool foundExisting = false;

        for (int i = doc.LineBulletAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineBulletAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line != line)
                continue;

            if (!foundExisting)
            {
                foundExisting = true;
                if (entry.Marker != marker)
                {
                    entry.Marker = marker;
                    entry.CreatedUtc = createdUtc ?? entry.CreatedUtc;
                    changed = true;
                }
                else if (createdUtc.HasValue && entry.CreatedUtc != createdUtc)
                {
                    entry.CreatedUtc = createdUtc;
                }
            }
            else
            {
                doc.LineBulletAnchors.RemoveAt(i);
                changed = true;
            }
        }

        if (!foundExisting)
        {
            var docLine = doc.Editor.Document.GetLineByNumber(line);
            var anchor = doc.Editor.Document.CreateAnchor(docLine.Offset);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            doc.LineBulletAnchors.Add(new TabDocument.LineBulletAnchor
            {
                Anchor = anchor,
                Marker = marker,
                CreatedUtc = createdUtc
            });
            changed = true;
        }

        return changed;
    }

    private void ShowBulletInfoAtCaret(TextEditor editor)
    {
        var doc = FindDocByEditor(editor);
        if (doc == null)
            return;

        if (!TryGetBulletLineAtCaret(editor, out var lineNumber, out var marker, out var lineText))
            return;

        // Bullet CreatedUtc is persisted on SaveSession; if missing, show Unknown.
        _ = TryGetLineBullet(doc, lineNumber, out _, out var createdUtc);
        DateTime? createdLocal = createdUtc?.ToLocalTime();
        var today = DateTime.Today;
        int? daysAgo = createdLocal.HasValue ? Math.Max(0, (int)(today - createdLocal.Value.Date).TotalDays) : null;
        int? weekdaysAgo = createdLocal.HasValue ? CountWeekdaysBetween(createdLocal.Value.Date, today) : null;

        var dlg = new Window
        {
            Title = "Bullet stats",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 260,
            ShowInTaskbar = false
        };

        var root = new DockPanel { Margin = new Thickness(14) };
        var okRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var okButton = new Button { Content = "OK", Width = 80, IsDefault = true, IsCancel = true };
        okButton.Click += (_, _) => dlg.Close();
        okRow.Children.Add(okButton);
        DockPanel.SetDock(okRow, Dock.Bottom);
        root.Children.Add(okRow);

        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(new TextBlock
        {
            Text = $"Line {lineNumber}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(new TextBlock { Text = createdLocal.HasValue ? $"Created: {createdLocal:yyyy-MM-dd HH:mm}" : "Created: Unknown" });
        content.Children.Add(new TextBlock { Text = daysAgo.HasValue ? $"Days ago: {daysAgo}" : "Days ago: Unknown" });
        content.Children.Add(new TextBlock { Text = weekdaysAgo.HasValue ? $"Weekdays ago: {weekdaysAgo}" : "Weekdays ago: Unknown" });

        root.Children.Add(content);
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private static void MoveCaretToMousePosition(TextEditor editor, MouseButtonEventArgs e)
    {
        var position = editor.GetPositionFromPoint(e.GetPosition(editor));
        if (!position.HasValue)
            return;

        // Preserve existing selection when right-click occurs inside it.
        if (editor.Document != null && editor.SelectionLength > 0)
        {
            int clickedOffset = editor.Document.GetOffset(position.Value.Location);
            int selectionStart = editor.SelectionStart;
            int selectionEnd = selectionStart + editor.SelectionLength;
            if (clickedOffset >= selectionStart && clickedOffset < selectionEnd)
                return;
        }

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

    private void FormatJson(TextEditor editor, bool keepOriginal)
    {
        if (editor.Document == null)
            return;

        string textToFormat;
        int replaceStart;
        int replaceLength;
        string? lineDelimiterSuffix = null;

        if (editor.SelectionLength > 0)
        {
            textToFormat = editor.SelectedText;
            replaceStart = editor.SelectionStart;
            replaceLength = editor.SelectionLength;
        }
        else
        {
            int lineNumber = Math.Max(1, Math.Min(editor.TextArea.Caret.Line, editor.Document.LineCount));
            var line = editor.Document.GetLineByNumber(lineNumber);
            textToFormat = editor.Document.GetText(line.Offset, line.Length);
            replaceStart = line.Offset;
            replaceLength = line.TotalLength;
            if (line.DelimiterLength > 0)
                lineDelimiterSuffix = editor.Document.GetText(line.Offset + line.Length, line.DelimiterLength);
        }

        if (string.IsNullOrWhiteSpace(textToFormat))
            return;

        try
        {
            using var jsonDocument = JsonDocument.Parse(textToFormat, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var formatted = JsonSerializer.Serialize(jsonDocument.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            if (keepOriginal)
            {
                var doc = editor.Document;
                // Same insert for selection and whole line: after the parsed range (replaceStart/Length).
                int insertAt = replaceStart + replaceLength;
                // No-selection range already includes the line delimiter (line.TotalLength),
                // so only one extra newline is needed to match selection behavior.
                var prefix = editor.SelectionLength > 0
                    ? Environment.NewLine + Environment.NewLine
                    : Environment.NewLine;
                doc.Insert(insertAt, prefix + formatted);
                editor.Select(insertAt + prefix.Length, formatted.Length);
            }
            else
            {
                if (lineDelimiterSuffix != null)
                    formatted += lineDelimiterSuffix;

                editor.Document.Replace(replaceStart, replaceLength, formatted);
                editor.Select(replaceStart, formatted.Length);
            }

            EnableJsonSyntaxHighlighting(editor);
        }
        catch (JsonException)
        {
            MessageBox.Show(
                "Could not parse as JSON.",
                "Format JSON",
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

    private CutLineMetadataCapture? CaptureCutLineMetadata(TabDocument doc)
    {
        var editor = doc.Editor;
        var document = editor.Document;
        if (document == null || document.LineCount <= 0)
            return null;

        string clipboardText;
        List<int> fullyCutLines;
        int firstClipboardLine;

        if (editor.SelectionLength <= 0)
        {
            int caretLine = Math.Max(1, Math.Min(editor.TextArea.Caret.Line, document.LineCount));
            var caretDocLine = document.GetLineByNumber(caretLine);
            clipboardText = document.GetText(caretDocLine.Offset, caretDocLine.TotalLength);
            fullyCutLines = [caretLine];
            firstClipboardLine = caretLine;
        }
        else
        {
            clipboardText = editor.SelectedText ?? string.Empty;
            int selectionStart = editor.SelectionStart;
            int selectionEnd = selectionStart + editor.SelectionLength;
            int firstLineNum = document.GetLineByOffset(selectionStart).LineNumber;
            int lastLineNum = document.GetLineByOffset(Math.Max(selectionStart, selectionEnd - 1)).LineNumber;
            firstClipboardLine = firstLineNum;

            fullyCutLines = new List<int>();
            for (int line = firstLineNum; line <= lastLineNum; line++)
            {
                var docLine = document.GetLineByNumber(line);
                // Consider a line fully included when the selection spans its
                // entire content (trailing newline is optional on the last
                // line). Partial lines are intentionally excluded so their
                // highlight stays on the remaining source text and doesn't
                // travel with a partial paste.
                bool startsAtLineStart = selectionStart <= docLine.Offset;
                bool endsAfterLineContent = selectionEnd >= docLine.Offset + docLine.Length;
                if (startsAtLineStart && endsAfterLineContent)
                    fullyCutLines.Add(line);
            }
        }

        if (clipboardText.Length == 0 || fullyCutLines.Count == 0)
            return null;

        var entries = new List<CutLineMetadataEntry>();
        foreach (var line in fullyCutLines)
        {
            HighlightKind? highlightKind = null;
            if (IsLineCriticalHighlighted(doc, line))
                highlightKind = HighlightKind.Critical;
            else if (IsLineHighlighted(doc, line))
                highlightKind = HighlightKind.Normal;

            string? assignee = null;
            DateTime? assigneeCreatedUtc = null;
            if (TryGetLineAssignee(doc, line, out var person, out var personCreatedUtc) && !string.IsNullOrWhiteSpace(person))
            {
                assignee = person;
                assigneeCreatedUtc = personCreatedUtc;
            }

            if (highlightKind == null && assignee == null)
                continue;

            // Measured against the first line of the clipboard text, not the
            // first fully-cut line. This matters when the selection starts
            // mid-line: the partial leading text becomes the first pasted
            // line, so the highlighted line lands at offset +1 (or later).
            entries.Add(new CutLineMetadataEntry
            {
                RelativeLineOffset = line - firstClipboardLine,
                Highlight = highlightKind,
                Assignee = assignee,
                AssigneeCreatedUtc = assigneeCreatedUtc
            });
        }

        if (entries.Count == 0)
            return null;

        return new CutLineMetadataCapture
        {
            Transfer = new CutLineMetadataTransfer
            {
                ClipboardText = clipboardText,
                Entries = entries
            },
            FullyCutLines = fullyCutLines
        };
    }

    private bool TryGetClipboardText(out string? clipboardText)
    {
        clipboardText = null;
        try
        {
            if (!Clipboard.ContainsText())
                return true;

            clipboardText = Clipboard.GetText();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeLineEndings(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private void ExecuteEditorCut(TabDocument doc)
    {
        var capture = CaptureCutLineMetadata(doc);
        _pendingCutLineMetadataTransfer = capture?.Transfer;

        // Strip metadata from the source BEFORE the cut so anchors at a
        // selection boundary (e.g. selection starts at the line's first
        // offset) can't survive the deletion and drift onto the next line.
        if (capture != null)
        {
            foreach (var line in capture.FullyCutLines)
                ClearLineMetadataForCutSource(doc, line);
        }
        else if (doc.Editor.SelectionLength <= 0)
        {
            int caretLine = Math.Max(1, doc.Editor.TextArea.Caret.Line);
            ClearLineMetadataForCutSource(doc, caretLine);
        }

        doc.Editor.Cut();
    }

    private void ExecuteEditorCopy(TabDocument doc)
    {
        // Capture the same metadata as cut but leave the source intact; the
        // transfer is applied on the next paste whose clipboard still matches.
        var capture = CaptureCutLineMetadata(doc);
        _pendingCutLineMetadataTransfer = capture?.Transfer;
        doc.Editor.Copy();
    }

    private void ExecuteEditorPaste(TabDocument doc)
    {
        if (TryPasteClipboardImage(doc))
            return;

        var editor = doc.Editor;
        var document = editor.Document;

        int insertedOffset = -1;
        int insertedLength = 0;

        void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        {
            // AvalonEdit may emit multiple changes for a single Paste (e.g.
            // remove-selection + insert). Take the first offset as the start
            // and sum the inserted character counts.
            if (insertedOffset < 0)
                insertedOffset = e.Offset;
            insertedLength += e.InsertionLength;
        }

        document.Changed += OnDocumentChanged;
        try
        {
            editor.Paste();
        }
        finally
        {
            document.Changed -= OnDocumentChanged;
        }

        if (insertedOffset >= 0 && insertedLength > 0)
            TryApplyPendingCutLineMetadataAfterPaste(doc, insertedOffset, insertedLength);
    }

    private void TryApplyPendingCutLineMetadataAfterPaste(TabDocument doc, int insertedStartOffset, int insertedLength)
    {
        var pendingTransfer = _pendingCutLineMetadataTransfer;
        if (pendingTransfer == null || pendingTransfer.Entries.Count == 0 || insertedLength <= 0)
            return;

        if (!TryGetClipboardText(out var clipboardText))
            return;

        // WPF clipboard normalizes line endings to CRLF, but the document may
        // use LF internally. Compare after normalization so we still accept
        // the paste as matching our previous cut/copy.
        if (!string.Equals(
                NormalizeLineEndings(clipboardText),
                NormalizeLineEndings(pendingTransfer.ClipboardText),
                StringComparison.Ordinal))
        {
            // Clipboard has drifted (another app copied something else); drop
            // the stored metadata so we don't apply it to unrelated text.
            _pendingCutLineMetadataTransfer = null;
            return;
        }

        int textLength = doc.Editor.Document.TextLength;
        if (textLength <= 0)
            return;

        int maxOffset = Math.Max(0, textLength - 1);
        int startOffset = Math.Max(0, Math.Min(insertedStartOffset, maxOffset));
        int endExclusiveOffset = Math.Max(startOffset, Math.Min(textLength, insertedStartOffset + insertedLength));
        int endOffset = Math.Max(startOffset, Math.Min(maxOffset, endExclusiveOffset - 1));
        int startLine = doc.Editor.Document.GetLineByOffset(startOffset).LineNumber;
        int endLine = doc.Editor.Document.GetLineByOffset(endOffset).LineNumber;

        bool changed = false;
        foreach (var entry in pendingTransfer.Entries.OrderBy(item => item.RelativeLineOffset))
        {
            int targetLine = startLine + entry.RelativeLineOffset;
            if (targetLine < startLine || targetLine > endLine)
                continue;

            if (entry.Highlight is HighlightKind kind)
            {
                changed |= RemoveHighlightedLine(doc, targetLine, OppositeHighlightKind(kind), markDirty: false, redraw: false);
                changed |= AddHighlightedLine(doc, targetLine, kind, markDirty: false, redraw: false);
            }

            if (!string.IsNullOrWhiteSpace(entry.Assignee))
                changed |= SetLineAssignee(doc, targetLine, entry.Assignee, markDirty: false, redraw: false, createdUtc: entry.AssigneeCreatedUtc ?? DateTime.UtcNow);
        }

        // Keep _pendingCutLineMetadataTransfer so repeated Ctrl+V pastes the
        // same highlight as long as the clipboard still matches.
        if (!changed)
            return;

        MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void ClearLineMetadataForCutSource(TabDocument doc, int lineNumber)
    {
        bool changed = false;
        changed |= RemoveHighlightedLine(doc, lineNumber, HighlightKind.Normal, markDirty: false, redraw: false);
        changed |= RemoveHighlightedLine(doc, lineNumber, HighlightKind.Critical, markDirty: false, redraw: false);
        changed |= RemoveLineAssignee(doc, lineNumber, markDirty: false, redraw: false);
        if (!changed)
            return;

        MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private double ClampedEditorDisplayFontSize()
        => Math.Max(6, Math.Min(72, _fontSize + _sessionEditorFontZoomDelta));

    private static int NormalizeVisualLineWrapColumn(int value)
        => Math.Max(MinVisualLineWrapColumn, Math.Min(MaxVisualLineWrapColumn, value));

    private static double EstimateWrapCharacterWidth(TextEditor editor)
    {
        var dpi = VisualTreeHelper.GetDpi(editor);
        var typeface = new Typeface(editor.FontFamily, FontStyles.Normal, editor.FontWeight, FontStretches.Normal);
        const string sample = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var formatted = new FormattedText(
            sample,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            editor.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        return Math.Max(1, formatted.WidthIncludingTrailingWhitespace / sample.Length);
    }

    private void ApplyVisualLineWrapSettings(TextEditor editor)
    {
        editor.TextArea.Options.InheritWordWrapIndentation = false;
        editor.TextArea.Options.WordWrapIndentation = 0;
        var textView = editor.TextArea.TextView;

        if (!_wrapLongLinesVisually)
        {
            editor.WordWrap = false;
            editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            editor.MaxWidth = double.PositiveInfinity;
            editor.HorizontalAlignment = HorizontalAlignment.Stretch;
            textView.MaxWidth = double.PositiveInfinity;
            textView.HorizontalAlignment = HorizontalAlignment.Stretch;
            return;
        }

        editor.WordWrap = true;
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.MaxWidth = double.PositiveInfinity;

        var charWidth = EstimateWrapCharacterWidth(editor);
        var targetWidth = Math.Max(280, (_visualLineWrapColumn * charWidth) + 80);

        // Keep wrapping visual-only by constraining the text viewport width, not the whole editor.
        textView.MaxWidth = targetWidth;
        textView.HorizontalAlignment = HorizontalAlignment.Left;
    }

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
            ApplyVisualLineWrapSettings(doc.Editor);
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

        var includeAssigneesDialogResult = MessageBox.Show(
            "Include assignees in exported text?\n\nYes: include assignees\nNo: plain text export\nCancel: abort export",
            "Export options",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (includeAssigneesDialogResult == MessageBoxResult.Cancel)
            return false;
        bool includeAssignees = includeAssigneesDialogResult == MessageBoxResult.Yes;

        var dlg = new SaveFileDialog
        {
            FileName = doc.Header,
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            var textToSave = BuildExportText(doc, includeAssignees);
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

    /// <summary>Rewrites line-leading <c>- </c>/<c>* </c> list markers (after spaces/tabs) to <paramref name="saveAsMarker"/>.</summary>
    private static string UnifyMarkdownListBulletMarkersForSave(string text, char saveAsMarker)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (saveAsMarker != '-' && saveAsMarker != '*')
            saveAsMarker = '-';

        var sb = new StringBuilder(text.Length);
        int lineStart = 0;
        while (lineStart < text.Length)
        {
            int nextNl = text.IndexOf('\n', lineStart);
            int segmentEnd = nextNl >= 0 ? nextNl : text.Length;
            string line = text.Substring(lineStart, segmentEnd - lineStart);

            int indentEnd = 0;
            while (indentEnd < line.Length && (line[indentEnd] == ' ' || line[indentEnd] == '\t'))
                indentEnd++;

            if (indentEnd + 1 < line.Length)
            {
                char marker = line[indentEnd];
                if ((marker == '-' || marker == '*') && char.IsWhiteSpace(line[indentEnd + 1]) && marker != saveAsMarker)
                {
                    sb.Append(line, 0, indentEnd);
                    sb.Append(saveAsMarker);
                    sb.Append(line, indentEnd + 1, line.Length - indentEnd - 1);
                }
                else
                    sb.Append(line);
            }
            else
                sb.Append(line);

            if (nextNl < 0)
                break;
            sb.Append('\n');
            lineStart = nextNl + 1;
        }

        return sb.ToString();
    }

    private static string RemoveTrailingWhitespaces(string text)
        => string.IsNullOrEmpty(text) ? text : Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);

    private static string RemoveTrailingWhitespacesExceptLine(string text, int preservedLineNumber)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (preservedLineNumber < 1)
            return RemoveTrailingWhitespaces(text);

        var parts = Regex.Split(text, @"(\r\n|\n)");
        var builder = new StringBuilder(text.Length);
        int lineNumber = 1;

        for (int i = 0; i < parts.Length; i += 2)
        {
            var lineText = parts[i];
            builder.Append(lineNumber == preservedLineNumber
                ? lineText
                : Regex.Replace(lineText, @"[ \t]+$", string.Empty));

            if (i + 1 < parts.Length)
            {
                builder.Append(parts[i + 1]);
                lineNumber++;
            }
        }

        return builder.ToString();
    }

    private bool CopyCurrentTabToClipboard(bool includeAssignees)
    {
        var doc = CurrentDoc();
        if (doc == null)
            return false;

        try
        {
            var textToCopy = BuildExportText(doc, includeAssignees);
            Clipboard.SetText(textToCopy ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy text:\n{ex.Message}", "Noted",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private string BuildExportText(TabDocument doc, bool includeAssignees)
    {
        var textToSave = RemoveTrailingWhitespaces(doc.Editor.Text);
        if (!includeAssignees)
            return textToSave;

        var lineAssignments = GetLineAssignments(doc);
        var parts = Regex.Split(textToSave, @"(\r\n|\n)");
        var builder = new StringBuilder(textToSave.Length + 128);
        int lineNumber = 1;

        for (int i = 0; i < parts.Length; i += 2)
        {
            var lineText = parts[i];
            string transformedLine = TransformLineForExport(lineText, lineNumber, lineAssignments);
            builder.Append(transformedLine);

            if (i + 1 < parts.Length)
            {
                builder.Append(parts[i + 1]);
                lineNumber++;
            }
        }

        return builder.ToString();
    }

    private static string TransformLineForExport(string lineText, int lineNumber, IReadOnlyDictionary<int, string> lineAssignments)
    {
        if (TryConvertInlineAssigneeSuffix(lineText, out var inlineConvertedLine))
            return inlineConvertedLine;

        if (lineAssignments.TryGetValue(lineNumber, out var person) && !string.IsNullOrWhiteSpace(person))
            return $"{lineText} - Assigned to {person.Trim()}";

        return lineText;
    }

    private static bool TryConvertInlineAssigneeSuffix(string lineText, out string convertedLine)
    {
        convertedLine = lineText;
        var match = InlineAssigneeSuffixRegex.Match(lineText);
        if (!match.Success)
            return false;

        var person = match.Groups["person"].Value.Trim();
        if (person.Length == 0)
            return false;

        var content = match.Groups["content"].Value.TrimEnd();
        convertedLine = content.Length == 0
            ? $"Assigned to {person}"
            : $"{content} - Assigned to {person}";
        return true;
    }

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
        _criticalHighlightedLineBrush = CreateFrozenBrush(_criticalHighlightedLineColor);
        _selectedCriticalHighlightedLineBrush = CreateFrozenBrush(_selectedCriticalHighlightedLineColor);

        foreach (var doc in _docs.Values)
        {
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

    private static List<TextAnchor> GetHighlightAnchors(TabDocument doc, HighlightKind kind)
        => kind == HighlightKind.Critical
            ? doc.CriticalHighlightAnchors
            : doc.HighlightAnchors;

    private static HighlightKind OppositeHighlightKind(HighlightKind kind)
        => kind == HighlightKind.Critical ? HighlightKind.Normal : HighlightKind.Critical;

    private IReadOnlyCollection<int> GetHighlightedLineNumbers(TabDocument doc)
        => GetHighlightedLineNumbers(doc, HighlightKind.Normal);

    private IReadOnlyCollection<int> GetCriticalHighlightedLineNumbers(TabDocument doc)
        => GetHighlightedLineNumbers(doc, HighlightKind.Critical);

    private IReadOnlyCollection<int> GetHighlightedLineNumbers(TabDocument doc, HighlightKind kind)
    {
        var anchors = GetHighlightAnchors(doc, kind);
        if (anchors.Count == 0)
            return [];

        var lines = new HashSet<int>();
        for (int i = anchors.Count - 1; i >= 0; i--)
        {
            var anchor = anchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                anchors.RemoveAt(i);
                continue;
            }

            lines.Add(anchor.Line);
        }

        return lines;
    }

    private bool IsLineHighlighted(TabDocument doc, int lineNumber)
        => IsLineHighlighted(doc, lineNumber, HighlightKind.Normal);

    private bool IsLineCriticalHighlighted(TabDocument doc, int lineNumber)
        => IsLineHighlighted(doc, lineNumber, HighlightKind.Critical);

    private bool IsLineHighlighted(TabDocument doc, int lineNumber, HighlightKind kind)
    {
        var anchors = GetHighlightAnchors(doc, kind);
        for (int i = anchors.Count - 1; i >= 0; i--)
        {
            var anchor = anchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                anchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line == lineNumber)
                return true;
        }

        return false;
    }

    private bool AddHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
        => AddHighlightedLine(doc, lineNumber, HighlightKind.Normal, markDirty, redraw);

    private bool AddCriticalHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
        => AddHighlightedLine(doc, lineNumber, HighlightKind.Critical, markDirty, redraw);

    private bool AddHighlightedLine(TabDocument doc, int lineNumber, HighlightKind kind, bool markDirty = true, bool redraw = true)
    {
        int lineCount = doc.Editor.Document.LineCount;
        if (lineCount <= 0)
            lineCount = 1;

        int line = Math.Max(1, Math.Min(lineNumber, lineCount));
        if (IsLineHighlighted(doc, line, kind))
            return false;

        var docLine = doc.Editor.Document.GetLineByNumber(line);
        var anchor = doc.Editor.Document.CreateAnchor(docLine.Offset);
        // Keep highlight tied to the original line content when inserting at column 1.
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        GetHighlightAnchors(doc, kind).Add(anchor);

        if (markDirty)
            MarkDirty(doc);
        if (redraw)
            RedrawHighlight(doc);
        return true;
    }

    private bool RemoveHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
        => RemoveHighlightedLine(doc, lineNumber, HighlightKind.Normal, markDirty, redraw);

    private bool RemoveCriticalHighlightedLine(TabDocument doc, int lineNumber, bool markDirty = true, bool redraw = true)
        => RemoveHighlightedLine(doc, lineNumber, HighlightKind.Critical, markDirty, redraw);

    private bool RemoveHighlightedLine(TabDocument doc, int lineNumber, HighlightKind kind, bool markDirty = true, bool redraw = true)
    {
        var anchors = GetHighlightAnchors(doc, kind);
        bool removed = false;
        for (int i = anchors.Count - 1; i >= 0; i--)
        {
            var anchor = anchors[i];
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                anchors.RemoveAt(i);
                continue;
            }

            if (anchor.Line == lineNumber)
            {
                anchors.RemoveAt(i);
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
        => SetHighlightedLines(doc, lineNumbers, HighlightKind.Normal, markDirty);

    private void SetCriticalHighlightedLines(TabDocument doc, IEnumerable<int>? lineNumbers, bool markDirty = true)
        => SetHighlightedLines(doc, lineNumbers, HighlightKind.Critical, markDirty);

    private void SetHighlightedLines(TabDocument doc, IEnumerable<int>? lineNumbers, HighlightKind kind, bool markDirty = true)
    {
        GetHighlightAnchors(doc, kind).Clear();

        if (lineNumbers != null)
        {
            foreach (var line in lineNumbers.Where(line => line > 0).Distinct())
                AddHighlightedLine(doc, line, kind, markDirty: false, redraw: false);
        }

        if (markDirty)
            MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void ToggleHighlightedCaretLine(TabDocument doc)
        => ToggleHighlightedCaretLine(doc, HighlightKind.Normal);

    private void ToggleCriticalHighlightedCaretLine(TabDocument doc)
        => ToggleHighlightedCaretLine(doc, HighlightKind.Critical);

    private void ToggleHighlightedCaretLine(TabDocument doc, HighlightKind kind)
    {
        var selectedLines = GetSelectedLineNumbers(doc);
        if (selectedLines.Count > 0)
        {
            bool allHighlighted = selectedLines.All(line => IsLineHighlighted(doc, line, kind));
            bool changed = false;
            foreach (var line in selectedLines)
            {
                if (allHighlighted)
                {
                    changed |= RemoveHighlightedLine(doc, line, kind, markDirty: false, redraw: false);
                    continue;
                }

                changed |= RemoveHighlightedLine(doc, line, OppositeHighlightKind(kind), markDirty: false, redraw: false);
                changed |= AddHighlightedLine(doc, line, kind, markDirty: false, redraw: false);
            }

            if (changed)
            {
                MarkDirty(doc);
                RedrawHighlight(doc);
            }
            return;
        }

        int caretLine = Math.Max(1, doc.Editor.TextArea.Caret.Line);
        if (IsLineHighlighted(doc, caretLine, kind))
            RemoveHighlightedLine(doc, caretLine, kind);
        else
        {
            RemoveHighlightedLine(doc, caretLine, OppositeHighlightKind(kind), markDirty: false, redraw: false);
            AddHighlightedLine(doc, caretLine, kind);
        }
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

    private static string GetLineText(TabDocument doc, int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > doc.Editor.Document.LineCount)
            return string.Empty;

        var line = doc.Editor.Document.GetLineByNumber(lineNumber);
        return doc.Editor.Document.GetText(line.Offset, line.Length);
    }

    private void QueueShiftDeleteAssigneeUndo(TabDocument doc, IReadOnlyList<LineAssigneeUndoEntry> removedAssignees)
    {
        if (removedAssignees.Count == 0)
            return;

        if (!_pendingShiftDeleteAssigneeUndo.TryGetValue(doc, out var stack))
        {
            stack = new Stack<LineAssigneeUndoRecord>();
            _pendingShiftDeleteAssigneeUndo[doc] = stack;
        }

        stack.Push(new LineAssigneeUndoRecord { Entries = removedAssignees });
    }

    private void QueueShiftDeleteHighlightUndo(TabDocument doc, IReadOnlyList<HighlightLineUndoEntry> removedHighlights)
    {
        if (removedHighlights.Count == 0)
            return;

        if (!_pendingShiftDeleteHighlightUndo.TryGetValue(doc, out var stack))
        {
            stack = new Stack<HighlightLineUndoRecord>();
            _pendingShiftDeleteHighlightUndo[doc] = stack;
        }

        stack.Push(new HighlightLineUndoRecord { Entries = removedHighlights });
    }

    private void QueueTryRestoreShiftDeleteAssigneesOnUndo(TabDocument doc)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_pendingShiftDeleteAssigneeUndo.TryGetValue(doc, out var stack) || stack.Count == 0)
                return;

            var record = stack.Peek();
            bool isMatchingUndoStep = record.Entries.All(entry =>
                string.Equals(GetLineText(doc, entry.LineNumber), entry.LineText, StringComparison.Ordinal));
            if (!isMatchingUndoStep)
                return;

            bool changed = false;
            foreach (var entry in record.Entries)
                changed |= SetLineAssignee(doc, entry.LineNumber, entry.Person, markDirty: false, redraw: false, createdUtc: entry.CreatedUtc);

            if (changed)
                RedrawHighlight(doc);

            stack.Pop();
        }));
    }

    private void QueueTryRestoreShiftDeleteHighlightsOnUndo(TabDocument doc)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_pendingShiftDeleteHighlightUndo.TryGetValue(doc, out var stack) || stack.Count == 0)
                return;

            var record = stack.Peek();
            bool isMatchingUndoStep = record.Entries.All(entry =>
                string.Equals(GetLineText(doc, entry.LineNumber), entry.LineText, StringComparison.Ordinal));
            if (!isMatchingUndoStep)
                return;

            bool changed = false;
            foreach (var entry in record.Entries)
            {
                changed |= entry.IsCritical
                    ? AddCriticalHighlightedLine(doc, entry.LineNumber, markDirty: false, redraw: false)
                    : AddHighlightedLine(doc, entry.LineNumber, markDirty: false, redraw: false);
            }

            if (changed)
                RedrawHighlight(doc);

            stack.Pop();
        }));
    }

    private void HandleEditorPreviewKeyDown(TabDocument doc, KeyEventArgs e)
    {
        if (TryInsertNewlineClosingTagCompletion(doc, e))
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Back
            && Keyboard.Modifiers == ModifierKeys.None
            && TryRemoveHighlightBeforeLineJoin(doc))
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt
            && (key == Key.Up || key == Key.Down))
        {
            if (TryMoveCurrentLine(doc, moveDown: key == Key.Down))
                e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && (key == Key.Up || key == Key.Down))
        {
            if (TryMoveCaretByLines(doc, key == Key.Down ? ControlArrowLineJump : -ControlArrowLineJump))
                e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.X)
        {
            ExecuteEditorCut(doc);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.C)
        {
            ExecuteEditorCopy(doc);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            && key == Key.V)
        {
            ExecuteEditorPaste(doc);
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

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Z)
        {
            if (TryUndoLineAssigneeChange(doc))
            {
                e.Handled = true;
                return;
            }

            QueueTryRestoreShiftDeleteAssigneesOnUndo(doc);
            QueueTryRestoreShiftDeleteHighlightsOnUndo(doc);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Y)
        {
            if (TryRedoLineAssigneeChange(doc))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Delete || (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        var removedAssignees = new List<LineAssigneeUndoEntry>();
        var removedHighlights = new List<HighlightLineUndoEntry>();
        bool changed = false;
        foreach (var line in GetSelectedOrCaretLineNumbers(doc))
        {
            if (IsLineHighlighted(doc, line))
            {
                removedHighlights.Add(new HighlightLineUndoEntry
                {
                    LineNumber = line,
                    IsCritical = false,
                    LineText = GetLineText(doc, line)
                });
            }

            if (IsLineCriticalHighlighted(doc, line))
            {
                removedHighlights.Add(new HighlightLineUndoEntry
                {
                    LineNumber = line,
                    IsCritical = true,
                    LineText = GetLineText(doc, line)
                });
            }

            if (TryGetLineAssignee(doc, line, out var person, out var personCreatedUtc))
            {
                removedAssignees.Add(new LineAssigneeUndoEntry
                {
                    LineNumber = line,
                    Person = person,
                    LineText = GetLineText(doc, line),
                    CreatedUtc = personCreatedUtc
                });
            }

            changed |= RemoveLineAssignee(doc, line, markDirty: false, redraw: false);
        }

        bool removedHighlight = false;
        foreach (var entry in removedHighlights)
        {
            removedHighlight |= entry.IsCritical
                ? RemoveCriticalHighlightedLine(doc, entry.LineNumber, markDirty: false, redraw: false)
                : RemoveHighlightedLine(doc, entry.LineNumber, markDirty: false, redraw: false);
        }

        if (changed || removedHighlight)
        {
            QueueShiftDeleteAssigneeUndo(doc, removedAssignees);
            QueueShiftDeleteHighlightUndo(doc, removedHighlights);
            RedrawHighlight(doc);
        }
    }

    private bool TryRemoveHighlightBeforeLineJoin(TabDocument doc)
    {
        var editor = doc.Editor;
        var selection = editor.TextArea.Selection;
        if (selection != null && !selection.IsEmpty)
            return false;

        int caretLine = Math.Max(1, editor.TextArea.Caret.Line);
        int caretColumn = Math.Max(1, editor.TextArea.Caret.Column);
        bool hasNormalHighlight = IsLineHighlighted(doc, caretLine);
        bool hasCriticalHighlight = IsLineCriticalHighlighted(doc, caretLine);
        if (caretLine <= 1 || caretColumn != 1 || (!hasNormalHighlight && !hasCriticalHighlight))
            return false;

        var previousLine = editor.Document.GetLineByNumber(caretLine - 1);
        var previousLineText = editor.Document.GetText(previousLine.Offset, previousLine.Length);
        if (string.IsNullOrWhiteSpace(previousLineText))
            return false;

        bool removed = false;
        if (hasNormalHighlight)
            removed |= RemoveHighlightedLine(doc, caretLine, markDirty: false, redraw: false);
        if (hasCriticalHighlight)
            removed |= RemoveCriticalHighlightedLine(doc, caretLine, markDirty: false, redraw: false);
        if (removed)
        {
            MarkDirty(doc);
            RedrawHighlight(doc);
        }
        return removed;
    }

    private bool TryMoveCurrentLine(TabDocument doc, bool moveDown)
    {
        var editor = doc.Editor;
        var document = editor.Document;
        if (document.LineCount <= 1)
            return false;

        int caretLineNumber = Math.Max(1, editor.TextArea.Caret.Line);
        int targetLineNumber = moveDown ? caretLineNumber + 1 : caretLineNumber - 1;
        if (targetLineNumber < 1 || targetLineNumber > document.LineCount)
            return false;

        var currentLine = document.GetLineByNumber(caretLineNumber);
        var targetLine = document.GetLineByNumber(targetLineNumber);
        int caretColumn = Math.Max(1, editor.TextArea.Caret.Column);

        int currentStart = currentLine.Offset;
        int currentEnd = currentLine.EndOffset + currentLine.DelimiterLength;
        if (currentEnd > document.TextLength)
            currentEnd = currentLine.EndOffset;

        int targetStart = targetLine.Offset;
        int targetEnd = targetLine.EndOffset + targetLine.DelimiterLength;
        if (targetEnd > document.TextLength)
            targetEnd = targetLine.EndOffset;

        // Snapshot per-line markers before the document mutation. Replacing the
        // entire two-line range leaves the backing TextAnchors at the replace
        // boundary, so they no longer point to the moved content. We clear the
        // markers first and reapply them on the swapped line numbers afterwards.
        bool currentHighlighted = IsLineHighlighted(doc, caretLineNumber);
        bool currentCritical = IsLineCriticalHighlighted(doc, caretLineNumber);
        bool targetHighlighted = IsLineHighlighted(doc, targetLineNumber);
        bool targetCritical = IsLineCriticalHighlighted(doc, targetLineNumber);
        TryGetLineAssignee(doc, caretLineNumber, out var currentAssignee, out var currentAssigneeCreatedUtc);
        TryGetLineAssignee(doc, targetLineNumber, out var targetAssignee, out var targetAssigneeCreatedUtc);

        if (currentHighlighted)
            RemoveHighlightedLine(doc, caretLineNumber, HighlightKind.Normal, markDirty: false, redraw: false);
        if (currentCritical)
            RemoveHighlightedLine(doc, caretLineNumber, HighlightKind.Critical, markDirty: false, redraw: false);
        if (targetHighlighted)
            RemoveHighlightedLine(doc, targetLineNumber, HighlightKind.Normal, markDirty: false, redraw: false);
        if (targetCritical)
            RemoveHighlightedLine(doc, targetLineNumber, HighlightKind.Critical, markDirty: false, redraw: false);
        if (!string.IsNullOrEmpty(currentAssignee))
            RemoveLineAssignee(doc, caretLineNumber, markDirty: false, redraw: false);
        if (!string.IsNullOrEmpty(targetAssignee))
            RemoveLineAssignee(doc, targetLineNumber, markDirty: false, redraw: false);

        using (document.RunUpdate())
        {
            if (moveDown)
            {
                string currentText = document.GetText(currentStart, currentEnd - currentStart);
                string targetText = document.GetText(targetStart, targetEnd - targetStart);
                document.Replace(currentStart, targetEnd - currentStart, targetText + currentText);
            }
            else
            {
                string targetText = document.GetText(targetStart, targetEnd - targetStart);
                string currentText = document.GetText(currentStart, currentEnd - currentStart);
                document.Replace(targetStart, currentEnd - targetStart, currentText + targetText);
            }
        }

        if (currentHighlighted)
            AddHighlightedLine(doc, targetLineNumber, HighlightKind.Normal, markDirty: false, redraw: false);
        if (currentCritical)
            AddHighlightedLine(doc, targetLineNumber, HighlightKind.Critical, markDirty: false, redraw: false);
        if (targetHighlighted)
            AddHighlightedLine(doc, caretLineNumber, HighlightKind.Normal, markDirty: false, redraw: false);
        if (targetCritical)
            AddHighlightedLine(doc, caretLineNumber, HighlightKind.Critical, markDirty: false, redraw: false);
        if (!string.IsNullOrEmpty(currentAssignee))
            SetLineAssignee(doc, targetLineNumber, currentAssignee, markDirty: false, redraw: false, createdUtc: currentAssigneeCreatedUtc);
        if (!string.IsNullOrEmpty(targetAssignee))
            SetLineAssignee(doc, caretLineNumber, targetAssignee, markDirty: false, redraw: false, createdUtc: targetAssigneeCreatedUtc);

        RedrawHighlight(doc);

        var movedLine = document.GetLineByNumber(targetLineNumber);
        int movedCaretOffset = Math.Min(movedLine.EndOffset, movedLine.Offset + (caretColumn - 1));
        editor.TextArea.Caret.Offset = movedCaretOffset;
        editor.Select(movedCaretOffset, 0);
        editor.ScrollToLine(targetLineNumber);
        return true;
    }

    private static bool TryMoveCaretByLines(TabDocument doc, int lineDelta)
    {
        if (lineDelta == 0)
            return false;

        var editor = doc.Editor;
        var document = editor.Document;
        int currentLine = Math.Max(1, editor.TextArea.Caret.Line);
        int targetLine = Math.Clamp(currentLine + lineDelta, 1, Math.Max(1, document.LineCount));
        if (targetLine == currentLine)
            return false;

        int currentColumn = Math.Max(1, editor.TextArea.Caret.Column);
        var targetDocumentLine = document.GetLineByNumber(targetLine);
        int targetColumn = Math.Min(currentColumn, targetDocumentLine.Length + 1);

        editor.TextArea.Caret.Location = new TextLocation(targetLine, targetColumn);
        editor.Select(editor.CaretOffset, 0);
        editor.ScrollToLine(targetLine);
        return true;
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

    private void UpdateAssigneeHoverTooltip(TabDocument doc, MouseEventArgs e)
    {
        var bounds = doc.AssigneeBadgeBoundsCache;
        if (bounds.Count == 0)
        {
            HideAssigneeHoverTooltip();
            return;
        }

        var textView = doc.Editor.TextArea.TextView;
        var pos = e.GetPosition(textView);
        TabDocument.AssigneeBadgeBounds? hit = null;
        foreach (var badge in bounds)
        {
            if (badge.Bounds.Contains(pos))
            {
                hit = badge;
                break;
            }
        }

        if (hit == null)
        {
            HideAssigneeHoverTooltip();
            return;
        }

        var key = BuildAssigneeHoverKey(hit);
        if (_assigneeHoverTooltip != null
            && _assigneeHoverTooltip.IsOpen
            && _assigneeHoverTooltipDoc == doc
            && _assigneeHoverTooltipKey == key)
        {
            return;
        }

        ShowAssigneeHoverTooltip(doc, hit, key);
    }

    private void UpdateBulletHoverTooltip(TabDocument doc, MouseEventArgs e)
    {
        if (!_showBulletHoverTooltips)
        {
            HideBulletHoverTooltip();
            return;
        }

        if (doc.Editor?.Document == null || doc.Editor.Document.LineCount == 0)
        {
            HideBulletHoverTooltip();
            return;
        }

        var textView = doc.Editor.TextArea.TextView;
        var position = doc.Editor.GetPositionFromPoint(e.GetPosition(textView));
        if (!position.HasValue)
        {
            HideBulletHoverTooltip();
            return;
        }

        int lineNumber = position.Value.Location.Line;
        if (lineNumber <= 0 || lineNumber > doc.Editor.Document.LineCount)
        {
            HideBulletHoverTooltip();
            return;
        }

        var line = doc.Editor.Document.GetLineByNumber(lineNumber);
        var lineText = doc.Editor.Document.GetText(line.Offset, line.Length);
        if (!TryGetBulletLineMarker(lineText, out var marker))
        {
            HideBulletHoverTooltip();
            return;
        }

        // Only trigger when hovering over the bullet prefix area ("- " / "* "),
        // not anywhere on the line.
        var mousePos = e.GetPosition(textView);
        if (!TryGetBulletPrefixHitRect(textView, lineNumber, out var bulletRect)
            || !bulletRect.Contains(mousePos))
        {
            HideBulletHoverTooltip();
            return;
        }

        // Bullet metadata may be missing for older files: show Unknown in that case.
        _ = TryGetLineBullet(doc, lineNumber, out _, out var createdUtc);

        var key = BuildBulletHoverKey(lineNumber, marker, createdUtc);
        if (_bulletHoverTooltip != null
            && _bulletHoverTooltip.IsOpen
            && _bulletHoverTooltipDoc == doc
            && _bulletHoverTooltipKey == key)
        {
            return;
        }

        ShowBulletHoverTooltip(doc, lineNumber, marker, createdUtc, key);
    }

    private static bool TryGetBulletPrefixHitRect(TextView textView, int lineNumber, out Rect rect)
    {
        rect = Rect.Empty;
        try
        {
            // Column is 1-based. Bullet prefix is the first two chars: [1..3) == "- ".
            var startTop = textView.GetVisualPosition(new TextViewPosition(lineNumber, 1), VisualYPosition.LineTop);
            var endTop = textView.GetVisualPosition(new TextViewPosition(lineNumber, 3), VisualYPosition.LineTop);
            var startBottom = textView.GetVisualPosition(new TextViewPosition(lineNumber, 1), VisualYPosition.LineBottom);

            if (double.IsNaN(startTop.X) || double.IsNaN(startTop.Y)
                || double.IsNaN(endTop.X) || double.IsNaN(endTop.Y)
                || double.IsNaN(startBottom.X) || double.IsNaN(startBottom.Y))
            {
                return false;
            }

            var x1 = Math.Min(startTop.X, endTop.X);
            var x2 = Math.Max(startTop.X, endTop.X);
            var y1 = Math.Min(startTop.Y, startBottom.Y);
            var y2 = Math.Max(startTop.Y, startBottom.Y);

            // Add a tiny horizontal padding so it's not pixel-perfect.
            const double pad = 2;
            rect = new Rect(new Point(Math.Max(0, x1 - pad), y1), new Point(Math.Max(0, x2 + pad), y2));
            return rect.Width > 0 && rect.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildBulletHoverKey(int lineNumber, char marker, DateTime? createdUtc)
        => string.Concat(
            lineNumber.ToString(CultureInfo.InvariantCulture),
            "|",
            marker,
            "|",
            createdUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private void ShowBulletHoverTooltip(TabDocument doc, int lineNumber, char marker, DateTime? createdUtc, string key)
    {
        _bulletHoverTooltip ??= new ToolTip
        {
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 14,
            VerticalOffset = 18,
            HasDropShadow = true,
            StaysOpen = true,
            Focusable = false
        };

        if (_bulletHoverTooltip.IsOpen)
            _bulletHoverTooltip.IsOpen = false;

        _bulletHoverTooltip.PlacementTarget = doc.Editor;
        _bulletHoverTooltip.Content = BuildBulletHoverContent(lineNumber, marker, createdUtc);
        _bulletHoverTooltipDoc = doc;
        _bulletHoverTooltipKey = key;
        _bulletHoverTooltip.IsOpen = true;
    }

    private void HideBulletHoverTooltip()
    {
        if (_bulletHoverTooltip != null && _bulletHoverTooltip.IsOpen)
            _bulletHoverTooltip.IsOpen = false;
        _bulletHoverTooltipDoc = null;
        _bulletHoverTooltipKey = string.Empty;
    }

    private static UIElement BuildBulletHoverContent(int lineNumber, char marker, DateTime? createdUtc)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = "Bullet stats",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });

        if (createdUtc is DateTime created)
        {
            var local = created.ToLocalTime();
            var today = DateTime.Today;
            int daysAgo = Math.Max(0, (int)(today - local.Date).TotalDays);
            int weekdaysAgo = CountWeekdaysBetween(local.Date, today);

            panel.Children.Add(new TextBlock { Text = $"Created: {local:yyyy-MM-dd HH:mm}" });
            panel.Children.Add(new TextBlock { Text = $"Days ago: {daysAgo}" });
            panel.Children.Add(new TextBlock { Text = $"Weekdays ago: {weekdaysAgo}" });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Created: (unknown)",
                Foreground = SystemColors.GrayTextBrush
            });
        }

        return new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            Child = panel
        };
    }

    private static string BuildAssigneeHoverKey(TabDocument.AssigneeBadgeBounds badge)
        => string.Concat(
            badge.LineNumber.ToString(CultureInfo.InvariantCulture),
            "|",
            badge.Person,
            "|",
            badge.CreatedUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private void ShowAssigneeHoverTooltip(TabDocument doc, TabDocument.AssigneeBadgeBounds badge, string key)
    {
        _assigneeHoverTooltip ??= new ToolTip
        {
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 14,
            VerticalOffset = 18,
            HasDropShadow = true,
            StaysOpen = true,
            Focusable = false
        };

        // Force-close before reopening so the tooltip re-anchors to the new mouse point
        // when moving between badges.
        if (_assigneeHoverTooltip.IsOpen)
            _assigneeHoverTooltip.IsOpen = false;

        _assigneeHoverTooltip.PlacementTarget = doc.Editor;
        _assigneeHoverTooltip.Content = BuildAssigneeHoverContent(badge);
        _assigneeHoverTooltipDoc = doc;
        _assigneeHoverTooltipKey = key;
        _assigneeHoverTooltip.IsOpen = true;
    }

    private void HideAssigneeHoverTooltip()
    {
        if (_assigneeHoverTooltip != null && _assigneeHoverTooltip.IsOpen)
            _assigneeHoverTooltip.IsOpen = false;
        _assigneeHoverTooltipDoc = null;
        _assigneeHoverTooltipKey = string.Empty;
    }

    private static UIElement BuildAssigneeHoverContent(TabDocument.AssigneeBadgeBounds badge)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Assigned to: {badge.Person}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });

        if (badge.CreatedUtc is DateTime createdUtc)
        {
            var local = createdUtc.ToLocalTime();
            var today = DateTime.Today;
            int daysAgo = Math.Max(0, (int)(today - local.Date).TotalDays);
            int weekdaysAgo = CountWeekdaysBetween(local.Date, today);

            panel.Children.Add(new TextBlock { Text = $"Created: {local:yyyy-MM-dd HH:mm}" });
            panel.Children.Add(new TextBlock { Text = $"Days ago: {daysAgo}" });
            panel.Children.Add(new TextBlock { Text = $"Weekdays ago: {weekdaysAgo}" });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Created: (unknown)",
                Foreground = SystemColors.GrayTextBrush
            });
        }

        return new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            Child = panel
        };
    }

    private static int CountWeekdaysBetween(DateTime startInclusive, DateTime endInclusive)
    {
        if (endInclusive <= startInclusive)
            return 0;

        int totalDays = (int)(endInclusive.Date - startInclusive.Date).TotalDays;
        int fullWeeks = totalDays / 7;
        int weekdays = fullWeeks * 5;
        int remainder = totalDays - (fullWeeks * 7);
        var startDow = (int)startInclusive.DayOfWeek;
        for (int i = 1; i <= remainder; i++)
        {
            var day = (DayOfWeek)((startDow + i) % 7);
            if (day != DayOfWeek.Saturday && day != DayOfWeek.Sunday)
                weekdays++;
        }
        return weekdays;
    }

    private IReadOnlyDictionary<int, (string Person, DateTime? CreatedUtc)> GetLineAssigneeDetails(TabDocument doc)
    {
        if (doc.LineAssigneeAnchors.Count == 0)
            return new Dictionary<int, (string, DateTime?)>();

        var result = new Dictionary<int, (string, DateTime?)>();
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

            result[anchor.Line] = (person, entry.CreatedUtc);
        }

        return result;
    }

    private bool TryGetLineAssignee(TabDocument doc, int lineNumber, out string person)
        => TryGetLineAssignee(doc, lineNumber, out person, out _);

    private bool TryGetLineAssignee(TabDocument doc, int lineNumber, out string person, out DateTime? createdUtc)
    {
        person = string.Empty;
        createdUtc = null;
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

            createdUtc = entry.CreatedUtc;
            return true;
        }

        return false;
    }

    private bool SetLineAssignee(TabDocument doc, int lineNumber, string person, bool markDirty = true, bool redraw = true, DateTime? createdUtc = null)
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
                    // Reassignment to a different person resets the creation timestamp.
                    entry.CreatedUtc = createdUtc;
                    changed = true;
                }
                else if (createdUtc.HasValue && entry.CreatedUtc != createdUtc)
                {
                    // Restoring a known timestamp (e.g., loading from disk).
                    entry.CreatedUtc = createdUtc;
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
            // Keep assignee badges attached to the original line content when text is
            // inserted/deleted at the beginning of that line (e.g., Shift+Delete above).
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            doc.LineAssigneeAnchors.Add(new TabDocument.LineAssigneeAnchor
            {
                Anchor = anchor,
                Person = cleaned,
                CreatedUtc = createdUtc
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

                SetLineAssignee(
                    doc,
                    assignee.Line,
                    assignee.Person,
                    markDirty: false,
                    redraw: false,
                    createdUtc: assignee.CreatedUtc);
            }
        }

        if (markDirty)
            MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void PushLineAssigneeUndoRecord(TabDocument doc, IReadOnlyList<LineAssigneeChangeEntry> entries)
    {
        if (entries.Count == 0)
            return;

        if (!_lineAssigneeUndoHistory.TryGetValue(doc, out var undoStack))
        {
            undoStack = new Stack<LineAssigneeChangeRecord>();
            _lineAssigneeUndoHistory[doc] = undoStack;
        }

        undoStack.Push(new LineAssigneeChangeRecord { Entries = entries });

        if (!_lineAssigneeRedoHistory.TryGetValue(doc, out var redoStack))
        {
            redoStack = new Stack<LineAssigneeChangeRecord>();
            _lineAssigneeRedoHistory[doc] = redoStack;
        }
        else
        {
            redoStack.Clear();
        }
    }

    private bool ApplyLineAssigneeRecord(TabDocument doc, LineAssigneeChangeRecord record, bool useBeforeState)
    {
        bool changed = false;
        foreach (var entry in record.Entries)
        {
            var targetPerson = useBeforeState ? entry.BeforePerson : entry.AfterPerson;
            var targetCreatedUtc = useBeforeState ? entry.BeforeCreatedUtc : entry.AfterCreatedUtc;
            if (string.IsNullOrWhiteSpace(targetPerson))
                changed |= RemoveLineAssignee(doc, entry.LineNumber, markDirty: false, redraw: false);
            else
                changed |= SetLineAssignee(doc, entry.LineNumber, targetPerson, markDirty: false, redraw: false, createdUtc: targetCreatedUtc);
        }

        if (changed)
        {
            MarkDirty(doc);
            RedrawHighlight(doc);
        }

        return changed;
    }

    private bool TryUndoLineAssigneeChange(TabDocument doc)
    {
        if (!_lineAssigneeUndoHistory.TryGetValue(doc, out var undoStack) || undoStack.Count == 0)
            return false;

        var record = undoStack.Pop();
        bool changed = ApplyLineAssigneeRecord(doc, record, useBeforeState: true);
        if (!changed)
            return false;

        if (!_lineAssigneeRedoHistory.TryGetValue(doc, out var redoStack))
        {
            redoStack = new Stack<LineAssigneeChangeRecord>();
            _lineAssigneeRedoHistory[doc] = redoStack;
        }

        redoStack.Push(record);
        return true;
    }

    private bool TryRedoLineAssigneeChange(TabDocument doc)
    {
        if (!_lineAssigneeRedoHistory.TryGetValue(doc, out var redoStack) || redoStack.Count == 0)
            return false;

        var record = redoStack.Pop();
        bool changed = ApplyLineAssigneeRecord(doc, record, useBeforeState: false);
        if (!changed)
            return false;

        if (!_lineAssigneeUndoHistory.TryGetValue(doc, out var undoStack))
        {
            undoStack = new Stack<LineAssigneeChangeRecord>();
            _lineAssigneeUndoHistory[doc] = undoStack;
        }

        undoStack.Push(record);
        return true;
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
        var undoEntries = new List<LineAssigneeChangeEntry>();
        var assignmentTimestamp = DateTime.UtcNow;
        foreach (var line in lines)
        {
            string? beforePerson = TryGetLineAssignee(doc, line, out var owner, out var beforeCreatedUtc) ? owner : null;
            DateTime? beforeCreated = beforePerson != null ? beforeCreatedUtc : null;
            if (SetLineAssignee(doc, line, cleaned, markDirty: false, redraw: false, createdUtc: assignmentTimestamp))
            {
                changed = true;
                undoEntries.Add(new LineAssigneeChangeEntry
                {
                    LineNumber = line,
                    BeforePerson = beforePerson,
                    AfterPerson = cleaned,
                    BeforeCreatedUtc = beforeCreated,
                    AfterCreatedUtc = assignmentTimestamp
                });
            }
        }

        if (changed)
        {
            PushLineAssigneeUndoRecord(doc, undoEntries);
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
        var undoEntries = new List<LineAssigneeChangeEntry>();
        foreach (var line in lines)
        {
            if (!TryGetLineAssignee(doc, line, out var beforePerson, out var beforeCreatedUtc))
                continue;

            if (RemoveLineAssignee(doc, line, markDirty: false, redraw: false))
            {
                changed = true;
                undoEntries.Add(new LineAssigneeChangeEntry
                {
                    BeforeCreatedUtc = beforeCreatedUtc,
                    LineNumber = line,
                    BeforePerson = beforePerson,
                    AfterPerson = null
                });
            }
        }

        if (changed)
        {
            PushLineAssigneeUndoRecord(doc, undoEntries);
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
        AddShortcutBinding(_shortcutToggleCriticalHighlight, ToggleCriticalHighlightCommand);
        AddShortcutBinding(_shortcutGoToLine, GoToLineCommand);
        AddShortcutBinding(_shortcutGoToTab, GoToTabCommand);
        AddShortcutBinding(DefaultShortcutSwitchToPreviousTab, SwitchToPreviousTabCommand);
        AddShortcutBinding(DefaultShortcutToggleTodoPanel, ToggleTodoPanelCommand);
        AddShortcutBinding(_shortcutMidiPlayer, ToggleMidiPlayerCommand);
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

    private void ExecuteSwitchToPreviousTab()
    {
        var target = _previousSelectedTab;
        if (target == null || !MainTabControl.Items.Contains(target))
            return;
        if (ReferenceEquals(MainTabControl.SelectedItem, target))
            return;

        MainTabControl.SelectedItem = target;
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

        int unchangedPrefixLength = 0;
        int maxPrefixLength = Math.Min(text.Length, updated.Length);
        while (unchangedPrefixLength < maxPrefixLength
               && text[unchangedPrefixLength] == updated[unchangedPrefixLength])
        {
            unchangedPrefixLength++;
        }

        int caretOffset = Math.Min(doc.Editor.CaretOffset, updated.Length);
        int removedLength = text.Length - unchangedPrefixLength;
        string insertedTail = updated[unchangedPrefixLength..];
        doc.Editor.Document.Replace(unchangedPrefixLength, removedLength, insertedTail);
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

    private void ExecuteToggleCriticalHighlight()
    {
        var doc = CurrentDoc();
        if (doc != null)
            ToggleCriticalHighlightedCaretLine(doc);
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
        const double compactFindDialogWidth = 520;
        const double expandedFindDialogWidth = 760;
        const double compactFindDialogHeight = 340;
        const double expandedFindDialogHeight = 470;

        var dlg = new Window
        {
            Title = "Find + Replace",
            Width = compactFindDialogWidth,
            Height = compactFindDialogHeight,
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

        var tabMatchesPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        var tabMatchesScroll = new ScrollViewer
        {
            Content = tabMatchesPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 62,
            MaxHeight = 180
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

        List<int> GetMatchOffsets(string text, string findText, StringComparison comparison, bool wholeWord)
        {
            var offsets = new List<int>();
            if (string.IsNullOrEmpty(findText))
                return offsets;

            int scanIndex = 0;
            while (TryFindNextIndex(text, findText, scanIndex, comparison, wholeWord, out var found))
            {
                offsets.Add(found);
                scanIndex = found + findText.Length;
            }

            return offsets;
        }

        void AppendHighlightedLineRuns(InlineCollection inlines, string lineText, string findText, StringComparison comparison, bool wholeWord)
        {
            if (string.IsNullOrEmpty(findText))
            {
                inlines.Add(new Run(lineText));
                return;
            }

            int scanIndex = 0;
            while (TryFindNextIndex(lineText, findText, scanIndex, comparison, wholeWord, out var found))
            {
                if (found > scanIndex)
                    inlines.Add(new Run(lineText.Substring(scanIndex, found - scanIndex)));

                inlines.Add(new Run(lineText.Substring(found, findText.Length))
                {
                    Background = SystemColors.HighlightBrush,
                    Foreground = SystemColors.HighlightTextBrush,
                    FontWeight = FontWeights.SemiBold
                });

                scanIndex = found + findText.Length;
            }

            if (scanIndex < lineText.Length)
                inlines.Add(new Run(lineText.Substring(scanIndex)));
        }

        Border CreateResultRow(TextBlock content, Action onClick)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.White,
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Child = content
            };

            row.MouseEnter += (_, _) => row.Background = Brushes.AliceBlue;
            row.MouseLeave += (_, _) => row.Background = Brushes.White;
            row.MouseLeftButtonUp += (_, _) => onClick();
            return row;
        }

        void RefreshTabMatchButtons()
        {
            tabMatchesPanel.Children.Clear();

            var needle = txtFind.Text ?? string.Empty;
            if (chkAllTabs.IsChecked != true || string.IsNullOrEmpty(needle))
            {
                tabMatchesBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var comparison = GetComparison();
            bool wholeWord = chkWholeWord.IsChecked == true;
            bool supportsLinePreview = !needle.Contains('\n') && !needle.Contains('\r');
            var matches = new List<(TabItem Tab, TabDocument Doc, int Count, int FirstOffset, List<(int LineNumber, string Text, int FirstOffset)> Lines)>();
            foreach (var tab in MainTabControl.Items.OfType<TabItem>())
            {
                if (!_docs.TryGetValue(tab, out var tabDoc))
                    continue;

                var offsets = GetMatchOffsets(tabDoc.Editor.Text, needle, comparison, wholeWord);
                if (offsets.Count == 0)
                    continue;

                var lineMatches = new List<(int LineNumber, string Text, int FirstOffset)>();
                var addedLines = new HashSet<int>();
                if (supportsLinePreview)
                {
                    var document = tabDoc.Editor.Document;
                    foreach (var offset in offsets)
                    {
                        var line = document.GetLineByOffset(offset);
                        if (!addedLines.Add(line.LineNumber))
                            continue;

                        string lineText = document.GetText(line).TrimEnd('\r', '\n');
                        lineMatches.Add((line.LineNumber, lineText, offset));
                    }
                }

                matches.Add((tab, tabDoc, offsets.Count, offsets[0], lineMatches));
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
                if (item.Lines.Count == 0)
                {
                    var fallbackText = new TextBlock
                    {
                        TextWrapping = TextWrapping.NoWrap
                    };
                    fallbackText.Inlines.Add(new Run($"{targetDoc.DisplayHeader} ({item.Count}): ")
                    {
                        Foreground = Brushes.DimGray,
                        FontWeight = FontWeights.SemiBold
                    });
                    fallbackText.Inlines.Add(new Run("jump to first match"));
                    var fallbackRow = CreateResultRow(fallbackText, () => JumpToMatch(targetTab, item.FirstOffset, needle.Length));
                    tabMatchesPanel.Children.Add(fallbackRow);
                    continue;
                }

                foreach (var line in item.Lines)
                {
                    var lineText = new TextBlock
                    {
                        TextWrapping = TextWrapping.NoWrap
                    };
                    lineText.Inlines.Add(new Run($"{targetDoc.DisplayHeader} ({item.Count}) - {line.LineNumber}: ")
                    {
                        Foreground = Brushes.DimGray
                    });
                    AppendHighlightedLineRuns(lineText.Inlines, line.Text, needle, comparison, wholeWord);
                    int lineOffset = line.FirstOffset;
                    var lineRow = CreateResultRow(lineText, () => JumpToMatch(targetTab, lineOffset, needle.Length));
                    tabMatchesPanel.Children.Add(lineRow);
                }
            }

            if (!supportsLinePreview)
                status.Text = "Line previews are shown only for single-line find text.";
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
        chkAllTabs.Checked += (_, _) =>
        {
            dlg.Width = expandedFindDialogWidth;
            dlg.Height = expandedFindDialogHeight;
            RefreshTabMatchButtons();
        };
        chkAllTabs.Unchecked += (_, _) =>
        {
            dlg.Width = compactFindDialogWidth;
            dlg.Height = compactFindDialogHeight;
            RefreshTabMatchButtons();
        };
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

        static bool HeaderMatchesFilter(string header, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            var tokens = filter
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0)
                return true;

            return tokens.All(token => header.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        void RefreshList()
        {
            var filter = (input.Text ?? string.Empty).Trim();
            var filtered = tabs
                .Where(tab =>
                {
                    var header = (tab.Tag as TabDocument)?.Header ?? tab.Header?.ToString() ?? string.Empty;
                    return HeaderMatchesFilter(header, filter);
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
        if (MenuItemMidiPlayer != null)
            MenuItemMidiPlayer.InputGestureText = GestureDisplayText(_shortcutMidiPlayer);
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
        if (string.IsNullOrWhiteSpace(doc.CachedText))
            return;

        _closedTabHistory.Insert(0, new ClosedTabEntry
        {
            Header = doc.Header,
            Content = doc.CachedText,
            IsDirty = doc.IsDirty,
            Metadata = CreateFileMetadata(doc),
            ClosedAtUtc = DateTime.UtcNow
        });

        SaveClosedTabHistory();
    }

    private static int NormalizeClosedTabsMaxCount(int? maxCount)
    {
        var value = maxCount ?? DefaultClosedTabsMaxCount;
        return Math.Clamp(value, MinClosedTabsMaxCount, MaxClosedTabsMaxCount);
    }

    private static int NormalizeClosedTabsRetentionDays(int? retentionDays)
    {
        var value = retentionDays ?? DefaultClosedTabsRetentionDays;
        return Math.Clamp(value, MinClosedTabsRetentionDays, MaxClosedTabsRetentionDays);
    }

    private void NormalizeClosedTabHistory()
    {
        var normalized = _closedTabsService.NormalizeHistory(
            _closedTabHistory,
            _closedTabsMaxCount,
            _closedTabsRetentionDays);
        _closedTabHistory.Clear();
        _closedTabHistory.AddRange(normalized);
    }

    private void SaveClosedTabHistory()
    {
        try
        {
            NormalizeClosedTabHistory();
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

            _closedTabHistory.AddRange(_closedTabsService.NormalizeHistory(parsed, _closedTabsMaxCount, _closedTabsRetentionDays));
            SaveClosedTabHistory();
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

        RestoreClosedTabAt(0);
    }

    private static DateTime ResolveClosedTabTimestampUtc(ClosedTabEntry entry)
        => entry.ClosedAtUtc?.ToUniversalTime()
            ?? entry.Metadata?.LastChangedUtc?.ToUniversalTime()
            ?? entry.Metadata?.LastSavedUtc?.ToUniversalTime()
            ?? DateTime.UtcNow;

    private static string BuildClosedTabRecoveryDisplayText(ClosedTabEntry entry)
    {
        var statusText = entry.IsDirty ? "unsaved" : "saved";
        var closedAtLocal = ResolveClosedTabTimestampUtc(entry).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return $"{entry.Header} ({statusText}, closed {closedAtLocal})";
    }

    private void RestoreClosedTabAt(int index)
    {
        if (index < 0 || index >= _closedTabHistory.Count)
            return;

        var entry = _closedTabHistory[index];
        _closedTabHistory.RemoveAt(index);
        SaveClosedTabHistory();
        RestoreClosedTabEntry(entry);
    }

    private void RestoreClosedTabEntry(ClosedTabEntry entry)
    {
        var doc = CreateTab(entry.Header, entry.Content);
        var highlightedLines = entry.Metadata?.HighlightLines ?? [];
        if (highlightedLines.Count == 0 && entry.Metadata?.HighlightLine is int legacyHighlightLine && legacyHighlightLine > 0)
            highlightedLines = [legacyHighlightLine];
        var criticalHighlightedLines = entry.Metadata?.CriticalHighlightLines ?? [];
        SetHighlightedLines(doc, highlightedLines, markDirty: false);
        SetCriticalHighlightedLines(doc, criticalHighlightedLines, markDirty: false);
        SetLineAssignments(doc, entry.Metadata?.Assignees, markDirty: false);
        SetLineBullets(doc, entry.Metadata?.Bullets, markDirty: false);
        RestoreCaretPosition(doc, entry.Metadata);
        doc.LastSavedUtc = entry.Metadata?.LastSavedUtc?.ToUniversalTime();
        doc.LastChangedUtc = entry.Metadata?.LastChangedUtc?.ToUniversalTime()
            ?? entry.Metadata?.LastSavedUtc?.ToUniversalTime()
            ?? DateTime.UtcNow;
        doc.IsDirty = entry.IsDirty;
        RefreshTabHeader(doc);
    }

    private void ShowRecoverTabsDialog()
    {
        var dlg = new Window
        {
            Title = "Recover Tabs",
            Width = 980,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.CanResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        var btnRestore = new Button
        {
            Content = "Restore Selected",
            Width = 130,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var btnDelete = new Button
        {
            Content = "Delete Selected",
            Width = 120,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnClose = new Button { Content = "Close", Width = 90, IsCancel = true };
        buttonRow.Children.Add(btnRestore);
        buttonRow.Children.Add(btnDelete);
        buttonRow.Children.Add(btnClose);
        root.Children.Add(buttonRow);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var lblList = new TextBlock
        {
            Text = "Closed tabs (newest first):",
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetColumn(lblList, 0);
        Grid.SetRow(lblList, 0);
        body.Children.Add(lblList);

        var list = new ListBox
        {
            MinHeight = 360
        };
        Grid.SetColumn(list, 0);
        Grid.SetRow(list, 1);
        body.Children.Add(list);

        var previewPanel = new Grid();
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(previewPanel, 2);
        Grid.SetRow(previewPanel, 1);
        body.Children.Add(previewPanel);

        var txtDetails = new TextBlock
        {
            Text = "Select a tab to preview.",
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(txtDetails, 0);
        previewPanel.Children.Add(txtDetails);

        var txtPreview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
        Grid.SetRow(txtPreview, 1);
        previewPanel.Children.Add(txtPreview);

        root.Children.Add(body);
        dlg.Content = root;

        void RefreshList(int? preferredIndex = null)
        {
            list.Items.Clear();
            for (var i = 0; i < _closedTabHistory.Count; i++)
            {
                var entry = _closedTabHistory[i];
                list.Items.Add(new ListBoxItem
                {
                    Content = BuildClosedTabRecoveryDisplayText(entry),
                    Tag = i
                });
            }

            if (list.Items.Count == 0)
            {
                txtDetails.Text = "No closed tabs available.";
                txtPreview.Text = string.Empty;
                btnRestore.IsEnabled = false;
                btnDelete.IsEnabled = false;
                return;
            }

            var indexToSelect = preferredIndex ?? 0;
            if (indexToSelect < 0 || indexToSelect >= list.Items.Count)
                indexToSelect = list.Items.Count - 1;
            list.SelectedIndex = indexToSelect;
            btnRestore.IsEnabled = true;
            btnDelete.IsEnabled = true;
        }

        void UpdatePreview()
        {
            if (list.SelectedItem is not ListBoxItem selectedItem
                || selectedItem.Tag is not int selectedIndex
                || selectedIndex < 0
                || selectedIndex >= _closedTabHistory.Count)
            {
                txtDetails.Text = "Select a tab to preview.";
                txtPreview.Text = string.Empty;
                btnRestore.IsEnabled = false;
                btnDelete.IsEnabled = false;
                return;
            }

            var entry = _closedTabHistory[selectedIndex];
            var statusText = entry.IsDirty ? "Unsaved changes" : "No unsaved changes";
            var closedText = ResolveClosedTabTimestampUtc(entry).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            txtDetails.Text = $"{entry.Header}  |  {statusText}  |  Closed: {closedText}";
            txtPreview.Text = entry.Content ?? string.Empty;
            btnRestore.IsEnabled = true;
            btnDelete.IsEnabled = true;
        }

        void RestoreSelected()
        {
            if (list.SelectedItem is not ListBoxItem selectedItem
                || selectedItem.Tag is not int selectedIndex
                || selectedIndex < 0
                || selectedIndex >= _closedTabHistory.Count)
            {
                return;
            }

            RestoreClosedTabAt(selectedIndex);
            var nextIndex = Math.Min(selectedIndex, _closedTabHistory.Count - 1);
            RefreshList(nextIndex);
        }

        void DeleteSelected()
        {
            if (list.SelectedItem is not ListBoxItem selectedItem
                || selectedItem.Tag is not int selectedIndex
                || selectedIndex < 0
                || selectedIndex >= _closedTabHistory.Count)
            {
                return;
            }

            var entry = _closedTabHistory[selectedIndex];
            var confirm = MessageBox.Show(
                $"Delete closed tab '{entry.Header}' from recovery history?\n\nThis cannot be undone.",
                "Delete Closed Tab",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            _closedTabHistory.RemoveAt(selectedIndex);
            SaveClosedTabHistory();
            var nextIndex = Math.Min(selectedIndex, _closedTabHistory.Count - 1);
            RefreshList(nextIndex);
        }

        list.SelectionChanged += (_, _) => UpdatePreview();
        list.MouseDoubleClick += (_, _) => RestoreSelected();
        btnRestore.Click += (_, _) => RestoreSelected();
        btnDelete.Click += (_, _) => DeleteSelected();
        btnClose.Click += (_, _) => dlg.Close();

        RefreshList();
        dlg.ShowDialog();
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
        var criticalHighlighted = GetCriticalHighlightedLineNumbers(doc).OrderBy(line => line).ToList();
        var assignees = GetLineAssigneeDetails(doc)
            .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value.Person))
            .Select(pair => new FileLineAssignee
            {
                Line = pair.Key,
                Person = pair.Value.Person,
                CreatedUtc = pair.Value.CreatedUtc
            })
            .OrderBy(entry => entry.Line)
            .ToList();

        var bullets = GetLineBulletDetails(doc)
            .Where(pair => pair.Key > 0)
            .Select(pair => new FileLineBullet
            {
                Line = pair.Key,
                Marker = pair.Value.Marker.ToString(),
                CreatedUtc = pair.Value.CreatedUtc
            })
            .OrderBy(entry => entry.Line)
            .ToList();

        return new FileMetadata
        {
            HighlightLines = highlighted.Count > 0 ? highlighted : null,
            CriticalHighlightLines = criticalHighlighted.Count > 0 ? criticalHighlighted : null,
            Assignees = assignees.Count > 0 ? assignees : null,
            Bullets = bullets.Count > 0 ? bullets : null,
            LastSavedUtc = doc.LastSavedUtc,
            LastChangedUtc = doc.LastChangedUtc,
            CaretOffset = doc.Editor.CaretOffset
        };
    }

    private IReadOnlyDictionary<int, (char Marker, DateTime? CreatedUtc)> GetLineBulletDetails(TabDocument doc)
    {
        if (doc.LineBulletAnchors.Count == 0)
            return new Dictionary<int, (char, DateTime?)>();

        var result = new Dictionary<int, (char, DateTime?)>();
        for (int i = doc.LineBulletAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineBulletAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }

            if (doc.Editor.Document == null || anchor.Line > doc.Editor.Document.LineCount)
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }

            var line = doc.Editor.Document.GetLineByNumber(anchor.Line);
            var lineText = doc.Editor.Document.GetText(line.Offset, line.Length);
            if (!TryGetBulletLineMarker(lineText, out var marker))
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }

            entry.Marker = marker;
            result[anchor.Line] = (marker, entry.CreatedUtc);
        }

        return result;
    }

    private void SetLineBullets(TabDocument doc, IEnumerable<FileLineBullet>? bullets, bool markDirty = true)
    {
        doc.LineBulletAnchors.Clear();

        if (bullets != null)
        {
            foreach (var bullet in bullets)
            {
                if (bullet == null || bullet.Line <= 0)
                    continue;

                var marker = (bullet.Marker ?? string.Empty).Trim();
                char markerChar = marker.Length > 0 ? marker[0] : '-';
                if (markerChar is not ('-' or '*'))
                    markerChar = '-';

                SetLineBullet(doc, bullet.Line, markerChar, bullet.CreatedUtc);
            }
        }

        if (markDirty)
            MarkDirty(doc);
        RedrawHighlight(doc);
    }

    private void EnsureBulletMetadataForNewBullets(TabDocument doc)
    {
        if (doc.Editor?.Document == null || doc.Editor.Document.LineCount <= 0)
            return;

        var existing = new HashSet<int>();
        for (int i = doc.LineBulletAnchors.Count - 1; i >= 0; i--)
        {
            var entry = doc.LineBulletAnchors[i];
            var anchor = entry.Anchor;
            if (anchor == null || anchor.IsDeleted || anchor.Line <= 0)
            {
                doc.LineBulletAnchors.RemoveAt(i);
                continue;
            }
            existing.Add(anchor.Line);
        }

        var nowUtc = DateTime.UtcNow;
        for (int lineNumber = 1; lineNumber <= doc.Editor.Document.LineCount; lineNumber++)
        {
            var line = doc.Editor.Document.GetLineByNumber(lineNumber);
            var lineText = doc.Editor.Document.GetText(line.Offset, line.Length);
            if (!TryGetBulletLineMarker(lineText, out var marker))
                continue;

            if (existing.Contains(lineNumber))
                continue;

            SetLineBullet(doc, lineNumber, marker, createdUtc: nowUtc);
        }
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
            var activeDoc = CurrentDoc();
            foreach (var doc in _docs.Values)
            {
                var originalText = doc.Editor.Text;
                int preservedLineNumber = doc == activeDoc ? doc.Editor.TextArea.Caret.Line : 0;
                // Replacing Editor.Text creates a new document and invalidates TextAnchors.
                // Snapshot line-based state first so assignees/highlights survive normalization.
                var assigneeSnapshot = GetLineAssigneeDetails(doc)
                    .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value.Person))
                    .Select(pair => new FileLineAssignee
                    {
                        Line = pair.Key,
                        Person = pair.Value.Person,
                        CreatedUtc = pair.Value.CreatedUtc
                    })
                    .ToList();
                var highlightSnapshot = GetHighlightedLineNumbers(doc).OrderBy(line => line).ToList();
                var criticalHighlightSnapshot = GetCriticalHighlightedLineNumbers(doc).OrderBy(line => line).ToList();

                var normalizedText = RemoveTrailingWhitespacesExceptLine(originalText, preservedLineNumber);
                normalizedText = UnifyMarkdownListBulletMarkersForSave(normalizedText, _saveBulletsAsMarker);
                if (!string.Equals(originalText, normalizedText, StringComparison.Ordinal))
                {
                    int caretOffset = Math.Min(doc.Editor.CaretOffset, normalizedText.Length);
                    doc.Editor.Text = normalizedText;
                    doc.Editor.CaretOffset = caretOffset;
                    SetHighlightedLines(doc, highlightSnapshot.Count > 0 ? highlightSnapshot : null, markDirty: false);
                    SetCriticalHighlightedLines(doc, criticalHighlightSnapshot.Count > 0 ? criticalHighlightSnapshot : null, markDirty: false);
                    SetLineAssignments(doc, assigneeSnapshot.Count > 0 ? assigneeSnapshot : null, markDirty: false);
                }

                doc.CachedText = normalizedText;

                // Assign CreatedUtc for newly created/detected bullet lines so the timestamp
                // is persisted into FileMetadata on this save.
                EnsureBulletMetadataForNewBullets(doc);
            }
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

        long sourceSizeBytes = GetFileSizeBytesOrZero(justSavedBackupPath);
        var cloudCopyStopwatch = Stopwatch.StartNew();

        if (!_backupService.TryCopyBackupToFolder(justSavedBackupPath, cloudFolder))
        {
            cloudCopyStopwatch.Stop();
            AppendAppLog(
                $"Cloud copy completed with failure in {cloudCopyStopwatch.ElapsedMilliseconds} ms. size={FormatSizeForLog(sourceSizeBytes)}, throughput={FormatThroughputForLog(sourceSizeBytes, cloudCopyStopwatch.Elapsed)}. source='{justSavedBackupPath}', target='{cloudFolder}'");
            return;
        }

        // Sync sidecars (including noted.log) before appending lines for this run, so incremental copy compares pre-log snapshot.
        var extraArtifacts = CopySelectedAdditionalBackupArtifacts(_backupFolder, cloudFolder);
        AppendAppLog($"Cloud copy started. source='{justSavedBackupPath}', target='{cloudFolder}', manualCloudSave={forceCloudBackup}");
        AppendAppLog($"Cloud copying additional files - {extraArtifacts.ToLogLine()}");

        _lastCloudSaveUtc = DateTime.UtcNow;
        _lastSaveIncludedCloudCopy = true;
        if (persistCloudMetadata)
            SaveWindowSettings();

        cloudCopyStopwatch.Stop();
        AppendAppLog(
            $"Cloud copy completed successfully in {cloudCopyStopwatch.ElapsedMilliseconds} ms. size={FormatSizeForLog(sourceSizeBytes)}, throughput={FormatThroughputForLog(sourceSizeBytes, cloudCopyStopwatch.Elapsed)}. source='{justSavedBackupPath}', target='{cloudFolder}'");
    }

    private static long GetFileSizeBytesOrZero(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSizeForLog(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} bytes";

        var kilobytes = bytes / 1024d;
        return $"{kilobytes:F2} KB ({bytes} bytes)";
    }

    private static string FormatThroughputForLog(long bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001d);
        var bytesPerSecond = bytes / seconds;

        if (bytesPerSecond < 1024d)
            return $"{bytesPerSecond:F2} bytes/s";

        return $"{(bytesPerSecond / 1024d):F2} KB/s";
    }

    private void AppendAppLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(_backupFolder))
                return;

            Directory.CreateDirectory(_backupFolder);
            var logPath = Path.Combine(_backupFolder, AppLogFileName);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            File.AppendAllText(logPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best effort; never block save flow.
        }
    }

    private void StartBackupHeartbeatTimer()
    {
        _backupHeartbeatTimer.Stop();
        if (!_writeUptimeHeartbeatInNoted)
        {
            _uptimeHeartbeatStartupBeatWritten = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_backupFolder))
            _lastBackupHeartbeatAtLocal = UptimeHeartbeatService.ReadLastHeartbeatTimestamp(GetUptimeHeartbeatFilePath(DateTimeOffset.Now)) ?? DateTimeOffset.MinValue;

        if (!_uptimeHeartbeatStartupBeatWritten && !string.IsNullOrWhiteSpace(_backupFolder))
        {
            try
            {
                UptimeHeartbeatService.AppendHeartbeatTimestamp(
                    _backupFolder,
                    DateTimeOffset.Now,
                    _audioSessionSnapshotService.CaptureOutputAudioSummary,
                    "n",
                    ref _lastBackupHeartbeatAtLocal,
                    markAsStartup: true);
            }
            catch
            {
                // Best-effort activity marker; ignore failures.
            }

            _uptimeHeartbeatStartupBeatWritten = true;
        }

        ScheduleNextBackupHeartbeatTick();
    }

    private DateTimeOffset GetNextBackupHeartbeatAtLocal(DateTimeOffset nowLocal)
        => UptimeHeartbeatService.GetNextHeartbeatAtLocal(nowLocal, _uptimeHeartbeatSeconds, _lastBackupHeartbeatAtLocal);

    private void ScheduleNextBackupHeartbeatTick()
    {
        _nextBackupHeartbeatAtLocal = GetNextBackupHeartbeatAtLocal(DateTimeOffset.Now);
        var wait = _nextBackupHeartbeatAtLocal - DateTimeOffset.Now;
        if (wait <= TimeSpan.Zero)
            wait = TimeSpan.FromMilliseconds(200);

        _backupHeartbeatTimer.Interval = wait;
        _backupHeartbeatTimer.Start();
    }

    private void HandleBackupHeartbeatTick()
    {
        if (!_writeUptimeHeartbeatInNoted)
            return;

        try
        {
            AppendBackupHeartbeatTimestamp(_nextBackupHeartbeatAtLocal);
        }
        catch
        {
            // Best-effort activity marker; ignore failures.
        }
        finally
        {
            ScheduleNextBackupHeartbeatTick();
        }
    }

    private void TryAppendUptimeHeartbeatShutdownBeat()
    {
        if (!_writeUptimeHeartbeatInNoted || string.IsNullOrWhiteSpace(_backupFolder))
            return;

        if (Interlocked.Exchange(ref _uptimeHeartbeatShutdownBeatWritten, 1) != 0)
            return;

        try
        {
            var last = _lastBackupHeartbeatAtLocal;
            if (last == DateTimeOffset.MinValue)
                last = UptimeHeartbeatService.ReadLastHeartbeatTimestamp(GetUptimeHeartbeatFilePath(DateTimeOffset.Now)) ?? DateTimeOffset.MinValue;

            UptimeHeartbeatService.AppendHeartbeatTimestamp(
                _backupFolder,
                DateTimeOffset.Now,
                _audioSessionSnapshotService.CaptureOutputAudioSummary,
                "n",
                ref last,
                markAsShutdown: true);
        }
        catch
        {
            // Best-effort activity marker; ignore failures.
        }
    }

    private void AppendBackupHeartbeatTimestamp(DateTimeOffset timestampLocal)
    {
        UptimeHeartbeatService.AppendHeartbeatTimestamp(
            _backupFolder,
            timestampLocal,
            _audioSessionSnapshotService.CaptureOutputAudioSummary,
            "n",
            ref _lastBackupHeartbeatAtLocal);
    }

    private string GetUptimeHeartbeatFilePath(DateTimeOffset timestampLocal)
        => UptimeHeartbeatService.GetHeartbeatFilePath(_backupFolder, timestampLocal);

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
                var criticalHighlightedLines = metadata.CriticalHighlightLines ?? [];
                SetHighlightedLines(doc, highlightedLines, markDirty: false);
                SetCriticalHighlightedLines(doc, criticalHighlightedLines, markDirty: false);
                SetLineAssignments(doc, metadata.Assignees, markDirty: false);
                SetLineBullets(doc, metadata.Bullets, markDirty: false);
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
        int bulletCount = CountBulletsAtLineStart(doc.CachedText);
        StatusLine.Text = $"Ln {caret.Line}";
        StatusColumn.Text = $"Col {caret.Column}";
        StatusBullets.Text = $"B {bulletCount}";
    }

    private static int CountBulletsAtLineStart(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int count = 0;
        int lineStart = 0;
        while (lineStart < text.Length)
        {
            if (lineStart + 1 < text.Length && text[lineStart + 1] == ' ' &&
                (text[lineStart] == '-' || text[lineStart] == '*'))
                count++;

            int nextLineBreak = text.IndexOf('\n', lineStart);
            if (nextLineBreak < 0)
                break;

            lineStart = nextLineBreak + 1;
        }

        return count;
    }

    private void UpdateViewMenuChecks()
    {
        MenuItemWrapLongLinesVisually.IsChecked = _wrapLongLinesVisually;
        MenuItemStyledBullets.IsChecked = _fancyBulletsEnabled;
        MenuItemShowSmileys.IsChecked = _showSmileys;
        MenuItemRenderStyledTags.IsChecked = _renderStyledTags;
        MenuItemShowLineAssignments.IsChecked = _showLineAssignments;
        MenuItemShowHorizontalRuler.IsChecked = _showHorizontalRuler;
        MenuItemShowInlineImages.IsChecked = _showInlineImages;
    }

    private void ApplyViewRenderingSettings()
    {
        UpdateViewMenuChecks();
        foreach (var doc in _docs.Values)
        {
            ApplyVisualLineWrapSettings(doc.Editor);
            doc.Editor.TextArea.TextView.Redraw();
        }
    }

    // --- Event handlers -------------------------------------------------------

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            var removedTab = e.RemovedItems.OfType<TabItem>().FirstOrDefault();
            if (removedTab != null && MainTabControl.Items.Contains(removedTab))
                _previousSelectedTab = removedTab;
        }

        CloseTagCompletionIfAny();
        HideAssigneeHoverTooltip();
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
    private void MenuCopyFileContentWithoutAssignees_Click(object sender, RoutedEventArgs e) => CopyCurrentTabToClipboard(includeAssignees: false);
    private void MenuCopyFileContentWithAssignees_Click(object sender, RoutedEventArgs e) => CopyCurrentTabToClipboard(includeAssignees: true);
    private void MenuSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsDialog();
    private void MenuStyledBullets_Click(object sender, RoutedEventArgs e)
    {
        _fancyBulletsEnabled = MenuItemStyledBullets.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuWrapLongLinesVisually_Click(object sender, RoutedEventArgs e)
    {
        _wrapLongLinesVisually = MenuItemWrapLongLinesVisually.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuShowSmileys_Click(object sender, RoutedEventArgs e)
    {
        _showSmileys = MenuItemShowSmileys.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuRenderStyledTags_Click(object sender, RoutedEventArgs e)
    {
        _renderStyledTags = MenuItemRenderStyledTags.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuShowLineAssignments_Click(object sender, RoutedEventArgs e)
    {
        _showLineAssignments = MenuItemShowLineAssignments.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuShowHorizontalRuler_Click(object sender, RoutedEventArgs e)
    {
        _showHorizontalRuler = MenuItemShowHorizontalRuler.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuShowInlineImages_Click(object sender, RoutedEventArgs e)
    {
        _showInlineImages = MenuItemShowInlineImages.IsChecked;
        ApplyViewRenderingSettings();
        SaveWindowSettings();
    }
    private void MenuAlarms_Click(object sender, RoutedEventArgs e) => ShowAlarmsDialog();
    private void MenuTags_Click(object sender, RoutedEventArgs e) => ShowTagsDialog();
    private void MenuUsers_Click(object sender, RoutedEventArgs e) => ShowUsersDialog();
    private void MenuTabCleanup_Click(object sender, RoutedEventArgs e) => ShowTabCleanupDialog();
    private void MenuRecoverTabs_Click(object sender, RoutedEventArgs e) => ShowRecoverTabsDialog();
    private void MenuAboutInfo_Click(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();
    private void MenuTimeReport_Click(object sender, RoutedEventArgs e) => ShowTimeReportDialog();
    private void MenuBase64_Click(object sender, RoutedEventArgs e) => ShowBase64Dialog();
    private void MenuQuickMessageOverlay_Click(object sender, RoutedEventArgs e) => ShowQuickMessageOverlayDialog();
    private void MenuMidiPlayer_Click(object sender, RoutedEventArgs e) => OpenOrRestoreMidiPlayer();
    private void MenuCidrConverter_Click(object sender, RoutedEventArgs e) => ShowCidrConverterDialog();
    private void MenuPasswordGenerator_Click(object sender, RoutedEventArgs e) => ShowPasswordGeneratorDialog();
    private void MenuSafePasteArea_Click(object sender, RoutedEventArgs e) => ShowSafePasteAreaDialog();
    private void MenuJwtDecoder_Click(object sender, RoutedEventArgs e) => ShowJwtDecoderDialog();
    private void MenuSearchFiles_Click(object sender, RoutedEventArgs e) => ShowSearchFilesDialog();
    private void MenuTextSplitter_Click(object sender, RoutedEventArgs e) => ShowTextSplitterDialog();
    private void MenuTxtLookup_Click(object sender, RoutedEventArgs e) => ShowTxtLookupDialog();
    private void MenuTimeConverter_Click(object sender, RoutedEventArgs e) => ShowTimeConverterDialog();
    private void MenuMongoObjectIdTimestampConverter_Click(object sender, RoutedEventArgs e) => ShowMongoObjectIdTimestampConverterDialog();
    private void MenuMongoDbApiGetToken_Click(object sender, RoutedEventArgs e) => ShowMongoDbApiGetTokenDialog();
    private void MenuMongoSrvLookup_Click(object sender, RoutedEventArgs e) => ShowMongoSrvLookupDialog();
    private void MenuProjectLineCounter_Click(object sender, RoutedEventArgs e) => ShowProjectLineCounterDialog();

    private void MenuUndo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Undo();
    private void MenuRedo_Click(object sender, RoutedEventArgs e) => CurrentDoc()?.Editor.Redo();
    private void MenuCut_Click(object sender, RoutedEventArgs e)
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        ExecuteEditorCut(doc);
    }
    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        ExecuteEditorCopy(doc);
    }
    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        var doc = CurrentDoc();
        if (doc == null)
            return;

        ExecuteEditorPaste(doc);
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
        EndSelectionCursorClip();
        _autoSaveTimer.Stop();
        _pluginAlarmTimer.Stop();
        _backupHeartbeatTimer.Stop();
        SaveWindowSettings();
        SaveStateConfigOnExit();
        if (!_sessionSaved)
            SaveSession(updateStatus: false);
    }

    private void Window_Closed(object sender, EventArgs e)
        => TryAppendUptimeHeartbeatShutdownBeat();

}
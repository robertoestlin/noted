namespace Noted.Models;

public sealed class FileMetadata
{
    // Backward-compatible legacy field (single highlight).
    public int? HighlightLine { get; set; }

    // Current format supports multiple highlighted lines.
    public List<int>? HighlightLines { get; set; }

    // Optional critical highlight rows (rendered using a separate color).
    public List<int>? CriticalHighlightLines { get; set; }

    // Optional line ownership metadata.
    public List<FileLineAssignee>? Assignees { get; set; }

    // UTC timestamp for when this tab was last saved with changes.
    public DateTime? LastSavedUtc { get; set; }

    /// <summary>UTC when the tab text was last edited (optional).</summary>
    public DateTime? LastChangedUtc { get; set; }

    /// <summary>0-based caret offset in the tab text.</summary>
    public int? CaretOffset { get; set; }
}

public sealed class FileLineAssignee
{
    public int Line { get; set; }
    public string Person { get; set; } = string.Empty;
}

public sealed class UserProfile
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class TimeReportMonthRecord
{
    public string Month { get; set; } = string.Empty;
    public Dictionary<int, double>? DayHours { get; set; }
    public Dictionary<int, string>? DayValues { get; set; }
    public Dictionary<string, string>? WeekComments { get; set; }
}

public sealed class TimeReportMonthState
{
    public Dictionary<int, string> DayValues { get; } = [];
    public Dictionary<string, string> WeekComments { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum TodoBucket
{
    Today = 0,
    ThisWeek = 1
}

public sealed class TodoItemState
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>Legacy bucket kept for backward compatibility. New items use <see cref="GroupId"/>.</summary>
    public TodoBucket Bucket { get; set; } = TodoBucket.Today;

    /// <summary>Owning area for this task. May be null/empty for legacy items.</summary>
    public string? AreaId { get; set; }

    /// <summary>Owning group for this task. May be null/empty for legacy items.</summary>
    public string? GroupId { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class TaskGroupState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortcutKey { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Number of days a completed task stays visible in the task panel for this group.
    /// 0 means never auto-hide (when combined with <see cref="CompletedRetentionHours"/> also being 0).
    /// Null falls back to per-group defaults for known built-in groups.
    /// Hidden items remain visible in Recently Completed.
    /// </summary>
    public int? CompletedRetentionDays { get; set; }

    /// <summary>
    /// Additional hours (0-23) on top of <see cref="CompletedRetentionDays"/>.
    /// Null is treated as 0.
    /// </summary>
    public int? CompletedRetentionHours { get; set; }
}

public sealed class TaskAreaState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<TaskGroupState> Groups { get; set; } = [];
}

public sealed class PluginAlarmSettings
{
    public string Name { get; set; } = string.Empty;
    public List<PluginAlarmTime>? Times { get; set; }
}

public sealed class PluginAlarmTime
{
    public int Hour { get; set; }
    public int Minute { get; set; }
}

public sealed class ProjectLineCounterProject
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

public sealed class ProjectLineCounterType
{
    public string Name { get; set; } = string.Empty;
    public List<string>? FileTypes { get; set; }
}

public sealed class SearchFilesHistoryMatch
{
    public string RelativePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string LinePreview { get; set; } = string.Empty;
}

public sealed class SearchFilesHistoryEntry
{
    public DateTime CreatedUtc { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string SearchInFiles { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public int MatchedFileCount { get; set; }
    public bool IsResultTruncated { get; set; }
    public List<SearchFilesHistoryMatch>? Matches { get; set; }
}

public sealed class ClosedTabEntry
{
    public string Header { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public FileMetadata? Metadata { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

public sealed class SafePasteKeyRecord
{
    public string Identifier { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

public sealed class WindowSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 700;
    public bool Maximized { get; set; }
    public int AutoSaveSeconds { get; set; } = 30;
    public int UptimeHeartbeatSeconds { get; set; } = 300;
    public bool WriteUptimeHeartbeatInNoted { get; set; } = true;
    public bool UseStandaloneHeartbeatApp { get; set; } = false;
    public int InitialLines { get; set; } = 50;
    public string FontFamily { get; set; } = "Consolas, Courier New";
    public double FontSize { get; set; } = 13;
    public int FontWeight { get; set; } = 400;
    public string ShortcutNewPrimary { get; set; } = "Ctrl+N";
    public string ShortcutNewSecondary { get; set; } = "Ctrl+T";
    public string ShortcutCloseTab { get; set; } = "Ctrl+W";
    public string ShortcutRenameTab { get; set; } = "F2";
    public string ShortcutAddBlankLines { get; set; } = "Ctrl+Space";
    public string ShortcutTrimTrailingEmptyLines { get; set; } = "Ctrl+Shift+Space";
    public string ShortcutToggleHighlight { get; set; } = "Ctrl+J";
    public string ShortcutToggleCriticalHighlight { get; set; } = "Ctrl+K";
    public string ShortcutGoToLine { get; set; } = "Ctrl+G";
    public string ShortcutGoToTab { get; set; } = "Ctrl+P";
    public string ShortcutMidiPlayer { get; set; } = "Ctrl+M";
    public string? SelectedLineColor { get; set; }
    public string? HighlightedLineColor { get; set; }
    public string? SelectedHighlightedLineColor { get; set; }
    public string? CriticalHighlightedLineColor { get; set; }
    public string? SelectedCriticalHighlightedLineColor { get; set; }
    public string? BackupFolder { get; set; }
    public string? CloudBackupFolder { get; set; }
    public int? CloudSaveHours { get; set; }
    public int? CloudSaveMinutes { get; set; }
    public DateTime? LastCloudCopyUtc { get; set; }
    public int ActiveTabIndex { get; set; }
    public bool FridayFeelingEnabled { get; set; } = true;
    public bool FancyBulletsEnabled { get; set; }
    public bool WrapLongLinesVisually { get; set; } = true;
    public int VisualLineWrapColumn { get; set; } = 150;
    public bool ShowSmileys { get; set; } = true;
    /// <summary>When null (older settings files), styled tag badges stay enabled.</summary>
    public bool? RenderStyledTags { get; set; }
    /// <summary>When null (older settings files), line assignment badges stay enabled.</summary>
    public bool? ShowLineAssignments { get; set; }
    public bool ShowHorizontalRuler { get; set; } = true;
    public bool ShowInlineImages { get; set; } = true;
    public string FancyBulletStyle { get; set; } = "dot";
    public List<string>? Users { get; set; }
    public List<UserProfile>? UserProfiles { get; set; }
    public List<PluginAlarmSettings>? PluginAlarms { get; set; }
    public bool PluginAlarmsEnabled { get; set; } = true;
    public DateTime? PluginAlarmsSnoozedUntilLocal { get; set; }
    public double? AlarmPopupLeft { get; set; }
    public double? AlarmPopupTop { get; set; }
    public List<ProjectLineCounterProject>? ProjectLineCounterProjects { get; set; }
    public List<ProjectLineCounterType>? ProjectLineCounterTypes { get; set; }
    public List<string>? ProjectLineCounterAutoDetectedFileTypes { get; set; }
    public List<string>? ProjectLineCounterIgnoredFileTypes { get; set; }
    public List<string>? ProjectLineCounterIgnoredFolders { get; set; }
    public int SearchFilesHistoryLimit { get; set; } = 20;
    public int TabCleanupStaleDays { get; set; } = 30;
    public int ClosedTabsMaxCount { get; set; } = 10;
    public int ClosedTabsRetentionDays { get; set; }
    public List<string>? QuickMessagePresets { get; set; }
    public string? QuickMessageColor { get; set; }
    public string? QuickMessageCustom { get; set; }
    public int? MessageOverlayBlinkIntervalMs { get; set; }
    public int? MessageOverlayFadeMs { get; set; }
    public int? MessageOverlayBlinkPhaseMs { get; set; }
    public int? MessageOverlayHoldMs { get; set; }
    public string? MessageOverlayBlinkMode { get; set; }
    public int? MessageOverlayCountdownMinutes { get; set; }
    public int? MessageOverlayCountdownSeconds { get; set; }
    public List<SafePasteKeyRecord>? SafePasteKeyRecords { get; set; }
    public List<string>? SafePasteKeys { get; set; }
    public string? TaskPanelTitle { get; set; }
    public List<TaskAreaState>? TaskAreas { get; set; }
    public string? CurrentTaskAreaId { get; set; }
}

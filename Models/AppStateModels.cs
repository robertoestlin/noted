namespace Noted.Models;

public sealed class FileMetadata
{
    // Backward-compatible legacy field (single highlight).
    public int? HighlightLine { get; set; }

    // Current format supports multiple highlighted lines.
    public List<int>? HighlightLines { get; set; }

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

public sealed class ClosedTabEntry
{
    public string Header { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public FileMetadata? Metadata { get; set; }
}

public sealed class WindowSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 700;
    public bool Maximized { get; set; }
    public int AutoSaveSeconds { get; set; } = 30;
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
    public string ShortcutGoToLine { get; set; } = "Ctrl+G";
    public string ShortcutGoToTab { get; set; } = "Ctrl+P";
    public string? SelectedLineColor { get; set; }
    public string? HighlightedLineColor { get; set; }
    public string? SelectedHighlightedLineColor { get; set; }
    public string? BackupFolder { get; set; }
    public string? CloudBackupFolder { get; set; }
    public int? CloudSaveHours { get; set; }
    public int? CloudSaveMinutes { get; set; }
    public DateTime? LastCloudCopyUtc { get; set; }
    public int ActiveTabIndex { get; set; }
    public bool FridayFeelingEnabled { get; set; } = true;
    public List<string>? Users { get; set; }
    public List<UserProfile>? UserProfiles { get; set; }
    public List<TimeReportMonthRecord>? TimeReports { get; set; }
    public List<PluginAlarmSettings>? PluginAlarms { get; set; }
    public bool PluginAlarmsEnabled { get; set; } = true;
    public double? AlarmPopupLeft { get; set; }
    public double? AlarmPopupTop { get; set; }
    public List<ProjectLineCounterProject>? ProjectLineCounterProjects { get; set; }
    public List<ProjectLineCounterType>? ProjectLineCounterTypes { get; set; }
    public List<string>? ProjectLineCounterAutoDetectedFileTypes { get; set; }
    public List<string>? ProjectLineCounterIgnoredFileTypes { get; set; }
    public List<string>? ProjectLineCounterIgnoredFolders { get; set; }
    public int TabCleanupStaleDays { get; set; } = 30;
    public List<string>? QuickMessagePresets { get; set; }
    public string? QuickMessageColor { get; set; }
    public string? QuickMessageCustom { get; set; }
}

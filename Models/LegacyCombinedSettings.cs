namespace Noted.Models;

/// <summary>One-shot deserialization of pre-split <c>settings.json</c> that still contained session fields.</summary>
public sealed class LegacyCombinedSettings
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public DateTime? LastCloudCopyUtc { get; set; }
    public int ActiveTabIndex { get; set; }
    public DateTime? PluginAlarmsSnoozedUntilLocal { get; set; }
    public double? AlarmPopupLeft { get; set; }
    public double? AlarmPopupTop { get; set; }
    public List<SafePasteKeyRecord>? SafePasteKeyRecords { get; set; }
    public List<string>? SafePasteKeys { get; set; }
    public StandupSettingsLegacy? Standup { get; set; }
}

/// <summary>Legacy <c>Standup</c> JSON object that included window bounds.</summary>
public sealed class StandupSettingsLegacy
{
    public List<StandupWeekdayTime>? WeekdayTimes { get; set; }
    public int RetentionDays { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

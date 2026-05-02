namespace Noted.Models;

public enum TabSyncDirection
{
    Outstream = 0,
    Instream = 1
}

public enum TabSyncItemStatus
{
    Wrote = 0,
    AutoApplied = 1,
    Conflict = 2,
    NoChange = 3,
    NoFile = 4,
    Skipped = 5,
    Failed = 6
}

public sealed class TabSyncItem
{
    /// <summary>Stable tab identity when known (plain-text sync); legacy rows may be empty.</summary>
    public string TabId { get; set; } = string.Empty;

    public string TabHeader { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime? LastUpdatedUtc { get; set; }
    public TabSyncItemStatus Status { get; set; }
    public string? Detail { get; set; }

    public string? IncomingText { get; set; }
    public string? CurrentText { get; set; }
    public bool Resolved { get; set; }

    /// <summary>When <see cref="Resolved"/> is true: <c>Appended</c> or <c>Resolved</c> (dismissed without append).</summary>
    public string? ConflictResolution { get; set; }
}

public sealed class TabSyncHistoryEntry
{
    public DateTime TimestampUtc { get; set; }
    public TabSyncDirection Direction { get; set; }
    public List<TabSyncItem> Items { get; set; } = [];
}

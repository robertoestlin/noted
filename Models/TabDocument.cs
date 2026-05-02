using System;
using System.Collections.Generic;
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Models;

public class TabDocument
{
    public sealed class LineAssigneeAnchor
    {
        public TextAnchor Anchor { get; init; } = null!;
        public string Person { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the current person was assigned to this line.</summary>
        public DateTime? CreatedUtc { get; set; }
    }

    public sealed class LineBulletAnchor
    {
        public TextAnchor Anchor { get; init; } = null!;

        /// <summary>Bullet marker, e.g. '-' or '*'.</summary>
        public char Marker { get; set; }

        /// <summary>UTC timestamp when this line was first seen as a bullet.</summary>
        public DateTime? CreatedUtc { get; set; }
    }

    public sealed class AssigneeBadgeBounds
    {
        public Rect Bounds { get; init; }
        public int LineNumber { get; init; }
        public string Person { get; init; } = string.Empty;
        public DateTime? CreatedUtc { get; init; }
    }

    /// <summary>Display name shown on the tab header (e.g. "file1").</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Stable identity for plain-text sync (GUID <c>D</c> format). Persists in session metadata.</summary>
    public string StableTabId { get; set; } = string.Empty;

    /// <summary>Optional bound file path (reserved; export does not set this).</summary>
    public string? FilePath { get; set; }

    /// <summary>Whether the content has changed since the last explicit save or session load.</summary>
    public bool IsDirty { get; set; }

    /// <summary>The AvalonEdit control that owns this document's text.</summary>
    public TextEditor Editor { get; set; } = null!;

    /// <summary>Cached copy of the editor text, updated on every keystroke. Used by SaveSession so that text is available even during window shutdown (WPF may clear AvalonEdit content before the Closing event finishes).</summary>
    public string CachedText { get; set; } = string.Empty;

    /// <summary>UTC timestamp from the last save operation that included this tab's latest edits.</summary>
    public DateTime? LastSavedUtc { get; set; }

    /// <summary>UTC timestamp when this tab's text was last edited.</summary>
    public DateTime LastChangedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Anchors for highlighted lines (track edits as text shifts).</summary>
    public List<TextAnchor> HighlightAnchors { get; } = [];

    /// <summary>Anchors for critical highlighted lines (track edits as text shifts).</summary>
    public List<TextAnchor> CriticalHighlightAnchors { get; } = [];

    /// <summary>Anchors for line assignees (track edits as text shifts).</summary>
    public List<LineAssigneeAnchor> LineAssigneeAnchors { get; } = [];

    /// <summary>Anchors for bullet lines (track edits as text shifts).</summary>
    public List<LineBulletAnchor> LineBulletAnchors { get; } = [];

    /// <summary>
    /// Live cache of the on-screen rectangles for assignee badges, populated by the
    /// background renderer on each draw. Used by the editor to show an instant hover
    /// tooltip with assignment metadata.
    /// </summary>
    public List<AssigneeBadgeBounds> AssigneeBadgeBoundsCache { get; } = [];

    /// <summary>Renderer used to paint the full-width highlighted line background.</summary>
    public IBackgroundRenderer? HighlightRenderer { get; set; }

    /// <summary>Tab header shown in the UI.</summary>
    public string DisplayHeader => Header;
}

using System.Collections.Generic;
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
    }

    /// <summary>Display name shown on the tab header (e.g. "file1").</summary>
    public string Header { get; set; } = string.Empty;

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

    /// <summary>Anchors for highlighted lines (track edits as text shifts).</summary>
    public List<TextAnchor> HighlightAnchors { get; } = [];

    /// <summary>Anchors for line assignees (track edits as text shifts).</summary>
    public List<LineAssigneeAnchor> LineAssigneeAnchors { get; } = [];

    /// <summary>Renderer used to paint the full-width highlighted line background.</summary>
    public IBackgroundRenderer? HighlightRenderer { get; set; }

    /// <summary>Tab header shown in the UI.</summary>
    public string DisplayHeader => Header;
}

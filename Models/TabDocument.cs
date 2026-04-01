using ICSharpCode.AvalonEdit;

namespace Noted.Models;

public class TabDocument
{
    /// <summary>Display name shown on the tab header (e.g. "file1").</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Full path if the user has explicitly saved with Save / Save As. Null for unsaved docs.</summary>
    public string? FilePath { get; set; }

    /// <summary>Whether the content has changed since the last explicit save or session load.</summary>
    public bool IsDirty { get; set; }

    /// <summary>The AvalonEdit control that owns this document's text.</summary>
    public TextEditor Editor { get; set; } = null!;

    /// <summary>Cached copy of the editor text, updated on every keystroke. Used by SaveSession so that text is available even during window shutdown (WPF may clear AvalonEdit content before the Closing event finishes).</summary>
    public string CachedText { get; set; } = string.Empty;

    /// <summary>Tab header shown in the UI.</summary>
    public string DisplayHeader => Header;
}

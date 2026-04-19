using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    /// <summary>Tag in text: # then letters/digits, words separated by hyphen only (e.g. #Foo-bar).</summary>
    private static readonly Regex TagTokenRegex = new(
        @"(?<![A-Za-z0-9_])#(?<name>[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Body after # while typing (allows trailing hyphen before next word).</summary>
    private static readonly Regex TagBodyWhileTypingRegex = new(
        @"^[A-Za-z0-9]+(?:-[A-Za-z0-9]*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Finished tag body (no trailing hyphen).</summary>
    private static readonly Regex TagNameBodyCompleteRegex = new(
        @"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Hyphens in stored name become spaces in the label (e.g. Foo-bar → #Foo bar).</summary>
    private static string FormatTagLabelForDisplay(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return "#";

        var displayBody = Regex.Replace(rawName, "-+", " ");
        return "#" + displayBody;
    }

    private CompletionWindow? _tagCompletionWindow;

    private readonly record struct TagVisualToken(int StartIndex, int Length, string Name);

    private static IEnumerable<TagVisualToken> EnumerateTagTokens(string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
            yield break;

        foreach (Match match in TagTokenRegex.Matches(lineText))
        {
            var name = match.Groups["name"].Value;
            if (name.Length == 0)
                continue;

            yield return new TagVisualToken(match.Index, match.Length, name);
        }
    }

    private static void AddDistinctTagNamesFromText(string text, HashSet<string> into)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var line in text.Split('\n'))
        {
            foreach (var token in EnumerateTagTokens(line))
                into.Add(token.Name);
        }
    }

    private static bool TabHasAnyTags(TabDocument doc)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDistinctTagNamesFromText(doc.CachedText, set);
        return set.Count > 0;
    }

    private List<string> GetDistinctTagNamesAcrossTabs(TabDocument? onlyFromTab)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (onlyFromTab != null)
        {
            AddDistinctTagNamesFromText(onlyFromTab.CachedText, set);
        }
        else
        {
            foreach (var doc in _docs.Values)
                AddDistinctTagNamesFromText(doc.CachedText, set);
        }

        return set.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class TagTextMaskingTransformer : DocumentColorizingTransformer
    {
        private readonly Func<bool> _enabledProvider;

        public TagTextMaskingTransformer(Func<bool> enabledProvider)
            => _enabledProvider = enabledProvider;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_enabledProvider())
                return;

            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var lineText = CurrentContext.Document.GetText(line.Offset, line.Length);
            foreach (var token in EnumerateTagTokens(lineText))
            {
                ChangeLinePart(line.Offset + token.StartIndex, line.Offset + token.StartIndex + token.Length, visualElement =>
                {
                    visualElement.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                });
            }
        }
    }

    private sealed class TagCompletionData : ICompletionData
    {
        private readonly string _name;

        public TagCompletionData(string name)
        {
            _name = name;
            Text = name;
            Content = FormatTagLabelForDisplay(name);
            Description = "Tag from your tabs";
        }

        public System.Windows.Media.ImageSource? Image => null;

        public string Text { get; }

        public object Content { get; }

        public object Description { get; }

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _name);
        }
    }

    private void CloseTagCompletionIfAny()
    {
        if (_tagCompletionWindow == null)
            return;

        try
        {
            _tagCompletionWindow.Close();
        }
        catch
        {
            /* ignore */
        }

        _tagCompletionWindow = null;
    }

    private void HandleTagHashTextEntered(TabDocument doc, TextCompositionEventArgs e)
    {
        if (e.Text != "#")
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TryShowTagCompletionAfterHash(doc));
    }

    /// <summary>
    /// Space/tab after # becomes '-' so multi-word tags can be typed with the spacebar (#Do Later → #Do-Later).
    /// Literal space is kept when the fragment is already a completed multi-word tag, or a longer single-word tag.
    /// </summary>
    private void HandleTagWhitespaceInputAsHyphen(TabDocument doc, TextCompositionEventArgs e)
    {
        if (e.Text is not (" " or "\t"))
            return;

        var editor = doc.Editor;
        if (!string.IsNullOrEmpty(editor.SelectedText))
            return;

        var document = editor.Document;
        int caret = editor.CaretOffset;
        if (caret < 1 || caret > document.TextLength)
            return;

        try
        {
            var line = document.GetLineByOffset(caret - 1);
            int rel = caret - line.Offset;
            if (rel < 1)
                return;

            var lineText = document.GetText(line.Offset, line.Length);
            int hashIndex = lineText.LastIndexOf('#', rel - 1);
            if (hashIndex < 0)
                return;

            int afterLen = rel - hashIndex - 1;
            if (afterLen < 1)
                return;

            string afterHash = lineText.Substring(hashIndex + 1, afterLen);
            if (!TagBodyWhileTypingRegex.IsMatch(afterHash))
                return;

            char last = afterHash[^1];
            if (!char.IsLetterOrDigit(last))
                return;

            bool complete = TagNameBodyCompleteRegex.IsMatch(afterHash);
            bool hasHyphen = afterHash.Contains('-');
            // Let space through after e.g. #Do-Later (done) or a longer single token like #Later.
            if (complete && (hasHyphen || afterHash.Length > 3))
                return;

            document.Insert(caret, "-");
            editor.CaretOffset = caret + 1;
            e.Handled = true;
        }
        catch
        {
            /* ignore */
        }
    }

    private void TryShowTagCompletionAfterHash(TabDocument doc)
    {
        if (_tagCompletionWindow != null)
            return;

        var editor = doc.Editor;
        var area = editor.TextArea;
        int caret = editor.CaretOffset;
        if (caret <= 0)
            return;

        if (editor.Document.GetText(caret - 1, 1) != "#")
            return;

        var tags = GetDistinctTagNamesAcrossTabs(onlyFromTab: null);
        if (tags.Count == 0)
            return;

        var window = new CompletionWindow(area)
        {
            CloseWhenCaretAtBeginning = true
        };
        window.StartOffset = caret;
        window.EndOffset = caret;

        foreach (var t in tags)
            window.CompletionList.CompletionData.Add(new TagCompletionData(t));

        _tagCompletionWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_tagCompletionWindow, window))
                _tagCompletionWindow = null;
        };
        window.PreviewKeyDown += TagCompletionWindow_OnPreviewKeyDown;

        window.Show();
    }

    private void TagCompletionWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Enter or Key.Return or Key.LineFeed))
            return;

        if (sender is not CompletionWindow window)
            return;

        var area = window.TextArea;
        int offset = area.Caret.Offset;
        e.Handled = true;
        window.Close();
        area.Document.Insert(offset, "\n");
        area.Caret.Offset = offset + 1;
    }

    /// <summary>Enter always inserts a new line while tag completion is open (caret on the editor).</summary>
    private bool TryInsertNewlineClosingTagCompletion(TabDocument doc, KeyEventArgs e)
    {
        if (_tagCompletionWindow == null)
            return false;
        if (Keyboard.Modifiers != ModifierKeys.None)
            return false;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Enter or Key.Return or Key.LineFeed))
            return false;

        var area = doc.Editor.TextArea;
        int offset = area.Caret.Offset;
        e.Handled = true;
        CloseTagCompletionIfAny();
        area.Document.Insert(offset, "\n");
        area.Caret.Offset = offset + 1;
        return true;
    }

    private FrameworkElement BuildTagsSettingsTabContent()
    {
        var root = new StackPanel { Margin = new Thickness(12) };

        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        var cmb = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        var allItem = new ComboBoxItem { Content = "All tabs", Tag = null };
        cmb.Items.Add(allItem);
        foreach (var doc in _docs.Values
                     .Where(TabHasAnyTags)
                     .OrderBy(d => d.Header, StringComparer.OrdinalIgnoreCase))
            cmb.Items.Add(new ComboBoxItem { Content = doc.Header, Tag = doc });
        cmb.SelectedItem = allItem;

        var list = new ListBox
        {
            MinHeight = 240,
            Margin = new Thickness(0, 4, 0, 0)
        };

        void RefreshTagList()
        {
            list.Items.Clear();
            var filter = (cmb.SelectedItem as ComboBoxItem)?.Tag as TabDocument;
            var names = GetDistinctTagNamesAcrossTabs(filter);
            foreach (var name in names)
                list.Items.Add(FormatTagLabelForDisplay(name));
        }

        cmb.SelectionChanged += (_, _) => RefreshTagList();
        filterRow.Children.Add(cmb);
        root.Children.Add(filterRow);
        root.Children.Add(list);
        RefreshTagList();
        return root;
    }

    private void ShowTagsDialog()
    {
        var dlg = new Window
        {
            Title = "Tags",
            Width = 560,
            Height = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var btnOk = new Button { Content = "OK", Width = 80, IsDefault = true };
        footer.Children.Add(btnOk);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(new ScrollViewer
        {
            Content = BuildTagsSettingsTabContent(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        dlg.Content = root;
        btnOk.Click += (_, _) => { dlg.DialogResult = true; };
        dlg.ShowDialog();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Models;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private readonly DocumentationService _documentationService = new();

    private readonly List<DocPackage> _docPackages = new();
    private DocPackagesIndex _docIndex = new();
    private readonly Dictionary<string, TabDocument> _docNodeDocs = new();
    private readonly HashSet<string> _docDirtyPackageIds = new(StringComparer.Ordinal);
    private bool _docIndexDirty;

    private DocPackage? _docCurrentPackage;
    private DocNode? _docCurrentNode;

    private ComboBox? _docPackageCombo;
    private TreeView? _docTree;
    private ContentControl? _docEditorHost;
    private Grid? _docEmptyState;
    private Grid? _docMainGrid;

    private void LoadDocumentationFromDisk()
    {
        _docPackages.Clear();
        _docIndex = _documentationService.LoadIndex(_backupFolder);
        var loaded = _documentationService.LoadAllPackages(_backupFolder);
        var byId = loaded.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var id in _docIndex.Order)
        {
            if (byId.TryGetValue(id, out var pkg))
            {
                _docPackages.Add(pkg);
                byId.Remove(id);
            }
        }
        foreach (var pkg in byId.Values)
            _docPackages.Add(pkg);

        if (_docIndex.CurrentId is { Length: > 0 } currentId)
            _docCurrentPackage = _docPackages.FirstOrDefault(p => p.Id == currentId);
        if (_docCurrentPackage == null && _docPackages.Count > 0)
            _docCurrentPackage = _docPackages[0];
    }

    private void BuildDocumentationView()
    {
        if (DocumentationView == null) return;
        DocumentationView.Children.Clear();

        if (_docIndex.Order.Count == 0 && _docPackages.Count == 0)
            LoadDocumentationFromDisk();

        // Empty state
        _docEmptyState = new Grid { Visibility = Visibility.Collapsed };
        var emptyStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        emptyStack.Children.Add(new TextBlock
        {
            Text = "No doc packages yet",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        emptyStack.Children.Add(new TextBlock
        {
            Text = "Create your first doc package to start writing documentation.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var createBtn = new Button
        {
            Content = "Create doc package",
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        createBtn.Click += (_, _) => PromptCreateDocPackage();
        emptyStack.Children.Add(createBtn);
        _docEmptyState.Children.Add(emptyStack);
        DocumentationView.Children.Add(_docEmptyState);

        // Main grid: Col 0 = combo + tree, Col 2 = editor host
        _docMainGrid = new Grid();
        _docMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _docMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _docMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftDock = new DockPanel { Margin = new Thickness(8) };
        Grid.SetColumn(leftDock, 0);

        var packageHeader = new Grid();
        packageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        packageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(packageHeader, Dock.Top);

        _docPackageCombo = new ComboBox { Margin = new Thickness(0, 0, 4, 6) };
        Grid.SetColumn(_docPackageCombo, 0);
        _docPackageCombo.SelectionChanged += DocPackageCombo_SelectionChanged;
        packageHeader.Children.Add(_docPackageCombo);

        var addBtn = new Button { Content = "+", Width = 26, Height = 24, ToolTip = "Add doc package" };
        Grid.SetColumn(addBtn, 1);
        addBtn.Click += (_, _) => PromptCreateDocPackage();
        packageHeader.Children.Add(addBtn);
        leftDock.Children.Add(packageHeader);

        var treeLabel = new TextBlock
        {
            Text = "Sections / Pages",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };
        DockPanel.SetDock(treeLabel, Dock.Top);
        leftDock.Children.Add(treeLabel);

        _docTree = new TreeView { BorderThickness = new Thickness(1) };
        _docTree.SelectedItemChanged += DocTree_SelectedItemChanged;
        leftDock.Children.Add(_docTree);
        _docMainGrid.Children.Add(leftDock);

        var splitter = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetColumn(splitter, 1);
        _docMainGrid.Children.Add(splitter);

        _docEditorHost = new ContentControl();
        Grid.SetColumn(_docEditorHost, 2);
        _docMainGrid.Children.Add(_docEditorHost);

        DocumentationView.Children.Add(_docMainGrid);

        RefreshDocumentationView();
    }

    private void RefreshDocumentationView()
    {
        if (_docMainGrid == null || _docEmptyState == null) return;
        bool has = _docPackages.Count > 0;
        _docEmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        _docMainGrid.Visibility   = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;

        RefreshDocPackageCombo();
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private void RefreshDocPackageCombo()
    {
        if (_docPackageCombo == null) return;
        var prev = _docCurrentPackage;
        _docPackageCombo.SelectionChanged -= DocPackageCombo_SelectionChanged;
        _docPackageCombo.Items.Clear();
        foreach (var pkg in _docPackages)
            _docPackageCombo.Items.Add(new ComboBoxItem { Content = pkg.Name, Tag = pkg });
        if (prev != null)
        {
            foreach (ComboBoxItem item in _docPackageCombo.Items)
            {
                if (item.Tag is DocPackage p && p.Id == prev.Id)
                {
                    _docPackageCombo.SelectedItem = item;
                    break;
                }
            }
        }
        if (_docPackageCombo.SelectedIndex < 0 && _docPackageCombo.Items.Count > 0)
            _docPackageCombo.SelectedIndex = 0;
        _docPackageCombo.SelectionChanged += DocPackageCombo_SelectionChanged;

        if (_docPackageCombo.SelectedItem is ComboBoxItem chosen && chosen.Tag is DocPackage chosenPkg)
            _docCurrentPackage = chosenPkg;
    }

    private void DocPackageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_docPackageCombo?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not DocPackage pkg) return;

        FlushActiveDocPageText();

        _docCurrentPackage = pkg;
        _docIndex.CurrentId = pkg.Id;
        MarkDocIndexDirty();
        _docCurrentNode = FindDocNodeById(pkg.Nodes, pkg.CurrentNodeId) ?? FirstPageNode(pkg.Nodes);
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private void RefreshDocTree()
    {
        if (_docTree == null) return;
        _docTree.Items.Clear();
        if (_docCurrentPackage == null) return;
        foreach (var node in _docCurrentPackage.Nodes)
        {
            var tvi = BuildDocTreeItem(node);
            _docTree.Items.Add(tvi);
        }
        _docTree.ContextMenu = BuildDocRootContextMenu();
    }

    private TreeViewItem BuildDocTreeItem(DocNode node)
    {
        var tvi = new TreeViewItem
        {
            Header = FormatDocNodeHeader(node),
            Tag = node,
            IsSelected = _docCurrentNode != null && ReferenceEquals(node, _docCurrentNode),
            IsExpanded = ContainsCurrent(node)
        };
        tvi.ContextMenu = BuildDocNodeContextMenu(node);
        foreach (var child in node.Children)
            tvi.Items.Add(BuildDocTreeItem(child));
        return tvi;
    }

    private bool ContainsCurrent(DocNode node)
    {
        if (_docCurrentNode == null) return false;
        if (ReferenceEquals(node, _docCurrentNode)) return true;
        foreach (var c in node.Children)
            if (ContainsCurrent(c)) return true;
        return false;
    }

    private static string FormatDocNodeHeader(DocNode node)
        => node.Kind switch
        {
            DocNodeKind.Section => "▸ " + node.Name,
            DocNodeKind.SubSection => "· " + node.Name,
            DocNodeKind.Page => "▤ " + node.Name,
            DocNodeKind.SubPage => "▦ " + node.Name,
            _ => node.Name
        };

    private void DocTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem tvi) return;
        if (tvi.Tag is not DocNode node) return;

        FlushActiveDocPageText();

        _docCurrentNode = node;
        if (_docCurrentPackage != null)
        {
            _docCurrentPackage.CurrentNodeId = node.Id;
            MarkDocPackageDirty(_docCurrentPackage);
        }
        ShowActiveDocPageEditor();
    }

    private ContextMenu BuildDocRootContextMenu()
    {
        var menu = new ContextMenu();
        var addSection = new MenuItem { Header = "Add section..." };
        addSection.Click += (_, _) => PromptAddDocNode(parent: null, kind: DocNodeKind.Section);
        menu.Items.Add(addSection);
        return menu;
    }

    private ContextMenu BuildDocNodeContextMenu(DocNode node)
    {
        var menu = new ContextMenu();
        switch (node.Kind)
        {
            case DocNodeKind.Section:
                AddMenuItem(menu, "Add sub-section...",
                    () => PromptAddDocNode(parent: node, kind: DocNodeKind.SubSection));
                AddMenuItem(menu, "Add page...",
                    () => PromptAddDocNode(parent: node, kind: DocNodeKind.Page));
                menu.Items.Add(new Separator());
                break;
            case DocNodeKind.SubSection:
                AddMenuItem(menu, "Add page...",
                    () => PromptAddDocNode(parent: node, kind: DocNodeKind.Page));
                menu.Items.Add(new Separator());
                break;
            case DocNodeKind.Page:
                AddMenuItem(menu, "Add sub-page...",
                    () => PromptAddDocNode(parent: node, kind: DocNodeKind.SubPage));
                menu.Items.Add(new Separator());
                break;
        }
        AddMenuItem(menu, "Rename...", () => PromptRenameDocNode(node));
        AddMenuItem(menu, "Delete", () => DeleteDocNode(node));
        return menu;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => onClick();
        menu.Items.Add(mi);
    }

    // ── Editor host ─────────────────────────────────────────────────────────

    private void ShowActiveDocPageEditor()
    {
        if (_docEditorHost == null) return;
        if (_docCurrentNode == null || (_docCurrentNode.Kind != DocNodeKind.Page && _docCurrentNode.Kind != DocNodeKind.SubPage))
        {
            _docEditorHost.Content = new TextBlock
            {
                Text = "Select or create a page to start writing.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }
        var doc = GetOrCreateDocNodeDocument(_docCurrentNode);
        _docEditorHost.Content = doc.Editor;
    }

    private TabDocument GetOrCreateDocNodeDocument(DocNode node)
    {
        if (_docNodeDocs.TryGetValue(node.Id, out var existing))
            return existing;

        var editor = CreateEditor();
        editor.Text = node.Content ?? string.Empty;

        var doc = new TabDocument
        {
            Header = node.Name,
            StableTabId = node.Id,
            Editor = editor,
            CachedText = editor.Text,
            IsDirty = false,
            LastChangedUtc = DateTime.UtcNow
        };
        BindDocumentToEditor(doc, editor);

        // Markdown line transformers — added on top of the standard set installed by CreateEditor().
        editor.TextArea.TextView.LineTransformers.Add(new MarkdownHeadingTransformer());
        editor.TextArea.TextView.LineTransformers.Add(new MarkdownFencedCodeBlockTransformer());

        editor.TextChanged += (_, _) =>
        {
            if (_docCurrentPackage != null)
                MarkDocPackageDirty(_docCurrentPackage);
        };

        _docNodeDocs[node.Id] = doc;
        return doc;
    }

    private void FlushActiveDocPageText()
    {
        if (_docCurrentNode == null) return;
        if (!_docNodeDocs.TryGetValue(_docCurrentNode.Id, out var doc)) return;
        var newText = doc.Editor?.Text ?? doc.CachedText;
        if (newText != _docCurrentNode.Content)
        {
            _docCurrentNode.Content = newText;
            if (_docCurrentPackage != null)
                MarkDocPackageDirty(_docCurrentPackage);
        }
    }

    private void FocusActiveDocPageEditor()
    {
        if (!_docViewBuilt) return;
        if (_docEditorHost?.Content is TextEditor editor)
            editor.Focus();
    }

    // ── Mutations ───────────────────────────────────────────────────────────

    private void PromptCreateDocPackage()
    {
        var name = PromptForName("New doc package", "Doc package name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var pkg = new DocPackage { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        _docPackages.Add(pkg);
        _docIndex.Order.Add(pkg.Id);
        _docIndex.CurrentId = pkg.Id;
        _docCurrentPackage = pkg;
        _docCurrentNode = null;
        MarkDocPackageDirty(pkg);
        MarkDocIndexDirty();
        RefreshDocumentationView();
    }

    private void PromptAddDocNode(DocNode? parent, DocNodeKind kind)
    {
        if (_docCurrentPackage == null) return;
        var label = kind switch
        {
            DocNodeKind.Section => "New section",
            DocNodeKind.SubSection => "New sub-section",
            DocNodeKind.Page => "New page",
            DocNodeKind.SubPage => "New sub-page",
            _ => "New"
        };
        var name = PromptForName(label, "Name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var node = new DocNode { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), Kind = kind };
        if (parent == null)
            _docCurrentPackage.Nodes.Add(node);
        else
            parent.Children.Add(node);

        if (kind is DocNodeKind.Page or DocNodeKind.SubPage)
            _docCurrentNode = node;
        MarkDocPackageDirty(_docCurrentPackage);
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private void PromptRenameDocNode(DocNode node)
    {
        var name = PromptForName("Rename", "New name:", node.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        node.Name = name.Trim();
        if (_docNodeDocs.TryGetValue(node.Id, out var doc))
            doc.Header = name.Trim();
        if (_docCurrentPackage != null)
            MarkDocPackageDirty(_docCurrentPackage);
        RefreshDocTree();
    }

    private void DeleteDocNode(DocNode node)
    {
        if (_docCurrentPackage == null) return;
        if (MessageBox.Show($"Delete '{node.Name}' and all of its children?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        if (!_docCurrentPackage.Nodes.Remove(node))
            RemoveDocNodeFromTree(_docCurrentPackage.Nodes, node);

        foreach (var id in CollectDocNodeIds(node))
            _docNodeDocs.Remove(id);

        if (_docCurrentNode != null && CollectDocNodeIds(node).Any(id => id == _docCurrentNode.Id))
            _docCurrentNode = FirstPageNode(_docCurrentPackage.Nodes);

        MarkDocPackageDirty(_docCurrentPackage);
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private static bool RemoveDocNodeFromTree(List<DocNode> roots, DocNode target)
    {
        foreach (var n in roots)
        {
            if (n.Children.Remove(target)) return true;
            if (RemoveDocNodeFromTree(n.Children, target)) return true;
        }
        return false;
    }

    private static IEnumerable<string> CollectDocNodeIds(DocNode node)
    {
        yield return node.Id;
        foreach (var c in node.Children)
        foreach (var id in CollectDocNodeIds(c))
            yield return id;
    }

    private static DocNode? FindDocNodeById(List<DocNode> roots, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var n in roots)
        {
            if (n.Id == id) return n;
            var inner = FindDocNodeById(n.Children, id);
            if (inner != null) return inner;
        }
        return null;
    }

    private static DocNode? FirstPageNode(List<DocNode> roots)
    {
        foreach (var n in roots)
        {
            if (n.Kind is DocNodeKind.Page or DocNodeKind.SubPage)
                return n;
            var inner = FirstPageNode(n.Children);
            if (inner != null) return inner;
        }
        return null;
    }

    private void MarkDocPackageDirty(DocPackage pkg) => _docDirtyPackageIds.Add(pkg.Id);
    private void MarkDocIndexDirty() => _docIndexDirty = true;

    private void SaveDirtyDocPackages()
    {
        FlushActiveDocPageText();
        if (_docDirtyPackageIds.Count > 0)
        {
            foreach (var id in _docDirtyPackageIds)
            {
                var pkg = _docPackages.FirstOrDefault(p => p.Id == id);
                if (pkg == null) continue;
                _documentationService.SavePackage(_backupFolder, pkg);
            }
            _docDirtyPackageIds.Clear();
        }
        if (_docIndexDirty)
        {
            _docIndex.Order = _docPackages.Select(p => p.Id).ToList();
            _documentationService.SaveIndex(_backupFolder, _docIndex);
            _docIndexDirty = false;
        }
    }

    // ── Markdown transformers ───────────────────────────────────────────────

    /// <summary>
    /// Inline-styles lines starting with <c>#</c>, <c>##</c>, or <c>###</c> as markdown headings:
    /// progressively larger font + bold weight. Mirrors the existing line-transformer pattern
    /// used for bullets / smileys / horizontal rule.
    /// </summary>
    private sealed class MarkdownHeadingTransformer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var text = CurrentContext.Document.GetText(line.Offset, line.Length);
            int hashes = 0;
            while (hashes < text.Length && hashes < 3 && text[hashes] == '#')
                hashes++;
            if (hashes == 0) return;
            if (hashes >= text.Length || text[hashes] != ' ')
                return;

            double sizeMul = hashes switch
            {
                1 => 1.7,
                2 => 1.4,
                3 => 1.2,
                _ => 1.0
            };

            ChangeLinePart(line.Offset, line.EndOffset, ve =>
            {
                ve.TextRunProperties.SetFontRenderingEmSize(ve.TextRunProperties.FontRenderingEmSize * sizeMul);
                var typeface = ve.TextRunProperties.Typeface;
                ve.TextRunProperties.SetTypeface(new Typeface(
                    typeface.FontFamily,
                    typeface.Style,
                    FontWeights.Bold,
                    typeface.Stretch));
            });
        }
    }

    /// <summary>
    /// Tints lines inside a fenced code block (between matching <c>```</c> markers) with a
    /// monospace typeface and a subtle background. Determines fence state by scanning from
    /// the document start each call — fine for typical doc page sizes.
    /// </summary>
    private sealed class MarkdownFencedCodeBlockTransformer : DocumentColorizingTransformer
    {
        private static readonly SolidColorBrush CodeBackground = MakeFrozen(Color.FromRgb(0xF3, 0xF4, 0xF6));
        private static readonly FontFamily CodeFont = new("Consolas, Courier New");

        private static SolidColorBrush MakeFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (CurrentContext?.Document == null) return;
            var doc = CurrentContext.Document;

            bool insideFence = false;
            foreach (var prior in doc.Lines)
            {
                if (prior.Offset >= line.Offset)
                    break;
                if (IsFenceLine(doc, prior))
                    insideFence = !insideFence;
            }

            bool thisLineIsFence = IsFenceLine(doc, line);
            if (!insideFence && !thisLineIsFence) return;

            ChangeLinePart(line.Offset, line.EndOffset, ve =>
            {
                ve.TextRunProperties.SetBackgroundBrush(CodeBackground);
                var t = ve.TextRunProperties.Typeface;
                ve.TextRunProperties.SetTypeface(new Typeface(CodeFont, t.Style, t.Weight, t.Stretch));
            });
        }

        private static bool IsFenceLine(TextDocument doc, DocumentLine line)
        {
            if (line.Length < 3) return false;
            var text = doc.GetText(line.Offset, line.Length).TrimEnd();
            return text.StartsWith("```", StringComparison.Ordinal);
        }
    }
}

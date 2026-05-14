using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Models;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private readonly DocumentationService _documentationService = new();

    private readonly List<DocPackage> _docPackages = new();
    private readonly Dictionary<string, TabDocument> _docNodeDocs = new();
    private readonly Dictionary<TextEditor, string> _docEditorPackageIds = new();
    private readonly HashSet<string> _docDirtyPackageIds = new(StringComparer.Ordinal);

    private DocPackage? _docCurrentPackage;
    private DocNode? _docCurrentNode;

    /// <summary>Last-selected package id read from <c>session-state.json</c> at startup; consumed by
    /// <see cref="LoadDocumentationFromDisk"/> the first time Documentation mode is opened.</summary>
    private string? _pendingDocPackageId;

    private ComboBox? _docPackageCombo;
    private TreeView? _docTree;
    private ContentControl? _docEditorHost;
    private Grid? _docEmptyState;
    private Grid? _docMainGrid;

    private void LoadDocumentationFromDisk()
    {
        _docPackages.Clear();
        var loaded = _documentationService.LoadAllPackages(_backupFolder);
        foreach (var pkg in loaded.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Id, StringComparer.Ordinal))
            _docPackages.Add(pkg);

        if (_docCurrentPackage == null && !string.IsNullOrEmpty(_pendingDocPackageId))
        {
            _docCurrentPackage = _docPackages.FirstOrDefault(p => p.Id == _pendingDocPackageId);
            _pendingDocPackageId = null;
        }
        if (_docCurrentPackage == null && _docPackages.Count > 0)
            _docCurrentPackage = _docPackages[0];

        SyncDocCurrentNodeFromPackage();
    }

    private void BuildDocumentationView()
    {
        if (DocumentationView == null) return;
        DocumentationView.Children.Clear();
        DocumentationView.RowDefinitions.Clear();
        DocumentationView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        DocumentationView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var docBody = new Grid();
        Grid.SetRow(docBody, 0);

        if (_docPackages.Count == 0)
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
        docBody.Children.Add(_docEmptyState);

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

        docBody.Children.Add(_docMainGrid);

        var docStatusBar = BuildGrayFooterStatusBar("Documentation");
        Grid.SetRow(docStatusBar, 1);
        DocumentationView.Children.Add(docBody);
        DocumentationView.Children.Add(docStatusBar);

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

        SyncDocCurrentNodeFromPackage();
    }

    private void DocPackageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_docPackageCombo?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not DocPackage pkg) return;

        FlushActiveDocPageText();

        _docCurrentPackage = pkg;
        SyncDocCurrentNodeFromPackage();
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private void RefreshDocTree()
    {
        if (_docTree == null) return;
        _docTree.Items.Clear();
        if (_docCurrentPackage == null) return;
        foreach (var node in DocNodesSortedForTree(_docCurrentPackage.Nodes))
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
        foreach (var child in DocNodesSortedForTree(node.Children))
            tvi.Items.Add(BuildDocTreeItem(child));
        return tvi;
    }

    private static IEnumerable<DocNode> DocNodesSortedForTree(IEnumerable<DocNode> nodes)
        => nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Id, StringComparer.Ordinal);

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

        var editor = CreateEditor(addTagTextMaskingTransformer: false);
        editor.Text = node.Content ?? string.Empty;

        var owningPackage = FindDocPackageOwningNode(node);
        if (owningPackage != null)
            _docEditorPackageIds[editor] = owningPackage.Id;

        var doc = new TabDocument
        {
            Header = node.Name,
            StableTabId = node.Id,
            Editor = editor,
            CachedText = editor.Text,
            IsDirty = false,
            LastChangedUtc = DateTime.UtcNow,
            TagFeaturesEnabled = false
        };
        BindDocumentToEditor(doc, editor);

        // Markdown: hide ``` fence lines in the view; "# " markers stay visible, only the heading
        // title is styled by MarkdownHeadingTransformer.
        editor.TextArea.TextView.ElementGenerators.Insert(0, new MarkdownFenceLineHiddenGenerator());
        editor.TextArea.TextView.LineTransformers.Add(new MarkdownHeadingTransformer());
        editor.TextArea.TextView.LineTransformers.Add(new MarkdownFencedCodeBlockTransformer());
        editor.TextArea.TextView.BackgroundRenderers.Add(new MarkdownFencedCodeBackgroundRenderer());

        // Lock fence lines of every closed ``` … ``` pair so typing/backspace/delete cannot corrupt
        // them. Snippet add/remove uses Document.Replace directly, which bypasses this provider.
        editor.TextArea.ReadOnlySectionProvider =
            new MarkdownFenceReadOnlySectionProvider(() => editor.Document);

        // Fence state for a line depends on backticks elsewhere; AvalonEdit only invalidates the
        // line whose text changed, so a ``` typed near an existing "# heading" leaves the cached
        // heading styling on that line. Force a full redraw on backtick changes.
        editor.Document.Changed += (_, e) =>
        {
            if (e.InsertedText.Text.IndexOf('`') >= 0 || e.RemovedText.Text.IndexOf('`') >= 0)
                editor.TextArea.TextView.Redraw();
        };

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

    private TabDocument? _documentationFindScratchDoc;

    /// <summary>Used by Find dialog when scope is documentation pages (not main-window tabs).</summary>
    private bool IsDocumentationPageDocument(TabDocument doc)
        => _docNodeDocs.Values.Any(d => ReferenceEquals(d, doc));

    private TabDocument? GetActiveDocumentationTabDocument()
    {
        if (_docCurrentNode == null || _docCurrentNode.Kind is not (DocNodeKind.Page or DocNodeKind.SubPage))
            return null;
        return GetOrCreateDocNodeDocument(_docCurrentNode);
    }

    private void OpenDocumentationFindDialog()
        => ShowFindReplaceDialog(explicitDoc: ResolveDocumentationFindSeedDocument(), findOnly: true,
            documentationPackageScope: true);

    private TabDocument ResolveDocumentationFindSeedDocument()
        => GetActiveDocumentationTabDocument()
           ?? GetFirstDocumentationPageDocumentInPackage(_docCurrentPackage)
           ?? GetFirstDocumentationPageDocumentAcrossAllPackages()
           ?? GetDocumentationFindScratchDocument();

    private TabDocument? GetFirstDocumentationPageDocumentInPackage(DocPackage? pkg)
    {
        if (pkg == null) return null;
        foreach (var n in EnumerateDocumentationPageNodesInPackage(pkg))
            return GetOrCreateDocNodeDocument(n);
        return null;
    }

    private TabDocument? GetFirstDocumentationPageDocumentAcrossAllPackages()
    {
        foreach (var pkg in _docPackages)
        {
            foreach (var n in EnumerateDocumentationPageNodesInPackage(pkg))
                return GetOrCreateDocNodeDocument(n);
        }

        return null;
    }

    private static IEnumerable<DocNode> EnumerateDocumentationPageNodesInPackage(DocPackage pkg)
    {
        foreach (var root in DocNodesSortedForTree(pkg.Nodes))
        {
            var acc = new List<DocNode>();
            EnumerateDocPageNodes(root, acc);
            foreach (var p in acc)
                yield return p;
        }
    }

    private TabDocument GetDocumentationFindScratchDocument()
    {
        if (_documentationFindScratchDoc != null)
            return _documentationFindScratchDoc;

        var editor = CreateEditor(addTagTextMaskingTransformer: false);
        _documentationFindScratchDoc = new TabDocument
        {
            Header = "",
            StableTabId = "__documentation_find_scratch__",
            Editor = editor,
            CachedText = editor.Text,
            IsDirty = false,
            LastChangedUtc = DateTime.UtcNow,
            TagFeaturesEnabled = false
        };
        BindDocumentToEditor(_documentationFindScratchDoc, editor);
        return _documentationFindScratchDoc;
    }

    private static string FormatDocumentationFindLocation(DocPackage pkg, TabDocument tabDoc)
        => $"{pkg.Name} › {tabDoc.DisplayHeader}";

    private static void EnumerateDocPageNodes(DocNode node, List<DocNode> list)
    {
        if (node.Kind is DocNodeKind.Page or DocNodeKind.SubPage)
            list.Add(node);
        foreach (var child in DocNodesSortedForTree(node.Children))
            EnumerateDocPageNodes(child, list);
    }

    /// <summary>All notebook (doc package) order in <see cref="_docPackages"/>, pages in tree order per package.</summary>
    private List<(DocPackage Pkg, DocNode Node, TabDocument Doc)> GetAllDocumentationNotebookPageDocumentsOrdered()
    {
        var list = new List<(DocPackage, DocNode, TabDocument)>();
        foreach (var pkg in _docPackages)
        {
            foreach (var n in EnumerateDocumentationPageNodesInPackage(pkg))
                list.Add((pkg, n, GetOrCreateDocNodeDocument(n)));
        }

        return list;
    }

    /// <summary>Switches notebook if needed, selects the tree node, and shows the editor (Find across notebooks).</summary>
    private void NavigateToDocumentationNodeForFind(DocPackage pkg, DocNode targetNode)
    {
        FlushActiveDocPageText();

        _docCurrentPackage = pkg;
        pkg.CurrentNodeId = targetNode.Id;
        MarkDocPackageDirty(pkg);
        RefreshDocPackageCombo();

        _docCurrentNode = targetNode;
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private void NavigateToDocumentationPageForFind(DocPackage pkg, DocNode targetNode)
        => NavigateToDocumentationNodeForFind(pkg, targetNode);

    private void NavigateToDocumentationPackageSurfaceForFind(DocPackage pkg)
    {
        FlushActiveDocPageText();
        _docCurrentPackage = pkg;
        RefreshDocPackageCombo();
        RefreshDocTree();
        ShowActiveDocPageEditor();
    }

    private static IEnumerable<DocNode> EnumerateDocTreePreorderSorted(DocNode root)
    {
        yield return root;
        foreach (var child in DocNodesSortedForTree(root.Children))
        {
            foreach (var n in EnumerateDocTreePreorderSorted(child))
                yield return n;
        }
    }

    private List<(string Label, Action Jump)> CollectDocumentationStructuralNameFindJumps(string needle,
        StringComparison comparison, bool wholeWord)
    {
        var list = new List<(string Label, Action Jump)>();

        foreach (var pk in _docPackages)
        {
            if (!NeedleMatchesInNamedText(pk.Name ?? string.Empty, needle, comparison, wholeWord))
                continue;

            var p = pk;
            list.Add(($"{p.Name}: notebook name (name)", () => NavigateToDocumentationPackageSurfaceForFind(p)));
        }

        foreach (var pkg in _docPackages)
        foreach (var root in DocNodesSortedForTree(pkg.Nodes))
        foreach (var node in EnumerateDocTreePreorderSorted(root))
        {
            if (!NeedleMatchesInNamedText(node.Name ?? string.Empty, needle, comparison, wholeWord))
                continue;

            var nk = pkg;
            var nn = node;
            var label = $"{nk.Name} › {FormatDocNodeHeader(nn)} (name)";
            list.Add((label, () =>
            {
                NavigateToDocumentationNodeForFind(nk, nn);
                if (nn is { Kind: DocNodeKind.Page or DocNodeKind.SubPage })
                {
                    var ed = GetOrCreateDocNodeDocument(nn).Editor;
                    ed.Focus();
                    ed.TextArea.ClearSelection();
                    ed.TextArea.Caret.Offset = 0;
                }
            }));
        }

        return list;
    }

    // ── Mutations ───────────────────────────────────────────────────────────

    private void PromptCreateDocPackage()
    {
        var name = PromptForName("New doc package", "Doc package name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var pkg = new DocPackage { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        _docPackages.Add(pkg);
        _docCurrentPackage = pkg;
        _docCurrentNode = null;
        MarkDocPackageDirty(pkg);
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
        {
            if (_docNodeDocs.TryGetValue(id, out var docToRemove) && docToRemove.Editor != null)
                _docEditorPackageIds.Remove(docToRemove.Editor);
            _docNodeDocs.Remove(id);
        }

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

    /// <summary>Restores selection from <see cref="DocPackage.CurrentNodeId"/> (persisted with the package).</summary>
    private void SyncDocCurrentNodeFromPackage()
    {
        if (_docCurrentPackage == null)
        {
            _docCurrentNode = null;
            return;
        }

        var pkg = _docCurrentPackage;
        _docCurrentNode = FindDocNodeById(pkg.Nodes, pkg.CurrentNodeId) ?? FirstPageNode(pkg.Nodes);
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

    /// <summary>True when the given editor belongs to a Documentation page; the package id routes inline image lookups
    /// to the package's <c>.docp</c> zip rather than the global <c>{BackupFolder}/images/</c> folder.</summary>
    private bool TryGetDocPackageIdForEditor(TextEditor? editor,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? packageId)
    {
        packageId = null;
        if (editor == null) return false;
        if (_docEditorPackageIds.TryGetValue(editor, out var id) && !string.IsNullOrEmpty(id))
        {
            packageId = id;
            return true;
        }
        return false;
    }

    private DocPackage? FindDocPackageOwningNode(DocNode node)
    {
        foreach (var pkg in _docPackages)
        {
            if (DocPackageContainsNode(pkg.Nodes, node.Id))
                return pkg;
        }
        return null;
    }

    private static bool DocPackageContainsNode(IEnumerable<DocNode> nodes, string nodeId)
    {
        foreach (var n in nodes)
        {
            if (n.Id == nodeId) return true;
            if (DocPackageContainsNode(n.Children, nodeId)) return true;
        }
        return false;
    }

    private void SaveDirtyDocPackages()
    {
        FlushActiveDocPageText();
        if (_docDirtyPackageIds.Count == 0)
            return;
        foreach (var id in _docDirtyPackageIds)
        {
            var pkg = _docPackages.FirstOrDefault(p => p.Id == id);
            if (pkg == null) continue;
            _documentationService.SavePackage(_backupFolder, pkg);
        }
        _docDirtyPackageIds.Clear();
    }

    // ── Markdown transformers ───────────────────────────────────────────────

    private static bool IsMarkdownFenceLine(TextDocument doc, DocumentLine line)
    {
        if (line.Length < 3) return false;
        var t = doc.GetText(line.Offset, line.Length).TrimEnd();
        return t.StartsWith("```", StringComparison.Ordinal);
    }

    /// <summary>
    /// Closing-style fence (only ticks + whitespace) hides the entire line body; opener with info hides
    /// only the run of backticks (language text after <c>```</c> stays visible).
    /// </summary>
    private static bool TryGetMarkdownFenceHiddenSpan(string fullLine, out int hideStartRel, out int hideLength)
    {
        hideStartRel = 0;
        hideLength = 0;

        int ws = 0;
        while (ws < fullLine.Length && (fullLine[ws] is ' ' or '\t'))
            ws++;

        int i = ws;
        int ticks = 0;
        while (i < fullLine.Length && fullLine[i] == '`')
        {
            ticks++;
            i++;
        }

        if (ticks < 3)
            return false;

        bool remainderBlank = true;
        for (int k = ws + ticks; k < fullLine.Length; k++)
        {
            if (!char.IsWhiteSpace(fullLine[k]))
            {
                remainderBlank = false;
                break;
            }
        }

        if (remainderBlank)
        {
            hideStartRel = 0;
            hideLength = fullLine.Length;
        }
        else
        {
            hideStartRel = ws;
            hideLength = ticks;
        }

        return true;
    }

    /// <summary>
    /// Hides <c>```</c> fence delimiter lines visually (opening/closing); see <see cref="MarkdownFenceLineHiddenGenerator"/>.
    /// </summary>
    private sealed class MarkdownFenceLineHiddenGenerator : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (CurrentContext?.Document == null)
                return -1;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!IsMarkdownFenceLine(doc, docLine))
                return -1;

            var full = doc.GetText(docLine.Offset, docLine.Length);
            if (!TryGetMarkdownFenceHiddenSpan(full, out int hideRel, out _))
                return -1;

            int abs = docLine.Offset + hideRel;
            if (startOffset > abs)
                return -1;

            return abs;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            if (CurrentContext?.Document == null)
                return null;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!IsMarkdownFenceLine(doc, docLine))
                return null;

            var full = doc.GetText(docLine.Offset, docLine.Length);
            if (!TryGetMarkdownFenceHiddenSpan(full, out int hideRel, out int hideLen))
                return null;

            if (offset != docLine.Offset + hideRel || hideLen <= 0)
                return null;

            return new MarkdownHiddenDocumentSpanElement(hideLen);
        }
    }

    private static bool DocumentLineIsInsideFencedCodeBlock(TextDocument doc, DocumentLine line)
    {
        bool inside = false;
        foreach (var prior in doc.Lines)
        {
            if (prior.Offset >= line.Offset)
                break;
            if (IsMarkdownFenceLine(doc, prior))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Finds a closed <c>``` … ```</c> span whose lines contain <paramref name="offset"/> (fence lines included).
    /// Span is [<paramref name="spanStart"/>, <paramref name="spanStart"/> + <paramref name="spanLength"/>).
    /// </summary>
    private static bool TryGetFencedMarkdownBlockSpanContainingOffset(
        TextDocument doc, int offset, out int spanStart, out int spanLength)
    {
        spanStart = 0;
        spanLength = 0;
        if (doc.LineCount == 0)
            return false;

        offset = Math.Max(0, Math.Min(offset, doc.TextLength));
        var caretLineNum = doc.GetLineByOffset(offset).LineNumber;

        bool inside = false;
        int openLineNum = 0;
        int openOffset = 0;

        for (int lineNum = 1; lineNum <= doc.LineCount; lineNum++)
        {
            var line = doc.GetLineByNumber(lineNum);
            if (!IsMarkdownFenceLine(doc, line))
                continue;

            if (!inside)
            {
                inside = true;
                openLineNum = lineNum;
                openOffset = line.Offset;
            }
            else
            {
                int closeEnd = line.EndOffset;
                inside = false;
                if (caretLineNum >= openLineNum && caretLineNum <= lineNum)
                {
                    spanStart = openOffset;
                    spanLength = closeEnd - openOffset;
                    return spanLength > 0;
                }
            }
        }

        return false;
    }

    private static void RemoveDocumentationCodeSnippet(TextEditor editor)
    {
        if (editor.Document == null)
            return;

        int caret = editor.TextArea.Caret.Offset;
        if (!TryGetFencedMarkdownBlockSpanContainingOffset(editor.Document, caret, out int start, out int length))
            return;

        editor.Document.Replace(start, length, string.Empty);
        int newCaret = Math.Max(0, Math.Min(start, editor.Document.TextLength));
        editor.TextArea.Caret.Offset = newCaret;
        editor.Select(newCaret, 0);
        editor.TextArea.TextView.Redraw();
    }

    /// <summary>Fence delimiter lines plus body lines strictly between matching fences.</summary>
    private static bool DocumentLineShowsFencedCodeChrome(TextDocument doc, DocumentLine line)
    {
        bool inside = false;
        foreach (var prior in doc.Lines)
        {
            if (prior.Offset >= line.Offset)
                break;
            if (IsMarkdownFenceLine(doc, prior))
                inside = !inside;
        }

        return inside || IsMarkdownFenceLine(doc, line);
    }

    private static bool TryGetMarkdownHeadingPrefixLength(string lineText, out int prefixLength)
    {
        prefixLength = 0;
        int hashes = 0;
        while (hashes < lineText.Length && hashes < 3 && lineText[hashes] == '#')
            hashes++;
        if (hashes == 0) return false;
        if (hashes >= lineText.Length || lineText[hashes] != ' ')
            return false;
        prefixLength = hashes + 1;
        return true;
    }

    /// <summary>
    /// Uses <see cref="TextHidden"/> so the underlying document span occupies no horizontal width.
    /// </summary>
    private sealed class MarkdownHiddenDocumentSpanElement : VisualLineElement
    {
        public MarkdownHiddenDocumentSpanElement(int documentLength)
            : base(1, documentLength)
        {
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
            => new TextHidden(VisualLength);
    }

    /// <summary>
    /// Styles the visible heading title (after <c>#</c> markers); the markers themselves stay
    /// visible at normal size so the user can see what they typed.
    /// </summary>
    private sealed class MarkdownHeadingTransformer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (DocumentLineIsInsideFencedCodeBlock(doc, line))
                return;

            var text = doc.GetText(line.Offset, line.Length);
            if (!TryGetMarkdownHeadingPrefixLength(text, out int prefixLen))
                return;

            int hashes = prefixLen - 1;
            double sizeMul = hashes switch
            {
                1 => 1.7,
                2 => 1.4,
                3 => 1.2,
                _ => 1.0
            };

            int titleStart = line.Offset + prefixLen;
            if (titleStart >= line.EndOffset)
                return;

            ChangeLinePart(titleStart, line.EndOffset, ve =>
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
    /// Wiki-style full-width rectangular bands for fenced <c>```</c> regions (delimiter lines + interior),
    /// drawn beneath text on the Background layer so short lines still span the viewport.
    /// </summary>
    private sealed class MarkdownFencedCodeBackgroundRenderer : IBackgroundRenderer
    {
        private static readonly Brush Fill = CodeChromeBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
        private static readonly Brush LeftAccent = CodeChromeBrush(Color.FromRgb(0xD9, 0xDF, 0xEA));

        private static SolidColorBrush CodeChromeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            double drawWidth = textView.ActualWidth;
            if (textView is IScrollInfo si)
                drawWidth = Math.Max(drawWidth, si.HorizontalOffset + si.ViewportWidth);

            var doc = textView.Document;

            foreach (var vl in textView.VisualLines)
            {
                if (vl.IsDisposed)
                    continue;

                var docLine = vl.FirstDocumentLine;
                if (!DocumentLineShowsFencedCodeChrome(doc, docLine))
                    continue;

                var rowRects = BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                    textView, vl, 0, vl.VisualLength).ToList();
                if (rowRects.Count == 0)
                    continue;

                double top = rowRects.Min(r => r.Top);
                double bottom = rowRects.Max(r => r.Bottom);
                var band = new Rect(0, top, drawWidth, bottom - top);

                drawingContext.DrawRectangle(Fill, null, band);
                const double accentW = 3.5;
                drawingContext.DrawRectangle(LeftAccent, null, new Rect(0, top, accentW, bottom - top));
            }
        }
    }

    /// <summary>
    /// Monospace typography for fenced <c>```</c> regions; fill is painted by
    /// <see cref="MarkdownFencedCodeBackgroundRenderer"/> so bands span the viewport width.
    /// </summary>
    private sealed class MarkdownFencedCodeBlockTransformer : DocumentColorizingTransformer
    {
        private static readonly FontFamily CodeFont = new("Consolas, Courier New");

        protected override void ColorizeLine(DocumentLine line)
        {
            if (CurrentContext?.Document == null) return;
            var doc = CurrentContext.Document;

            if (!DocumentLineShowsFencedCodeChrome(doc, line))
                return;

            ChangeLinePart(line.Offset, line.EndOffset, ve =>
            {
                var t = ve.TextRunProperties.Typeface;
                ve.TextRunProperties.SetTypeface(new Typeface(CodeFont, t.Style, t.Weight, t.Stretch));
            });
        }
    }

    /// <summary>True when <paramref name="line"/> is the opener or closer of a closed <c>``` … ```</c>
    /// pair. An unmatched fence (user mid-typing) stays editable.</summary>
    private static bool IsFenceLineInClosedPair(TextDocument doc, DocumentLine line)
    {
        if (!IsMarkdownFenceLine(doc, line))
            return false;

        DocumentLine? opener = null;
        foreach (var candidate in doc.Lines)
        {
            if (!IsMarkdownFenceLine(doc, candidate))
                continue;
            if (opener == null)
            {
                opener = candidate;
            }
            else
            {
                if (opener.LineNumber == line.LineNumber || candidate.LineNumber == line.LineNumber)
                    return true;
                opener = null;
            }
        }
        return false;
    }

    /// <summary>
    /// Blocks edits to <c>```</c> fence lines (text and surrounding newlines) once they form a closed
    /// pair, so typing/backspace/delete cannot corrupt a code snippet. Removal goes through
    /// <see cref="RemoveDocumentationCodeSnippet"/>, which calls <c>Document.Replace</c> directly.
    /// </summary>
    private sealed class MarkdownFenceReadOnlySectionProvider : IReadOnlySectionProvider
    {
        private readonly Func<TextDocument?> _getDoc;

        public MarkdownFenceReadOnlySectionProvider(Func<TextDocument?> getDoc) => _getDoc = getDoc;

        public bool CanInsert(int offset)
        {
            var doc = _getDoc();
            if (doc == null || doc.TextLength == 0)
                return true;
            offset = Math.Clamp(offset, 0, doc.TextLength);
            if (offset == doc.TextLength)
                return true;
            var line = doc.GetLineByOffset(offset);
            return !IsFenceLineInClosedPair(doc, line);
        }

        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
        {
            if (segment == null)
                yield break;
            var doc = _getDoc();
            if (doc == null || doc.TextLength == 0 || segment.Length <= 0)
            {
                yield return segment;
                yield break;
            }

            int segStart = Math.Max(0, segment.Offset);
            int segEnd = Math.Min(doc.TextLength, segment.EndOffset);
            if (segEnd <= segStart)
                yield break;

            int? runStart = null;
            for (int o = segStart; o < segEnd; o++)
            {
                if (IsOffsetWritable(doc, o))
                {
                    runStart ??= o;
                }
                else if (runStart.HasValue)
                {
                    yield return new WritableSegment(runStart.Value, o - runStart.Value);
                    runStart = null;
                }
            }
            if (runStart.HasValue)
                yield return new WritableSegment(runStart.Value, segEnd - runStart.Value);
        }

        private static bool IsOffsetWritable(TextDocument doc, int offset)
        {
            if (offset < 0 || offset >= doc.TextLength) return true;
            var line = doc.GetLineByOffset(offset);
            if (IsFenceLineInClosedPair(doc, line))
                return false;

            // The newline at end of a content line is shared with the next line; if the next line
            // is a locked fence, deleting that newline would merge content into the fence.
            if (offset >= line.Offset + line.Length && line.LineNumber < doc.LineCount)
            {
                var next = doc.GetLineByNumber(line.LineNumber + 1);
                if (IsFenceLineInClosedPair(doc, next))
                    return false;
            }
            return true;
        }
    }
}

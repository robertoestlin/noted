using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Noted.Models;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private readonly LongTermNotesService _longTermNotesService = new();

    /// <summary>All notebooks loaded from disk (one file each). Order mirrors <see cref="_ltIndex"/>.Order.</summary>
    private readonly List<Notebook> _ltNotebooks = new();
    private NotebooksIndex _ltIndex = new();

    /// <summary>Per-page cached (TabDocument, TextEditor) so anchor-bound metadata survives page switches within a session.</summary>
    private readonly Dictionary<string, TabDocument> _ltPageDocs = new();
    /// <summary>Notebooks that have unsaved changes since last <see cref="SaveDirtyLongTermNotebooks"/>.</summary>
    private readonly HashSet<string> _ltDirtyNotebookIds = new(StringComparer.Ordinal);

    private Notebook? _ltCurrentNotebook;
    private LtSection? _ltCurrentSection;
    private LtPage? _ltCurrentPage;

    // UI elements built in BuildLongTermView
    private ComboBox? _ltNotebookCombo;
    private TreeView? _ltSectionsTree;
    private TreeView? _ltPagesTree;
    private ContentControl? _ltEditorHost;
    private Grid? _ltEmptyState;
    private Grid? _ltMainGrid;

    /// <summary>Loaded once at startup so the LT mode can render synchronously when first opened.</summary>
    private void LoadLongTermNotesFromDisk()
    {
        _ltNotebooks.Clear();
        _ltIndex = _longTermNotesService.LoadIndex(_backupFolder);
        var loaded = _longTermNotesService.LoadAllNotebooks(_backupFolder);

        // Apply user-chosen order from index, append unknown ids at the end.
        var byId = loaded.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var id in _ltIndex.Order)
        {
            if (byId.TryGetValue(id, out var nb))
            {
                _ltNotebooks.Add(nb);
                byId.Remove(id);
            }
        }
        foreach (var nb in byId.Values)
            _ltNotebooks.Add(nb);

        // Restore last-selected notebook if still present.
        if (_ltIndex.CurrentId is { Length: > 0 } currentId)
            _ltCurrentNotebook = _ltNotebooks.FirstOrDefault(n => n.Id == currentId);
        if (_ltCurrentNotebook == null && _ltNotebooks.Count > 0)
            _ltCurrentNotebook = _ltNotebooks[0];
    }

    private void BuildLongTermView()
    {
        if (LongTermView == null) return;
        LongTermView.Children.Clear();

        // Make sure data is loaded before we render.
        if (_ltIndex.Order.Count == 0 && _ltNotebooks.Count == 0)
            LoadLongTermNotesFromDisk();

        // ── Empty state ───────────────────────────────────────────────────────
        _ltEmptyState = new Grid { Visibility = Visibility.Collapsed };
        var emptyStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        emptyStack.Children.Add(new TextBlock
        {
            Text = "No notebooks yet",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        emptyStack.Children.Add(new TextBlock
        {
            Text = "Create your first notebook to start writing long-term notes.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var createBtn = new Button
        {
            Content = "Create notebook",
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        createBtn.Click += (_, _) => PromptCreateNotebook();
        emptyStack.Children.Add(createBtn);
        _ltEmptyState.Children.Add(emptyStack);
        LongTermView.Children.Add(_ltEmptyState);

        // ── Main grid (3 columns + 2 splitters) ──────────────────────────────
        _ltMainGrid = new Grid();
        _ltMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        _ltMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _ltMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _ltMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _ltMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Col 0: notebook combo + sections tree
        var leftDock = new DockPanel { Margin = new Thickness(8) };
        Grid.SetColumn(leftDock, 0);

        var notebookHeader = new Grid();
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(notebookHeader, Dock.Top);

        _ltNotebookCombo = new ComboBox { Margin = new Thickness(0, 0, 4, 6) };
        Grid.SetColumn(_ltNotebookCombo, 0);
        _ltNotebookCombo.SelectionChanged += LtNotebookCombo_SelectionChanged;
        notebookHeader.Children.Add(_ltNotebookCombo);

        var addNotebookBtn = new Button
        {
            Content = "+",
            Width = 26,
            Height = 24,
            ToolTip = "Add notebook"
        };
        Grid.SetColumn(addNotebookBtn, 1);
        addNotebookBtn.Click += (_, _) => PromptCreateNotebook();
        notebookHeader.Children.Add(addNotebookBtn);
        leftDock.Children.Add(notebookHeader);

        var sectionsLabel = new TextBlock
        {
            Text = "Sections",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };
        DockPanel.SetDock(sectionsLabel, Dock.Top);
        leftDock.Children.Add(sectionsLabel);

        _ltSectionsTree = new TreeView { BorderThickness = new Thickness(1) };
        _ltSectionsTree.SelectedItemChanged += LtSectionsTree_SelectedItemChanged;
        leftDock.Children.Add(_ltSectionsTree);
        _ltMainGrid.Children.Add(leftDock);

        var splitter1 = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetColumn(splitter1, 1);
        _ltMainGrid.Children.Add(splitter1);

        // Col 2: pages tree
        var middleDock = new DockPanel { Margin = new Thickness(8) };
        Grid.SetColumn(middleDock, 2);
        var pagesLabel = new TextBlock
        {
            Text = "Pages",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(pagesLabel, Dock.Top);
        middleDock.Children.Add(pagesLabel);
        _ltPagesTree = new TreeView { BorderThickness = new Thickness(1) };
        _ltPagesTree.SelectedItemChanged += LtPagesTree_SelectedItemChanged;
        middleDock.Children.Add(_ltPagesTree);
        _ltMainGrid.Children.Add(middleDock);

        var splitter2 = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetColumn(splitter2, 3);
        _ltMainGrid.Children.Add(splitter2);

        // Col 4: editor host
        _ltEditorHost = new ContentControl { Margin = new Thickness(0) };
        Grid.SetColumn(_ltEditorHost, 4);
        _ltMainGrid.Children.Add(_ltEditorHost);

        LongTermView.Children.Add(_ltMainGrid);

        RefreshLongTermView();
    }

    private void RefreshLongTermView()
    {
        if (_ltMainGrid == null || _ltEmptyState == null) return;

        bool hasNotebooks = _ltNotebooks.Count > 0;
        _ltEmptyState.Visibility = hasNotebooks ? Visibility.Collapsed : Visibility.Visible;
        _ltMainGrid.Visibility   = hasNotebooks ? Visibility.Visible : Visibility.Collapsed;
        if (!hasNotebooks)
            return;

        RefreshLtNotebookCombo();
        RefreshLtSectionsTree();
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    private void RefreshLtNotebookCombo()
    {
        if (_ltNotebookCombo == null) return;
        var prev = _ltCurrentNotebook;
        _ltNotebookCombo.SelectionChanged -= LtNotebookCombo_SelectionChanged;
        _ltNotebookCombo.Items.Clear();
        foreach (var nb in _ltNotebooks)
        {
            _ltNotebookCombo.Items.Add(new ComboBoxItem { Content = nb.Name, Tag = nb });
        }
        if (prev != null)
        {
            foreach (ComboBoxItem item in _ltNotebookCombo.Items)
            {
                if (item.Tag is Notebook nb && nb.Id == prev.Id)
                {
                    _ltNotebookCombo.SelectedItem = item;
                    break;
                }
            }
        }
        if (_ltNotebookCombo.SelectedIndex < 0 && _ltNotebookCombo.Items.Count > 0)
            _ltNotebookCombo.SelectedIndex = 0;
        _ltNotebookCombo.SelectionChanged += LtNotebookCombo_SelectionChanged;

        // Sync current notebook in case the SelectedIndex change above implicitly chose one.
        if (_ltNotebookCombo.SelectedItem is ComboBoxItem chosen && chosen.Tag is Notebook chosenNb)
            _ltCurrentNotebook = chosenNb;
    }

    private void LtNotebookCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ltNotebookCombo?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not Notebook nb) return;

        // Persist text from previously open page first.
        FlushActiveLongTermPageText();

        _ltCurrentNotebook = nb;
        _ltIndex.CurrentId = nb.Id;
        MarkLongTermIndexDirty();

        // Restore last-selected section/page within this notebook if possible, else first available.
        _ltCurrentSection = FindLtSectionById(nb, nb.CurrentSectionId)
                            ?? nb.Sections.FirstOrDefault();
        _ltCurrentPage = FindLtPageInSection(_ltCurrentSection, nb.CurrentPageId)
                         ?? FirstPageInSection(_ltCurrentSection);

        RefreshLtSectionsTree();
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    // ── Sections tree ───────────────────────────────────────────────────────

    private void RefreshLtSectionsTree()
    {
        if (_ltSectionsTree == null) return;
        _ltSectionsTree.Items.Clear();
        if (_ltCurrentNotebook == null) return;

        foreach (var section in _ltCurrentNotebook.Sections)
        {
            var node = BuildLtSectionNode(section);
            _ltSectionsTree.Items.Add(node);
            ExpandIfContainsCurrent(node, section);
        }

        // Add "+ Add section" affordance via context menu on the TreeView itself.
        _ltSectionsTree.ContextMenu = BuildLtSectionsRootContextMenu();
    }

    private TreeViewItem BuildLtSectionNode(LtSection section)
    {
        var item = new TreeViewItem
        {
            Header = section.Name,
            Tag = section,
            IsSelected = ReferenceEquals(section, _ltCurrentSection)
        };
        item.ContextMenu = BuildLtSectionContextMenu(section, isSubSection: false);
        foreach (var sub in section.SubSections)
        {
            var subItem = new TreeViewItem
            {
                Header = sub.Name,
                Tag = sub,
                IsSelected = ReferenceEquals(sub, _ltCurrentSection)
            };
            subItem.ContextMenu = BuildLtSectionContextMenu(sub, isSubSection: true);
            item.Items.Add(subItem);
        }
        return item;
    }

    private void ExpandIfContainsCurrent(TreeViewItem node, LtSection section)
    {
        if (_ltCurrentSection == null) return;
        if (ReferenceEquals(section, _ltCurrentSection))
        {
            node.IsExpanded = true;
            return;
        }
        if (section.SubSections.Any(s => ReferenceEquals(s, _ltCurrentSection)))
            node.IsExpanded = true;
    }

    private void LtSectionsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem tvi) return;
        if (tvi.Tag is not LtSection section) return;

        FlushActiveLongTermPageText();

        _ltCurrentSection = section;
        if (_ltCurrentNotebook != null)
        {
            _ltCurrentNotebook.CurrentSectionId = section.Id;
            MarkLongTermNotebookDirty(_ltCurrentNotebook);
        }
        _ltCurrentPage = FindLtPageInSection(_ltCurrentSection, _ltCurrentNotebook?.CurrentPageId)
                         ?? FirstPageInSection(_ltCurrentSection);

        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    private ContextMenu BuildLtSectionsRootContextMenu()
    {
        var menu = new ContextMenu();
        var addSection = new MenuItem { Header = "Add section..." };
        addSection.Click += (_, _) => PromptAddLtSection(parent: null);
        menu.Items.Add(addSection);
        return menu;
    }

    private ContextMenu BuildLtSectionContextMenu(LtSection section, bool isSubSection)
    {
        var menu = new ContextMenu();
        if (!isSubSection)
        {
            var addSub = new MenuItem { Header = "Add sub-section..." };
            addSub.Click += (_, _) => PromptAddLtSection(parent: section);
            menu.Items.Add(addSub);
            menu.Items.Add(new Separator());
        }
        var addPage = new MenuItem { Header = "Add page..." };
        addPage.Click += (_, _) => PromptAddLtPage(section, parentPage: null);
        menu.Items.Add(addPage);
        menu.Items.Add(new Separator());
        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => PromptRenameLtSection(section);
        menu.Items.Add(rename);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => DeleteLtSection(section);
        menu.Items.Add(delete);
        return menu;
    }

    // ── Pages tree ──────────────────────────────────────────────────────────

    private void RefreshLtPagesTree()
    {
        if (_ltPagesTree == null) return;
        _ltPagesTree.Items.Clear();
        if (_ltCurrentSection == null) return;

        foreach (var page in _ltCurrentSection.Pages)
        {
            var node = BuildLtPageNode(page);
            _ltPagesTree.Items.Add(node);
            if (_ltCurrentPage != null && (
                ReferenceEquals(page, _ltCurrentPage) ||
                page.SubPages.Any(s => ReferenceEquals(s, _ltCurrentPage))))
            {
                node.IsExpanded = true;
            }
        }
        _ltPagesTree.ContextMenu = BuildLtPagesRootContextMenu();
    }

    private TreeViewItem BuildLtPageNode(LtPage page)
    {
        var item = new TreeViewItem
        {
            Header = page.Name,
            Tag = page,
            IsSelected = ReferenceEquals(page, _ltCurrentPage)
        };
        item.ContextMenu = BuildLtPageContextMenu(page, isSubPage: false);
        foreach (var sub in page.SubPages)
        {
            var subItem = new TreeViewItem
            {
                Header = sub.Name,
                Tag = sub,
                IsSelected = ReferenceEquals(sub, _ltCurrentPage)
            };
            subItem.ContextMenu = BuildLtPageContextMenu(sub, isSubPage: true);
            item.Items.Add(subItem);
        }
        return item;
    }

    private void LtPagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem tvi) return;
        if (tvi.Tag is not LtPage page) return;

        FlushActiveLongTermPageText();

        _ltCurrentPage = page;
        if (_ltCurrentNotebook != null)
        {
            _ltCurrentNotebook.CurrentPageId = page.Id;
            MarkLongTermNotebookDirty(_ltCurrentNotebook);
        }
        ShowActiveLongTermPageEditor();
    }

    private ContextMenu BuildLtPagesRootContextMenu()
    {
        var menu = new ContextMenu();
        var addPage = new MenuItem
        {
            Header = "Add page...",
            IsEnabled = _ltCurrentSection != null
        };
        addPage.Click += (_, _) =>
        {
            if (_ltCurrentSection != null)
                PromptAddLtPage(_ltCurrentSection, parentPage: null);
        };
        menu.Items.Add(addPage);
        return menu;
    }

    private ContextMenu BuildLtPageContextMenu(LtPage page, bool isSubPage)
    {
        var menu = new ContextMenu();
        if (!isSubPage)
        {
            var addSub = new MenuItem { Header = "Add sub-page..." };
            addSub.Click += (_, _) => PromptAddLtPage(_ltCurrentSection!, parentPage: page);
            menu.Items.Add(addSub);
            menu.Items.Add(new Separator());
        }
        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => PromptRenameLtPage(page);
        menu.Items.Add(rename);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => DeleteLtPage(page);
        menu.Items.Add(delete);
        return menu;
    }

    // ── Editor host ─────────────────────────────────────────────────────────

    private void ShowActiveLongTermPageEditor()
    {
        if (_ltEditorHost == null) return;
        if (_ltCurrentPage == null)
        {
            _ltEditorHost.Content = new TextBlock
            {
                Text = "Select a page or create one to start writing.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }

        var doc = GetOrCreateLtPageDocument(_ltCurrentPage);
        _ltEditorHost.Content = doc.Editor;
    }

    private TabDocument GetOrCreateLtPageDocument(LtPage page)
    {
        if (_ltPageDocs.TryGetValue(page.Id, out var existing))
            return existing;

        var editor = CreateEditor();
        editor.Text = page.Content ?? string.Empty;

        var doc = new TabDocument
        {
            Header = page.Name,
            StableTabId = page.Id,
            Editor = editor,
            CachedText = editor.Text,
            IsDirty = false,
            LastChangedUtc = DateTime.UtcNow
        };

        BindDocumentToEditor(doc, editor);

        // After binding, route TextChanged into the LT dirty tracker so the
        // notebook gets persisted — BindDocumentToEditor's TextChanged also
        // fires, marking the (orphan) TabDocument dirty, but only this layer
        // marks the owning notebook for save.
        editor.TextChanged += (_, _) =>
        {
            if (_ltCurrentNotebook != null)
                MarkLongTermNotebookDirty(_ltCurrentNotebook);
        };

        _ltPageDocs[page.Id] = doc;
        return doc;
    }

    private void FlushActiveLongTermPageText()
    {
        if (_ltCurrentPage == null) return;
        if (!_ltPageDocs.TryGetValue(_ltCurrentPage.Id, out var doc)) return;
        var newText = doc.Editor?.Text ?? doc.CachedText;
        if (newText != _ltCurrentPage.Content)
        {
            _ltCurrentPage.Content = newText;
            if (_ltCurrentNotebook != null)
                MarkLongTermNotebookDirty(_ltCurrentNotebook);
        }
    }

    private void FocusActiveLongTermPageEditor()
    {
        if (!_ltViewBuilt) return;
        if (_ltEditorHost?.Content is TextEditor editor)
            editor.Focus();
    }

    // ── Mutations ───────────────────────────────────────────────────────────

    private void PromptCreateNotebook()
    {
        var name = PromptForName("New notebook", "Notebook name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var nb = new Notebook
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim()
        };
        _ltNotebooks.Add(nb);
        _ltIndex.Order.Add(nb.Id);
        _ltIndex.CurrentId = nb.Id;
        _ltCurrentNotebook = nb;
        _ltCurrentSection = null;
        _ltCurrentPage = null;
        MarkLongTermNotebookDirty(nb);
        MarkLongTermIndexDirty();
        RefreshLongTermView();
    }

    private void PromptAddLtSection(LtSection? parent)
    {
        if (_ltCurrentNotebook == null) return;
        var name = PromptForName(parent == null ? "New section" : "New sub-section", "Name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var section = new LtSection { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        if (parent == null)
            _ltCurrentNotebook.Sections.Add(section);
        else
            parent.SubSections.Add(section);
        _ltCurrentSection = section;
        _ltCurrentPage = null;
        MarkLongTermNotebookDirty(_ltCurrentNotebook);
        RefreshLtSectionsTree();
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    private void PromptRenameLtSection(LtSection section)
    {
        var name = PromptForName("Rename", "New name:", section.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        section.Name = name.Trim();
        if (_ltCurrentNotebook != null)
            MarkLongTermNotebookDirty(_ltCurrentNotebook);
        RefreshLtSectionsTree();
    }

    private void DeleteLtSection(LtSection section)
    {
        if (_ltCurrentNotebook == null) return;
        if (MessageBox.Show($"Delete section '{section.Name}' and all its pages?",
                "Delete section", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        // Remove from any parent
        if (_ltCurrentNotebook.Sections.Remove(section))
        {
            // top-level removed
        }
        else
        {
            foreach (var s in _ltCurrentNotebook.Sections)
                s.SubSections.Remove(section);
        }
        // Drop cached editors for any pages we just removed.
        foreach (var pid in CollectPageIds(section))
            _ltPageDocs.Remove(pid);

        if (ReferenceEquals(_ltCurrentSection, section))
        {
            _ltCurrentSection = _ltCurrentNotebook.Sections.FirstOrDefault();
            _ltCurrentPage = FirstPageInSection(_ltCurrentSection);
        }
        MarkLongTermNotebookDirty(_ltCurrentNotebook);
        RefreshLtSectionsTree();
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    private void PromptAddLtPage(LtSection section, LtPage? parentPage)
    {
        var name = PromptForName(parentPage == null ? "New page" : "New sub-page", "Name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var page = new LtPage { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        if (parentPage == null)
            section.Pages.Add(page);
        else
            parentPage.SubPages.Add(page);
        _ltCurrentSection = section;
        _ltCurrentPage = page;
        if (_ltCurrentNotebook != null)
        {
            _ltCurrentNotebook.CurrentSectionId = section.Id;
            _ltCurrentNotebook.CurrentPageId = page.Id;
            MarkLongTermNotebookDirty(_ltCurrentNotebook);
        }
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    private void PromptRenameLtPage(LtPage page)
    {
        var name = PromptForName("Rename", "New name:", page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        page.Name = name.Trim();
        if (_ltPageDocs.TryGetValue(page.Id, out var doc))
            doc.Header = name.Trim();
        if (_ltCurrentNotebook != null)
            MarkLongTermNotebookDirty(_ltCurrentNotebook);
        RefreshLtPagesTree();
    }

    private void DeleteLtPage(LtPage page)
    {
        if (_ltCurrentNotebook == null || _ltCurrentSection == null) return;
        if (MessageBox.Show($"Delete page '{page.Name}'?",
                "Delete page", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        if (!_ltCurrentSection.Pages.Remove(page))
        {
            foreach (var p in _ltCurrentSection.Pages)
                p.SubPages.Remove(page);
        }
        foreach (var pid in new[] { page.Id }.Concat(page.SubPages.Select(s => s.Id)))
            _ltPageDocs.Remove(pid);

        if (ReferenceEquals(_ltCurrentPage, page))
            _ltCurrentPage = FirstPageInSection(_ltCurrentSection);

        MarkLongTermNotebookDirty(_ltCurrentNotebook);
        RefreshLtPagesTree();
        ShowActiveLongTermPageEditor();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static LtSection? FindLtSectionById(Notebook nb, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var s in nb.Sections)
        {
            if (s.Id == id) return s;
            foreach (var sub in s.SubSections)
                if (sub.Id == id) return sub;
        }
        return null;
    }

    private static LtPage? FindLtPageInSection(LtSection? section, string? id)
    {
        if (section == null || string.IsNullOrEmpty(id)) return null;
        foreach (var p in section.Pages)
        {
            if (p.Id == id) return p;
            foreach (var sp in p.SubPages)
                if (sp.Id == id) return sp;
        }
        return null;
    }

    private static LtPage? FirstPageInSection(LtSection? section)
    {
        if (section == null) return null;
        return section.Pages.FirstOrDefault();
    }

    private static IEnumerable<string> CollectPageIds(LtSection section)
    {
        foreach (var p in section.Pages)
        {
            yield return p.Id;
            foreach (var sp in p.SubPages)
                yield return sp.Id;
        }
        foreach (var sub in section.SubSections)
        foreach (var pid in CollectPageIds(sub))
            yield return pid;
    }

    /// <summary>Modal name prompt shared by Long-Term Notes and Documentation. Returns trimmed input, or null on cancel/empty.</summary>
    internal string? PromptForName(string title, string label, string initialValue)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        var input = new TextBox { Text = initialValue, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                MessageBox.Show("Name cannot be empty.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dlg.DialogResult = true;
        };
        dlg.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
            Keyboard.Focus(input);
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        dlg.Content = root;
        var result = dlg.ShowDialog();
        return result == true ? input.Text?.Trim() : null;
    }

    private void MarkLongTermNotebookDirty(Notebook nb) => _ltDirtyNotebookIds.Add(nb.Id);
    private void MarkLongTermIndexDirty() => _ltIndexDirty = true;
    private bool _ltIndexDirty;

    /// <summary>Persist any notebook with pending edits + the index. Called from autosave + close.</summary>
    private void SaveDirtyLongTermNotebooks()
    {
        FlushActiveLongTermPageText();

        if (_ltDirtyNotebookIds.Count > 0)
        {
            foreach (var id in _ltDirtyNotebookIds)
            {
                var nb = _ltNotebooks.FirstOrDefault(n => n.Id == id);
                if (nb == null) continue;
                _longTermNotesService.SaveNotebook(_backupFolder, nb);
            }
            _ltDirtyNotebookIds.Clear();
        }
        if (_ltIndexDirty)
        {
            _ltIndex.Order = _ltNotebooks.Select(n => n.Id).ToList();
            _longTermNotesService.SaveIndex(_backupFolder, _ltIndex);
            _ltIndexDirty = false;
        }
    }
}

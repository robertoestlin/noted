using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const string DefaultShortcutToggleTodoPanel = "F3";
    private const double TodoPanelOpenWidth = 330;
    private const string TodoDragDataFormat = "Noted.TodoItemId";
    private const string TodoClosedAreaId = "__closed__";
    private const string TodoClosedGroupId = "__closed__";
    private readonly List<TodoItemState> _todoItems = [];
    private bool _todoPanelVisible;
    private Point _todoDragStartPoint;
    private string? _todoDragSourceItemId;
    private bool _suppressTaskAreaSelectionChanged;

    private void InitializeTodoPanel()
    {
        RefreshTodoAreaSelector();
        UpdateTodoPanelTitleText();
        UpdateTodoPanelVisibility();
        RenderTodoLists();
    }

    private List<TodoItemState> BuildTodoItemsSnapshot()
    {
        PruneCompletedTodoItems();
        return _todoItems.Select(item => new TodoItemState
        {
            Id = item.Id,
            Text = item.Text,
            Bucket = item.Bucket,
            AreaId = item.AreaId,
            GroupId = item.GroupId,
            SortOrder = item.SortOrder,
            CreatedUtc = item.CreatedUtc,
            CompletedAtUtc = item.CompletedAtUtc
        }).ToList();
    }

    private void ApplyTodoItems(IEnumerable<TodoItemState>? items)
    {
        _todoItems.Clear();
        if (items != null)
        {
            foreach (var rawItem in items)
            {
                var text = (rawItem.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                    continue;

                _todoItems.Add(new TodoItemState
                {
                    Id = string.IsNullOrWhiteSpace(rawItem.Id) ? Guid.NewGuid().ToString("N") : rawItem.Id,
                    Text = text,
                    Bucket = rawItem.Bucket,
                    AreaId = rawItem.AreaId,
                    GroupId = rawItem.GroupId,
                    SortOrder = rawItem.SortOrder,
                    CreatedUtc = rawItem.CreatedUtc == default ? DateTime.UtcNow : rawItem.CreatedUtc,
                    CompletedAtUtc = rawItem.CompletedAtUtc
                });
            }
        }

        MigrateLegacyTodoItemsToGroups();

        // Always start with the todo panel collapsed on app launch.
        _todoPanelVisible = false;
        NormalizeTodoSortOrders();
        PruneCompletedTodoItems();
        if (TodoPanelColumn != null)
            UpdateTodoPanelVisibility();
        if (TodoGroupsContainer != null)
        {
            RefreshTodoAreaSelector();
            UpdateTodoPanelTitleText();
            RenderTodoLists();
        }
    }

    private void MigrateLegacyTodoItemsToGroups()
    {
        if (_taskAreas == null || _taskAreas.Count == 0)
            return;

        var mainArea = _taskAreas.FirstOrDefault(area => string.Equals(area.Id, DefaultTaskAreaId, StringComparison.OrdinalIgnoreCase))
            ?? _taskAreas[0];
        var firstGroupId = mainArea.Groups.Count > 0 ? mainArea.Groups[0].Id : DefaultTaskGroups[0].Id;

        foreach (var item in _todoItems)
        {
            var areaExists = !string.IsNullOrWhiteSpace(item.AreaId)
                && _taskAreas.Any(area => string.Equals(area.Id, item.AreaId, StringComparison.OrdinalIgnoreCase));
            if (!areaExists)
                item.AreaId = mainArea.Id;

            var ownerArea = _taskAreas.FirstOrDefault(area => string.Equals(area.Id, item.AreaId, StringComparison.OrdinalIgnoreCase))
                ?? mainArea;
            var groupExists = !string.IsNullOrWhiteSpace(item.GroupId)
                && ownerArea.Groups.Any(group => string.Equals(group.Id, item.GroupId, StringComparison.OrdinalIgnoreCase));

            if (!groupExists)
            {
                // Map from legacy Bucket into the default Main area groups.
                item.AreaId = mainArea.Id;
                item.GroupId = item.Bucket switch
                {
                    TodoBucket.ThisWeek => mainArea.Groups.FirstOrDefault(g => string.Equals(g.Id, "this-week", StringComparison.OrdinalIgnoreCase))?.Id
                        ?? firstGroupId,
                    _ => mainArea.Groups.FirstOrDefault(g => string.Equals(g.Id, "today", StringComparison.OrdinalIgnoreCase))?.Id
                        ?? firstGroupId
                };
            }
        }
    }

    private TaskAreaState? GetCurrentTaskArea()
    {
        if (_taskAreas == null || _taskAreas.Count == 0)
            return null;
        return _taskAreas.FirstOrDefault(area => string.Equals(area.Id, _currentTaskAreaId, StringComparison.OrdinalIgnoreCase))
            ?? _taskAreas[0];
    }

    private static bool TryGetTaskPanelShortcutToken(Key key, out string token)
    {
        token = string.Empty;
        if (key == Key.OemPlus || key == Key.Add)
        {
            token = "+";
            return true;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            token = key.ToString().ToUpperInvariant();
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            token = ((char)('0' + (key - Key.D0))).ToString();
            return true;
        }

        return false;
    }

    private static TaskGroupState? FindGroupByShortcut(TaskAreaState area, string shortcutToken)
        => area.Groups
            .FirstOrDefault(group => string.Equals(
                NormalizeTaskGroupShortcutKey(group.ShortcutKey),
                shortcutToken,
                StringComparison.OrdinalIgnoreCase));

    private static TaskGroupState? GetDefaultAddGroup(TaskAreaState area)
        => FindGroupByShortcut(area, "+")
           ?? area.Groups.OrderBy(g => g.SortOrder).FirstOrDefault();

    private void UpdateTodoPanelTitleText()
    {
        if (TodoPanelTitleText != null)
            TodoPanelTitleText.Text = string.IsNullOrWhiteSpace(_taskPanelTitle) ? DefaultTaskPanelTitle : _taskPanelTitle;
    }

    private void RefreshTodoAreaSelector()
    {
        if (TodoAreaComboBox == null)
            return;

        _suppressTaskAreaSelectionChanged = true;
        try
        {
            TodoAreaComboBox.Items.Clear();
            if (_taskAreas != null)
            {
                foreach (var area in _taskAreas)
                {
                    var item = new ComboBoxItem
                    {
                        Content = area.Name,
                        Tag = area.Id
                    };
                    TodoAreaComboBox.Items.Add(item);
                    if (string.Equals(area.Id, _currentTaskAreaId, StringComparison.OrdinalIgnoreCase))
                        TodoAreaComboBox.SelectedItem = item;
                }
            }

            if (TodoAreaComboBox.SelectedItem == null && TodoAreaComboBox.Items.Count > 0)
                TodoAreaComboBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressTaskAreaSelectionChanged = false;
        }
    }

    private void TodoAreaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskAreaSelectionChanged)
            return;
        if (TodoAreaComboBox?.SelectedItem is not ComboBoxItem selected || selected.Tag is not string areaId)
            return;
        if (string.Equals(_currentTaskAreaId, areaId, StringComparison.OrdinalIgnoreCase))
            return;

        _currentTaskAreaId = areaId;
        RenderTodoLists();
        SaveWindowSettings();
    }

    private void ExecuteToggleTodoPanel()
    {
        _todoPanelVisible = !_todoPanelVisible;
        UpdateTodoPanelVisibility();
        SaveWindowSettings();
    }

    private void UpdateTodoPanelVisibility()
    {
        if (TodoPanelColumn == null || TodoPanelBorder == null)
            return;

        TodoPanelColumn.Width = _todoPanelVisible ? new GridLength(TodoPanelOpenWidth) : new GridLength(0);
        TodoPanelBorder.Visibility = _todoPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_todoPanelVisible)
        {
            TodoPanelBorder.Focus();
            Keyboard.Focus(TodoPanelBorder);
        }
        else
        {
            var currentDoc = CurrentDoc();
            if (currentDoc != null)
            {
                currentDoc.Editor.Focus();
                Keyboard.Focus(currentDoc.Editor);
            }
        }
    }

    private void PruneCompletedTodoItems()
    {
        var today = DateTime.Today;
        var currentWeekStart = StartOfWeekMonday(today);
        var currentMonthStart = new DateTime(today.Year, today.Month, 1);

        _todoItems.RemoveAll(item =>
        {
            if (!item.CompletedAtUtc.HasValue)
                return false;

            var completedDate = item.CompletedAtUtc.Value.ToLocalTime().Date;
            var groupId = (item.GroupId ?? string.Empty).ToLowerInvariant();

            return groupId switch
            {
                "today" => completedDate < today,
                "this-week" => StartOfWeekMonday(completedDate) < currentWeekStart,
                "this-month" => new DateTime(completedDate.Year, completedDate.Month, 1) < currentMonthStart,
                _ => false
            };
        });
        NormalizeTodoSortOrders();
    }

    private void RenderTodoLists()
    {
        if (TodoGroupsContainer == null || TodoCompletedToggleButton == null)
            return;

        PruneCompletedTodoItems();
        TodoGroupsContainer.Children.Clear();

        var area = GetCurrentTaskArea();
        if (area == null || area.Groups.Count == 0)
        {
            TodoGroupsContainer.Children.Add(BuildEmptyHint("No groups in this area. Configure them in Tools → Settings → Task Panel."));
        }
        else
        {
            foreach (var group in area.Groups.OrderBy(g => g.SortOrder))
                TodoGroupsContainer.Children.Add(BuildGroupSection(area, group));
        }

        var completedCount = _todoItems.Count(item => item.CompletedAtUtc != null);
        TodoCompletedToggleButton.ToolTip = $"Recently Completed ({completedCount})";
        TodoCompletedToggleButton.Content = "\u2713";
    }

    private UIElement BuildGroupSection(TaskAreaState area, TaskGroupState group)
    {
        var groupTag = new TodoGroupPanelTag { AreaId = area.Id, GroupId = group.Id };
        var wrapper = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            AllowDrop = true,
            Tag = groupTag
        };
        wrapper.DragOver += TodoGroupPanel_DragOver;
        wrapper.Drop += TodoGroupPanel_Drop;

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerRow.AllowDrop = true;
        headerRow.Tag = groupTag;
        headerRow.DragOver += TodoGroupPanel_DragOver;
        headerRow.Drop += TodoGroupPanel_Drop;
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerText, 0);
        headerRow.Children.Add(headerText);

        var addButton = new Button
        {
            Content = "+",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            ToolTip = $"Add task to {group.Name}",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.Gray
        };
        addButton.Click += (_, _) => ShowAddTodoDialog(area.Id, group.Id);
        Grid.SetColumn(addButton, 1);
        headerRow.Children.Add(addButton);
        wrapper.Children.Add(headerRow);

        var itemsPanel = new StackPanel
        {
            AllowDrop = true,
            Margin = new Thickness(0, 0, 0, 0),
            MinHeight = 18
        };
        itemsPanel.Tag = groupTag;
        itemsPanel.DragOver += TodoGroupPanel_DragOver;
        itemsPanel.Drop += TodoGroupPanel_Drop;

        var activeItems = _todoItems
            .Where(item => !item.CompletedAtUtc.HasValue
                && string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();
        var completedItems = _todoItems
            .Where(item => item.CompletedAtUtc.HasValue
                && string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();

        AddTodoSectionRows(itemsPanel, activeItems.Concat(completedItems), area.Id, group.Id);

        if (activeItems.Count == 0 && completedItems.Count == 0)
            itemsPanel.Children.Add(BuildEmptyHint($"No tasks for {group.Name.ToLower(CultureInfo.CurrentCulture)}."));

        wrapper.Children.Add(itemsPanel);
        return wrapper;
    }

    private sealed class TodoGroupPanelTag
    {
        public string AreaId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
    }

    private static TextBlock BuildEmptyHint(string text)
        => new()
        {
            Text = text,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6),
            FontStyle = FontStyles.Italic
        };

    private void AddTodoSectionRows(Panel panel, IEnumerable<TodoItemState> items, string areaId, string groupId)
    {
        foreach (var item in items)
        {
            bool isCompleted = item.CompletedAtUtc.HasValue;
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6),
                Tag = new TodoRowTag { ItemId = item.Id, AreaId = areaId, GroupId = groupId },
                AllowDrop = true
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source
                    && FindAncestor<Button>(source) != null)
                {
                    return;
                }

                _todoDragSourceItemId = item.Id;
                _todoDragStartPoint = e.GetPosition(row);
            };

            row.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed
                    || string.IsNullOrWhiteSpace(_todoDragSourceItemId)
                    || !string.Equals(_todoDragSourceItemId, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var currentPos = e.GetPosition(row);
                if (Math.Abs(currentPos.X - _todoDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(currentPos.Y - _todoDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                var data = new DataObject();
                data.SetData(TodoDragDataFormat, item.Id);
                DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
                _todoDragSourceItemId = null;
                e.Handled = true;
            };

            row.MouseLeftButtonUp += (_, _) => _todoDragSourceItemId = null;

            row.DragOver += (_, e) =>
            {
                if (!TryGetDraggedTodoItem(e.Data, out TodoItemState _))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            };

            row.Drop += (_, e) =>
            {
                if (row.Tag is not TodoRowTag tag)
                    return;
                if (!TryGetDraggedTodoItem(e.Data, out var draggedItem))
                    return;

                var dropPos = e.GetPosition(row);
                bool insertBefore = dropPos.Y <= (row.ActualHeight / 2.0);
                MoveTodoItem(draggedItem.Id, tag.AreaId, tag.GroupId, tag.ItemId, insertBefore);
                e.Handled = true;
            };

            var rowContextMenu = new ContextMenu();
            var renameItem = new MenuItem { Header = "Rename..." };
            renameItem.Click += (_, _) => RenameTodoItem(item);
            rowContextMenu.Items.Add(renameItem);
            row.ContextMenu = rowContextMenu;

            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = isCompleted,
                Opacity = isCompleted ? 0.75 : 1.0
            };
            checkBox.Content = new TextBlock
            {
                Text = item.Text,
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
            };
            checkBox.Checked += (_, _) =>
            {
                if (item.CompletedAtUtc.HasValue)
                    return;
                item.CompletedAtUtc = DateTime.UtcNow;
                RenderTodoLists();
                SaveWindowSettings();
                TodoPanelBorder?.Focus();
                Keyboard.Focus(TodoPanelBorder);
            };
            checkBox.Unchecked += (_, _) =>
            {
                if (!item.CompletedAtUtc.HasValue)
                    return;
                item.CompletedAtUtc = null;
                RenderTodoLists();
                SaveWindowSettings();
                TodoPanelBorder?.Focus();
                Keyboard.Focus(TodoPanelBorder);
            };
            Grid.SetColumn(checkBox, 0);
            row.Children.Add(checkBox);

            var removeButton = new Button
            {
                Content = "\u00D7",
                Width = 20,
                Height = 20,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Remove task",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.Gray,
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Opacity = 0.55,
                Padding = new Thickness(0)
            };
            removeButton.MouseEnter += (_, _) =>
            {
                removeButton.Opacity = 1.0;
                removeButton.Foreground = Brushes.DimGray;
            };
            removeButton.MouseLeave += (_, _) =>
            {
                removeButton.Opacity = 0.55;
                removeButton.Foreground = Brushes.Gray;
            };
            removeButton.Click += (_, _) =>
            {
                if (item.CompletedAtUtc.HasValue)
                {
                    // Keep completed tasks in Recently Completed, but hide them from the panel.
                    item.AreaId = TodoClosedAreaId;
                    item.GroupId = TodoClosedGroupId;
                    RenderTodoLists();
                    SaveWindowSettings();
                    TodoPanelBorder?.Focus();
                    Keyboard.Focus(TodoPanelBorder);
                    return;
                }

                _todoItems.RemoveAll(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                RenderTodoLists();
                SaveWindowSettings();
                TodoPanelBorder?.Focus();
                Keyboard.Focus(TodoPanelBorder);
            };
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            panel.Children.Add(row);
        }
    }

    private sealed class TodoRowTag
    {
        public string ItemId { get; set; } = string.Empty;
        public string AreaId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
    }

    private UIElement BuildCompletedRow(TodoItemState item)
    {
        var completedAt = item.CompletedAtUtc?.ToLocalTime() ?? DateTime.Now;

        var grid = new Grid { Margin = new Thickness(4, 4, 4, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var checkIcon = new TextBlock
        {
            Text = "\u2713",
            Foreground = Brushes.MediumSeaGreen,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0)
        };
        Grid.SetColumn(checkIcon, 0);
        grid.Children.Add(checkIcon);

        var textContainer = new StackPanel();
        Grid.SetColumn(textContainer, 1);

        var nameText = new TextBlock
        {
            Text = item.Text,
            FontSize = 14,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        textContainer.Children.Add(nameText);

        var detailsText = new TextBlock
        {
            Text = $"Completed at {completedAt.ToString("g", CultureInfo.CurrentCulture)} \u2022 {GetTodoItemLocationLabel(item)}",
            Foreground = Brushes.Gray,
            FontSize = 12
        };
        textContainer.Children.Add(detailsText);

        grid.Children.Add(textContainer);

        var border = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid
        };

        return border;
    }

    private string GetTodoItemLocationLabel(TodoItemState item)
    {
        var area = _taskAreas.FirstOrDefault(a => string.Equals(a.Id, item.AreaId, StringComparison.OrdinalIgnoreCase));
        var group = area?.Groups.FirstOrDefault(g => string.Equals(g.Id, item.GroupId, StringComparison.OrdinalIgnoreCase));
        if (area == null || group == null)
            return item.Bucket == TodoBucket.ThisWeek ? "This Week" : "Today";
        return $"{area.Name} \u203A {group.Name}";
    }

    private void TodoGroupPanel_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Panel { Tag: TodoGroupPanelTag } || !TryGetDraggedTodoItem(e.Data, out TodoItemState _))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TodoGroupPanel_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Panel panel || panel.Tag is not TodoGroupPanelTag tag)
            return;
        if (!TryGetDraggedTodoItem(e.Data, out var draggedItem))
            return;

        MoveTodoItem(draggedItem.Id, tag.AreaId, tag.GroupId, targetItemId: null, insertBeforeTarget: false);
        e.Handled = true;
    }

    private void NormalizeTodoSortOrders()
    {
        foreach (var group in _todoItems.GroupBy(item => new { Area = item.AreaId ?? string.Empty, Group = item.GroupId ?? string.Empty }))
        {
            int order = 1;
            foreach (var item in group.OrderBy(item => item.SortOrder).ThenBy(item => item.CreatedUtc))
                item.SortOrder = order++;
        }
    }

    private int NextTodoSortOrder(string areaId, string groupId)
    {
        var max = _todoItems
            .Where(item => string.Equals(item.AreaId, areaId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
            .Select(item => (int?)item.SortOrder)
            .Max() ?? 0;
        return max + 1;
    }

    private bool TryGetDraggedTodoItem(IDataObject dragData, out TodoItemState item)
    {
        item = null!;
        if (!dragData.GetDataPresent(TodoDragDataFormat))
            return false;

        var id = dragData.GetData(TodoDragDataFormat) as string;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var existing = _todoItems.FirstOrDefault(todo => string.Equals(todo.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return false;

        item = existing;
        return true;
    }

    private void MoveTodoItem(string draggedItemId, string targetAreaId, string targetGroupId, string? targetItemId, bool insertBeforeTarget)
    {
        var draggedItem = _todoItems.FirstOrDefault(item => string.Equals(item.Id, draggedItemId, StringComparison.OrdinalIgnoreCase));
        if (draggedItem == null)
            return;

        draggedItem.AreaId = targetAreaId;
        draggedItem.GroupId = targetGroupId;

        var targetGroupItems = _todoItems
            .Where(item => string.Equals(item.AreaId, targetAreaId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, targetGroupId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Id, draggedItem.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();

        int targetIndex;
        if (string.IsNullOrWhiteSpace(targetItemId))
        {
            targetIndex = targetGroupItems.Count;
        }
        else
        {
            var locatedTargetIndex = targetGroupItems.FindIndex(item => string.Equals(item.Id, targetItemId, StringComparison.OrdinalIgnoreCase));
            if (locatedTargetIndex < 0)
                targetIndex = targetGroupItems.Count;
            else
                targetIndex = insertBeforeTarget ? locatedTargetIndex : locatedTargetIndex + 1;
        }

        targetIndex = Math.Max(0, Math.Min(targetIndex, targetGroupItems.Count));
        targetGroupItems.Insert(targetIndex, draggedItem);

        for (int i = 0; i < targetGroupItems.Count; i++)
            targetGroupItems[i].SortOrder = i + 1;

        NormalizeTodoSortOrders();
        RenderTodoLists();
        SaveWindowSettings();
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T typed)
                return typed;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void TodoAddTodayButton_Click(object sender, RoutedEventArgs e)
    {
        var area = GetCurrentTaskArea();
        if (area == null || area.Groups.Count == 0)
            return;
        var targetGroup = GetDefaultAddGroup(area);
        if (targetGroup == null)
            return;
        ShowAddTodoDialog(area.Id, targetGroup.Id);
    }

    private void TodoCompletedToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRecentlyCompletedDialog();
    }

    private void TodoPanelBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TodoPanelBorder == null)
            return;

        TodoPanelBorder.Focus();
        Keyboard.Focus(TodoPanelBorder);
    }

    private void TodoPanelBorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleMessageOverlayKey(e))
            return;

        if (!_todoPanelVisible)
            return;

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryGetTaskPanelShortcutToken(key, out var shortcutToken))
        {
            var area = GetCurrentTaskArea();
            var shortcutGroup = area == null ? null : FindGroupByShortcut(area, shortcutToken);
            if (shortcutGroup != null)
            {
                e.Handled = true;
                ShowAddTodoDialog(area!.Id, shortcutGroup.Id);
                return;
            }
        }

        if (key == Key.C)
        {
            e.Handled = true;
            ShowRecentlyCompletedDialog();
        }
    }

    private void ShowRecentlyCompletedDialog()
    {
        PruneCompletedTodoItems();
        var completed = _todoItems
            .Where(item => item.CompletedAtUtc != null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .ToList();

        var dialog = new Window
        {
            Title = "Recently Completed",
            Width = 520,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var closeButton = new Button
        {
            Content = "Close",
            IsCancel = true,
            IsDefault = true,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);

        var content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var listPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        content.Content = listPanel;

        if (completed.Count == 0)
        {
            listPanel.Children.Add(BuildEmptyHint("No recently completed tasks."));
        }
        else
        {
            foreach (var item in completed)
                listPanel.Children.Add(BuildCompletedRow(item));
        }

        root.Children.Add(content);
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void TodoInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TodoAddTodayButton_Click(sender, new RoutedEventArgs());
        }
    }

    private void MenuTodoList_Click(object sender, RoutedEventArgs e)
        => ExecuteToggleTodoPanel();

    private void ShowAddTodoDialog(string areaId, string groupId)
    {
        var area = _taskAreas.FirstOrDefault(a => string.Equals(a.Id, areaId, StringComparison.OrdinalIgnoreCase));
        var group = area?.Groups.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
        var groupLabel = group?.Name ?? groupId;

        var dialog = new Window
        {
            Title = $"Add task to {groupLabel}",
            Width = 440,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var addButton = new Button { Content = "Add", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        buttons.Children.Add(addButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        var textBox = new TextBox
        {
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        root.Children.Add(textBox);

        addButton.Click += (_, _) =>
        {
            var text = (textBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return;

            _todoItems.Add(new TodoItemState
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                AreaId = areaId,
                GroupId = groupId,
                Bucket = string.Equals(groupId, "this-week", StringComparison.OrdinalIgnoreCase) ? TodoBucket.ThisWeek : TodoBucket.Today,
                SortOrder = NextTodoSortOrder(areaId, groupId),
                CreatedUtc = DateTime.UtcNow
            });

            dialog.DialogResult = true;
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
        };

        var wasAdded = dialog.ShowDialog() == true;
        if (wasAdded)
        {
            RenderTodoLists();
            SaveWindowSettings();
        }

        // Keep focus behavior identical to clicking inside the task panel.
        TodoPanelBorder?.Focus();
        Keyboard.Focus(TodoPanelBorder);
    }

    private void RenameTodoItem(TodoItemState item)
    {
        var renamedText = ShowRenameTodoDialog(item.Text);
        if (string.IsNullOrWhiteSpace(renamedText))
            return;

        item.Text = renamedText.Trim();
        RenderTodoLists();
        SaveWindowSettings();
        TodoPanelBorder?.Focus();
        Keyboard.Focus(TodoPanelBorder);
    }

    private string? ShowRenameTodoDialog(string currentText)
    {
        var dialog = new Window
        {
            Title = "Rename Todo Item",
            Width = 440,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var renameButton = new Button { Content = "Rename", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        buttons.Children.Add(renameButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        var textBox = new TextBox
        {
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = currentText
        };
        root.Children.Add(textBox);

        string? updatedText = null;
        renameButton.Click += (_, _) =>
        {
            var text = (textBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("Todo text cannot be empty.", "Rename Todo Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            updatedText = text;
            dialog.DialogResult = true;
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? updatedText : null;
    }
}

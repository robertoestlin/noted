using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            CompletedAtUtc = item.CompletedAtUtc,
            Status = item.Status,
            IsImportant = item.IsImportant,
            Log = item.Log == null ? new List<TodoLogEntry>() : new List<TodoLogEntry>(item.Log)
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

                var migratedStatus = rawItem.Status;
                if (rawItem.CompletedAtUtc.HasValue && migratedStatus == TodoStatus.Queued)
                {
                    // Legacy item: had CompletedAtUtc but no Status field in JSON (defaulted to Queued).
                    migratedStatus = TodoStatus.Done;
                }

                _todoItems.Add(new TodoItemState
                {
                    Id = string.IsNullOrWhiteSpace(rawItem.Id) ? Guid.NewGuid().ToString("N") : rawItem.Id,
                    Text = text,
                    Bucket = rawItem.Bucket,
                    AreaId = rawItem.AreaId,
                    GroupId = rawItem.GroupId,
                    SortOrder = rawItem.SortOrder,
                    CreatedUtc = rawItem.CreatedUtc == default ? DateTime.UtcNow : rawItem.CreatedUtc,
                    CompletedAtUtc = rawItem.CompletedAtUtc,
                    Status = migratedStatus,
                    IsImportant = rawItem.IsImportant,
                    Log = rawItem.Log ?? new List<TodoLogEntry>()
                });
            }
        }

        MigrateLegacyTodoItemsToGroups();

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

    private static bool IsVisibleInPanel(TodoItemState item) =>
        item.Status != TodoStatus.Cancelled;

    private static void SetTodoItemStatus(TodoItemState item, TodoStatus newStatus)
    {
        if (item.Status == newStatus)
            return;
        var oldStatus = item.Status;
        item.Log ??= new List<TodoLogEntry>();
        var now = DateTime.UtcNow;

        // Leaving "not started": record an implicit move to in progress first, then the chosen status.
        if (oldStatus == TodoStatus.Queued && newStatus != TodoStatus.Active)
        {
            item.Log.Add(new TodoLogEntry
            {
                TimestampUtc = now,
                Kind = TodoLogEntryKind.StatusChange,
                FromStatus = TodoStatus.Queued,
                ToStatus = TodoStatus.Active
            });
            item.Log.Add(new TodoLogEntry
            {
                TimestampUtc = now,
                Kind = TodoLogEntryKind.StatusChange,
                FromStatus = TodoStatus.Active,
                ToStatus = newStatus
            });
        }
        else
        {
            item.Log.Add(new TodoLogEntry
            {
                TimestampUtc = now,
                Kind = TodoLogEntryKind.StatusChange,
                FromStatus = oldStatus,
                ToStatus = newStatus
            });
        }

        item.Status = newStatus;
        item.CompletedAtUtc = newStatus == TodoStatus.Done ? DateTime.UtcNow : null;
    }

    private static TodoStatus DerivePreviousStatusBeforeDone(TodoItemState item)
    {
        if (item.Log != null)
        {
            for (int i = item.Log.Count - 1; i >= 0; i--)
            {
                var entry = item.Log[i];
                if (entry.Kind == TodoLogEntryKind.StatusChange && entry.ToStatus == TodoStatus.Done)
                    return entry.FromStatus ?? TodoStatus.Queued;
            }
        }
        return TodoStatus.Queued;
    }

    private static Brush GetStatusBrush(TodoStatus status) => status switch
    {
        TodoStatus.Queued => new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81)),
        TodoStatus.Active => new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        TodoStatus.Waiting => new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C)),
        TodoStatus.Blocked => new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E)),
        TodoStatus.Done => new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37)),
        _ => new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81))
    };

    private static Brush GetStatusBackgroundBrush(TodoStatus status) => status switch
    {
        TodoStatus.Queued => new SolidColorBrush(Color.FromRgb(0xEA, 0xEE, 0xF2)),
        TodoStatus.Active => new SolidColorBrush(Color.FromRgb(0xDD, 0xF4, 0xFF)),
        TodoStatus.Waiting => new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xD5)),
        TodoStatus.Blocked => new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0xE6)),
        TodoStatus.Done => new SolidColorBrush(Color.FromRgb(0xDA, 0xFB, 0xE1)),
        _ => new SolidColorBrush(Color.FromRgb(0xEA, 0xEE, 0xF2))
    };

    /// <summary>User-visible task status label (panel badges, tooltips, menus).</summary>
    private static string GetTodoStatusDisplayName(TodoStatus status) => status switch
    {
        TodoStatus.Queued => "Not started",
        TodoStatus.Active => "In progress",
        TodoStatus.Waiting => "Waiting",
        TodoStatus.Blocked => "Blocked",
        TodoStatus.Done => "Done",
        TodoStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    private static string FormatLogEntryForDisplay(TodoLogEntry entry)
    {
        var stamp = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        if (entry.Kind == TodoLogEntryKind.StatusChange)
        {
            var from = entry.FromStatus is { } fs ? GetTodoStatusDisplayName(fs) : "?";
            var to = entry.ToStatus is { } ts ? GetTodoStatusDisplayName(ts) : "?";
            return $"{stamp}  {from} → {to}";
        }
        return $"{stamp}  {entry.Text ?? string.Empty}";
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
        // The task panel is part of Short-Term Notes only; ignore the F3 / menu trigger in other modes.
        if (!IsTabModeActive())
            return;
        _todoPanelVisible = !_todoPanelVisible;
        UpdateTodoPanelVisibility();
        SaveWindowSettings();
    }

    private void UpdateTodoPanelVisibility()
    {
        if (TodoPanelColumn == null || TodoPanelBorder == null)
            return;

        bool show = _todoPanelVisible && IsTabModeActive();

        TodoPanelColumn.Width = show ? new GridLength(TodoPanelOpenWidth) : new GridLength(0);
        TodoPanelBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            TodoPanelBorder.Focus();
            Keyboard.Focus(TodoPanelBorder);
        }
        else if (IsTabModeActive())
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
        foreach (var item in _todoItems)
        {
            if (!item.CompletedAtUtc.HasValue)
                continue;

            // Items already archived into the closed bucket are untouched so
            // they remain visible in Recently Completed.
            if (string.Equals(item.AreaId, TodoClosedAreaId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, TodoClosedGroupId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owningArea = _taskAreas.FirstOrDefault(area => string.Equals(area.Id, item.AreaId, StringComparison.OrdinalIgnoreCase));
            var owningGroup = owningArea?.Groups.FirstOrDefault(group => string.Equals(group.Id, item.GroupId, StringComparison.OrdinalIgnoreCase));
            if (owningGroup == null)
                continue;

            var retentionDays = NormalizeCompletedRetentionDays(owningGroup.CompletedRetentionDays, owningGroup.Id);
            var retentionHours = NormalizeCompletedRetentionHours(owningGroup.CompletedRetentionHours);
            if (retentionDays == 0 && retentionHours == 0)
                continue;

            var retention = TimeSpan.FromDays(retentionDays) + TimeSpan.FromHours(retentionHours);
            var elapsed = DateTime.UtcNow - item.CompletedAtUtc.Value;
            if (elapsed >= retention)
            {
                // Hide from the task panel by moving into the closed bucket.
                // Completed-at timestamp is preserved so the entry stays in
                // Recently Completed.
                item.AreaId = TodoClosedAreaId;
                item.GroupId = TodoClosedGroupId;
            }
        }

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
            AllowDrop = true,
            Tag = groupTag
        };
        wrapper.DragOver += TodoGroupPanel_DragOver;
        wrapper.Drop += TodoGroupPanel_Drop;

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A)),
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
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            ToolTip = $"Add task to {group.Name}",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81)),
            Cursor = Cursors.Hand
        };
        addButton.MouseEnter += (_, _) => addButton.Foreground = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA));
        addButton.MouseLeave += (_, _) => addButton.Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81));
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
            .Where(item => IsVisibleInPanel(item)
                && !item.CompletedAtUtc.HasValue
                && string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();
        var completedItems = _todoItems
            .Where(item => IsVisibleInPanel(item)
                && item.CompletedAtUtc.HasValue
                && string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();

        AddTodoSectionRows(itemsPanel, activeItems.Concat(completedItems), area.Id, group.Id);

        if (activeItems.Count == 0 && completedItems.Count == 0)
            itemsPanel.Children.Add(BuildEmptyHint($"No tasks for {group.Name.ToLower(CultureInfo.CurrentCulture)}."));

        wrapper.Children.Add(itemsPanel);

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = wrapper
        };
        return card;
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA1)),
            Margin = new Thickness(0, 0, 0, 4),
            FontSize = 12,
            FontStyle = FontStyles.Italic
        };

    private static Border BuildPillBadge(
        string text,
        Brush foreground,
        Brush background,
        Brush? borderBrush = null,
        FontWeight? fontWeight = null,
        bool compact = false)
    {
        var weight = fontWeight ?? FontWeights.SemiBold;
        var radius = compact ? 8 : 10;
        var pad = compact ? new Thickness(6, 2, 6, 2) : new Thickness(8, 3, 8, 3);
        var margin = compact ? new Thickness(6, 0, 0, 0) : new Thickness(8, 0, 0, 0);
        var fontSize = compact ? 10.0 : 11.0;
        return new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = borderBrush != null ? new Thickness(1) : new Thickness(0),
            CornerRadius = new CornerRadius(radius),
            Padding = pad,
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            }
        };
    }

    private void AddTodoSectionRows(Panel panel, IEnumerable<TodoItemState> items, string areaId, string groupId)
    {
        var owningArea = _taskAreas.FirstOrDefault(area => string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase));
        var owningGroup = owningArea?.Groups.FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase));
        TimeSpan? overdueThreshold = null;
        if (owningGroup?.UndoneMarkEnabled == true)
        {
            overdueThreshold = TimeSpan.FromDays(NormalizeUndoneMarkDays(owningGroup.UndoneMarkDays))
                + TimeSpan.FromHours(NormalizeUndoneMarkHours(owningGroup.UndoneMarkHours));
        }

        foreach (var item in items)
        {
            bool isCompleted = item.CompletedAtUtc.HasValue;
            bool isOverdue = !isCompleted
                && overdueThreshold.HasValue
                && item.CreatedUtc != default
                && (DateTime.UtcNow - item.CreatedUtc) >= overdueThreshold.Value;
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

            var setStatusItem = new MenuItem { Header = "Set status" };
            foreach (TodoStatus statusOption in Enum.GetValues<TodoStatus>())
            {
                if (statusOption == item.Status)
                    continue;
                if (statusOption == TodoStatus.Done)
                    continue; // canonical path is the checkbox
                var captured = statusOption;
                var statusMenuItem = new MenuItem { Header = GetTodoStatusDisplayName(captured) };
                statusMenuItem.Click += (_, _) =>
                {
                    SetTodoItemStatus(item, captured);
                    RenderTodoLists();
                    SaveWindowSettings();
                    TodoPanelBorder?.Focus();
                    Keyboard.Focus(TodoPanelBorder);
                };
                setStatusItem.Items.Add(statusMenuItem);
            }
            rowContextMenu.Items.Add(setStatusItem);

            var importantItem = new MenuItem
            {
                Header = item.IsImportant ? "Remove Prio" : "Mark as Prio"
            };
            importantItem.Click += (_, _) =>
            {
                item.IsImportant = !item.IsImportant;
                item.Log ??= new List<TodoLogEntry>();
                item.Log.Add(new TodoLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Kind = TodoLogEntryKind.Note,
                    Text = item.IsImportant ? "Marked as Prio" : "Removed Prio"
                });
                RenderTodoLists();
                SaveWindowSettings();
                TodoPanelBorder?.Focus();
                Keyboard.Focus(TodoPanelBorder);
            };
            rowContextMenu.Items.Add(importantItem);

            var updateTaskItem = new MenuItem { Header = "Update task..." };
            updateTaskItem.Click += (_, _) => ShowTaskLogDialog(item, focusInput: true);
            rowContextMenu.Items.Add(updateTaskItem);

            var showLogItem = new MenuItem { Header = "Show log..." };
            showLogItem.Click += (_, _) => ShowTaskLogDialog(item, focusInput: false);
            rowContextMenu.Items.Add(showLogItem);

            if (isOverdue)
            {
                var resetCreatedTimeItem = new MenuItem
                {
                    Header = "Reset created time",
                    ToolTip = "Sets created time to now so the overdue highlight clears."
                };
                resetCreatedTimeItem.Click += (_, _) =>
                {
                    item.CreatedUtc = DateTime.UtcNow;
                    RenderTodoLists();
                    SaveWindowSettings();
                    TodoPanelBorder?.Focus();
                    Keyboard.Focus(TodoPanelBorder);
                };
                rowContextMenu.Items.Add(resetCreatedTimeItem);
            }
            row.ContextMenu = rowContextMenu;

            var createdLocalText = item.CreatedUtc != default
                ? item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                : "Unknown";
            var tipLines = new List<string>(capacity: 4)
            {
                item.Text ?? string.Empty,
                $"Created: {createdLocalText}",
                $"Status: {GetTodoStatusDisplayName(item.Status)}"
            };
            if (isOverdue)
                tipLines.Add("Overdue: not completed within the configured time for this group.");
            if (item.Log != null && item.Log.Count > 0)
            {
                const int maxTooltipLogEntries = 10;
                int total = item.Log.Count;
                int skipped = Math.Max(0, total - maxTooltipLogEntries);
                tipLines.Add(string.Empty);
                tipLines.Add(skipped > 0 ? $"Log (last {maxTooltipLogEntries} of {total}):" : "Log:");
                for (int i = skipped; i < total; i++)
                {
                    tipLines.Add(FormatLogEntryForDisplay(item.Log[i]));
                }
            }
            row.ToolTip = new ToolTip
            {
                Content = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, tipLines),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                }
            };
            ToolTipService.SetInitialShowDelay(row, 0);
            ToolTipService.SetBetweenShowDelay(row, 0);

            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = isCompleted,
                Opacity = isCompleted ? 0.6 : 1.0
            };
            var textBlock = new TextBlock
            {
                Text = item.Text,
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isCompleted
                    ? new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA1))
                    : new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
            };
            if (isOverdue)
            {
                textBlock.FontWeight = FontWeights.Bold;
            }
            bool showStatusBadge = item.Status != TodoStatus.Queued;
            if (!showStatusBadge && !item.IsImportant)
            {
                checkBox.Content = textBlock;
            }
            else
            {
                var contentPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                contentPanel.Children.Add(textBlock);
                if (showStatusBadge)
                {
                    var statusFg = GetStatusBrush(item.Status);
                    var statusBg = GetStatusBackgroundBrush(item.Status);
                    Brush? statusBorder = item.Status == TodoStatus.Waiting
                        ? new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C))
                        : null;
                    contentPanel.Children.Add(BuildPillBadge(
                        GetTodoStatusDisplayName(item.Status),
                        statusFg,
                        statusBg,
                        statusBorder,
                        compact: item.Status != TodoStatus.Done));
                }
                if (item.IsImportant)
                {
                    contentPanel.Children.Add(BuildPillBadge(
                        "Prio",
                        new SolidColorBrush(Color.FromRgb(0x9D, 0x17, 0x4D)),
                        new SolidColorBrush(Color.FromRgb(0xFC, 0xE7, 0xF3)),
                        new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0xD4)),
                        FontWeights.Bold,
                        compact: true));
                }
                checkBox.Content = contentPanel;
            }
            checkBox.Checked += (_, _) =>
            {
                if (item.Status == TodoStatus.Done)
                    return;
                SetTodoItemStatus(item, TodoStatus.Done);
                RenderTodoLists();
                SaveWindowSettings();
                TodoPanelBorder?.Focus();
                Keyboard.Focus(TodoPanelBorder);
            };
            checkBox.Unchecked += (_, _) =>
            {
                if (item.Status != TodoStatus.Done)
                    return;
                var previous = DerivePreviousStatusBeforeDone(item);
                SetTodoItemStatus(item, previous);
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
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA1)),
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Opacity = 0.6,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand
            };
            removeButton.MouseEnter += (_, _) =>
            {
                removeButton.Opacity = 1.0;
                removeButton.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E));
            };
            removeButton.MouseLeave += (_, _) =>
            {
                removeButton.Opacity = 0.6;
                removeButton.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA1));
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37)),
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        textContainer.Children.Add(nameText);

        var detailsText = new TextBlock
        {
            Text = $"Completed at {completedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} \u2022 {GetTodoItemLocationLabel(item)}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81)),
            FontSize = 12
        };
        textContainer.Children.Add(detailsText);

        grid.Children.Add(textContainer);

        var border = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),
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

        // Ctrl+E opens the export dialog before the generic Ctrl/Alt early-return.
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Alt) == 0
            && e.Key == Key.E)
        {
            e.Handled = true;
            ShowExportTasksDialog();
            return;
        }

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

    private void ShowTaskLogDialog(TodoItemState item, bool focusInput)
    {
        item.Log ??= new List<TodoLogEntry>();

        var dialog = new Window
        {
            Title = $"Log: {item.Text}",
            Width = 560,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var headerText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Text = item.Text
        };
        DockPanel.SetDock(headerText, Dock.Top);
        root.Children.Add(headerText);

        var createdText = item.CreatedUtc != default
            ? item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "Unknown";
        var statusText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Text = $"Status: {GetTodoStatusDisplayName(item.Status)} · Created: {createdText}"
        };
        DockPanel.SetDock(statusText, Dock.Top);
        root.Children.Add(statusText);

        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var actionRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actionRow, Dock.Bottom);

        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(rightButtons, Dock.Right);
        var addButton = new Button { Content = "Add entry", Width = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var closeButton = new Button { Content = "Close", Width = 90, IsCancel = true };
        rightButtons.Children.Add(addButton);
        rightButtons.Children.Add(closeButton);
        actionRow.Children.Add(rightButtons);

        var statusGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusGroup.Children.Add(new TextBlock
        {
            Text = "Set status to:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var statusCombo = new ComboBox
        {
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusCombo.ItemsSource = Enum.GetValues<TodoStatus>()
            .Cast<TodoStatus>()
            .Select(s => new { Status = s, Label = GetTodoStatusDisplayName(s) })
            .ToList();
        statusCombo.DisplayMemberPath = "Label";
        statusCombo.SelectedValuePath = "Status";
        statusCombo.SelectedValue = item.Status;
        statusGroup.Children.Add(statusCombo);
        actionRow.Children.Add(statusGroup);

        var entryBox = new TextBox
        {
            Height = 60,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        bottom.Children.Add(actionRow);
        bottom.Children.Add(entryBox);

        root.Children.Add(bottom);

        var listBox = new ListBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        root.Children.Add(listBox);

        void RefreshList()
        {
            listBox.Items.Clear();
            foreach (var entry in item.Log!)
                listBox.Items.Add(FormatLogEntryForDisplay(entry));
            if (listBox.Items.Count > 0)
                listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
        }
        RefreshList();

        addButton.Click += (_, _) =>
        {
            var text = (entryBox.Text ?? string.Empty).Trim();
            var selectedStatus = statusCombo.SelectedValue is TodoStatus picked ? picked : item.Status;
            bool willChangeStatus = selectedStatus != item.Status;
            if (text.Length == 0 && !willChangeStatus)
                return;

            // First note on a not-started task: move to In progress before applying note / combo status.
            var logHasNote = item.Log!.Any(e => e.Kind == TodoLogEntryKind.Note);
            if (text.Length > 0
                && !logHasNote
                && item.Status == TodoStatus.Queued)
            {
                SetTodoItemStatus(item, TodoStatus.Active);
                if (selectedStatus == TodoStatus.Queued)
                    statusCombo.SelectedValue = item.Status;
                statusText.Text = $"Status: {GetTodoStatusDisplayName(item.Status)} · Created: {createdText}";
            }

            selectedStatus = statusCombo.SelectedValue is TodoStatus p2 ? p2 : item.Status;
            willChangeStatus = selectedStatus != item.Status;

            if (text.Length > 0)
            {
                item.Log!.Add(new TodoLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Kind = TodoLogEntryKind.Note,
                    Text = text
                });
            }
            if (willChangeStatus)
            {
                SetTodoItemStatus(item, selectedStatus);
                statusText.Text = $"Status: {GetTodoStatusDisplayName(item.Status)} · Created: {createdText}";
                statusCombo.SelectedValue = item.Status;
            }

            entryBox.Text = string.Empty;
            RefreshList();
            RenderTodoLists();
            SaveWindowSettings();
            entryBox.Focus();
            Keyboard.Focus(entryBox);
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            if (focusInput)
            {
                entryBox.Focus();
                Keyboard.Focus(entryBox);
            }
            else
            {
                closeButton.Focus();
            }
        };

        dialog.ShowDialog();

        TodoPanelBorder?.Focus();
        Keyboard.Focus(TodoPanelBorder);
    }

    private void ShowExportTasksDialog()
    {
        var area = GetCurrentTaskArea();
        if (area == null || area.Groups.Count == 0)
            return;

        var dialog = new Window
        {
            Title = $"Export Tasks - {area.Name}",
            Width = 560,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new TextBlock
        {
            Text = area.Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var includeFinishedCheck = new CheckBox
        {
            Content = "Include finished items",
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var copyChecklistButton = new Button
        {
            Content = "Export as Teams Loop",
            Width = 170,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Copy as markdown checklist (pastes nicely into Teams)"
        };
        var copyRawButton = new Button
        {
            Content = "Export",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Copy plain text grouped by section"
        };
        var closeButton = new Button
        {
            Content = "Close",
            Width = 80,
            IsCancel = true
        };
        buttons.Children.Add(copyChecklistButton);
        buttons.Children.Add(copyRawButton);
        buttons.Children.Add(closeButton);
        root.Children.Add(buttons);

        var statusText = new TextBlock
        {
            Foreground = Brushes.MediumSeaGreen,
            Margin = new Thickness(0, 6, 0, 0),
            Text = string.Empty
        };
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        DockPanel.SetDock(includeFinishedCheck, Dock.Bottom);
        root.Children.Add(includeFinishedCheck);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var listPanel = new StackPanel();
        scroll.Content = listPanel;
        root.Children.Add(scroll);

        // Tracks whether each task id should be included in the copy output.
        // Absent = included (default); explicit false = user excluded it.
        var itemIncludes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        void RebuildList()
        {
            listPanel.Children.Clear();
            bool showFinished = includeFinishedCheck.IsChecked == true;

            bool renderedAnything = false;
            foreach (var group in area.Groups.OrderBy(g => g.SortOrder))
            {
                var groupItems = _todoItems
                    .Where(item => IsVisibleInPanel(item)
                        && string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                        && (showFinished || !item.CompletedAtUtc.HasValue))
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.CreatedUtc)
                    .ToList();

                if (groupItems.Count == 0)
                    continue;

                renderedAnything = true;
                var groupBlock = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                var groupCheck = new CheckBox
                {
                    IsThreeState = true,
                    Content = new TextBlock
                    {
                        Text = group.Name,
                        FontWeight = FontWeights.SemiBold
                    },
                    Margin = new Thickness(0, 0, 0, 4)
                };
                groupBlock.Children.Add(groupCheck);

                var childPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
                var itemCheckBoxes = new List<CheckBox>();
                bool suppressSync = false;

                void UpdateGroupState()
                {
                    if (suppressSync) return;
                    suppressSync = true;
                    bool all = itemCheckBoxes.Count > 0 && itemCheckBoxes.All(cb => cb.IsChecked == true);
                    bool any = itemCheckBoxes.Any(cb => cb.IsChecked == true);
                    groupCheck.IsChecked = all ? true : (any ? (bool?)null : false);
                    suppressSync = false;
                }

                foreach (var item in groupItems)
                {
                    bool included = !itemIncludes.TryGetValue(item.Id, out var value) || value;

                    var itemRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var itemCheck = new CheckBox
                    {
                        IsChecked = included,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var textBlock = new TextBlock
                    {
                        Text = item.Text,
                        TextWrapping = TextWrapping.Wrap
                    };
                    if (item.CompletedAtUtc.HasValue)
                    {
                        textBlock.TextDecorations = TextDecorations.Strikethrough;
                        textBlock.Opacity = 0.75;
                    }
                    itemCheck.Content = textBlock;

                    var capturedItem = item;
                    itemCheck.Checked += (_, _) =>
                    {
                        itemIncludes[capturedItem.Id] = true;
                        UpdateGroupState();
                    };
                    itemCheck.Unchecked += (_, _) =>
                    {
                        itemIncludes[capturedItem.Id] = false;
                        UpdateGroupState();
                    };
                    Grid.SetColumn(itemCheck, 0);
                    itemRow.Children.Add(itemCheck);

                    var removeButton = new Button
                    {
                        Content = "\u00D7",
                        Width = 20,
                        Height = 20,
                        Margin = new Thickness(8, 0, 0, 0),
                        ToolTip = "Exclude from export",
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = Brushes.Gray,
                        FontSize = 12,
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
                        itemIncludes[capturedItem.Id] = false;
                        itemRow.Visibility = Visibility.Collapsed;
                        itemCheck.IsChecked = false;
                    };
                    Grid.SetColumn(removeButton, 1);
                    itemRow.Children.Add(removeButton);

                    childPanel.Children.Add(itemRow);
                    itemCheckBoxes.Add(itemCheck);
                }

                groupCheck.Checked += (_, _) =>
                {
                    if (suppressSync) return;
                    suppressSync = true;
                    foreach (var cb in itemCheckBoxes)
                        cb.IsChecked = true;
                    suppressSync = false;
                };
                groupCheck.Unchecked += (_, _) =>
                {
                    if (suppressSync) return;
                    suppressSync = true;
                    foreach (var cb in itemCheckBoxes)
                        cb.IsChecked = false;
                    suppressSync = false;
                };

                groupBlock.Children.Add(childPanel);
                listPanel.Children.Add(groupBlock);

                UpdateGroupState();
            }

            if (!renderedAnything)
                listPanel.Children.Add(BuildEmptyHint("No tasks to export."));
        }

        includeFinishedCheck.Checked += (_, _) => RebuildList();
        includeFinishedCheck.Unchecked += (_, _) => RebuildList();

        RebuildList();

        bool ShouldIncludeItem(TodoItemState item, bool includeFinished)
        {
            if (!IsVisibleInPanel(item))
                return false;
            if (!string.Equals(item.AreaId, area.Id, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!includeFinished && item.CompletedAtUtc.HasValue)
                return false;
            if (itemIncludes.TryGetValue(item.Id, out var included) && !included)
                return false;
            return true;
        }

        void ShowStatus(string message)
        {
            statusText.Text = message;
        }

        copyChecklistButton.Click += (_, _) =>
        {
            var text = BuildChecklistExport(area, ShouldIncludeItem, includeFinishedCheck.IsChecked == true);
            if (string.IsNullOrEmpty(text))
            {
                ShowStatus("Nothing selected to copy.");
                return;
            }
            Clipboard.SetText(text);
            ShowStatus("Teams Loop checklist copied to clipboard.");
        };
        copyRawButton.Click += (_, _) =>
        {
            var text = BuildRawExport(area, ShouldIncludeItem, includeFinishedCheck.IsChecked == true);
            if (string.IsNullOrEmpty(text))
            {
                ShowStatus("Nothing selected to copy.");
                return;
            }
            Clipboard.SetText(text);
            ShowStatus("Copied to clipboard.");
        };
        closeButton.Click += (_, _) => dialog.Close();

        dialog.Content = root;
        dialog.ShowDialog();

        TodoPanelBorder?.Focus();
        Keyboard.Focus(TodoPanelBorder);
    }

    private string BuildChecklistExport(TaskAreaState area, Func<TodoItemState, bool, bool> shouldInclude, bool includeFinished)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(area.Name);

        foreach (var group in area.Groups.OrderBy(g => g.SortOrder))
        {
            var items = _todoItems
                .Where(item => string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                    && shouldInclude(item, includeFinished))
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedUtc)
                .ToList();

            if (items.Count == 0)
                continue;

            sb.AppendLine();
            sb.Append("## ").AppendLine(group.Name);
            foreach (var item in items)
            {
                var box = item.CompletedAtUtc.HasValue ? "- [x]" : "- [ ]";
                sb.Append(box).Append(' ').AppendLine(item.Text);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private string BuildRawExport(TaskAreaState area, Func<TodoItemState, bool, bool> shouldInclude, bool includeFinished)
    {
        var sb = new StringBuilder();
        sb.AppendLine(area.Name);

        foreach (var group in area.Groups.OrderBy(g => g.SortOrder))
        {
            var items = _todoItems
                .Where(item => string.Equals(item.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                    && shouldInclude(item, includeFinished))
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedUtc)
                .ToList();

            if (items.Count == 0)
                continue;

            sb.AppendLine();
            sb.Append(group.Name).AppendLine(":");
            foreach (var item in items)
            {
                var suffix = item.CompletedAtUtc.HasValue ? " (done)" : string.Empty;
                sb.Append("  - ").Append(item.Text).AppendLine(suffix);
            }
        }
        return sb.ToString().TrimEnd();
    }
}

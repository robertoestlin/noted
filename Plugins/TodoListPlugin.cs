using System.Globalization;
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
    private readonly List<TodoItemState> _todoItems = [];
    private bool _todoPanelVisible;
    private Point _todoDragStartPoint;
    private string? _todoDragSourceItemId;

    private void InitializeTodoPanel()
    {
        ConfigureTodoSectionDropTarget(TodoTodayItemsPanel, TodoBucket.Today);
        ConfigureTodoSectionDropTarget(TodoThisWeekItemsPanel, TodoBucket.ThisWeek);
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
                    SortOrder = rawItem.SortOrder,
                    CreatedUtc = rawItem.CreatedUtc == default ? DateTime.UtcNow : rawItem.CreatedUtc,
                    CompletedAtUtc = rawItem.CompletedAtUtc
                });
            }
        }

        // Always start with the todo panel collapsed on app launch.
        _todoPanelVisible = false;
        NormalizeTodoSortOrders();
        PruneCompletedTodoItems();
        if (TodoPanelColumn != null)
            UpdateTodoPanelVisibility();
        if (TodoTodayItemsPanel != null)
            RenderTodoLists();
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
    }

    private void PruneCompletedTodoItems()
    {
        var today = DateTime.Today;
        var currentWeekStart = StartOfWeekMonday(today);

        _todoItems.RemoveAll(item =>
        {
            if (!item.CompletedAtUtc.HasValue)
                return false;

            var completedDate = item.CompletedAtUtc.Value.ToLocalTime().Date;
            if (item.Bucket == TodoBucket.Today)
                return completedDate < today;

            var completedWeekStart = StartOfWeekMonday(completedDate);
            return completedWeekStart < currentWeekStart;
        });
        NormalizeTodoSortOrders();
    }

    private void RenderTodoLists()
    {
        if (TodoTodayItemsPanel == null || TodoThisWeekItemsPanel == null || TodoCompletedToggleButton == null)
            return;

        PruneCompletedTodoItems();
        TodoTodayItemsPanel.Children.Clear();
        TodoThisWeekItemsPanel.Children.Clear();

        var activeToday = _todoItems
            .Where(item => item.Bucket == TodoBucket.Today)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();
        var activeThisWeek = _todoItems
            .Where(item => item.Bucket == TodoBucket.ThisWeek)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();
        var completed = _todoItems
            .Where(item => item.CompletedAtUtc != null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .Take(100)
            .ToList();

        AddTodoSectionRows(TodoTodayItemsPanel, activeToday, TodoBucket.Today);
        AddTodoSectionRows(TodoThisWeekItemsPanel, activeThisWeek, TodoBucket.ThisWeek);

        if (activeToday.Count == 0)
            TodoTodayItemsPanel.Children.Add(BuildEmptyHint("No tasks for today."));
        if (activeThisWeek.Count == 0)
            TodoThisWeekItemsPanel.Children.Add(BuildEmptyHint("No tasks for this week."));

        TodoCompletedToggleButton.ToolTip = $"Recently Completed ({completed.Count})";
        TodoCompletedToggleButton.Content = "✓";
    }

    private static TextBlock BuildEmptyHint(string text)
        => new()
        {
            Text = text,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6),
            FontStyle = FontStyles.Italic
        };

    private void AddTodoSectionRows(Panel panel, IEnumerable<TodoItemState> items, TodoBucket bucket)
    {
        foreach (var item in items)
        {
            bool isCompleted = item.CompletedAtUtc.HasValue;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Tag = item.Id, AllowDrop = true };
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

            row.MouseMove += (_, e) =>
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
            };

            row.MouseLeftButtonUp += (_, _) => _todoDragSourceItemId = null;

            row.DragOver += (_, e) =>
            {
                if (!TryGetDraggedTodoItem(e.Data, out var draggedItem) || draggedItem.Bucket != bucket)
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
                if (!TryGetDraggedTodoItem(e.Data, out var draggedItem))
                    return;
                if (draggedItem.Bucket != bucket)
                    return;

                var dropPos = e.GetPosition(row);
                bool insertBefore = dropPos.Y <= (row.ActualHeight / 2.0);
                ReorderTodoItemWithinBucket(draggedItem.Id, item.Id, bucket, insertBefore);
            };

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
            };
            checkBox.Unchecked += (_, _) =>
            {
                if (!item.CompletedAtUtc.HasValue)
                    return;
                item.CompletedAtUtc = null;
                RenderTodoLists();
                SaveWindowSettings();
            };
            Grid.SetColumn(checkBox, 0);
            row.Children.Add(checkBox);

            var removeButton = new Button
            {
                Content = "×",
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
                _todoItems.RemoveAll(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                RenderTodoLists();
                SaveWindowSettings();
            };
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            panel.Children.Add(row);
        }
    }

    private UIElement BuildCompletedRow(TodoItemState item)
    {
        var completedAt = item.CompletedAtUtc?.ToLocalTime() ?? DateTime.Now;
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        container.Children.Add(new TextBlock
        {
            Text = "X " + item.Text,
            Opacity = 0.75
        });

        container.Children.Add(new TextBlock
        {
            Text = $"[{GetTodoBucketLabel(item.Bucket)}]  Completed {completedAt.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)}",
            Foreground = Brushes.DimGray,
            FontSize = 11,
            Margin = new Thickness(18, 1, 0, 0)
        });

        return container;
    }

    private static string GetTodoBucketLabel(TodoBucket bucket)
        => bucket == TodoBucket.ThisWeek ? "This Week" : "Today";

    private void ConfigureTodoSectionDropTarget(Panel? panel, TodoBucket bucket)
    {
        if (panel == null)
            return;

        panel.AllowDrop = true;
        panel.Tag = bucket;
        panel.DragOver += TodoSectionPanel_DragOver;
        panel.Drop += TodoSectionPanel_Drop;
    }

    private void TodoSectionPanel_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Panel panel
            || panel.Tag is not TodoBucket bucket
            || !TryGetDraggedTodoItem(e.Data, out var draggedItem)
            || draggedItem.Bucket != bucket)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TodoSectionPanel_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Panel panel
            || panel.Tag is not TodoBucket bucket
            || !TryGetDraggedTodoItem(e.Data, out var draggedItem)
            || draggedItem.Bucket != bucket)
        {
            return;
        }

        ReorderTodoItemWithinBucket(draggedItem.Id, targetItemId: null, bucket, insertBeforeTarget: false);
    }

    private void NormalizeTodoSortOrders()
    {
        foreach (var group in _todoItems.GroupBy(item => item.Bucket))
        {
            int order = 1;
            foreach (var item in group.OrderBy(item => item.SortOrder).ThenBy(item => item.CreatedUtc))
            {
                item.SortOrder = order++;
            }
        }
    }

    private int NextTodoSortOrder(TodoBucket bucket)
    {
        var max = _todoItems
            .Where(item => item.Bucket == bucket)
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

    private void ReorderTodoItemWithinBucket(string draggedItemId, string? targetItemId, TodoBucket bucket, bool insertBeforeTarget)
    {
        var orderedBucketItems = _todoItems
            .Where(item => item.Bucket == bucket)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedUtc)
            .ToList();

        if (orderedBucketItems.Count <= 1)
            return;

        var draggedItem = orderedBucketItems.FirstOrDefault(item => string.Equals(item.Id, draggedItemId, StringComparison.OrdinalIgnoreCase));
        if (draggedItem == null)
            return;

        orderedBucketItems.Remove(draggedItem);
        int targetIndex;
        if (string.IsNullOrWhiteSpace(targetItemId))
        {
            targetIndex = orderedBucketItems.Count;
        }
        else
        {
            var locatedTargetIndex = orderedBucketItems.FindIndex(item => string.Equals(item.Id, targetItemId, StringComparison.OrdinalIgnoreCase));
            if (locatedTargetIndex < 0)
                targetIndex = orderedBucketItems.Count;
            else
                targetIndex = insertBeforeTarget ? locatedTargetIndex : locatedTargetIndex + 1;
        }

        targetIndex = Math.Max(0, Math.Min(targetIndex, orderedBucketItems.Count));
        orderedBucketItems.Insert(targetIndex, draggedItem);

        for (int i = 0; i < orderedBucketItems.Count; i++)
            orderedBucketItems[i].SortOrder = i + 1;

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
        => ShowAddTodoDialog(TodoBucket.Today);

    private void TodoAddThisWeekButton_Click(object sender, RoutedEventArgs e)
        => ShowAddTodoDialog(TodoBucket.ThisWeek);

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
        if (!_todoPanelVisible)
            return;

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.OemPlus || key == Key.Add)
        {
            e.Handled = true;
            ShowAddTodoDialog(TodoBucket.Today);
            return;
        }

        if (key == Key.W)
        {
            e.Handled = true;
            ShowAddTodoDialog(TodoBucket.ThisWeek);
            return;
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
            ShowAddTodoDialog(TodoBucket.Today);
        }
    }

    private void MenuTodoList_Click(object sender, RoutedEventArgs e)
        => ExecuteToggleTodoPanel();

    private void ShowAddTodoDialog(TodoBucket bucket)
    {
        var dialog = new Window
        {
            Title = bucket == TodoBucket.ThisWeek ? "Add This Week Task" : "Add Today Task",
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
                Bucket = bucket,
                SortOrder = NextTodoSortOrder(bucket),
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

        if (dialog.ShowDialog() != true)
            return;

        RenderTodoLists();
        SaveWindowSettings();
    }
}

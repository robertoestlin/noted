using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const int MaxAlarmTimesPerAlarm = 10;

    private static (double Left, double Top) ClampAlarmPopupPositionToVisibleArea(double left, double top, double width, double height)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        var maxLeft = virtualLeft + Math.Max(0, virtualWidth - Math.Min(width, virtualWidth));
        var maxTop = virtualTop + Math.Max(0, virtualHeight - Math.Min(height, virtualHeight));

        var clampedLeft = Math.Max(virtualLeft, Math.Min(left, maxLeft));
        var clampedTop = Math.Max(virtualTop, Math.Min(top, maxTop));
        return (clampedLeft, clampedTop);
    }

    private List<PluginAlarmSettings> BuildPluginAlarmsSnapshot()
        => NormalizePluginAlarms(_pluginAlarms);

    private void ApplyPluginAlarmSettings(IEnumerable<PluginAlarmSettings>? alarms)
        => _pluginAlarms = NormalizePluginAlarms(alarms);

    private static List<PluginAlarmSettings> NormalizePluginAlarms(IEnumerable<PluginAlarmSettings>? alarms)
    {
        if (alarms == null)
            return [];

        var byName = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var alarm in alarms)
        {
            var name = (alarm?.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;

            var normalizedTimes = NormalizePluginAlarmTimes(alarm?.Times);
            if (normalizedTimes.Count == 0)
                continue;

            if (!byName.TryGetValue(name, out var bucket))
            {
                bucket = [];
                byName[name] = bucket;
            }

            foreach (var time in normalizedTimes)
                bucket.Add((time.Hour * 60) + time.Minute);
        }

        return byName
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PluginAlarmSettings
            {
                Name = pair.Key,
                Times = pair.Value
                    .OrderBy(value => value)
                    .Take(MaxAlarmTimesPerAlarm)
                    .Select(value => new PluginAlarmTime
                    {
                        Hour = value / 60,
                        Minute = value % 60
                    })
                    .ToList()
            })
            .ToList();
    }

    private static List<PluginAlarmTime> NormalizePluginAlarmTimes(IEnumerable<PluginAlarmTime>? times)
    {
        if (times == null)
            return [];

        return times
            .Where(time => time is { Hour: >= 0 and <= 23, Minute: >= 0 and <= 59 })
            .Select(time => new PluginAlarmTime { Hour = time.Hour, Minute = time.Minute })
            .GroupBy(time => (time.Hour * 60) + time.Minute)
            .OrderBy(group => group.Key)
            .Take(MaxAlarmTimesPerAlarm)
            .Select(group => group.First())
            .ToList();
    }

    private static string FormatAlarmResumeTimestamp(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm");

    private bool TryGetActiveAlarmSnoozeUntil(out DateTime snoozedUntil)
    {
        snoozedUntil = DateTime.MinValue;
        if (_pluginAlarmsSnoozedUntilLocal is not DateTime configuredUntil)
            return false;

        if (configuredUntil <= DateTime.Now)
        {
            _pluginAlarmsSnoozedUntilLocal = null;
            UpdateAlarmSnoozeStatus();
            SaveWindowSettings();
            return false;
        }

        snoozedUntil = configuredUntil;
        return true;
    }

    private void SnoozePluginAlarmsForHours(int hours)
    {
        if (hours <= 0)
            return;

        _pluginAlarmsSnoozedUntilLocal = DateTime.Now.AddHours(hours);
        UpdateAlarmSnoozeStatus();
        SaveWindowSettings();
    }

    private void UpdateAlarmSnoozeStatus()
    {
        if (TryGetActiveAlarmSnoozeUntil(out var snoozedUntil))
        {
            SetAlarmSnoozeStatusText($"Alarms snoozed until {FormatAlarmResumeTimestamp(snoozedUntil)}");
            return;
        }

        SetAlarmSnoozeStatusText(string.Empty);
    }

    private void SetAlarmSnoozeStatusText(string text)
    {
        var hasText = !string.IsNullOrWhiteSpace(text);
        StatusAlarmSnooze.Text = text;
        StatusAlarmSnoozeSeparator.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        StatusAlarmSnoozeItem.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPluginAlarmMessage(IEnumerable<string> alarmNames, bool isTestPreview = false)
    {
        var names = alarmNames
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return;

        var popup = new Window
        {
            Title = "Alarm",
            Owner = this,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 320,
            MinHeight = 180,
            Width = 430,
            Height = 230,
            Background = Brushes.White
        };

        if (_alarmPopupLeft is double popupLeft
            && _alarmPopupTop is double popupTop
            && !double.IsNaN(popupLeft)
            && !double.IsInfinity(popupLeft)
            && !double.IsNaN(popupTop)
            && !double.IsInfinity(popupTop))
        {
            var clamped = ClampAlarmPopupPositionToVisibleArea(popupLeft, popupTop, popup.Width, popup.Height);
            popup.WindowStartupLocation = WindowStartupLocation.Manual;
            popup.Left = clamped.Left;
            popup.Top = clamped.Top;
        }
        else
        {
            popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var messageText = string.Join(Environment.NewLine, names);

        void CenterPopupOnOwnerWindow()
        {
            var ownerLeft = Left;
            var ownerTop = Top;
            var ownerWidth = ActualWidth > 0 ? ActualWidth : Width;
            var ownerHeight = ActualHeight > 0 ? ActualHeight : Height;

            var centeredLeft = ownerLeft + Math.Max(0, (ownerWidth - popup.Width) / 2);
            var centeredTop = ownerTop + Math.Max(0, (ownerHeight - popup.Height) / 2);
            var clamped = ClampAlarmPopupPositionToVisibleArea(centeredLeft, centeredTop, popup.Width, popup.Height);

            popup.WindowStartupLocation = WindowStartupLocation.Manual;
            popup.Left = clamped.Left;
            popup.Top = clamped.Top;
        }

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var messageHost = new Border
        {
            Margin = new Thickness(14, 14, 14, 8),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = messageText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(25, 36, 58)),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(messageHost, 0);
        layout.Children.Add(messageHost);

        var footer = new Grid
        {
            Margin = new Thickness(14, 0, 14, 14)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var snoozePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };
        var snoozeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        snoozePanel.Children.Add(new TextBlock
        {
            Text = "Snooze all alarms:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        foreach (var hours in new[] { 1, 4, 8 })
        {
            var button = new Button
            {
                Content = $"{hours}H",
                Width = 34,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = $"Snooze all alarms for {hours} hour{(hours == 1 ? string.Empty : "s")}"
            };
            button.Click += (_, _) =>
            {
                if (!isTestPreview)
                    SnoozePluginAlarmsForHours(hours);
                popup.Close();
            };
            snoozeButtons.Children.Add(button);
        }
        var btnDisableAlarms = new Button
        {
            Content = "D",
            Width = 28,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Disable alarms"
        };
        btnDisableAlarms.Click += (_, _) =>
        {
            if (!isTestPreview)
            {
                _pluginAlarmsEnabled = false;
                UpdateAlarmSnoozeStatus();
                SaveWindowSettings();
            }
            popup.Close();
        };
        snoozeButtons.Children.Add(btnDisableAlarms);
        snoozePanel.Children.Add(snoozeButtons);
        Grid.SetColumn(snoozePanel, 0);
        footer.Children.Add(snoozePanel);

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var btnOk = new Button
        {
            Content = "OK",
            Width = 72,
            Height = 30,
            IsDefault = true,
            IsCancel = true
        };
        btnOk.Click += (_, _) => popup.Close();
        actionButtons.Children.Add(btnOk);
        Grid.SetColumn(actionButtons, 1);
        footer.Children.Add(actionButtons);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);

        popup.PreviewKeyDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.M)
            {
                e.Handled = true;
                CenterPopupOnOwnerWindow();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.Close();
            }
        };

        popup.Content = layout;

        popup.Closed += (_, _) =>
        {
            double left;
            double top;
            if (popup.WindowState == WindowState.Normal)
            {
                left = popup.Left;
                top = popup.Top;
            }
            else
            {
                left = popup.RestoreBounds.Left;
                top = popup.RestoreBounds.Top;
            }

            var clamped = ClampAlarmPopupPositionToVisibleArea(left, top, popup.Width, popup.Height);
            _alarmPopupLeft = clamped.Left;
            _alarmPopupTop = clamped.Top;
            SaveWindowSettings();
        };

        popup.ShowDialog();
    }

    private void CheckPluginAlarms()
    {
        if (!_pluginAlarmsEnabled || _pluginAlarms.Count == 0)
            return;
        if (TryGetActiveAlarmSnoozeUntil(out _))
            return;

        var now = DateTime.Now;
        var minuteKey = now.ToString("yyyyMMddHHmm");
        if (!string.Equals(_triggeredPluginAlarmMinuteKey, minuteKey, StringComparison.Ordinal))
        {
            _triggeredPluginAlarmMinuteKey = minuteKey;
            _triggeredPluginAlarmKeysForMinute.Clear();
        }

        var dueNames = new List<string>();
        foreach (var alarm in _pluginAlarms)
        {
            if (string.IsNullOrWhiteSpace(alarm.Name) || alarm.Times == null)
                continue;

            foreach (var time in alarm.Times)
            {
                if (time.Hour != now.Hour || time.Minute != now.Minute)
                    continue;

                var key = $"{alarm.Name}|{time.Hour:00}:{time.Minute:00}";
                if (_triggeredPluginAlarmKeysForMinute.Add(key))
                    dueNames.Add(alarm.Name);
            }
        }

        if (dueNames.Count == 0)
            return;

        ShowPluginAlarmMessage(dueNames);
    }

    private void ShowAlarmsDialog()
    {
        var workingAlarms = NormalizePluginAlarms(_pluginAlarms);
        var workingPluginAlarmsEnabled = _pluginAlarmsEnabled;
        var workingPluginAlarmsSnoozedUntilLocal = _pluginAlarmsSnoozedUntilLocal;
        var settingsApplied = false;
        if (workingPluginAlarmsSnoozedUntilLocal is DateTime storedSnooze
            && storedSnooze <= DateTime.Now)
        {
            workingPluginAlarmsSnoozedUntilLocal = null;
        }

        var dlg = new Window
        {
            Title = "Alarms",
            Width = 720,
            Height = 500,
            MinWidth = 640,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var chkEnableAlarms = new CheckBox
        {
            Content = "Enable alarms",
            IsChecked = workingPluginAlarmsEnabled,
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(chkEnableAlarms, Dock.Top);
        root.Children.Add(chkEnableAlarms);
        var snoozeBorder = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            IsEnabled = workingPluginAlarmsEnabled
        };
        DockPanel.SetDock(snoozeBorder, Dock.Top);
        var snoozeControls = new StackPanel();
        snoozeControls.Children.Add(new TextBlock
        {
            Text = "Snooze all alarms:",
            FontWeight = FontWeights.SemiBold
        });
        var snoozeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var txtSnoozeStatus = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = Brushes.DimGray
        };
        void UpdateBottomSnoozeStatusPreview()
        {
            if (workingPluginAlarmsSnoozedUntilLocal is DateTime snoozedUntil
                && snoozedUntil > DateTime.Now)
            {
                SetAlarmSnoozeStatusText($"Alarms snoozed until {FormatAlarmResumeTimestamp(snoozedUntil)}");
                return;
            }

            SetAlarmSnoozeStatusText(string.Empty);
        }

        void RefreshSnoozeStatus()
        {
            if (workingPluginAlarmsSnoozedUntilLocal is DateTime snoozedUntil
                && snoozedUntil > DateTime.Now)
            {
                txtSnoozeStatus.Text = $"Alarms resume at {FormatAlarmResumeTimestamp(snoozedUntil)}";
                UpdateBottomSnoozeStatusPreview();
                return;
            }

            workingPluginAlarmsSnoozedUntilLocal = null;
            txtSnoozeStatus.Text = "Alarms are not snoozed.";
            UpdateBottomSnoozeStatusPreview();
        }

        foreach (var hours in new[] { 1, 4, 8 })
        {
            var btnSnooze = new Button
            {
                Content = $"{hours}H",
                Width = 48,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = $"Snooze all alarms for {hours} hour{(hours == 1 ? string.Empty : "s")}"
            };
            btnSnooze.Click += (_, _) =>
            {
                workingPluginAlarmsSnoozedUntilLocal = DateTime.Now.AddHours(hours);
                RefreshSnoozeStatus();
            };
            snoozeButtons.Children.Add(btnSnooze);
        }
        var btnResumeNow = new Button
        {
            Content = "Resume now",
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0)
        };
        btnResumeNow.Click += (_, _) =>
        {
            workingPluginAlarmsSnoozedUntilLocal = null;
            RefreshSnoozeStatus();
        };
        snoozeButtons.Children.Add(btnResumeNow);
        snoozeControls.Children.Add(snoozeButtons);
        snoozeControls.Children.Add(txtSnoozeStatus);
        snoozeBorder.Child = snoozeControls;
        root.Children.Add(snoozeBorder);
        RefreshSnoozeStatus();

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var btnOk = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        footer.Children.Add(btnOk);
        footer.Children.Add(btnCancel);
        root.Children.Add(footer);

        var listPanel = new DockPanel();
        var listButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var btnAddAlarm = new Button { Content = "+", Width = 30, Height = 30, ToolTip = "Add alarm", Margin = new Thickness(0, 0, 6, 0) };
        var btnRemoveAlarm = new Button { Content = "-", Width = 30, Height = 30, ToolTip = "Remove selected alarm" };
        listButtons.Children.Add(btnAddAlarm);
        listButtons.Children.Add(btnRemoveAlarm);
        DockPanel.SetDock(listButtons, Dock.Top);
        listPanel.Children.Add(listButtons);

        var alarmList = new ListBox();
        listPanel.Children.Add(alarmList);
        Grid.SetColumn(listPanel, 0);
        body.Children.Add(listPanel);

        var rightPanel = new StackPanel();
        Grid.SetColumn(rightPanel, 2);
        body.Children.Add(rightPanel);

        rightPanel.Children.Add(new TextBlock { Text = "Alarm name", FontWeight = FontWeights.SemiBold });
        var txtAlarmName = new TextBox { Margin = new Thickness(0, 4, 0, 10) };
        rightPanel.Children.Add(txtAlarmName);

        var timesHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        timesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timesHeader.Children.Add(new TextBlock { Text = "Times during day", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var btnAddTime = new Button { Content = "+", Width = 30, Height = 30, ToolTip = "Add time" };
        Grid.SetColumn(btnAddTime, 1);
        timesHeader.Children.Add(btnAddTime);
        var btnTestAlarm = new Button
        {
            Content = "Test alarm",
            MinWidth = 90,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Preview selected alarm"
        };
        Grid.SetColumn(btnTestAlarm, 2);
        timesHeader.Children.Add(btnTestAlarm);
        rightPanel.Children.Add(timesHeader);

        rightPanel.Children.Add(new TextBlock
        {
            Text = "Up to 10 times. Use + to add and - to remove.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var timesScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 280
        };
        var timesPanel = new StackPanel();
        timesScroll.Content = timesPanel;
        rightPanel.Children.Add(timesScroll);

        root.Children.Add(body);
        dlg.Content = root;

        int selectedIndex = -1;
        var suppressNameEvents = false;
        var suppressListSelectionEvents = false;

        void SetAlarmNameText(string value, bool focusNameBox = false, bool selectAll = false)
        {
            suppressNameEvents = true;
            txtAlarmName.Text = value ?? string.Empty;
            suppressNameEvents = false;

            if (focusNameBox)
                txtAlarmName.Focus();

            if (selectAll)
            {
                txtAlarmName.SelectAll();
                return;
            }

            txtAlarmName.CaretIndex = txtAlarmName.Text.Length;
        }

        void RefreshAlarmList()
        {
            var targetIndex = selectedIndex;
            suppressListSelectionEvents = true;
            alarmList.Items.Clear();
            foreach (var alarm in workingAlarms)
            {
                var label = string.IsNullOrWhiteSpace(alarm.Name) ? "(unnamed alarm)" : alarm.Name.Trim();
                var count = alarm.Times?.Count ?? 0;
                alarmList.Items.Add($"{label} ({count} {(count == 1 ? "time" : "times")})");
            }

            if (workingAlarms.Count == 0)
            {
                selectedIndex = -1;
                alarmList.SelectedIndex = -1;
            }
            else
            {
                selectedIndex = Math.Max(0, Math.Min(targetIndex, workingAlarms.Count - 1));
                alarmList.SelectedIndex = selectedIndex;
            }
            suppressListSelectionEvents = false;
        }

        void RebuildTimesEditor()
        {
            timesPanel.Children.Clear();

            if (selectedIndex < 0 || selectedIndex >= workingAlarms.Count)
            {
                SetAlarmNameText(string.Empty);
                txtAlarmName.IsEnabled = false;
                btnAddTime.IsEnabled = false;
                btnTestAlarm.IsEnabled = false;
                btnRemoveAlarm.IsEnabled = false;
                return;
            }

            txtAlarmName.IsEnabled = true;
            btnRemoveAlarm.IsEnabled = true;
            var selectedAlarm = workingAlarms[selectedIndex];
            selectedAlarm.Times ??= [];
            btnAddTime.IsEnabled = selectedAlarm.Times.Count < MaxAlarmTimesPerAlarm;
            btnTestAlarm.IsEnabled = true;

            for (var i = 0; i < selectedAlarm.Times.Count; i++)
            {
                var rowIndex = i;
                var time = selectedAlarm.Times[rowIndex];

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 6),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var cmbHour = new ComboBox { Width = 70, Margin = new Thickness(0, 0, 6, 0) };
                for (var hour = 0; hour < 24; hour++)
                    cmbHour.Items.Add(hour.ToString("00"));
                cmbHour.SelectedIndex = time.Hour;

                var separator = new TextBlock
                {
                    Text = ":",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var cmbMinute = new ComboBox { Width = 70, Margin = new Thickness(0, 0, 6, 0) };
                for (var minute = 0; minute < 60; minute++)
                    cmbMinute.Items.Add(minute.ToString("00"));
                cmbMinute.SelectedIndex = time.Minute;

                var btnRemoveTime = new Button { Content = "-", Width = 30, Height = 30, ToolTip = "Remove time" };

                cmbHour.SelectionChanged += (_, _) =>
                {
                    if (cmbHour.SelectedIndex >= 0)
                        selectedAlarm.Times[rowIndex].Hour = cmbHour.SelectedIndex;
                };
                cmbMinute.SelectionChanged += (_, _) =>
                {
                    if (cmbMinute.SelectedIndex >= 0)
                        selectedAlarm.Times[rowIndex].Minute = cmbMinute.SelectedIndex;
                };
                btnRemoveTime.Click += (_, _) =>
                {
                    selectedAlarm.Times.RemoveAt(rowIndex);
                    selectedAlarm.Times = NormalizePluginAlarmTimes(selectedAlarm.Times);
                    RefreshAlarmList();
                    RebuildTimesEditor();
                };

                row.Children.Add(cmbHour);
                row.Children.Add(separator);
                row.Children.Add(cmbMinute);
                row.Children.Add(btnRemoveTime);
                timesPanel.Children.Add(row);
            }

            if (selectedAlarm.Times.Count == 0)
            {
                timesPanel.Children.Add(new TextBlock
                {
                    Text = "No times yet. Click + to add one.",
                    Foreground = Brushes.IndianRed
                });
            }
        }

        txtAlarmName.TextChanged += (_, _) =>
        {
            if (suppressNameEvents)
                return;
            if (selectedIndex < 0 || selectedIndex >= workingAlarms.Count)
                return;

            var keepNameFocus = txtAlarmName.IsKeyboardFocusWithin;
            workingAlarms[selectedIndex].Name = txtAlarmName.Text ?? string.Empty;
            RefreshAlarmList();
            alarmList.SelectedIndex = selectedIndex;
            if (keepNameFocus)
            {
                txtAlarmName.Focus();
                txtAlarmName.CaretIndex = (txtAlarmName.Text ?? string.Empty).Length;
            }
        };

        chkEnableAlarms.Checked += (_, _) =>
        {
            workingPluginAlarmsEnabled = true;
            snoozeBorder.IsEnabled = true;
            UpdateBottomSnoozeStatusPreview();
            RebuildTimesEditor();
        };
        chkEnableAlarms.Unchecked += (_, _) =>
        {
            workingPluginAlarmsEnabled = false;
            snoozeBorder.IsEnabled = false;
            UpdateBottomSnoozeStatusPreview();
            RebuildTimesEditor();
        };

        btnAddAlarm.Click += (_, _) =>
        {
            var now = DateTime.Now;
            workingAlarms.Add(new PluginAlarmSettings
            {
                Name = "New alarm",
                Times = [new PluginAlarmTime { Hour = now.Hour, Minute = now.Minute }]
            });
            selectedIndex = workingAlarms.Count - 1;
            RefreshAlarmList();
            SetAlarmNameText(workingAlarms[selectedIndex].Name, focusNameBox: true, selectAll: true);
            RebuildTimesEditor();
        };

        btnRemoveAlarm.Click += (_, _) =>
        {
            if (selectedIndex < 0 || selectedIndex >= workingAlarms.Count)
                return;

            workingAlarms.RemoveAt(selectedIndex);
            if (selectedIndex >= workingAlarms.Count)
                selectedIndex = workingAlarms.Count - 1;
            RefreshAlarmList();
            if (selectedIndex >= 0 && selectedIndex < workingAlarms.Count)
                SetAlarmNameText(workingAlarms[selectedIndex].Name);
            RebuildTimesEditor();
        };

        btnTestAlarm.Click += (_, _) =>
        {
            if (selectedIndex < 0 || selectedIndex >= workingAlarms.Count)
                return;

            var selectedAlarm = workingAlarms[selectedIndex];
            var name = (selectedAlarm.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(dlg, "Set an alarm name first.", "Alarms", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ShowPluginAlarmMessage([name], isTestPreview: true);
        };

        alarmList.SelectionChanged += (_, _) =>
        {
            if (suppressListSelectionEvents)
                return;
            selectedIndex = alarmList.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < workingAlarms.Count)
                SetAlarmNameText(workingAlarms[selectedIndex].Name);
            RebuildTimesEditor();
        };

        btnAddTime.Click += (_, _) =>
        {
            if (selectedIndex < 0 || selectedIndex >= workingAlarms.Count)
                return;

            var selectedAlarm = workingAlarms[selectedIndex];
            selectedAlarm.Times ??= [];
            if (selectedAlarm.Times.Count >= MaxAlarmTimesPerAlarm)
            {
                MessageBox.Show(dlg, "Each alarm can have up to 10 times.", "Alarms", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var now = DateTime.Now;
            selectedAlarm.Times.Add(new PluginAlarmTime { Hour = now.Hour, Minute = now.Minute });
            selectedAlarm.Times = NormalizePluginAlarmTimes(selectedAlarm.Times);
            RefreshAlarmList();
            RebuildTimesEditor();
        };

        btnOk.Click += (_, _) =>
        {
            foreach (var alarm in workingAlarms)
            {
                alarm.Name = (alarm.Name ?? string.Empty).Trim();
                alarm.Times = NormalizePluginAlarmTimes(alarm.Times);
            }

            if (workingAlarms.Any(alarm => string.IsNullOrWhiteSpace(alarm.Name)))
            {
                MessageBox.Show(dlg, "Every alarm needs a name.", "Alarms", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (workingAlarms.Any(alarm => alarm.Times == null || alarm.Times.Count == 0))
            {
                MessageBox.Show(dlg, "Every alarm needs at least one time.", "Alarms", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pluginAlarms = NormalizePluginAlarms(workingAlarms);
            _pluginAlarmsEnabled = workingPluginAlarmsEnabled;
            _pluginAlarmsSnoozedUntilLocal = workingPluginAlarmsSnoozedUntilLocal;
            _triggeredPluginAlarmMinuteKey = string.Empty;
            _triggeredPluginAlarmKeysForMinute.Clear();
            UpdateAlarmSnoozeStatus();
            SaveWindowSettings();
            CheckPluginAlarms();
            settingsApplied = true;
            dlg.DialogResult = true;
        };

        dlg.Loaded += (_, _) =>
        {
            if (workingAlarms.Count == 0)
            {
                btnAddAlarm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }

            selectedIndex = 0;
            RefreshAlarmList();
            SetAlarmNameText(workingAlarms[0].Name);
            RebuildTimesEditor();
        };
        dlg.Closed += (_, _) =>
        {
            if (!settingsApplied)
                UpdateAlarmSnoozeStatus();
        };

        dlg.ShowDialog();
    }
}

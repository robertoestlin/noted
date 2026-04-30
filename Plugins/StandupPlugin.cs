using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const string StandupNotesFileName = "standup-notes.json";
    private const int StandupDefaultHour = 9;
    private const int StandupDefaultMinute = 0;
    private const int MaxStandupRetentionDays = 36500;

    private readonly Dictionary<string, string> _standupNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StandupWeekdayTime> _standupWeekdayTimes = BuildDefaultStandupWeekdayTimes();
    private int _standupRetentionDays;

    private double? _standupWindowLeft;
    private double? _standupWindowTop;
    private double? _standupWindowWidth;
    private double? _standupWindowHeight;
    private bool _standupWindowMaximized;
    private Window? _standupWindowInstance;

    private static List<StandupWeekdayTime> BuildDefaultStandupWeekdayTimes()
    {
        var list = new List<StandupWeekdayTime>(7);
        for (int day = 0; day < 7; day++)
        {
            var dow = (DayOfWeek)day;
            var weekday = dow is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            list.Add(new StandupWeekdayTime
            {
                DayOfWeek = day,
                Enabled = weekday,
                Hour = StandupDefaultHour,
                Minute = StandupDefaultMinute
            });
        }
        return list;
    }

    private static List<StandupWeekdayTime> NormalizeStandupWeekdayTimes(IEnumerable<StandupWeekdayTime>? source)
    {
        var defaults = BuildDefaultStandupWeekdayTimes();
        if (source == null)
            return defaults;

        var byDay = defaults.ToDictionary(entry => entry.DayOfWeek);
        foreach (var raw in source)
        {
            if (raw == null)
                continue;
            if (raw.DayOfWeek is < 0 or > 6)
                continue;

            byDay[raw.DayOfWeek] = new StandupWeekdayTime
            {
                DayOfWeek = raw.DayOfWeek,
                Enabled = raw.Enabled,
                Hour = Math.Clamp(raw.Hour, 0, 23),
                Minute = Math.Clamp(raw.Minute, 0, 59)
            };
        }

        return byDay
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();
    }

    private static int NormalizeStandupRetentionDays(int days)
        => Math.Clamp(days, 0, MaxStandupRetentionDays);

    private static string ToStandupDateKey(DateTime date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseStandupDateKey(string key, out DateTime date)
        => DateTime.TryParseExact(
            key ?? string.Empty,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private void ResetStandupSettingsToDefaults()
    {
        _standupNotes.Clear();
        _standupWeekdayTimes.Clear();
        _standupWeekdayTimes.AddRange(BuildDefaultStandupWeekdayTimes());
        _standupRetentionDays = 0;
        _standupWindowLeft = null;
        _standupWindowTop = null;
        _standupWindowWidth = null;
        _standupWindowHeight = null;
        _standupWindowMaximized = false;
    }

    private StandupSettings BuildStandupSettingsSnapshot()
        => new()
        {
            WeekdayTimes = NormalizeStandupWeekdayTimes(_standupWeekdayTimes),
            RetentionDays = NormalizeStandupRetentionDays(_standupRetentionDays),
            WindowLeft = _standupWindowLeft,
            WindowTop = _standupWindowTop,
            WindowWidth = _standupWindowWidth,
            WindowHeight = _standupWindowHeight,
            WindowMaximized = _standupWindowMaximized
        };

    private void ApplyStandupSettings(StandupSettings? settings)
    {
        _standupWeekdayTimes.Clear();
        _standupWeekdayTimes.AddRange(NormalizeStandupWeekdayTimes(settings?.WeekdayTimes));
        _standupRetentionDays = NormalizeStandupRetentionDays(settings?.RetentionDays ?? 0);
        _standupWindowLeft = settings?.WindowLeft;
        _standupWindowTop = settings?.WindowTop;
        _standupWindowWidth = settings?.WindowWidth;
        _standupWindowHeight = settings?.WindowHeight;
        _standupWindowMaximized = settings?.WindowMaximized ?? false;
    }

    private static bool StandupSavedBoundsIntersectVirtualScreen(double left, double top, double width, double height)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var wr = new Rect(left, top, width, height);
        var vr = new Rect(vl, vt, vw, vh);
        return vr.IntersectsWith(wr);
    }

    private static void ClampStandupWindowToVirtualScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal)
            return;

        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        const double pad = 8;
        var maxLeft = vl + vw - window.Width - pad;
        var maxTop = vt + vh - window.Height - pad;
        window.Left = Math.Clamp(window.Left, vl + pad, Math.Max(vl + pad, maxLeft));
        window.Top = Math.Clamp(window.Top, vt + pad, Math.Max(vt + pad, maxTop));
    }

    private void CaptureStandupWindowPlacement(Window dialog)
    {
        if (dialog.WindowState == WindowState.Maximized)
        {
            _standupWindowMaximized = true;
            var rb = dialog.RestoreBounds;
            _standupWindowLeft = rb.Left;
            _standupWindowTop = rb.Top;
            _standupWindowWidth = rb.Width;
            _standupWindowHeight = rb.Height;
        }
        else
        {
            _standupWindowMaximized = false;
            _standupWindowLeft = dialog.Left;
            _standupWindowTop = dialog.Top;
            _standupWindowWidth = dialog.Width;
            _standupWindowHeight = dialog.Height;
        }
    }

    private void ApplyStandupDialogPlacement(Window dialog)
    {
        const double defaultWidth = 880;
        const double defaultHeight = 520;

        var dw = _standupWindowWidth ?? defaultWidth;
        var dh = _standupWindowHeight ?? defaultHeight;
        dw = Math.Clamp(dw, dialog.MinWidth, 2000);
        dh = Math.Clamp(dh, dialog.MinHeight, SystemParameters.VirtualScreenHeight - 24);

        bool hasSavedPosition =
            _standupWindowLeft.HasValue
            && _standupWindowTop.HasValue
            && _standupWindowWidth.GetValueOrDefault() >= dialog.MinWidth
            && _standupWindowHeight.GetValueOrDefault() >= dialog.MinHeight
            && StandupSavedBoundsIntersectVirtualScreen(
                _standupWindowLeft!.Value,
                _standupWindowTop!.Value,
                dw,
                dh);

        if (hasSavedPosition)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Width = dw;
            dialog.Height = dh;
            dialog.Left = _standupWindowLeft!.Value;
            dialog.Top = _standupWindowTop!.Value;
            dialog.WindowState = WindowState.Normal;
            ClampStandupWindowToVirtualScreen(dialog);
            if (_standupWindowMaximized)
                dialog.WindowState = WindowState.Maximized;
        }
        else
        {
            dialog.Width = defaultWidth;
            dialog.Height = defaultHeight;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.WindowState = WindowState.Normal;
        }
    }

    private List<StandupNoteEntry> BuildStandupNotesSnapshot()
    {
        PruneStandupNotes();
        return _standupNotes
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new StandupNoteEntry { Date = pair.Key, Notes = pair.Value })
            .ToList();
    }

    private void ApplyStandupNotes(IEnumerable<StandupNoteEntry>? entries)
    {
        _standupNotes.Clear();
        if (entries == null)
            return;

        foreach (var entry in entries)
        {
            if (entry == null)
                continue;
            if (!TryParseStandupDateKey(entry.Date, out var parsed))
                continue;
            var text = entry.Notes ?? string.Empty;
            if (text.Length == 0)
                continue;
            _standupNotes[ToStandupDateKey(parsed)] = text;
        }

        PruneStandupNotes();
    }

    private void PruneStandupNotes()
    {
        if (_standupRetentionDays <= 0 || _standupNotes.Count == 0)
            return;

        var cutoff = DateTime.Today.AddDays(-_standupRetentionDays);
        var toRemove = new List<string>();
        foreach (var key in _standupNotes.Keys)
        {
            if (TryParseStandupDateKey(key, out var date) && date < cutoff)
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
            _standupNotes.Remove(key);
    }

    private void SaveStandupNotes(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, StandupNotesFileName);
        _windowSettingsStore.Save(path, BuildStandupNotesSnapshot(), options);
    }

    private void LoadStandupNotes()
    {
        var path = Path.Combine(_backupFolder, StandupNotesFileName);
        var entries = _windowSettingsStore.Load<List<StandupNoteEntry>>(path);
        ApplyStandupNotes(entries);
        if (entries == null)
            SaveStandupNotes(new JsonSerializerOptions { WriteIndented = true });
    }

    private const int StandupGraceMinutesAfterStart = 30;

    /// <summary>
    /// Calendar date to focus when opening standup: today's slot if the next meeting is today or still within grace after start; otherwise the next enabled weekday.
    /// </summary>
    private DateTime GetStandupFocusDate(DateTime now)
    {
        var enabled = _standupWeekdayTimes.Where(time => time.Enabled).ToList();
        if (enabled.Count == 0)
            return now.Date;

        var matchToday = enabled.FirstOrDefault(time => time.DayOfWeek == (int)now.Date.DayOfWeek);
        if (matchToday != null)
        {
            var standupStart = now.Date.AddHours(matchToday.Hour).AddMinutes(matchToday.Minute);
            var standupGraceEnd = standupStart.AddMinutes(StandupGraceMinutesAfterStart);
            if (now <= standupGraceEnd)
                return now.Date;
        }

        for (int offset = 1; offset < 14; offset++)
        {
            var candidateDate = now.Date.AddDays(offset);
            if (enabled.Exists(time => time.DayOfWeek == (int)candidateDate.DayOfWeek))
                return candidateDate;
        }

        return now.Date;
    }

    private void ShowStandupDialog()
    {
        if (_standupWindowInstance != null)
        {
            _standupWindowInstance.Activate();
            if (_standupWindowInstance.WindowState == WindowState.Minimized)
                _standupWindowInstance.WindowState = WindowState.Normal;
            return;
        }

        var dialog = new Window
        {
            Title = "Standup",
            MinWidth = 720,
            MinHeight = 480,
            ShowInTaskbar = true
        };
        ApplyStandupDialogPlacement(dialog);

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Standup"
        };
        Grid.SetColumn(titleText, 0);
        header.Children.Add(titleText);

        var btnSettings = new Button
        {
            Content = "⚙",
            Width = 30,
            Height = 28,
            FontSize = 16,
            ToolTip = "Standup settings"
        };
        Grid.SetColumn(btnSettings, 2);
        header.Children.Add(btnSettings);

        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var statusText = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: month navigation + day list
        var leftPanel = new DockPanel();
        var monthRow = new Grid();
        monthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        monthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        monthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnPrevMonth = new Button { Content = "<", Width = 30, Height = 26 };
        var btnNextMonth = new Button { Content = ">", Width = 30, Height = 26 };
        var monthTitle = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        Grid.SetColumn(btnPrevMonth, 0);
        Grid.SetColumn(monthTitle, 1);
        Grid.SetColumn(btnNextMonth, 2);
        monthRow.Children.Add(btnPrevMonth);
        monthRow.Children.Add(monthTitle);
        monthRow.Children.Add(btnNextMonth);
        DockPanel.SetDock(monthRow, Dock.Top);
        leftPanel.Children.Add(monthRow);

        var dayList = new ListBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            FontFamily = new FontFamily("Consolas, Courier New"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(dayList, ScrollBarVisibility.Auto);
        leftPanel.Children.Add(dayList);
        Grid.SetColumn(leftPanel, 0);
        body.Children.Add(leftPanel);

        // Right: notes editor
        var rightPanel = new DockPanel();
        var notesHeader = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(notesHeader, Dock.Top);
        rightPanel.Children.Add(notesHeader);

        var notesBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        rightPanel.Children.Add(notesBox);
        Grid.SetColumn(rightPanel, 2);
        body.Children.Add(rightPanel);

        root.Children.Add(body);
        dialog.Content = root;

        var today = DateTime.Today;
        var initialDate = GetStandupFocusDate(DateTime.Now);
        var currentMonth = new DateTime(initialDate.Year, initialDate.Month, 1);
        var selectedDate = initialDate;
        var suppressNotesPersist = false;
        var suppressDayListSelection = false;

        void SetStatus(string message, Brush? brush = null)
        {
            statusText.Text = message;
            statusText.Foreground = brush ?? Brushes.DimGray;
        }

        void PersistCurrentNotes()
        {
            if (suppressNotesPersist)
                return;

            var key = ToStandupDateKey(selectedDate);
            var text = (notesBox.Text ?? string.Empty).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(text))
                _standupNotes.Remove(key);
            else
                _standupNotes[key] = text;

            SaveWindowSettings();
        }

        void RenderMonth()
        {
            monthTitle.Text = currentMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

            suppressDayListSelection = true;
            dayList.Items.Clear();

            var monthStart = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var enabledByDay = _standupWeekdayTimes
                .Where(time => time.Enabled)
                .ToDictionary(time => time.DayOfWeek);

            int? prevIsoWeek = null;
            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var isoWeek = ISOWeek.GetWeekOfYear(date);
                if (prevIsoWeek != isoWeek)
                {
                    dayList.Items.Add(new ListBoxItem
                    {
                        Content = string.Format(CultureInfo.CurrentCulture, "Week {0}", isoWeek),
                        Tag = null,
                        IsEnabled = false,
                        Focusable = false,
                        Padding = new Thickness(6, prevIsoWeek == null ? 2 : 14, 6, 6),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.DimGray,
                        Background = Brushes.Transparent
                    });
                    prevIsoWeek = isoWeek;
                }

                var key = ToStandupDateKey(date);
                bool hasNote = _standupNotes.ContainsKey(key)
                    && !string.IsNullOrWhiteSpace(_standupNotes[key]);
                bool isStandupDay = enabledByDay.ContainsKey((int)date.DayOfWeek);

                var label = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}  {1:dd}  {2}{3}",
                    date.ToString("ddd", CultureInfo.CurrentCulture),
                    date,
                    hasNote ? "● " : "  ",
                    isStandupDay ? string.Empty : "(off)");

                var item = new ListBoxItem
                {
                    Content = label,
                    Tag = date,
                    Padding = new Thickness(6, 3, 6, 3)
                };

                if (date == today)
                    item.FontWeight = FontWeights.Bold;
                if (!isStandupDay)
                    item.Opacity = 0.65;

                dayList.Items.Add(item);
                if (date == selectedDate)
                    dayList.SelectedItem = item;
            }

            suppressDayListSelection = false;

            if (dayList.SelectedItem is ListBoxItem selected)
                selected.BringIntoView();

            UpdateNotesEditor();
        }

        void UpdateNotesEditor()
        {
            notesHeader.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Notes for {0}",
                selectedDate.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture));

            suppressNotesPersist = true;
            var key = ToStandupDateKey(selectedDate);
            notesBox.Text = _standupNotes.TryGetValue(key, out var existing) ? existing : string.Empty;
            suppressNotesPersist = false;
        }

        btnPrevMonth.Click += (_, _) =>
        {
            PersistCurrentNotes();
            currentMonth = currentMonth.AddMonths(-1);
            // Keep selection if same month logically; otherwise clamp to last day of new month.
            if (selectedDate.Month != currentMonth.Month || selectedDate.Year != currentMonth.Year)
            {
                var lastDay = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
                selectedDate = new DateTime(currentMonth.Year, currentMonth.Month, Math.Min(selectedDate.Day, lastDay));
            }
            RenderMonth();
        };

        btnNextMonth.Click += (_, _) =>
        {
            PersistCurrentNotes();
            currentMonth = currentMonth.AddMonths(1);
            if (selectedDate.Month != currentMonth.Month || selectedDate.Year != currentMonth.Year)
            {
                var lastDay = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
                selectedDate = new DateTime(currentMonth.Year, currentMonth.Month, Math.Min(selectedDate.Day, lastDay));
            }
            RenderMonth();
        };

        dayList.SelectionChanged += (_, _) =>
        {
            if (suppressDayListSelection)
                return;
            if (dayList.SelectedItem is not ListBoxItem item || item.Tag is not DateTime date)
            {
                if (dayList.SelectedItem is ListBoxItem { Tag: null })
                {
                    suppressDayListSelection = true;
                    try
                    {
                        foreach (var obj in dayList.Items)
                        {
                            if (obj is ListBoxItem row && row.Tag is DateTime d && d == selectedDate)
                            {
                                dayList.SelectedItem = row;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        suppressDayListSelection = false;
                    }
                }

                return;
            }
            if (date == selectedDate)
                return;

            PersistCurrentNotes();
            selectedDate = date;
            UpdateNotesEditor();
        };

        notesBox.LostFocus += (_, _) =>
        {
            PersistCurrentNotes();
            // Re-render so the bullet indicator updates without losing selection.
            var keepDate = selectedDate;
            suppressDayListSelection = true;
            for (int i = 0; i < dayList.Items.Count; i++)
            {
                if (dayList.Items[i] is ListBoxItem item && item.Tag is DateTime date && date == keepDate)
                {
                    var key = ToStandupDateKey(date);
                    bool hasNote = _standupNotes.ContainsKey(key);
                    var enabledByDay = _standupWeekdayTimes
                        .Where(time => time.Enabled)
                        .Select(time => time.DayOfWeek)
                        .ToHashSet();
                    bool isStandupDay = enabledByDay.Contains((int)date.DayOfWeek);
                    item.Content = string.Format(
                        CultureInfo.CurrentCulture,
                        "{0}  {1:dd}  {2}{3}",
                        date.ToString("ddd", CultureInfo.CurrentCulture),
                        date,
                        hasNote ? "● " : "  ",
                        isStandupDay ? string.Empty : "(off)");
                    break;
                }
            }
            suppressDayListSelection = false;
        };

        btnSettings.Click += (_, _) =>
        {
            PersistCurrentNotes();
            if (ShowStandupSettingsDialog(dialog))
            {
                SetStatus("Standup settings saved.");
                RenderMonth();
            }
        };

        dialog.Closing += (_, _) =>
        {
            CaptureStandupWindowPlacement(dialog);
            PersistCurrentNotes();
        };

        dialog.Closed += (_, _) => _standupWindowInstance = null;

        RenderMonth();
        dialog.Loaded += (_, _) =>
        {
            notesBox.Focus();
            Keyboard.Focus(notesBox);
        };

        _standupWindowInstance = dialog;
        dialog.Show();
    }

    private bool ShowStandupSettingsDialog(Window owner)
    {
        var dialog = new Window
        {
            Title = "Standup Settings",
            Width = 460,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var btnOk = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);
        root.Children.Add(buttons);

        var content = new StackPanel();

        content.Children.Add(new TextBlock
        {
            Text = "Standup time per weekday",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Enable a day to have a standup at the chosen time.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var rows = new List<(int DayOfWeek, CheckBox Check, ComboBox Hour, ComboBox Minute)>();
        var working = NormalizeStandupWeekdayTimes(_standupWeekdayTimes);
        var orderedDays = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        foreach (var dow in orderedDays)
        {
            var entry = working.FirstOrDefault(item => item.DayOfWeek == (int)dow)
                ?? new StandupWeekdayTime { DayOfWeek = (int)dow };

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var check = new CheckBox
            {
                Content = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(dow),
                IsChecked = entry.Enabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 0);
            row.Children.Add(check);

            var hour = new ComboBox { Margin = new Thickness(0, 0, 4, 0) };
            for (int h = 0; h < 24; h++)
                hour.Items.Add(h.ToString("00", CultureInfo.InvariantCulture));
            hour.SelectedIndex = Math.Clamp(entry.Hour, 0, 23);
            Grid.SetColumn(hour, 1);
            row.Children.Add(hour);

            var sep = new TextBlock
            {
                Text = ":",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0)
            };
            Grid.SetColumn(sep, 2);
            row.Children.Add(sep);

            var minute = new ComboBox();
            for (int m = 0; m < 60; m++)
                minute.Items.Add(m.ToString("00", CultureInfo.InvariantCulture));
            minute.SelectedIndex = Math.Clamp(entry.Minute, 0, 59);
            Grid.SetColumn(minute, 3);
            row.Children.Add(minute);

            content.Children.Add(row);
            rows.Add(((int)dow, check, hour, minute));
        }

        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });

        content.Children.Add(new TextBlock
        {
            Text = "Keep standup logs for (days)",
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "0 = never delete; otherwise the number of days to keep.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var retentionBox = new TextBox
        {
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = _standupRetentionDays.ToString(CultureInfo.InvariantCulture)
        };
        content.Children.Add(retentionBox);

        root.Children.Add(content);
        dialog.Content = root;

        bool saved = false;
        btnOk.Click += (_, _) =>
        {
            var retentionRaw = (retentionBox.Text ?? string.Empty).Trim();
            if (!int.TryParse(retentionRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retention)
                || retention < 0
                || retention > MaxStandupRetentionDays)
            {
                MessageBox.Show(
                    dialog,
                    $"Retention must be a whole number between 0 and {MaxStandupRetentionDays}.",
                    "Standup Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                retentionBox.Focus();
                retentionBox.SelectAll();
                return;
            }

            var newTimes = rows
                .Select(row => new StandupWeekdayTime
                {
                    DayOfWeek = row.DayOfWeek,
                    Enabled = row.Check.IsChecked == true,
                    Hour = Math.Clamp(row.Hour.SelectedIndex, 0, 23),
                    Minute = Math.Clamp(row.Minute.SelectedIndex, 0, 59)
                })
                .ToList();

            _standupWeekdayTimes.Clear();
            _standupWeekdayTimes.AddRange(NormalizeStandupWeekdayTimes(newTimes));
            _standupRetentionDays = NormalizeStandupRetentionDays(retention);
            PruneStandupNotes();
            SaveWindowSettings();
            saved = true;
            dialog.DialogResult = true;
        };

        dialog.ShowDialog();
        return saved;
    }
}

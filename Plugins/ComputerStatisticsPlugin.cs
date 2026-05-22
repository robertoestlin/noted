using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Noted.Models;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private const int DefaultComputerStatisticsIdleThresholdSeconds = 100;
    private const int MinComputerStatisticsIdleThresholdSeconds = 1;
    private const int MaxComputerStatisticsIdleThresholdSeconds = 24 * 3600;
    private const int ComputerStatisticsProgramDiscoveryDays = 30;

    private const int DefaultComputerStatisticsAwakeStartHour = 8;
    private const int DefaultComputerStatisticsAwakeEndHour = 0; // 0 == midnight

    // Daily work target — stored in minutes, rounded to 5-minute increments.
    private const int DefaultComputerStatisticsWorkMinutesPerDay = 8 * 60;
    private const int MaxComputerStatisticsWorkMinutesPerDay = 24 * 60;
    private const int ComputerStatisticsWorkMinutesStep = 5;
    // Mon–Fri is the default working week for forecasts (overridable via setting).
    private const int ComputerStatisticsBusinessDaysPerWeek = 5;
    private const int ComputerStatisticsTotalDaysPerWeek = 7;

    private const bool DefaultComputerStatisticsForecastFullWeek = false;
    private const bool DefaultComputerStatisticsWorkTimeIncludesPassive = false;

    // A "day" in this view runs from 04:00 to 04:00 the next morning, so late-night
    // sessions are counted under the day they started on.
    private const int ComputerStatisticsDayStartHour = 4;

    /// <summary>The logical day that a timestamp belongs to (its date at 00:00).</summary>
    private static DateTime ComputerStatisticsDayOf(DateTime t)
        => t.AddHours(-ComputerStatisticsDayStartHour).Date;

    /// <summary>The instant a logical day begins (its date + the day-start hour).</summary>
    private static DateTime ComputerStatisticsDayStart(DateTime dayDate)
        => dayDate.Date.AddHours(ComputerStatisticsDayStartHour);

    private int _computerStatisticsIdleThresholdSeconds = DefaultComputerStatisticsIdleThresholdSeconds;
    private int _computerStatisticsAwakeStartHour = DefaultComputerStatisticsAwakeStartHour;
    private int _computerStatisticsAwakeEndHour = DefaultComputerStatisticsAwakeEndHour;
    private int _computerStatisticsWorkMinutesPerDay = DefaultComputerStatisticsWorkMinutesPerDay;
    private bool _computerStatisticsForecastFullWeek = DefaultComputerStatisticsForecastFullWeek;
    private bool _computerStatisticsWorkTimeIncludesPassive = DefaultComputerStatisticsWorkTimeIncludesPassive;
    private readonly HashSet<string> _computerStatisticsPassiveProgramKeys = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Color ComputerStatisticsActiveColor = Color.FromRgb(0x4C, 0xAF, 0x50);
    private static readonly Color ComputerStatisticsPassiveColor = Color.FromRgb(0x21, 0x96, 0xF3);
    private static readonly Color ComputerStatisticsAwayColor = Color.FromRgb(0xFF, 0x98, 0x00);
    private static readonly Color ComputerStatisticsOfflineColor = Color.FromRgb(0xE0, 0xE0, 0xE0);

    private enum ComputerStatisticsSlotState
    {
        Offline = 0,
        Active = 1,
        Passive = 2,
        Away = 3
    }

    private enum ComputerStatisticsView
    {
        Year,
        Month,
        Week,
        Day
    }

    private void ResetComputerStatisticsSettingsToDefaults()
    {
        _computerStatisticsIdleThresholdSeconds = DefaultComputerStatisticsIdleThresholdSeconds;
        _computerStatisticsAwakeStartHour = DefaultComputerStatisticsAwakeStartHour;
        _computerStatisticsAwakeEndHour = DefaultComputerStatisticsAwakeEndHour;
        _computerStatisticsWorkMinutesPerDay = DefaultComputerStatisticsWorkMinutesPerDay;
        _computerStatisticsForecastFullWeek = DefaultComputerStatisticsForecastFullWeek;
        _computerStatisticsWorkTimeIncludesPassive = DefaultComputerStatisticsWorkTimeIncludesPassive;
        _computerStatisticsPassiveProgramKeys.Clear();
    }

    private void ApplyComputerStatisticsSettings(ComputerStatisticsPluginState? state)
    {
        var idle = state?.IdleThresholdSeconds ?? DefaultComputerStatisticsIdleThresholdSeconds;
        _computerStatisticsIdleThresholdSeconds = Math.Clamp(
            idle,
            MinComputerStatisticsIdleThresholdSeconds,
            MaxComputerStatisticsIdleThresholdSeconds);

        _computerStatisticsAwakeStartHour = Math.Clamp(
            state?.AwakeStartHour ?? DefaultComputerStatisticsAwakeStartHour, 0, 24);
        _computerStatisticsAwakeEndHour = Math.Clamp(
            state?.AwakeEndHour ?? DefaultComputerStatisticsAwakeEndHour, 0, 24);

        var workMinutes = state?.WorkMinutesPerDay ?? DefaultComputerStatisticsWorkMinutesPerDay;
        workMinutes = Math.Clamp(workMinutes, 0, MaxComputerStatisticsWorkMinutesPerDay);
        _computerStatisticsWorkMinutesPerDay =
            (workMinutes / ComputerStatisticsWorkMinutesStep) * ComputerStatisticsWorkMinutesStep;

        _computerStatisticsForecastFullWeek =
            state?.ForecastFullWeek ?? DefaultComputerStatisticsForecastFullWeek;
        _computerStatisticsWorkTimeIncludesPassive =
            state?.WorkTimeIncludesPassive ?? DefaultComputerStatisticsWorkTimeIncludesPassive;

        _computerStatisticsPassiveProgramKeys.Clear();
        if (state?.PassiveProgramKeys == null)
            return;

        foreach (var key in state.PassiveProgramKeys)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _computerStatisticsPassiveProgramKeys.Add(key.Trim());
        }
    }

    // -- Heartbeat parsing & slot classification ----------------------------------------

    private readonly struct ComputerStatisticsHeartbeat
    {
        public DateTime LocalTimestamp { get; init; }
        public int IdleSeconds { get; init; }
        public IReadOnlyList<string> AudioPrograms { get; init; }
        public char Source { get; init; } // 's', 'n', 'u'
        public bool IsStartup { get; init; }
        public bool IsShutdown { get; init; }
    }

    private static readonly Regex ComputerStatisticsAudioProgramRegex =
        new(@"([A-Za-z0-9_\-\.\+ ]+)=([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    private static bool TryParseHeartbeatLine(string line, out ComputerStatisticsHeartbeat parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var commaIndex = line.IndexOf(',');
        if (commaIndex <= 0)
            return false;

        var timestampPart = line.AsSpan(0, commaIndex).ToString().Trim();
        if (!DateTimeOffset.TryParseExact(
                timestampPart,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var ts))
        {
            return false;
        }

        var rest = line.AsSpan(commaIndex + 1);

        int idleSeconds = -1;
        var audio = Array.Empty<string>();
        char source = 'u';
        bool isStartup = false;
        bool isShutdown = false;

        // Manually walk the rest, respecting the {...} block in audio=.
        int cursor = 0;
        while (cursor < rest.Length)
        {
            // Find next "key=" — read up to '='.
            int eq = rest.Slice(cursor).IndexOf('=');
            if (eq < 0)
                break;
            var key = rest.Slice(cursor, eq).ToString().Trim();
            cursor += eq + 1;

            // Read value: either a {...} block or up to next ',' that's outside braces.
            int valueStart = cursor;
            int braceDepth = 0;
            while (cursor < rest.Length)
            {
                var ch = rest[cursor];
                if (ch == '{')
                    braceDepth++;
                else if (ch == '}')
                    braceDepth = Math.Max(0, braceDepth - 1);
                else if (ch == ',' && braceDepth == 0)
                    break;
                cursor++;
            }
            var value = rest.Slice(valueStart, cursor - valueStart).ToString().Trim();
            if (cursor < rest.Length)
                cursor++; // skip the comma

            switch (key)
            {
                case "idle":
                {
                    var raw = value;
                    if (raw.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                        raw = raw[..^1];
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle))
                        idleSeconds = (int)Math.Clamp(idle, 0, MaxComputerStatisticsIdleThresholdSeconds);
                    break;
                }
                case "audio":
                    audio = ExtractAudioPrograms(value);
                    break;
                case "source":
                    if (value.Length > 0)
                        source = char.ToLowerInvariant(value[0]);
                    break;
                case "start":
                    isStartup = value == "1";
                    break;
                case "stop":
                    isShutdown = value == "1";
                    break;
                // ignore "delayed" and other unknown keys
            }
        }

        parsed = new ComputerStatisticsHeartbeat
        {
            LocalTimestamp = ts.LocalDateTime,
            IdleSeconds = idleSeconds < 0 ? int.MaxValue : idleSeconds,
            AudioPrograms = audio,
            Source = source,
            IsStartup = isStartup,
            IsShutdown = isShutdown
        };
        return true;
    }

    private static string[] ExtractAudioPrograms(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "0")
            return Array.Empty<string>();

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            trimmed = trimmed[1..^1];
        if (string.IsNullOrWhiteSpace(trimmed))
            return Array.Empty<string>();

        var matches = ComputerStatisticsAudioProgramRegex.Matches(trimmed);
        if (matches.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value.Trim();
            if (name.Length > 0)
                result.Add(name);
        }
        return result.ToArray();
    }

    private List<ComputerStatisticsHeartbeat> ReadHeartbeatsBetween(DateTime fromLocalInclusive, DateTime toLocalExclusive)
    {
        var results = new List<ComputerStatisticsHeartbeat>(4096);
        if (string.IsNullOrWhiteSpace(_backupFolder) || !Directory.Exists(_backupFolder))
            return results;

        // Walk every monthly file that overlaps the range.
        var monthCursor = new DateTime(fromLocalInclusive.Year, fromLocalInclusive.Month, 1);
        var monthLimit = new DateTime(toLocalExclusive.Year, toLocalExclusive.Month, 1);
        if (toLocalExclusive > monthLimit)
            monthLimit = monthLimit.AddMonths(1);

        while (monthCursor < monthLimit)
        {
            var path = Path.Combine(
                _backupFolder,
                $"{UptimeHeartbeatService.FileNamePrefix}{monthCursor:yyyy-MM}.log");
            if (File.Exists(path))
                AppendHeartbeatsFromFile(path, fromLocalInclusive, toLocalExclusive, results);
            monthCursor = monthCursor.AddMonths(1);
        }

        return results;
    }

    private static void AppendHeartbeatsFromFile(
        string path,
        DateTime fromLocalInclusive,
        DateTime toLocalExclusive,
        List<ComputerStatisticsHeartbeat> sink)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!TryParseHeartbeatLine(line, out var hb))
                    continue;
                if (hb.LocalTimestamp < fromLocalInclusive || hb.LocalTimestamp >= toLocalExclusive)
                    continue;
                sink.Add(hb);
            }
        }
        catch
        {
            // File may be locked or unreadable; skip.
        }
    }

    private List<string> DiscoverRecentAudioPrograms()
    {
        var to = ComputerStatisticsDayStart(ComputerStatisticsDayOf(DateTime.Now).AddDays(1));
        var from = to.AddDays(-ComputerStatisticsProgramDiscoveryDays);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hb in ReadHeartbeatsBetween(from, to))
        {
            foreach (var name in hb.AudioPrograms)
                seen.Add(name);
        }

        return seen
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ComputerStatisticsSlotState ClassifyHeartbeat(in ComputerStatisticsHeartbeat hb)
    {
        if (hb.IdleSeconds < _computerStatisticsIdleThresholdSeconds)
            return ComputerStatisticsSlotState.Active;

        foreach (var program in hb.AudioPrograms)
        {
            if (_computerStatisticsPassiveProgramKeys.Contains(program))
                return ComputerStatisticsSlotState.Passive;
        }

        return ComputerStatisticsSlotState.Away;
    }

    /// <summary>
    /// Bucket heartbeats into fixed-size slots starting at <paramref name="rangeStartLocal"/>.
    /// </summary>
    private ComputerStatisticsSlotState[] BuildSlotStates(
        DateTime rangeStartLocal,
        DateTime rangeEndExclusiveLocal,
        int slotSeconds)
    {
        if (slotSeconds <= 0)
            slotSeconds = 300;

        var totalSeconds = (long)(rangeEndExclusiveLocal - rangeStartLocal).TotalSeconds;
        var slotCount = (int)Math.Max(0, totalSeconds / slotSeconds);
        var states = new ComputerStatisticsSlotState[slotCount];
        // states default to Offline (=0).

        var heartbeats = ReadHeartbeatsBetween(rangeStartLocal, rangeEndExclusiveLocal);
        foreach (var hb in heartbeats)
        {
            var offset = (long)(hb.LocalTimestamp - rangeStartLocal).TotalSeconds;
            if (offset < 0)
                continue;
            var slot = (int)(offset / slotSeconds);
            if (slot < 0 || slot >= slotCount)
                continue;

            var newState = ClassifyHeartbeat(in hb);

            // Multiple beats in one slot: prefer the most-active interpretation.
            // Active > Passive > Away > Offline.
            if (StateRank(newState) > StateRank(states[slot]))
                states[slot] = newState;
        }

        return states;
    }

    private static int StateRank(ComputerStatisticsSlotState state) => state switch
    {
        ComputerStatisticsSlotState.Active => 3,
        ComputerStatisticsSlotState.Passive => 2,
        ComputerStatisticsSlotState.Away => 1,
        _ => 0
    };

    private readonly struct ComputerStatisticsTotals
    {
        public int OfflineSlots { get; init; }
        public int ActiveSlots { get; init; }
        public int PassiveSlots { get; init; }
        public int AwaySlots { get; init; }
        public int TotalSlots => OfflineSlots + ActiveSlots + PassiveSlots + AwaySlots;
    }

    private static ComputerStatisticsTotals AggregateSlots(ReadOnlySpan<ComputerStatisticsSlotState> states)
    {
        int offline = 0, active = 0, passive = 0, away = 0;
        foreach (var s in states)
        {
            switch (s)
            {
                case ComputerStatisticsSlotState.Active: active++; break;
                case ComputerStatisticsSlotState.Passive: passive++; break;
                case ComputerStatisticsSlotState.Away: away++; break;
                default: offline++; break;
            }
        }
        return new ComputerStatisticsTotals
        {
            OfflineSlots = offline,
            ActiveSlots = active,
            PassiveSlots = passive,
            AwaySlots = away
        };
    }

    private static string FormatDurationFromSlots(int slotCount, int slotSeconds)
        => FormatHoursMinutes((long)slotCount * slotSeconds);

    /// <summary>
    /// Today's work seconds — Active (and Passive if the setting is enabled). Computed against the
    /// logical day boundary so a session that started before midnight still belongs to "today".
    /// Re-reads today's slice of the heartbeat log on each call; cheap because a day is small.
    /// </summary>
    private long ComputeTodayWorkSeconds(int slotSeconds)
    {
        var today = ComputerStatisticsDayOf(DateTime.Now);
        var todayStart = ComputerStatisticsDayStart(today);
        var todayEnd = todayStart.AddDays(1);
        var states = BuildSlotStates(todayStart, todayEnd, slotSeconds);

        long active = 0, passive = 0;
        foreach (var s in states)
        {
            if (s == ComputerStatisticsSlotState.Active) active++;
            else if (s == ComputerStatisticsSlotState.Passive) passive++;
        }
        long workSlots = active + (_computerStatisticsWorkTimeIncludesPassive ? passive : 0);
        return workSlots * slotSeconds;
    }

    private static string FormatHoursMinutes(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalDays >= 1)
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}d {1:D2}h {2:D2}m",
                (int)span.TotalDays,
                span.Hours,
                span.Minutes);
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0:D2}h {1:D2}m",
            (int)span.TotalHours,
            span.Minutes);
    }

    // -- UI ----------------------------------------------------------------------------

    private void ShowComputerStatisticsDialog()
    {
        var dialog = new Window
        {
            Title = "Computer Statistics",
            Width = 1300,
            Height = 1000,
            MinWidth = 760,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var statusText = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var viewSelector = new StackPanel { Orientation = Orientation.Horizontal };
        var btnYear = new RadioButton { Content = "Year", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4), GroupName = "ComputerStatsView" };
        var btnMonth = new RadioButton { Content = "Month", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4), GroupName = "ComputerStatsView", IsChecked = true };
        var btnWeek = new RadioButton { Content = "Week", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4), GroupName = "ComputerStatsView" };
        var btnDay = new RadioButton { Content = "Day", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4), GroupName = "ComputerStatsView" };
        viewSelector.Children.Add(btnYear);
        viewSelector.Children.Add(btnMonth);
        viewSelector.Children.Add(btnWeek);
        viewSelector.Children.Add(btnDay);
        Grid.SetColumn(viewSelector, 0);
        headerRow.Children.Add(viewSelector);

        var navPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var btnPrev = new Button { Content = "◀", Width = 30, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
        var btnToday = new Button { Content = "Today", MinWidth = 60, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
        var btnNext = new Button { Content = "▶", Width = 30, Height = 26 };
        navPanel.Children.Add(btnPrev);
        navPanel.Children.Add(btnToday);
        navPanel.Children.Add(btnNext);
        Grid.SetColumn(navPanel, 2);
        headerRow.Children.Add(navPanel);

        // Period title docks left, weekly worked-time label docks right. The worked label is
        // only made visible in the Week view (toggled inside Render()).
        var periodHost = new DockPanel
        {
            Margin = new Thickness(16, 0, 8, 0),
            LastChildFill = true
        };
        Grid.SetColumn(periodHost, 3);
        headerRow.Children.Add(periodHost);

        // Today's running work-time total — pinned to the top-right of every view.
        var todayWorkedLabel = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(ComputerStatisticsActiveColor),
            Margin = new Thickness(12, 0, 0, 0)
        };
        DockPanel.SetDock(todayWorkedLabel, Dock.Right);
        periodHost.Children.Add(todayWorkedLabel);

        var periodTitle = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        periodHost.Children.Add(periodTitle);

        var btnSettings = new Button { Content = "⚙", Width = 30, Height = 28, FontSize = 16, ToolTip = "Settings" };
        Grid.SetColumn(btnSettings, 4);
        headerRow.Children.Add(btnSettings);

        DockPanel.SetDock(headerRow, Dock.Top);
        root.Children.Add(headerRow);

        var totalsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(totalsPanel, Dock.Top);
        root.Children.Add(totalsPanel);

        var legendPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        legendPanel.Children.Add(BuildLegendChip(ComputerStatisticsActiveColor, "Active"));
        legendPanel.Children.Add(BuildLegendChip(ComputerStatisticsPassiveColor, "Passive"));
        legendPanel.Children.Add(BuildLegendChip(ComputerStatisticsAwayColor, "Away"));
        legendPanel.Children.Add(BuildLegendChip(ComputerStatisticsOfflineColor, "Offline"));
        DockPanel.SetDock(legendPanel, Dock.Top);
        root.Children.Add(legendPanel);

        var bodyHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var bodyStack = new StackPanel();
        bodyHost.Content = bodyStack;
        root.Children.Add(bodyHost);

        dialog.Content = root;

        var view = ComputerStatisticsView.Month;
        var anchor = ComputerStatisticsDayOf(DateTime.Now);

        void Render()
        {
            try
            {
                int slotSeconds = _uptimeHeartbeatSeconds > 0 ? _uptimeHeartbeatSeconds : 300;
                (var rangeStart, var rangeEnd, var label) = GetViewRange(view, anchor);
                periodTitle.Text = label;

                var states = BuildSlotStates(rangeStart, rangeEnd, slotSeconds);
                var totals = AggregateSlots(states);

                // Today's total — always shown top-right, regardless of view or anchor.
                long todayWorkSec = ComputeTodayWorkSeconds(slotSeconds);
                todayWorkedLabel.Text = FormatHoursMinutes(todayWorkSec);
                SetInstantTooltip(
                    todayWorkedLabel,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "Today's {0} time ({1:dddd d MMM}).",
                        _computerStatisticsWorkTimeIncludesPassive ? "Active + Passive" : "Active",
                        DateTime.Now));

                totalsPanel.Children.Clear();
                totalsPanel.Children.Add(BuildTotalCard("Active", ComputerStatisticsActiveColor, totals.ActiveSlots, slotSeconds, totals.TotalSlots));
                totalsPanel.Children.Add(BuildTotalCard("Passive", ComputerStatisticsPassiveColor, totals.PassiveSlots, slotSeconds, totals.TotalSlots));
                totalsPanel.Children.Add(BuildTotalCard("Away", ComputerStatisticsAwayColor, totals.AwaySlots, slotSeconds, totals.TotalSlots));
                totalsPanel.Children.Add(BuildTotalCard("Offline", ComputerStatisticsOfflineColor, totals.OfflineSlots, slotSeconds, totals.TotalSlots));

                bodyStack.Children.Clear();
                switch (view)
                {
                    case ComputerStatisticsView.Year:
                        RenderYearView(bodyStack, anchor, states, slotSeconds);
                        break;
                    case ComputerStatisticsView.Month:
                        RenderMonthView(bodyStack, anchor, states, slotSeconds);
                        break;
                    case ComputerStatisticsView.Week:
                        RenderWeekView(bodyStack, anchor, states, slotSeconds);
                        break;
                    case ComputerStatisticsView.Day:
                        RenderDayView(bodyStack, anchor, states, slotSeconds);
                        break;
                }

                statusText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "Slot size: {0:N0}s. Idle threshold: {1}s. Tracked passive programs: {2}. Day boundary: {3:D2}:00. Dashed lines = waking hours {4:D2}:00–{5:D2}:00.",
                    slotSeconds,
                    _computerStatisticsIdleThresholdSeconds,
                    _computerStatisticsPassiveProgramKeys.Count,
                    ComputerStatisticsDayStartHour,
                    _computerStatisticsAwakeStartHour % 24,
                    _computerStatisticsAwakeEndHour % 24);
            }
            catch (Exception ex)
            {
                statusText.Text = "Error rendering statistics: " + ex.Message;
                statusText.Foreground = Brushes.IndianRed;
            }
        }

        btnYear.Checked += (_, _) => { view = ComputerStatisticsView.Year; Render(); };
        btnMonth.Checked += (_, _) => { view = ComputerStatisticsView.Month; Render(); };
        btnWeek.Checked += (_, _) => { view = ComputerStatisticsView.Week; Render(); };
        btnDay.Checked += (_, _) => { view = ComputerStatisticsView.Day; Render(); };
        btnPrev.Click += (_, _) => { anchor = ShiftAnchor(view, anchor, -1); Render(); };
        btnNext.Click += (_, _) => { anchor = ShiftAnchor(view, anchor, +1); Render(); };
        btnToday.Click += (_, _) => { anchor = ComputerStatisticsDayOf(DateTime.Now); Render(); };
        btnSettings.Click += (_, _) =>
        {
            if (ShowComputerStatisticsSettingsDialog(dialog))
                Render();
        };

        Render();
        dialog.ShowDialog();
    }

    private static (DateTime Start, DateTime End, string Label) GetViewRange(ComputerStatisticsView view, DateTime anchor)
    {
        var date = anchor.Date;
        switch (view)
        {
            case ComputerStatisticsView.Year:
            {
                var start = ComputerStatisticsDayStart(new DateTime(date.Year, 1, 1));
                var end = start.AddYears(1);
                return (start, end, date.Year.ToString(CultureInfo.CurrentCulture));
            }
            case ComputerStatisticsView.Month:
            {
                var start = ComputerStatisticsDayStart(new DateTime(date.Year, date.Month, 1));
                var end = start.AddMonths(1);
                return (start, end, date.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
            }
            case ComputerStatisticsView.Week:
            {
                int delta = ((int)date.DayOfWeek + 6) % 7; // Monday=0
                var monday = date.AddDays(-delta);
                var start = ComputerStatisticsDayStart(monday);
                var end = start.AddDays(7);
                return (
                    start,
                    end,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "Week {0}, {1:dd MMM} – {2:dd MMM yyyy}",
                        ISOWeek.GetWeekOfYear(monday),
                        monday,
                        monday.AddDays(6)));
            }
            case ComputerStatisticsView.Day:
            default:
            {
                var start = ComputerStatisticsDayStart(date);
                var end = start.AddDays(1);
                return (start, end, date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture));
            }
        }
    }

    private static DateTime ShiftAnchor(ComputerStatisticsView view, DateTime anchor, int delta) => view switch
    {
        ComputerStatisticsView.Year => anchor.AddYears(delta),
        ComputerStatisticsView.Month => anchor.AddMonths(delta),
        ComputerStatisticsView.Week => anchor.AddDays(7 * delta),
        _ => anchor.AddDays(delta)
    };

    private static UIElement BuildLegendChip(Color color, string label)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Background = new SolidColorBrush(color),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray
        });
        return stack;
    }

    private static UIElement BuildTotalCard(string label, Color color, int slots, int slotSeconds, int totalSlots)
    {
        var pct = totalSlots > 0 ? (slots * 100.0 / totalSlots) : 0.0;
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(10, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(2),
            MinWidth = 140
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.DimGray,
            FontSize = 11
        });
        stack.Children.Add(new TextBlock
        {
            Text = FormatDurationFromSlots(slots, slotSeconds),
            FontWeight = FontWeights.SemiBold,
            FontSize = 18
        });
        stack.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, "{0:0.0}%", pct),
            Foreground = Brushes.DimGray,
            FontSize = 11
        });
        border.Child = stack;
        return border;
    }

    private void RenderYearView(StackPanel host, DateTime anchor, ComputerStatisticsSlotState[] states, int slotSeconds)
    {
        var start = ComputerStatisticsDayStart(new DateTime(anchor.Year, 1, 1));
        var end = start.AddYears(1);

        var perMonth = new List<ComputerStatisticsSlotState>[12];
        var monthStarts = new DateTime[12];
        for (int m = 0; m < 12; m++)
        {
            perMonth[m] = new List<ComputerStatisticsSlotState>();
            monthStarts[m] = ComputerStatisticsDayStart(new DateTime(anchor.Year, m + 1, 1));
        }

        for (int i = 0; i < states.Length; i++)
        {
            var ts = start.AddSeconds((long)i * slotSeconds);
            if (ts >= end) break;
            int month = ComputerStatisticsDayOf(ts).Month - 1;
            if (month < 0 || month >= 12) continue;
            perMonth[month].Add(states[i]);
        }

        host.Children.Add(BuildSectionHeader("Monthly breakdown"));
        for (int m = 0; m < 12; m++)
        {
            var label = string.Format(CultureInfo.CurrentCulture, "{0:MMM yyyy}", monthStarts[m]);
            host.Children.Add(BuildPeriodRow(label, perMonth[m], monthStarts[m], slotSeconds, multiDay: true));
        }
    }

    private void RenderMonthView(StackPanel host, DateTime anchor, ComputerStatisticsSlotState[] states, int slotSeconds)
    {
        var start = ComputerStatisticsDayStart(new DateTime(anchor.Year, anchor.Month, 1));
        var end = start.AddMonths(1);
        int days = (end - start).Days;

        var perDay = new List<ComputerStatisticsSlotState>[days];
        for (int d = 0; d < days; d++)
            perDay[d] = new List<ComputerStatisticsSlotState>();

        for (int i = 0; i < states.Length; i++)
        {
            var ts = start.AddSeconds((long)i * slotSeconds);
            if (ts >= end) break;
            int day = (ts - start).Days;
            if (day < 0 || day >= days) continue;
            perDay[day].Add(states[i]);
        }

        long targetWorkSec = (long)_computerStatisticsWorkMinutesPerDay * 60;
        bool includePassive = _computerStatisticsWorkTimeIncludesPassive;
        bool forecastFullWeek = _computerStatisticsForecastFullWeek;

        var dayWorkSec = new long[days];
        var fullDayAt = new DateTime?[days];
        var dayIsWorkDay = new bool[days];
        for (int d = 0; d < days; d++)
        {
            var dayStart = start.AddDays(d);
            var dow = dayStart.Date.DayOfWeek;
            dayIsWorkDay[d] = forecastFullWeek
                || (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday);
            (dayWorkSec[d], fullDayAt[d]) = ComputeDayWorkAndFullDayAt(
                perDay[d], dayStart, slotSeconds, includePassive, targetWorkSec);
        }

        var stats = ComputeForecastStats(dayWorkSec, start, dayIsWorkDay);

        int maxDayAbbrevLen = 0;
        for (int d = 0; d < days; d++)
        {
            var n = start.AddDays(d).ToString("ddd", CultureInfo.CurrentCulture);
            if (n.Length > maxDayAbbrevLen) maxDayAbbrevLen = n.Length;
        }

        host.Children.Add(BuildSectionHeader("Daily breakdown"));
        for (int d = 0; d < days; d++)
        {
            var dayStart = start.AddDays(d);
            var dayAbbrev = dayStart.ToString("ddd", CultureInfo.CurrentCulture);
            var label = dayAbbrev.PadRight(maxDayAbbrevLen) + "  " + dayStart.ToString("dd", CultureInfo.CurrentCulture);
            host.Children.Add(BuildPeriodRow(
                label, perDay[d], dayStart, slotSeconds, multiDay: false,
                _computerStatisticsAwakeStartHour, _computerStatisticsAwakeEndHour,
                dayIsWorkDay[d] ? fullDayAt[d] : null,
                dayIsWorkDay[d] ? targetWorkSec : 0));
        }

        // Month totals — same shape as the week summary, placed under the last day.
        host.Children.Add(BuildSectionHeader("Month totals", topSpacing: 14));
        host.Children.Add(BuildPeriodSummaryRow(
            periodLabel: "month",
            targetPerDaySec: targetWorkSec,
            forecastFullWeek: forecastFullWeek,
            includePassive: includePassive,
            stats: stats));
    }

    private void RenderWeekView(StackPanel host, DateTime anchor, ComputerStatisticsSlotState[] states, int slotSeconds)
    {
        int delta = ((int)anchor.Date.DayOfWeek + 6) % 7;
        var start = ComputerStatisticsDayStart(anchor.Date.AddDays(-delta));
        var end = start.AddDays(7);

        var perDay = new List<ComputerStatisticsSlotState>[7];
        for (int d = 0; d < 7; d++)
            perDay[d] = new List<ComputerStatisticsSlotState>();

        for (int i = 0; i < states.Length; i++)
        {
            var ts = start.AddSeconds((long)i * slotSeconds);
            if (ts >= end) break;
            int day = (ts - start).Days;
            if (day < 0 || day >= 7) continue;
            perDay[day].Add(states[i]);
        }

        long targetWorkSec = (long)_computerStatisticsWorkMinutesPerDay * 60;
        bool includePassive = _computerStatisticsWorkTimeIncludesPassive;
        bool forecastFullWeek = _computerStatisticsForecastFullWeek;

        var dayWorkSec = new long[7];
        var fullDayAt = new DateTime?[7];
        var dayIsWorkDay = new bool[7];
        for (int d = 0; d < 7; d++)
        {
            // d=0..4 are Mon–Fri; d=5..6 are Sat/Sun. ForecastFullWeek includes weekends.
            dayIsWorkDay[d] = forecastFullWeek || d < ComputerStatisticsBusinessDaysPerWeek;
            (dayWorkSec[d], fullDayAt[d]) = ComputeDayWorkAndFullDayAt(
                perDay[d], start.AddDays(d), slotSeconds, includePassive, targetWorkSec);
        }

        var stats = ComputeForecastStats(dayWorkSec, start, dayIsWorkDay);

        // Pad the longest day name so dates line up in a column (Consolas, fixed-width).
        int maxDayNameLen = 0;
        for (int d = 0; d < 7; d++)
        {
            var n = start.AddDays(d).ToString("dddd", CultureInfo.CurrentCulture);
            if (n.Length > maxDayNameLen) maxDayNameLen = n.Length;
        }

        host.Children.Add(BuildSectionHeader("Daily breakdown"));
        for (int d = 0; d < 7; d++)
        {
            var dayStart = start.AddDays(d);
            var dayName = dayStart.ToString("dddd", CultureInfo.CurrentCulture);
            var dateText = dayStart.ToString("dd MMM", CultureInfo.CurrentCulture);
            var label = dayName.PadRight(maxDayNameLen) + "  " + dateText;
            host.Children.Add(BuildPeriodRow(
                label, perDay[d], dayStart, slotSeconds, multiDay: false,
                _computerStatisticsAwakeStartHour, _computerStatisticsAwakeEndHour,
                dayIsWorkDay[d] ? fullDayAt[d] : null,
                dayIsWorkDay[d] ? targetWorkSec : 0));
        }

        // Summary block goes BELOW Sunday so the week ends with its totals.
        host.Children.Add(BuildSectionHeader("Week totals", topSpacing: 14));
        host.Children.Add(BuildPeriodSummaryRow(
            periodLabel: "week",
            targetPerDaySec: targetWorkSec,
            forecastFullWeek: forecastFullWeek,
            includePassive: includePassive,
            stats: stats));
    }

    /// <summary>
    /// Sum work seconds (Active, optionally + Passive) for one day's slots and report the moment
    /// the daily target was first hit (or null if it wasn't).
    /// </summary>
    private static (long workSec, DateTime? fullDayAt) ComputeDayWorkAndFullDayAt(
        IList<ComputerStatisticsSlotState> daySlots,
        DateTime dayStart,
        int slotSeconds,
        bool includePassive,
        long targetWorkSec)
    {
        long workSec = 0;
        DateTime? fullDayAt = null;
        for (int i = 0; i < daySlots.Count; i++)
        {
            if (!IsWorkState(daySlots[i], includePassive))
                continue;
            workSec += slotSeconds;
            if (targetWorkSec > 0 && fullDayAt == null && workSec >= targetWorkSec)
                fullDayAt = dayStart.AddSeconds((long)(i + 1) * slotSeconds);
        }
        return (workSec, fullDayAt);
    }

    private static bool IsWorkState(ComputerStatisticsSlotState state, bool includePassive)
        => state == ComputerStatisticsSlotState.Active
        || (includePassive && state == ComputerStatisticsSlotState.Passive);

    /// <summary>
    /// Bucket the per-day work seconds into completed/in-progress/future work-day stats and
    /// produce a pace-based forecast. Shared by the Week and Month views so the math is
    /// identical for both periods.
    /// </summary>
    private readonly struct ComputerStatisticsForecastStats
    {
        public long TotalWorkSec { get; init; }
        public long AvgPerWorkdaySec { get; init; }
        public long ForecastSec { get; init; }
        public int WorkdaysInPeriod { get; init; }
        public int CompletedWorkdaysCount { get; init; }
        public int FutureWorkdaysCount { get; init; }
        public bool TodayIsWorkdayInProgress { get; init; }
        public string ForecastBasis { get; init; }
    }

    private static ComputerStatisticsForecastStats ComputeForecastStats(
        long[] dayWorkSec,
        DateTime periodStart,
        bool[] dayIsWorkDay)
    {
        DateTime now = DateTime.Now;
        long completedWorkdayWorkSec = 0;
        int completedWorkdaysCount = 0;
        long todayWorkSec = 0;
        bool todayIsWorkdayInProgress = false;
        int futureWorkdaysCount = 0;
        long nonWorkdayWorkSec = 0;
        int workdaysInPeriod = 0;

        for (int d = 0; d < dayWorkSec.Length; d++)
        {
            var dayStart = periodStart.AddDays(d);
            var dayEnd = dayStart.AddDays(1);
            if (!dayIsWorkDay[d])
            {
                nonWorkdayWorkSec += dayWorkSec[d];
                continue;
            }
            workdaysInPeriod++;
            if (dayEnd <= now)
            {
                completedWorkdayWorkSec += dayWorkSec[d];
                completedWorkdaysCount++;
            }
            else if (dayStart <= now)
            {
                todayWorkSec = dayWorkSec[d];
                todayIsWorkdayInProgress = true;
            }
            else
            {
                futureWorkdaysCount++;
            }
        }

        long avgPerWorkdaySec;
        string forecastBasis;
        if (completedWorkdaysCount > 0)
        {
            avgPerWorkdaySec = completedWorkdayWorkSec / completedWorkdaysCount;
            forecastBasis = string.Format(
                CultureInfo.CurrentCulture,
                "{0} completed work day{1}",
                completedWorkdaysCount,
                completedWorkdaysCount == 1 ? string.Empty : "s");
        }
        else if (todayIsWorkdayInProgress && todayWorkSec > 0)
        {
            avgPerWorkdaySec = todayWorkSec;
            forecastBasis = "today's pace so far (no completed work days yet)";
        }
        else
        {
            avgPerWorkdaySec = 0;
            forecastBasis = "no work-day data yet";
        }

        long todayProjectedSec = todayIsWorkdayInProgress
            ? Math.Max(todayWorkSec, avgPerWorkdaySec)
            : 0;

        long forecastSec = completedWorkdayWorkSec
                         + todayProjectedSec
                         + (long)futureWorkdaysCount * avgPerWorkdaySec
                         + nonWorkdayWorkSec;

        return new ComputerStatisticsForecastStats
        {
            TotalWorkSec = dayWorkSec.Sum(),
            AvgPerWorkdaySec = avgPerWorkdaySec,
            ForecastSec = forecastSec,
            WorkdaysInPeriod = workdaysInPeriod,
            CompletedWorkdaysCount = completedWorkdaysCount,
            FutureWorkdaysCount = futureWorkdaysCount,
            TodayIsWorkdayInProgress = todayIsWorkdayInProgress,
            ForecastBasis = forecastBasis
        };
    }

    private void RenderDayView(StackPanel host, DateTime anchor, ComputerStatisticsSlotState[] states, int slotSeconds)
    {
        var dayDate = anchor.Date;
        var start = ComputerStatisticsDayStart(dayDate);
        var end = start.AddDays(1);

        var perHour = new List<ComputerStatisticsSlotState>[24];
        for (int k = 0; k < 24; k++)
            perHour[k] = new List<ComputerStatisticsSlotState>();

        DateTime? firstOn = null;
        DateTime? lastOnEnd = null;
        int onSlots = 0;
        for (int i = 0; i < states.Length; i++)
        {
            var ts = start.AddSeconds((long)i * slotSeconds);
            if (ts >= end) break;
            int k = (int)Math.Floor((ts - start).TotalHours);
            if (k >= 0 && k < 24)
                perHour[k].Add(states[i]);
            if (states[i] != ComputerStatisticsSlotState.Offline)
            {
                firstOn ??= ts;
                lastOnEnd = ts.AddSeconds(slotSeconds);
                onSlots++;
            }
        }

        host.Children.Add(BuildDaySummaryRow(firstOn, lastOnEnd, onSlots, slotSeconds));

        host.Children.Add(BuildSectionHeader("Timeline"));
        host.Children.Add(BuildPeriodRow(
            dayDate.ToString("ddd dd MMM", CultureInfo.CurrentCulture),
            states,
            start,
            slotSeconds,
            multiDay: false,
            _computerStatisticsAwakeStartHour,
            _computerStatisticsAwakeEndHour));

        host.Children.Add(BuildSectionHeader("Hourly breakdown", topSpacing: 12));
        for (int k = 0; k < 24; k++)
        {
            var hourStart = start.AddHours(k);
            host.Children.Add(BuildPeriodRow(
                hourStart.ToString("HH:mm", CultureInfo.CurrentCulture),
                perHour[k],
                hourStart,
                slotSeconds,
                multiDay: false));
        }
    }

    private static UIElement BuildDaySummaryRow(DateTime? firstOn, DateTime? lastOnEnd, int onSlots, int slotSeconds)
    {
        string text;
        if (firstOn.HasValue && lastOnEnd.HasValue)
        {
            var span = lastOnEnd.Value - firstOn.Value;
            text = string.Format(
                CultureInfo.CurrentCulture,
                "Started {0:HH:mm}  ·  Ended {1:HH:mm}  ·  Span {2:D2}h {3:D2}m  ·  Time on {4}",
                firstOn.Value,
                lastOnEnd.Value,
                (int)span.TotalHours,
                span.Minutes,
                FormatDurationFromSlots(onSlots, slotSeconds));
        }
        else
        {
            text = "No computer activity recorded for this day.";
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(2),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black
            }
        };
    }

    private static UIElement BuildSectionHeader(string text, double topSpacing = 0)
        => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, topSpacing, 0, 6)
        };

    /// <summary>
    /// Summary panel showing total worked, daily-avg-so-far, target, remaining vs target, and a
    /// pace-based forecast for the period (week or month). Tooltips explain how each number is
    /// computed.
    /// </summary>
    private static UIElement BuildPeriodSummaryRow(
        string periodLabel,
        long targetPerDaySec,
        bool forecastFullWeek,
        bool includePassive,
        in ComputerStatisticsForecastStats stats)
    {
        string scopeShort = forecastFullWeek ? "Mon–Sun" : "Mon–Fri";
        string workKind = includePassive ? "Active + Passive" : "Active";
        int workdays = stats.WorkdaysInPeriod;
        long targetSec = (long)workdays * targetPerDaySec;

        var outer = new StackPanel();

        var statsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        statsRow.Children.Add(BuildSummaryStat(
            "Total this " + periodLabel,
            FormatHoursMinutes(stats.TotalWorkSec),
            string.Format(
                CultureInfo.CurrentCulture,
                "Sum of {0} time across the whole {1} so far ({2} work days in scope).",
                workKind,
                periodLabel,
                workdays)));

        statsRow.Children.Add(BuildSummaryStat(
            "Avg / work day (so far)",
            FormatHoursMinutes(stats.AvgPerWorkdaySec),
            string.Format(
                CultureInfo.CurrentCulture,
                "Average {0} time on completed work days this {1}. Based on {2}.",
                workKind,
                periodLabel,
                stats.ForecastBasis)));

        if (targetPerDaySec > 0)
        {
            statsRow.Children.Add(BuildSummaryStat(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Target ({0} × {1})",
                    workdays,
                    FormatHoursMinutes(targetPerDaySec)),
                FormatHoursMinutes(targetSec),
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Daily target ({0}) × {1} work days ({2}) in this {3}.",
                    FormatHoursMinutes(targetPerDaySec),
                    workdays,
                    scopeShort,
                    periodLabel)));

            var remaining = Math.Max(0, targetSec - stats.TotalWorkSec);
            statsRow.Children.Add(BuildSummaryStat(
                "Remaining vs target",
                FormatHoursMinutes(remaining),
                string.Format(
                    CultureInfo.CurrentCulture,
                    "How much more {0} time is needed this {1} to hit the {2} target.",
                    workKind,
                    periodLabel,
                    scopeShort)));
        }

        // forecast = recorded(completed work days)
        //          + projection(today, if in progress)
        //          + future_work_days × avg/work-day
        //          + actuals on non-work days
        string forecastTip = string.Format(
            CultureInfo.CurrentCulture,
            "Forecast = recorded {0} on completed work days "
            + "+ projection for today ({1}) "
            + "+ {2} future work day{3} × avg/work day "
            + "+ actuals on non-work days.\n"
            + "Avg/work day = {4} ({5}).\n"
            + "Today: {6}.\n"
            + "Scope: {7} ({8} work days in this {9}).",
            workKind,
            stats.TodayIsWorkdayInProgress
                ? "the larger of today's actual or the average"
                : "n/a — not a work day in progress",
            stats.FutureWorkdaysCount,
            stats.FutureWorkdaysCount == 1 ? string.Empty : "s",
            FormatHoursMinutes(stats.AvgPerWorkdaySec),
            stats.ForecastBasis,
            stats.TodayIsWorkdayInProgress ? "in progress" : "not a work day in progress",
            scopeShort,
            workdays,
            periodLabel);
        statsRow.Children.Add(BuildSummaryStat(
            "Forecast",
            FormatHoursMinutes(stats.ForecastSec),
            forecastTip));

        outer.Children.Add(statsRow);

        string forecastSummary = string.Format(
            CultureInfo.CurrentCulture,
            "Forecast scope: {0} ({1} work days in this {2}). Counting {3} as work time. Basis: {4}.",
            scopeShort,
            workdays,
            periodLabel,
            workKind,
            stats.ForecastBasis);
        outer.Children.Add(new TextBlock
        {
            Text = forecastSummary,
            Foreground = Brushes.DimGray,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(2),
            Child = outer
        };
    }

    private static UIElement BuildSummaryStat(string label, string value, string tooltip)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        s.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.DimGray,
            FontSize = 11
        });
        s.Children.Add(new TextBlock
        {
            Text = value,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Foreground = Brushes.Black
        });
        SetInstantTooltip(s, tooltip);
        return s;
    }

    /// <summary>
    /// A chronological strip for one period: each slot keeps its real position in
    /// time, so a gap (Offline) in the middle shows where it actually happened.
    /// Hovering a block shows its time range. The right-hand label is the on-window
    /// (first activity – last activity) or, for multi-day rows, the total time on.
    /// When <paramref name="dayGuideStartHour"/>/<paramref name="dayGuideEndHour"/>
    /// are set (>= 0) the strip gets dashed vertical lines marking the waking day.
    /// </summary>
    private static UIElement BuildPeriodRow(
        string label,
        IList<ComputerStatisticsSlotState> slots,
        DateTime rowStart,
        int slotSeconds,
        bool multiDay,
        int dayGuideStartHour = -1,
        int dayGuideEndHour = -1,
        DateTime? fullDayAt = null,
        long targetWorkSeconds = 0)
    {
        // Wide enough to fit "Wednesday  21 May" without ellipsis in the Week view.
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        const double stripHeight = 18;
        var stripHost = new Grid
        {
            Height = stripHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA))
        };

        var strip = new Grid();

        int n = slots.Count;
        int onSlots = 0;
        int firstOnSlot = -1, lastOnSlot = -1;
        int colIndex = 0;

        // Collapse contiguous same-state slots into runs; one strip column per run,
        // weighted by run length so positions stay proportional to real time.
        int p = 0;
        while (p < n)
        {
            int q = p;
            while (q + 1 < n && slots[q + 1] == slots[p]) q++;
            var state = slots[p];
            int len = q - p + 1;

            if (state != ComputerStatisticsSlotState.Offline)
            {
                onSlots += len;
                if (firstOnSlot < 0) firstOnSlot = p;
                lastOnSlot = q;
            }

            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(len, GridUnitType.Star) });
            var color = state switch
            {
                ComputerStatisticsSlotState.Active => ComputerStatisticsActiveColor,
                ComputerStatisticsSlotState.Passive => ComputerStatisticsPassiveColor,
                ComputerStatisticsSlotState.Away => ComputerStatisticsAwayColor,
                _ => ComputerStatisticsOfflineColor
            };
            var segStart = rowStart.AddSeconds((long)p * slotSeconds);
            var segEnd = rowStart.AddSeconds((long)(q + 1) * slotSeconds);
            var seg = new Border { Background = new SolidColorBrush(color) };
            SetInstantTooltip(seg, string.Format(
                CultureInfo.CurrentCulture,
                "{0} – {1}  ·  {2} ({3})",
                FormatRowTime(segStart, multiDay),
                FormatRowTime(segEnd, multiDay),
                StateDisplayName(state),
                FormatDurationFromSlots(len, slotSeconds)));
            Grid.SetColumn(seg, colIndex++);
            strip.Children.Add(seg);

            p = q + 1;
        }

        stripHost.Children.Add(strip);

        if (dayGuideStartHour >= 0 && dayGuideEndHour >= 0)
        {
            var overlay = new Grid { IsHitTestVisible = false };
            for (int c = 0; c < 24; c++)
                overlay.ColumnDefinitions.Add(new ColumnDefinition());
            AddDayGuideLine(overlay, dayGuideStartHour, stripHeight);
            AddDayGuideLine(overlay, dayGuideEndHour, stripHeight);
            stripHost.Children.Add(overlay);
        }

        var onDur = FormatDurationFromSlots(onSlots, slotSeconds);
        var showOnDuration = !multiDay && rowStart <= DateTime.Now;

        var leftLabelHost = new Grid();
        leftLabelHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        leftLabelHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftLabelHost, 0);
        grid.Children.Add(leftLabelHost);

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Consolas, Courier New"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelText, 0);
        leftLabelHost.Children.Add(labelText);

        var onDurationLabel = new TextBlock
        {
            Text = showOnDuration ? onDur : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (showOnDuration)
            SetInstantTooltip(onDurationLabel, "Time on: " + onDur);
        Grid.SetColumn(onDurationLabel, 1);
        leftLabelHost.Children.Add(onDurationLabel);

        Grid.SetColumn(stripHost, 1);
        grid.Children.Add(stripHost);

        string rightText;
        string? rightTip = null;
        if (!showOnDuration)
        {
            // The period hasn't happened yet — leave it blank rather than "off".
            rightText = string.Empty;
        }
        else if (multiDay)
        {
            rightText = "On: " + onDur;
        }
        else if (firstOnSlot >= 0)
        {
            var ws = rowStart.AddSeconds((long)firstOnSlot * slotSeconds);
            var we = rowStart.AddSeconds((long)(lastOnSlot + 1) * slotSeconds);
            rightText = string.Format(CultureInfo.CurrentCulture, "{0:HH:mm} – {1:HH:mm}", ws, we);
            rightTip = string.Format(
                CultureInfo.CurrentCulture,
                "On from {0:HH:mm} to {1:HH:mm}. Time on (excluding gaps): {2}.",
                ws, we, onDur);
        }
        else
        {
            rightText = n == 0 ? "—" : "off";
        }

        var rightLabel = new TextBlock
        {
            Text = rightText,
            Foreground = Brushes.DimGray,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        if (rightTip != null)
            SetInstantTooltip(rightLabel, rightTip);

        var rightHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        rightHost.Children.Add(rightLabel);

        if (targetWorkSeconds > 0 && showOnDuration && fullDayAt.HasValue)
        {
            var fdLabel = new TextBlock
            {
                Text = "Full day @ " + fullDayAt.Value.ToString("HH:mm", CultureInfo.CurrentCulture),
                Foreground = new SolidColorBrush(ComputerStatisticsActiveColor),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            SetInstantTooltip(fdLabel, string.Format(
                CultureInfo.CurrentCulture,
                "Cumulative work time hit the daily target of {0} at {1:HH:mm}.",
                FormatHoursMinutes(targetWorkSeconds),
                fullDayAt.Value));
            rightHost.Children.Add(fdLabel);
        }

        Grid.SetColumn(rightHost, 2);
        grid.Children.Add(rightHost);

        return grid;
    }

    private static void AddDayGuideLine(Grid overlay24, int clockHour, double height)
    {
        int h = ((clockHour % 24) + 24) % 24;
        int offset = h >= ComputerStatisticsDayStartHour
            ? h - ComputerStatisticsDayStartHour
            : h + (24 - ComputerStatisticsDayStartHour);
        if (offset < 0 || offset > 23)
            return;

        var line = new System.Windows.Shapes.Line
        {
            X1 = 0,
            X2 = 0,
            Y1 = 0,
            Y2 = height,
            Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        Grid.SetColumn(line, offset);
        overlay24.Children.Add(line);
    }

    private static void SetInstantTooltip(FrameworkElement element, object tip)
    {
        element.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(element, 0);
        ToolTipService.SetBetweenShowDelay(element, 0);
        ToolTipService.SetShowDuration(element, 30000);
    }

    private static string FormatRowTime(DateTime t, bool withDate)
        => withDate
            ? t.ToString("d MMM HH:mm", CultureInfo.CurrentCulture)
            : t.ToString("HH:mm", CultureInfo.CurrentCulture);

    private static string StateDisplayName(ComputerStatisticsSlotState state) => state switch
    {
        ComputerStatisticsSlotState.Active => "Active",
        ComputerStatisticsSlotState.Passive => "Passive",
        ComputerStatisticsSlotState.Away => "Away",
        _ => "Offline"
    };

    private bool ShowComputerStatisticsSettingsDialog(Window owner)
    {
        var dialog = new Window
        {
            Title = "Computer Statistics Settings",
            Width = 520,
            Height = 800,
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

        var content = new DockPanel();

        var topStack = new StackPanel();
        topStack.Children.Add(new TextBlock
        {
            Text = "Idle threshold (seconds)",
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = "When a slot's idle time is below this, it counts as Active. At or above, the slot is Passive (if a tracked program plays sound) or Away.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var idleBox = new TextBox
        {
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = _computerStatisticsIdleThresholdSeconds.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 0, 12)
        };
        topStack.Children.Add(idleBox);

        topStack.Children.Add(new TextBlock
        {
            Text = "Waking hours",
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = "Dashed guide lines on the timelines mark your typical waking day — the hour it usually starts and the hour it ends (e.g. 8 to 0 for 08:00 until midnight). Use 0 (or 24) for midnight.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var awakeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        awakeRow.Children.Add(new TextBlock
        {
            Text = "Awake from hour",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var awakeStartBox = new TextBox
        {
            Width = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Text = _computerStatisticsAwakeStartHour.ToString(CultureInfo.InvariantCulture)
        };
        awakeRow.Children.Add(awakeStartBox);
        awakeRow.Children.Add(new TextBlock
        {
            Text = "to hour",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0)
        });
        var awakeEndBox = new TextBox
        {
            Width = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Text = _computerStatisticsAwakeEndHour.ToString(CultureInfo.InvariantCulture)
        };
        awakeRow.Children.Add(awakeEndBox);
        awakeRow.Children.Add(new TextBlock
        {
            Text = "  (0–24)",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        });
        topStack.Children.Add(awakeRow);

        topStack.Children.Add(new TextBlock
        {
            Text = "Daily work target",
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = "How long a full working day is. Used by the Week view to forecast the week and to mark the moment cumulative Active time hits a full day.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var workTargetRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var currentWorkHours = _computerStatisticsWorkMinutesPerDay / 60;
        var currentWorkMinutes = _computerStatisticsWorkMinutesPerDay % 60;
        currentWorkMinutes = (currentWorkMinutes / ComputerStatisticsWorkMinutesStep) * ComputerStatisticsWorkMinutesStep;

        var workHoursBox = new ComboBox
        {
            Width = 56,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (int h = 0; h <= 23; h++)
            workHoursBox.Items.Add(h);
        workHoursBox.SelectedItem = Math.Clamp(currentWorkHours, 0, 23);

        var workMinutesBox = new ComboBox
        {
            Width = 56,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (int m = 0; m < 60; m += ComputerStatisticsWorkMinutesStep)
            workMinutesBox.Items.Add(m);
        workMinutesBox.SelectedItem = currentWorkMinutes;

        workTargetRow.Children.Add(workHoursBox);
        workTargetRow.Children.Add(new TextBlock
        {
            Text = "h",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 0)
        });
        workTargetRow.Children.Add(workMinutesBox);
        workTargetRow.Children.Add(new TextBlock
        {
            Text = "m",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        });
        workTargetRow.Children.Add(new TextBlock
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                "  (minutes snap to {0}-minute steps)",
                ComputerStatisticsWorkMinutesStep),
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        });
        topStack.Children.Add(workTargetRow);

        topStack.Children.Add(new TextBlock
        {
            Text = "Week view forecast",
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = "Controls the scope used by the Week view totals, target and forecast, "
                 + "and whether Passive time (idle with tracked audio playing) counts as work.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var forecastFullWeekCheck = new CheckBox
        {
            Content = "Forecast over the full week (Mon–Sun, 7 days)",
            IsChecked = _computerStatisticsForecastFullWeek,
            Margin = new Thickness(0, 0, 0, 4)
        };
        SetInstantTooltip(forecastFullWeekCheck,
            "Unchecked: business days only (Mon–Fri, 5 days). Checked: all 7 days.");
        topStack.Children.Add(forecastFullWeekCheck);

        var includePassiveCheck = new CheckBox
        {
            Content = "Include Passive time as work time (top-right total, forecast, full-day marker)",
            IsChecked = _computerStatisticsWorkTimeIncludesPassive,
            Margin = new Thickness(0, 0, 0, 12)
        };
        SetInstantTooltip(includePassiveCheck,
            "Unchecked: only Active counts as work. Checked: Active + Passive count as work.");
        topStack.Children.Add(includePassiveCheck);

        topStack.Children.Add(new TextBlock
        {
            Text = "Tracked programs (Passive)",
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                "Programs detected from heartbeat audio in the last {0} days. Tick the ones whose sound should mark a slot as Passive (e.g., music players).",
                ComputerStatisticsProgramDiscoveryDays),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        DockPanel.SetDock(topStack, Dock.Top);
        content.Children.Add(topStack);

        var programsList = new ListBox
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(programsList, ScrollBarVisibility.Auto);

        var detected = DiscoverRecentAudioPrograms();
        // Make sure we also surface any keys that were saved earlier but aren't in the recent set.
        var allPrograms = new List<string>(detected);
        foreach (var existing in _computerStatisticsPassiveProgramKeys)
        {
            if (!allPrograms.Contains(existing, StringComparer.OrdinalIgnoreCase))
                allPrograms.Add(existing);
        }
        allPrograms.Sort(StringComparer.OrdinalIgnoreCase);

        var checkboxes = new List<CheckBox>(allPrograms.Count);
        if (allPrograms.Count == 0)
        {
            programsList.Items.Add(new TextBlock
            {
                Text = "No audio sessions detected yet. Use the computer for a while and check back.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(8)
            });
        }
        else
        {
            foreach (var name in allPrograms)
            {
                var cb = new CheckBox
                {
                    Content = name,
                    IsChecked = _computerStatisticsPassiveProgramKeys.Contains(name),
                    Margin = new Thickness(2),
                    Tag = name
                };
                checkboxes.Add(cb);
                programsList.Items.Add(cb);
            }
        }
        content.Children.Add(programsList);

        root.Children.Add(content);
        dialog.Content = root;

        bool saved = false;
        btnOk.Click += (_, _) =>
        {
            var rawIdle = (idleBox.Text ?? string.Empty).Trim();
            if (!int.TryParse(rawIdle, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle)
                || idle < MinComputerStatisticsIdleThresholdSeconds
                || idle > MaxComputerStatisticsIdleThresholdSeconds)
            {
                MessageBox.Show(
                    dialog,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "Idle threshold must be between {0} and {1} seconds.",
                        MinComputerStatisticsIdleThresholdSeconds,
                        MaxComputerStatisticsIdleThresholdSeconds),
                    "Computer Statistics Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                idleBox.Focus();
                idleBox.SelectAll();
                return;
            }

            if (!int.TryParse((awakeStartBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var awakeStart)
                || awakeStart < 0 || awakeStart > 24
                || !int.TryParse((awakeEndBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var awakeEnd)
                || awakeEnd < 0 || awakeEnd > 24)
            {
                MessageBox.Show(
                    dialog,
                    "Waking hours must each be a whole number from 0 to 24.",
                    "Computer Statistics Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                awakeStartBox.Focus();
                awakeStartBox.SelectAll();
                return;
            }

            int workHours = workHoursBox.SelectedItem is int h ? h : DefaultComputerStatisticsWorkMinutesPerDay / 60;
            int workMinutes = workMinutesBox.SelectedItem is int m ? m : 0;
            workMinutes = (workMinutes / ComputerStatisticsWorkMinutesStep) * ComputerStatisticsWorkMinutesStep;
            var workTotalMinutes = Math.Clamp(
                workHours * 60 + workMinutes,
                0,
                MaxComputerStatisticsWorkMinutesPerDay);

            _computerStatisticsIdleThresholdSeconds = idle;
            _computerStatisticsAwakeStartHour = awakeStart;
            _computerStatisticsAwakeEndHour = awakeEnd;
            _computerStatisticsWorkMinutesPerDay = workTotalMinutes;
            _computerStatisticsForecastFullWeek = forecastFullWeekCheck.IsChecked == true;
            _computerStatisticsWorkTimeIncludesPassive = includePassiveCheck.IsChecked == true;
            _computerStatisticsPassiveProgramKeys.Clear();
            foreach (var cb in checkboxes)
            {
                if (cb.IsChecked == true && cb.Tag is string name && !string.IsNullOrWhiteSpace(name))
                    _computerStatisticsPassiveProgramKeys.Add(name);
            }
            SaveWindowSettings();
            saved = true;
            dialog.DialogResult = true;
        };

        dialog.ShowDialog();
        return saved;
    }
}

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
    {
        var totalSeconds = (long)slotCount * slotSeconds;
        var span = TimeSpan.FromSeconds(totalSeconds);
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
            Width = 1100,
            Height = 720,
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

        var periodTitle = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(periodTitle, 3);
        headerRow.Children.Add(periodTitle);

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

        host.Children.Add(BuildSectionHeader("Daily breakdown"));
        for (int d = 0; d < days; d++)
        {
            var dayStart = start.AddDays(d);
            var label = string.Format(CultureInfo.CurrentCulture, "{0:ddd}  {1:dd}", dayStart, dayStart);
            host.Children.Add(BuildPeriodRow(
                label, perDay[d], dayStart, slotSeconds, multiDay: false,
                _computerStatisticsAwakeStartHour, _computerStatisticsAwakeEndHour));
        }
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

        host.Children.Add(BuildSectionHeader("Daily breakdown"));
        for (int d = 0; d < 7; d++)
        {
            var dayStart = start.AddDays(d);
            var label = string.Format(CultureInfo.CurrentCulture, "{0:dddd}  {1:dd MMM}", dayStart, dayStart);
            host.Children.Add(BuildPeriodRow(
                label, perDay[d], dayStart, slotSeconds, multiDay: false,
                _computerStatisticsAwakeStartHour, _computerStatisticsAwakeEndHour));
        }
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
        int dayGuideEndHour = -1)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Consolas, Courier New"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

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

        Grid.SetColumn(stripHost, 1);
        grid.Children.Add(stripHost);

        string rightText;
        string? rightTip = null;
        var onDur = FormatDurationFromSlots(onSlots, slotSeconds);
        if (rowStart > DateTime.Now)
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
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        if (rightTip != null)
            SetInstantTooltip(rightLabel, rightTip);
        Grid.SetColumn(rightLabel, 2);
        grid.Children.Add(rightLabel);

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
            Width = 460,
            Height = 660,
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

            _computerStatisticsIdleThresholdSeconds = idle;
            _computerStatisticsAwakeStartHour = awakeStart;
            _computerStatisticsAwakeEndHour = awakeEnd;
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

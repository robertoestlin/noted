using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static string ToMonthKey(DateTime monthStart)
        => monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateTime StartOfWeekMonday(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private static string ToDateKey(DateTime date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseHours(string text, out double hours)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out hours))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out hours))
            return true;
        return false;
    }

    private static bool TryNormalizeTimeReportDayValue(string text, out string normalized)
    {
        normalized = string.Empty;
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (string.Equals(raw, "x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "X";
            return true;
        }

        if (!TryParseHours(raw, out var parsedHours) || parsedHours < 0 || parsedHours > 24)
            return false;

        var rounded = Math.Round(parsedHours, 2, MidpointRounding.AwayFromZero);
        normalized = rounded.ToString("0.##", CultureInfo.InvariantCulture);
        return true;
    }

    private static string FormatTimeReportDayValueForDisplay(string value)
    {
        if (string.Equals(value, "X", StringComparison.OrdinalIgnoreCase))
            return "X";

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed.ToString("0.##", CultureInfo.CurrentCulture);
        }

        return value;
    }

    private void ShowTimeReportDialog()
    {
        var dialog = new Window
        {
            Title = "Time Reporter",
            Width = 780,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnPrevMonth = new Button { Content = "<", Width = 34, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var btnNextMonth = new Button { Content = ">", Width = 34, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        var monthTitle = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var btnDeleteMonth = new Button
        {
            Content = "Remove Month Report",
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(btnPrevMonth, 0);
        Grid.SetColumn(monthTitle, 1);
        Grid.SetColumn(btnDeleteMonth, 3);
        Grid.SetColumn(btnNextMonth, 4);
        header.Children.Add(btnPrevMonth);
        header.Children.Add(monthTitle);
        header.Children.Add(btnDeleteMonth);
        header.Children.Add(btnNextMonth);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var contentPanel = new StackPanel();
        scroll.Content = contentPanel;
        root.Children.Add(scroll);

        var closePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var closeButton = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true,
            IsDefault = true
        };
        closePanel.Children.Add(closeButton);
        DockPanel.SetDock(closePanel, Dock.Bottom);
        root.Children.Add(closePanel);

        closeButton.Click += (_, _) => dialog.Close();

        dialog.Content = root;

        var today = DateTime.Today;
        var currentMonth = StartOfMonth(today);

        void SaveAndPruneMonth(string monthKey, TimeReportMonthState monthState)
        {
            if (monthState.DayValues.Count == 0 && monthState.WeekComments.Count == 0)
            {
                _timeReports.Remove(monthKey);
                SaveWindowSettings();
                return;
            }

            _timeReports[monthKey] = monthState;
            PruneTimeReportMonth(monthKey);
            SaveWindowSettings();
        }

        void RenderMonth()
        {
            var monthKey = ToMonthKey(currentMonth);
            if (!_timeReports.TryGetValue(monthKey, out var monthState))
            {
                monthState = new TimeReportMonthState();
                _timeReports[monthKey] = monthState;
            }

            monthTitle.Text = currentMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            contentPanel.Children.Clear();

            var monthStart = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var firstWeekStart = StartOfWeekMonday(monthStart);

            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            var weekColumnLabel = new TextBlock
            {
                Text = "Week",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(weekColumnLabel, 0);
            headerRow.Children.Add(weekColumnLabel);

            var dayNamesGrid = new UniformGrid { Columns = 7, Margin = new Thickness(6, 0, 6, 0) };
            var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            for (int i = 0; i < 7; i++)
            {
                var dayName = dayNames[(i + 1) % 7]; // Monday first
                dayNamesGrid.Children.Add(new TextBlock
                {
                    Text = dayName,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                });
            }
            Grid.SetColumn(dayNamesGrid, 1);
            headerRow.Children.Add(dayNamesGrid);

            var commentColumnLabel = new TextBlock
            {
                Text = "Weekly comment",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(commentColumnLabel, 2);
            headerRow.Children.Add(commentColumnLabel);

            contentPanel.Children.Add(headerRow);

            var weeksContainer = new StackPanel();
            var weeksBorder = new Border
            {
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8)
            };
            weeksBorder.Child = weeksContainer;
            contentPanel.Children.Add(weeksBorder);

            for (var weekStart = firstWeekStart; weekStart <= monthEnd; weekStart = weekStart.AddDays(7))
            {
                var weekEnd = weekStart.AddDays(6);
                var commentKey = ToDateKey(weekStart);
                var weekRow = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 8)
                };
                weekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                weekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                weekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

                var weekText = new TextBlock
                {
                    Text = $"Week {ISOWeek.GetWeekOfYear(weekStart)} ({weekStart:dd MMM} - {weekEnd:dd MMM})",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(weekText, 0);
                weekRow.Children.Add(weekText);

                var daysGrid = new UniformGrid
                {
                    Columns = 7,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                for (int offset = 0; offset < 7; offset++)
                {
                    var date = weekStart.AddDays(offset);
                    bool inCurrentMonth = date.Month == currentMonth.Month && date.Year == currentMonth.Year;
                    bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    int dayOfMonth = date.Day;

                    var cell = new Border
                    {
                        BorderBrush = Brushes.Gainsboro,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(1),
                        Padding = new Thickness(4),
                        Background = date.Date == today && inCurrentMonth
                            ? new SolidColorBrush(Color.FromRgb(255, 245, 199))
                            : inCurrentMonth && isWeekend
                                ? new SolidColorBrush(Color.FromRgb(255, 236, 236))
                                : Brushes.Transparent,
                        Opacity = inCurrentMonth ? 1.0 : 0.45
                    };

                    var dayPanel = new StackPanel();
                    var dayLabel = new TextBlock
                    {
                        Text = date.ToString("dd", CultureInfo.CurrentCulture),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontWeight = date.Date == today && inCurrentMonth ? FontWeights.Bold : FontWeights.Normal
                    };
                    var hoursBox = new TextBox
                    {
                        Width = 44,
                        MinWidth = 44,
                        Margin = new Thickness(0, 2, 0, 0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        ToolTip = "Hours (0-24) or X",
                        IsEnabled = inCurrentMonth
                    };

                    if (inCurrentMonth && monthState.DayValues.TryGetValue(dayOfMonth, out var existingDayValue))
                        hoursBox.Text = FormatTimeReportDayValueForDisplay(existingDayValue);

                    void PersistHours()
                    {
                        if (!inCurrentMonth)
                            return;

                        var raw = (hoursBox.Text ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            monthState.DayValues.Remove(dayOfMonth);
                            hoursBox.ClearValue(TextBox.BorderBrushProperty);
                            SaveAndPruneMonth(monthKey, monthState);
                            return;
                        }

                        if (!TryNormalizeTimeReportDayValue(raw, out var normalizedValue))
                        {
                            hoursBox.BorderBrush = Brushes.IndianRed;
                            return;
                        }

                        monthState.DayValues[dayOfMonth] = normalizedValue;
                        hoursBox.Text = FormatTimeReportDayValueForDisplay(normalizedValue);
                        hoursBox.ClearValue(TextBox.BorderBrushProperty);
                        SaveAndPruneMonth(monthKey, monthState);
                    }

                    hoursBox.LostFocus += (_, _) => PersistHours();
                    hoursBox.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            e.Handled = true;
                            PersistHours();
                        }
                    };

                    dayPanel.Children.Add(dayLabel);
                    dayPanel.Children.Add(hoursBox);
                    cell.Child = dayPanel;
                    daysGrid.Children.Add(cell);
                }
                Grid.SetColumn(daysGrid, 1);
                weekRow.Children.Add(daysGrid);

                var commentBox = new TextBox
                {
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MinHeight = 58
                };
                if (monthState.WeekComments.TryGetValue(commentKey, out var existingComment))
                    commentBox.Text = existingComment;

                commentBox.LostFocus += (_, _) =>
                {
                    var text = (commentBox.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        monthState.WeekComments.Remove(commentKey);
                    else
                        monthState.WeekComments[commentKey] = text;
                    SaveAndPruneMonth(monthKey, monthState);
                };
                Grid.SetColumn(commentBox, 2);
                weekRow.Children.Add(commentBox);

                weeksContainer.Children.Add(weekRow);
            }

            btnDeleteMonth.IsEnabled = monthState.DayValues.Count > 0 || monthState.WeekComments.Count > 0;
        }

        btnPrevMonth.Click += (_, _) =>
        {
            currentMonth = currentMonth.AddMonths(-1);
            RenderMonth();
        };

        btnNextMonth.Click += (_, _) =>
        {
            currentMonth = currentMonth.AddMonths(1);
            RenderMonth();
        };

        btnDeleteMonth.Click += (_, _) =>
        {
            var monthKey = ToMonthKey(currentMonth);
            if (!_timeReports.ContainsKey(monthKey))
                return;

            var result = MessageBox.Show(
                $"Remove all time report data for {currentMonth:MMMM yyyy}?",
                "Remove Month Report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            _timeReports.Remove(monthKey);
            SaveWindowSettings();
            RenderMonth();
        };

        RenderMonth();
        dialog.ShowDialog();
    }
}

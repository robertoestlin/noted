using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Noted;

public partial class MainWindow
{
    private static readonly string[] ClockOverlayDayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private static readonly SolidColorBrush ClockOverlayDayInactiveFg = new(Color.FromRgb(0x6E, 0x86, 0xA8));
    private static readonly SolidColorBrush ClockOverlayDayActiveFg = new(Color.FromRgb(0xDE, 0xF7, 0xFF));
    private static readonly SolidColorBrush ClockOverlayDayInactiveBorder = new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ClockOverlayDayActiveBorder = new(Color.FromRgb(0x62, 0xE5, 0xFF));
    private static readonly SolidColorBrush ClockOverlayDayActiveBg = new(Color.FromRgb(0x10, 0x5A, 0x6E));

    private DispatcherTimer? _clockOverlayTimer;
    private Border[]? _clockOverlayDayChips;
    private TextBlock[]? _clockOverlayDayCells;

    private void ToggleClockOverlay()
    {
        if (ClockOverlay.Visibility == Visibility.Visible)
            HideClockOverlay();
        else
            ShowClockOverlay();
    }

    private void ShowClockOverlay()
    {
        EnsureClockOverlayDayStrip();
        UpdateClockOverlay();

        _clockOverlayTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _clockOverlayTimer.Tick -= ClockOverlayTimer_Tick;
        _clockOverlayTimer.Tick += ClockOverlayTimer_Tick;
        _clockOverlayTimer.Start();

        ClockOverlay.Visibility = Visibility.Visible;
        ClockOverlay.Focus();
        Keyboard.Focus(ClockOverlay);
    }

    private void HideClockOverlay()
    {
        _clockOverlayTimer?.Stop();
        ClockOverlay.Visibility = Visibility.Collapsed;
    }

    private void ClockOverlayTimer_Tick(object? sender, EventArgs e) => UpdateClockOverlay();

    private void EnsureClockOverlayDayStrip()
    {
        if (_clockOverlayDayChips != null)
            return;

        ClockOverlayDayStrip.Children.Clear();
        _clockOverlayDayChips = new Border[7];
        _clockOverlayDayCells = new TextBlock[7];

        for (var i = 0; i < 7; i++)
        {
            var label = new TextBlock
            {
                Text = ClockOverlayDayLabels[i],
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                Foreground = ClockOverlayDayInactiveFg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var chip = new Border
            {
                Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
                Padding = new Thickness(18, 8, 18, 10),
                CornerRadius = new CornerRadius(12),
                MinWidth = 92,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = ClockOverlayDayInactiveBorder,
                Child = label
            };

            _clockOverlayDayChips[i] = chip;
            _clockOverlayDayCells[i] = label;
            ClockOverlayDayStrip.Children.Add(chip);
        }
    }

    private void UpdateClockOverlay()
    {
        var now = DateTime.Now;

        ClockOverlayTime.Text = now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        ClockOverlayDate.Text = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var monthName = now.ToString("MMMM", CultureInfo.InvariantCulture);
        var week = ISOWeek.GetWeekOfYear(now);
        var dayName = now.ToString("dddd", CultureInfo.InvariantCulture);
        ClockOverlayMonthWeek.Text = $"{monthName}  ·  Week {week}  ·  {dayName}";

        if (_clockOverlayDayChips == null || _clockOverlayDayCells == null)
            return;

        // DateTime.DayOfWeek: Sunday = 0 .. Saturday = 6; the strip is Mon..Sun (index 0..6).
        var todayIndex = ((int)now.DayOfWeek + 6) % 7;
        for (var i = 0; i < 7; i++)
        {
            var isToday = i == todayIndex;
            _clockOverlayDayChips[i].Background = isToday ? ClockOverlayDayActiveBg : Brushes.Transparent;
            _clockOverlayDayChips[i].BorderBrush = isToday ? ClockOverlayDayActiveBorder : ClockOverlayDayInactiveBorder;
            _clockOverlayDayCells[i].Foreground = isToday ? ClockOverlayDayActiveFg : ClockOverlayDayInactiveFg;
            _clockOverlayDayCells[i].FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold;
        }
    }

    private void ClockOverlay_DismissByMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideClockOverlay();
        e.Handled = true;
    }

    private void ClockOverlay_DismissByKeyDown(object sender, KeyEventArgs e)
    {
        // Any key dismisses the clock overlay.
        HideClockOverlay();
        e.Handled = true;
    }
}

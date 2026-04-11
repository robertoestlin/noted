using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static bool TryParseIso8601Utc(string? text, out DateTimeOffset utcDateTime)
    {
        utcDateTime = default;
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out utcDateTime);
    }

    private static bool TryParseUnixTime(string? text, out DateTimeOffset utcDateTime)
    {
        utcDateTime = default;
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fractionalSeconds))
        {
            try
            {
                var milliseconds = decimal.ToInt64(decimal.Round(fractionalSeconds * 1000m, MidpointRounding.AwayFromZero));
                utcDateTime = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;

        var digitCount = raw.TrimStart('+', '-').Length;
        var treatAsMilliseconds = digitCount > 10;

        try
        {
            utcDateTime = treatAsMilliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildTimeZoneAbbreviation(TimeZoneInfo timeZone, DateTime localDateTime)
    {
        if (timeZone.BaseUtcOffset == TimeSpan.FromHours(1))
            return timeZone.IsDaylightSavingTime(localDateTime) ? "CEST" : "CET";

        var name = timeZone.IsDaylightSavingTime(localDateTime) ? timeZone.DaylightName : timeZone.StandardName;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Equals("time", StringComparison.OrdinalIgnoreCase)
                || word.Equals("standard", StringComparison.OrdinalIgnoreCase)
                || word.Equals("daylight", StringComparison.OrdinalIgnoreCase))
                continue;

            var first = word[0];
            if (char.IsLetter(first))
                sb.Append(char.ToUpperInvariant(first));
        }

        if (sb.Length > 1)
            return sb.ToString();

        var offset = timeZone.GetUtcOffset(localDateTime);
        return FormatUtcOffset(offset);
    }

    private static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var totalHours = (int)absolute.TotalHours;
        return string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{totalHours:00}:{absolute.Minutes:00}");
    }

    private static string FormatPlusHours(TimeSpan offset)
    {
        var hours = offset.TotalHours;
        if (Math.Abs(hours % 1) < 0.00001)
            return string.Create(CultureInfo.InvariantCulture, $"{hours:+0;-0;+0}h");
        return string.Create(CultureInfo.InvariantCulture, $"{hours:+0.##;-0.##;+0}h");
    }

    private static bool TryGetSwedenTimeZone(out TimeZoneInfo swedenTimeZone)
    {
        var candidates = new[]
        {
            "Europe/Stockholm",
            "W. Europe Standard Time",
            "Central European Standard Time"
        };

        foreach (var id in candidates)
        {
            try
            {
                swedenTimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch
            {
                // Try next candidate id.
            }
        }

        swedenTimeZone = TimeZoneInfo.Local;
        return false;
    }

    private static TimeZoneInfo.AdjustmentRule? FindRuleForYear(TimeZoneInfo timeZone, int year)
    {
        var rules = timeZone.GetAdjustmentRules();
        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (year >= rule.DateStart.Year && year <= rule.DateEnd.Year)
                return rule;
        }

        return null;
    }

    private static DateTime BuildTransitionLocalDateTime(int year, TimeZoneInfo.TransitionTime transition)
    {
        var month = Math.Max(1, Math.Min(12, transition.Month));

        if (transition.IsFixedDateRule)
        {
            var dayCandidate = transition.Day <= 0 ? 1 : transition.Day;
            var fixedDay = Math.Min(DateTime.DaysInMonth(year, month), dayCandidate);
            return new DateTime(
                year,
                month,
                fixedDay,
                transition.TimeOfDay.Hour,
                transition.TimeOfDay.Minute,
                transition.TimeOfDay.Second,
                transition.TimeOfDay.Millisecond,
                DateTimeKind.Unspecified);
        }

        var week = Math.Max(1, Math.Min(5, transition.Week));
        var daysInMonth = DateTime.DaysInMonth(year, month);
        int day;

        if (week == 5)
        {
            // Last <DayOfWeek> of the month.
            var lastOfMonth = new DateTime(year, month, daysInMonth);
            var backwardOffset = ((int)lastOfMonth.DayOfWeek - (int)transition.DayOfWeek + 7) % 7;
            day = daysInMonth - backwardOffset;
        }
        else
        {
            var firstOfMonth = new DateTime(year, month, 1);
            var dayOffset = ((int)transition.DayOfWeek - (int)firstOfMonth.DayOfWeek + 7) % 7;
            day = 1 + dayOffset + ((week - 1) * 7);
        }

        day = Math.Max(1, Math.Min(daysInMonth, day));

        return new DateTime(
            year,
            month,
            day,
            transition.TimeOfDay.Hour,
            transition.TimeOfDay.Minute,
            transition.TimeOfDay.Second,
            transition.TimeOfDay.Millisecond,
            DateTimeKind.Unspecified);
    }

    private static string BuildSwedenDstPeriodText(int year)
    {
        try
        {
            if (!TryGetSwedenTimeZone(out var swedenTimeZone))
                return "Sweden timezone data is unavailable on this system.";

            var rule = FindRuleForYear(swedenTimeZone, year);
            var winterOffset = swedenTimeZone.BaseUtcOffset;
            var summerOffset = winterOffset;

            var sb = new StringBuilder();
            sb.AppendLine($"Sweden timezone: {swedenTimeZone.Id}");
            sb.AppendLine($"Year: {year}");
            sb.AppendLine();

            if (rule is not null && rule.DaylightDelta != TimeSpan.Zero)
            {
                summerOffset = winterOffset + rule.DaylightDelta;

                var summerStartLocal = BuildTransitionLocalDateTime(year, rule.DaylightTransitionStart);
                var winterStartLocal = BuildTransitionLocalDateTime(year, rule.DaylightTransitionEnd);
                var summerStartUtc = new DateTimeOffset(summerStartLocal, winterOffset).UtcDateTime;
                var winterStartUtc = new DateTimeOffset(winterStartLocal, summerOffset).UtcDateTime;

                var summerFromDate = summerStartLocal.Date;
                var summerToDate = winterStartLocal.Date;
                var winterAFromDate = new DateTime(year, 1, 1);
                var winterAToDate = summerStartLocal.Date;
                var winterBFromDate = winterStartLocal.Date;
                var winterBToDate = new DateTime(year, 12, 31);

                sb.AppendLine($"Summer time starts on: {summerStartLocal:yyyy-MM-dd} (Sweden local)");
                sb.AppendLine($"Winter time starts on: {winterStartLocal:yyyy-MM-dd} (Sweden local)");
                sb.AppendLine("Rule (EU/Sweden): last Sunday in March -> summer time, last Sunday in October -> winter time.");
                sb.AppendLine("So the rule is the same each year, but the calendar date changes year to year.");
                sb.AppendLine();

                sb.AppendLine("Summer time (CEST - Central European Summer Time):");
                sb.AppendLine($"  from {summerFromDate:yyyy-MM-dd} to {summerToDate:yyyy-MM-dd} (Sweden local dates)");
                sb.AppendLine($"  starts at UTC: {summerStartUtc:yyyy-MM-dd HH:mm}Z");
                sb.AppendLine();
                sb.AppendLine("Winter time (CET - Central European Time):");
                sb.AppendLine($"  from {winterAFromDate:yyyy-MM-dd} to {winterAToDate:yyyy-MM-dd}");
                sb.AppendLine($"  and  {winterBFromDate:yyyy-MM-dd} to {winterBToDate:yyyy-MM-dd}");
                sb.AppendLine($"  starts at UTC: {winterStartUtc:yyyy-MM-dd HH:mm}Z");
            }
            else
            {
                sb.AppendLine("No daylight saving transitions found for this year.");
            }

            sb.AppendLine();
            sb.AppendLine("Difference from UTC/GMT:");
            sb.AppendLine($"  Winter (CET): {FormatUtcOffset(winterOffset)} = {FormatPlusHours(winterOffset)} vs UTC, {FormatPlusHours(winterOffset)} vs GMT");
            sb.AppendLine($"  Summer (CEST): {FormatUtcOffset(summerOffset)} = {FormatPlusHours(summerOffset)} vs UTC, {FormatPlusHours(summerOffset)} vs GMT");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Could not calculate Sweden DST details for {year}: {ex.Message}";
        }
    }

    private void ShowTimeConverterDialog()
    {
        var dlg = new Window
        {
            Title = "Time Converter",
            Width = 1140,
            Height = 900,
            MinWidth = 1000,
            MinHeight = 780,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsDefault = true,
            IsCancel = true
        };
        closeRow.Children.Add(btnClose);
        bottom.Children.Add(status);
        bottom.Children.Add(closeRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        static TextBlock Label(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        static TextBox InputBox() => new()
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        static TextBox OutputBox() => new()
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 0, 8)
        };

        static TextBox InfoBox() => new()
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 330,
            Height = 360,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Paste UTC ISO-8601 (with or without offset) or Linux/Unix time. Output is local time with timezone and formatted value.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Examples: 2026-04-11T10:13:40Z, 2026-04-11T12:13:40+02:00, 2026-04-11 10:13:40, 1775902420, 1775902420000.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Label("UTC / ISO-8601 input"));
        var txtUtcInput = InputBox();
        panel.Children.Add(txtUtcInput);

        panel.Children.Add(Label("Linux / Unix time input (seconds, milliseconds, or fractional seconds)"));
        var txtUnixInput = InputBox();
        panel.Children.Add(txtUnixInput);

        panel.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 10) });

        panel.Children.Add(Label("Local time (ISO-8601)"));
        var txtLocalIso = OutputBox();
        panel.Children.Add(txtLocalIso);

        panel.Children.Add(Label("Formatted local time"));
        var txtLocalFormatted = OutputBox();
        panel.Children.Add(txtLocalFormatted);

        panel.Children.Add(Label("UTC normalized"));
        var txtUtcNormalized = OutputBox();
        panel.Children.Add(txtUtcNormalized);

        panel.Children.Add(Label("Unix time (seconds)"));
        var txtUnixSeconds = OutputBox();
        panel.Children.Add(txtUnixSeconds);

        panel.Children.Add(Label("Unix time (milliseconds)"));
        var txtUnixMilliseconds = OutputBox();
        panel.Children.Add(txtUnixMilliseconds);

        panel.Children.Add(Label("Local timezone"));
        var txtTimeZone = OutputBox();
        panel.Children.Add(txtTimeZone);

        panel.Children.Add(Label("Sweden DST details (summer/winter period + UTC/GMT difference)"));
        var txtSwedenDstPeriod = InfoBox();
        panel.Children.Add(txtSwedenDstPeriod);

        root.Children.Add(panel);

        var isUpdating = false;

        void ClearOutput()
        {
            txtLocalIso.Text = string.Empty;
            txtLocalFormatted.Text = string.Empty;
            txtUtcNormalized.Text = string.Empty;
            txtUnixSeconds.Text = string.Empty;
            txtUnixMilliseconds.Text = string.Empty;
            txtTimeZone.Text = string.Empty;
            txtSwedenDstPeriod.Text = BuildSwedenDstPeriodText(DateTime.UtcNow.Year);
        }

        void PopulateFromUtc(DateTimeOffset utc, string source)
        {
            var local = utc.ToLocalTime();
            var zone = TimeZoneInfo.Local;
            var zoneOffset = zone.GetUtcOffset(local.DateTime);
            var zoneAbbreviation = BuildTimeZoneAbbreviation(zone, local.DateTime);

            txtLocalIso.Text = local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
            txtLocalFormatted.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{local:yyyy-MM-dd HH:mm:ss} {zoneAbbreviation} ({FormatUtcOffset(zoneOffset)})");
            txtUtcNormalized.Text = utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            txtUnixSeconds.Text = utc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            txtUnixMilliseconds.Text = utc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            txtTimeZone.Text = $"{zone.Id} ({zoneAbbreviation})";
            var swedenYear = utc.UtcDateTime.Year;
            txtSwedenDstPeriod.Text = BuildSwedenDstPeriodText(swedenYear);
            SetStatus($"Converted from {source} input.");
        }

        void ConvertFromIsoInput()
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                var raw = txtUtcInput.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ClearOutput();
                    SetStatus("Paste a UTC/ISO-8601 value or Linux/Unix time.");
                    return;
                }

                if (!TryParseIso8601Utc(raw, out var utc))
                {
                    SetStatus("Invalid ISO-8601 input. Try values like 2026-04-11T10:13:40Z.", Brushes.IndianRed);
                    return;
                }

                PopulateFromUtc(utc, "ISO-8601");
            }
            catch (Exception ex)
            {
                ClearOutput();
                SetStatus($"ISO conversion failed: {ex.Message}", Brushes.IndianRed);
            }
            finally
            {
                isUpdating = false;
            }
        }

        void ConvertFromUnixInput()
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                var raw = txtUnixInput.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ClearOutput();
                    SetStatus("Paste a UTC/ISO-8601 value or Linux/Unix time.");
                    return;
                }

                if (!TryParseUnixTime(raw, out var utc))
                {
                    SetStatus("Invalid Unix time input. Use seconds, milliseconds, or fractional seconds.", Brushes.IndianRed);
                    return;
                }

                PopulateFromUtc(utc, "Unix");
            }
            catch (Exception ex)
            {
                ClearOutput();
                SetStatus($"Unix conversion failed: {ex.Message}", Brushes.IndianRed);
            }
            finally
            {
                isUpdating = false;
            }
        }

        txtUtcInput.TextChanged += (_, _) => ConvertFromIsoInput();
        txtUnixInput.TextChanged += (_, _) => ConvertFromUnixInput();
        btnClose.Click += (_, _) => dlg.Close();

        try
        {
            txtUtcInput.Text = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            ClearOutput();
            SetStatus($"Could not initialize time conversion: {ex.Message}", Brushes.IndianRed);
        }
        dlg.Content = root;
        dlg.ShowDialog();
    }
}

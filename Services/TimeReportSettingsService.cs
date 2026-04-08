using System.Globalization;
using Noted.Models;

namespace Noted.Services;

public sealed class TimeReportSettingsService
{
    public List<TimeReportMonthRecord> BuildRecords(IReadOnlyDictionary<string, TimeReportMonthState> timeReports)
    {
        var records = new List<TimeReportMonthRecord>();
        var monthKeys = timeReports.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var monthKey in monthKeys)
        {
            if (!timeReports.TryGetValue(monthKey, out var monthState))
                continue;
            if (monthState.DayValues.Count == 0 && monthState.WeekComments.Count == 0)
                continue;

            records.Add(new TimeReportMonthRecord
            {
                Month = monthKey,
                DayValues = monthState.DayValues.Count == 0
                    ? null
                    : monthState.DayValues.ToDictionary(entry => entry.Key, entry => entry.Value),
                WeekComments = monthState.WeekComments.Count == 0
                    ? null
                    : monthState.WeekComments.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        return records;
    }

    public Dictionary<string, TimeReportMonthState> LoadStates(IEnumerable<TimeReportMonthRecord>? records)
    {
        var states = new Dictionary<string, TimeReportMonthState>(StringComparer.OrdinalIgnoreCase);
        if (records == null)
            return states;

        foreach (var record in records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Month))
                continue;

            if (!DateTime.TryParseExact(
                    $"{record.Month}-01",
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedMonth))
            {
                continue;
            }

            var monthKey = ToMonthKey(parsedMonth);
            var state = new TimeReportMonthState();

            if (record.DayValues != null)
            {
                foreach (var pair in record.DayValues)
                {
                    if (pair.Key is >= 1 and <= 31
                        && TryNormalizeTimeReportDayValue(pair.Value, out var normalizedValue))
                    {
                        state.DayValues[pair.Key] = normalizedValue;
                    }
                }
            }

            if (record.DayHours != null)
            {
                foreach (var pair in record.DayHours)
                {
                    if (pair.Key is >= 1 and <= 31
                        && pair.Value is >= 0 and <= 24
                        && !state.DayValues.ContainsKey(pair.Key))
                    {
                        state.DayValues[pair.Key] =
                            Math.Round(pair.Value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                }
            }

            if (record.WeekComments != null)
            {
                foreach (var pair in record.WeekComments)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    state.WeekComments[pair.Key.Trim()] = pair.Value.Trim();
                }
            }

            if (state.DayValues.Count > 0 || state.WeekComments.Count > 0)
                states[monthKey] = state;
        }

        return states;
    }

    private static string ToMonthKey(DateTime monthStart)
        => monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

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

    private static bool TryParseHours(string text, out double hours)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out hours))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out hours))
            return true;
        return false;
    }
}

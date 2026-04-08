using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static bool TryNormalizeMongoObjectId(string? text, out string objectId)
    {
        objectId = string.Empty;
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (raw.StartsWith("ObjectId(", StringComparison.OrdinalIgnoreCase) && raw.EndsWith(")", StringComparison.Ordinal))
        {
            raw = raw["ObjectId(".Length..^1].Trim();
        }

        if ((raw.StartsWith('"') && raw.EndsWith('"')) || (raw.StartsWith('\'') && raw.EndsWith('\'')))
            raw = raw[1..^1].Trim();

        if (raw.Length != 24)
            return false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            var isHex = (c >= '0' && c <= '9')
                        || (c >= 'a' && c <= 'f')
                        || (c >= 'A' && c <= 'F');
            if (!isHex)
                return false;
        }

        objectId = raw.ToLowerInvariant();
        return true;
    }

    private static bool TryGetUtcFromObjectId(string objectId, out DateTimeOffset utcDateTime, out long utcTimestamp)
    {
        utcDateTime = default;
        utcTimestamp = 0;

        var hexTimestamp = objectId[..8];
        if (!uint.TryParse(hexTimestamp, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var seconds))
            return false;

        utcTimestamp = seconds;
        utcDateTime = DateTimeOffset.FromUnixTimeSeconds(utcTimestamp);
        return true;
    }

    private static string ToMongoObjectIdWrapped(string objectId) => $"ObjectId(\"{objectId}\")";

    private void ShowMongoObjectIdTimestampConverterDialog()
    {
        var dlg = new Window
        {
            Title = "ObjectId to Timestamp Converter",
            Width = 1020,
            Height = 780,
            MinWidth = 900,
            MinHeight = 700,
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
            IsCancel = true,
            IsDefault = true
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
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Edit ObjectId or any date/time field. Values sync both ways, and invalid combinations mark ObjectId as invalid.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        panel.Children.Add(Label("ObjectId (24 hex chars)"));
        var txtObjectIdRaw = InputBox();
        panel.Children.Add(txtObjectIdRaw);

        panel.Children.Add(Label("ObjectId wrapper"));
        var txtObjectIdWrapped = InputBox();
        panel.Children.Add(txtObjectIdWrapped);

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        modeRow.Children.Add(new TextBlock
        {
            Text = "Time mode:",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var cmbTimeMode = new ComboBox
        {
            Width = 150
        };
        cmbTimeMode.Items.Add("Local");
        cmbTimeMode.Items.Add("UTC");
        cmbTimeMode.SelectedIndex = 1;
        modeRow.Children.Add(cmbTimeMode);
        panel.Children.Add(modeRow);

        var fieldsGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < 4; i++)
            fieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        (TextBlock label, TextBox box) AddField(string labelText, int row, int col)
        {
            var slot = new StackPanel { Margin = new Thickness(0, 0, col < 2 ? 8 : 0, 0) };
            var label = Label(labelText);
            slot.Children.Add(label);
            var box = InputBox();
            slot.Children.Add(box);
            Grid.SetRow(slot, row);
            Grid.SetColumn(slot, col);
            fieldsGrid.Children.Add(slot);
            return (label, box);
        }

        var (lblYear, txtYear) = AddField("Year (Local)", 0, 0);
        var (lblMonth, txtMonth) = AddField("Month (Local)", 0, 1);
        var (lblDay, txtDay) = AddField("Date (Local)", 0, 2);
        var (lblHours, txtHours) = AddField("Hours (Local)", 1, 0);
        var (lblMinutes, txtMinutes) = AddField("Minutes (Local)", 1, 1);
        var (lblSeconds, txtSeconds) = AddField("Seconds (Local)", 1, 2);
        var (_, txtUtcTimestamp) = AddField("UTC Timestamp (Unix seconds)", 2, 0);
        var (_, txtUtcDateTime) = AddField("UTC DateTime (ISO-8601)", 2, 1);

        var spacer = new Border { Height = 0, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(spacer, 3);
        Grid.SetColumnSpan(spacer, 3);
        fieldsGrid.Children.Add(spacer);

        panel.Children.Add(fieldsGrid);
        panel.Children.Add(Label("How ObjectId is calculated (date -> ObjectId)"));
        var txtCalculationForward = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 96,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtCalculationForward);
        panel.Children.Add(Label("How date is calculated from ObjectId (ObjectId -> date)"));
        var txtCalculationReverse = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 96,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtCalculationReverse);
        root.Children.Add(panel);

        void ClearOutput()
        {
            txtYear.Text = string.Empty;
            txtMonth.Text = string.Empty;
            txtDay.Text = string.Empty;
            txtHours.Text = string.Empty;
            txtMinutes.Text = string.Empty;
            txtSeconds.Text = string.Empty;
            txtUtcTimestamp.Text = string.Empty;
            txtUtcDateTime.Text = string.Empty;
            txtCalculationForward.Text =
                "Math.floor(date.getTime() / 1000).toString(16) + \"0000000000000000\"\n"
                + "Creates ObjectId prefix from UTC seconds, then appends suffix.";
            txtCalculationReverse.Text =
                "new Date(parseInt(objectId.substring(0, 8), 16) * 1000)\n"
                + "Extracts first 8 hex chars, converts to seconds, then to JS Date.";
        }

        var isUpdating = false;
        const string InvalidObjectIdText = "Invalid ObjectId";

        bool UseUtcDateParts() => string.Equals(cmbTimeMode.SelectedItem?.ToString(), "UTC", StringComparison.OrdinalIgnoreCase);

        void UpdateDatePartLabels()
        {
            var modeText = UseUtcDateParts() ? "UTC" : "Local";
            lblYear.Text = $"Year ({modeText})";
            lblMonth.Text = $"Month ({modeText})";
            lblDay.Text = $"Date ({modeText})";
            lblHours.Text = $"Hours ({modeText})";
            lblMinutes.Text = $"Minutes ({modeText})";
            lblSeconds.Text = $"Seconds ({modeText})";
        }

        static bool TryParseNumber(string? text, int min, int max, out int value)
        {
            value = 0;
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return false;
            if (parsed < min || parsed > max)
                return false;
            value = parsed;
            return true;
        }

        static bool TryParseUtcIsoDateTime(string? text, out DateTimeOffset utc)
        {
            var raw = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                utc = default;
                return false;
            }

            var formats = new[]
            {
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss.fffK",
                "yyyy-MM-dd'T'HH:mm:ssK"
            };

            return DateTimeOffset.TryParseExact(
                raw,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc);
        }

        string GetSuffixFromCurrentObjectId()
        {
            if (TryNormalizeMongoObjectId(txtObjectIdRaw.Text, out var currentObjectId))
                return currentObjectId[8..];
            if (TryNormalizeMongoObjectId(txtObjectIdWrapped.Text, out currentObjectId))
                return currentObjectId[8..];
            return "0000000000000000";
        }

        void SetObjectIdInvalid(string message)
        {
            txtObjectIdRaw.Text = InvalidObjectIdText;
            txtObjectIdWrapped.Text = InvalidObjectIdText;
            txtCalculationForward.Text =
                "Math.floor(date.getTime() / 1000).toString(16) + \"0000000000000000\"\n"
                + "Cannot build valid ObjectId from current inputs.";
            txtCalculationReverse.Text =
                "new Date(parseInt(objectId.substring(0, 8), 16) * 1000)\n"
                + "Cannot derive valid date from current ObjectId.";
            SetStatus(message, Brushes.IndianRed);
        }

        void UpdateCalculationDetails(DateTimeOffset utc, long unixTimestamp, string objectId, string suffix)
        {
            var prefix = objectId[..8];
            var derivedDate = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(prefix, 16)).UtcDateTime;
            txtCalculationForward.Text =
                "Math.floor(date.getTime() / 1000).toString(16) + \"0000000000000000\"\n"
                + $"date (UTC): {utc.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}\n"
                + $"Math.floor(date.getTime() / 1000): {unixTimestamp} // Unix time (Linux epoch seconds)\n"
                + $"toString(16): {prefix}\n"
                + $"suffix used: {suffix}\n"
                + $"ObjectId: {objectId}";
            txtCalculationReverse.Text =
                "new Date(parseInt(objectId.substring(0, 8), 16) * 1000)\n"
                + $"ObjectId first 8 hex chars: {prefix}\n"
                + $"parseInt(\"{prefix}\", 16): {Convert.ToInt64(prefix, 16)} // set base to 16 (not 10) to interpret string as hexadecimal\n"
                + $"Date result (UTC): {derivedDate:yyyy-MM-ddTHH:mm:ss.fffZ}";
        }

        void PopulateFromUtc(DateTimeOffset utc, string suffix)
        {
            var unixTimestamp = utc.ToUnixTimeSeconds();
            if (unixTimestamp < 0 || unixTimestamp > uint.MaxValue)
            {
                SetObjectIdInvalid("Timestamp is outside MongoDB ObjectId range.");
                return;
            }

            var objectId = $"{((uint)unixTimestamp):x8}{suffix}";
            var dateParts = UseUtcDateParts() ? utc : utc.ToLocalTime();

            txtObjectIdRaw.Text = objectId;
            txtObjectIdWrapped.Text = ToMongoObjectIdWrapped(objectId);
            txtYear.Text = dateParts.Year.ToString(CultureInfo.InvariantCulture);
            txtMonth.Text = dateParts.Month.ToString("00", CultureInfo.InvariantCulture);
            txtDay.Text = dateParts.Day.ToString("00", CultureInfo.InvariantCulture);
            txtHours.Text = dateParts.Hour.ToString("00", CultureInfo.InvariantCulture);
            txtMinutes.Text = dateParts.Minute.ToString("00", CultureInfo.InvariantCulture);
            txtSeconds.Text = dateParts.Second.ToString("00", CultureInfo.InvariantCulture);
            txtUtcTimestamp.Text = unixTimestamp.ToString(CultureInfo.InvariantCulture);
            txtUtcDateTime.Text = utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            UpdateCalculationDetails(utc, unixTimestamp, objectId, suffix);
            SetStatus("Values synchronized.");
        }

        void ConvertFromObjectIdInput(string inputText, bool sourceIsRaw)
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                var sourceTrimmed = (inputText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sourceTrimmed))
                {
                    if (sourceIsRaw)
                        txtObjectIdWrapped.Text = string.Empty;
                    else
                        txtObjectIdRaw.Text = string.Empty;
                    ClearOutput();
                    SetStatus("Paste a MongoDB ObjectId value.");
                    return;
                }

                if (!TryNormalizeMongoObjectId(sourceTrimmed, out var objectId))
                {
                    ClearOutput();
                    SetStatus("Invalid ObjectId. Use 24 hex chars or ObjectId(\"24-hex\").", Brushes.IndianRed);
                    return;
                }

                if (!TryGetUtcFromObjectId(objectId, out _, out var unixTimestamp))
                {
                    ClearOutput();
                    SetStatus("Failed to parse ObjectId timestamp.", Brushes.IndianRed);
                    return;
                }

                var suffix = objectId[8..];
                PopulateFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp), suffix);
            }
            finally
            {
                isUpdating = false;
            }
        }

        void ConvertFromDatePartFields()
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                if (!TryParseNumber(txtYear.Text, 1970, 3000, out var year)
                    || !TryParseNumber(txtMonth.Text, 1, 12, out var month)
                    || !TryParseNumber(txtDay.Text, 1, 31, out var day)
                    || !TryParseNumber(txtHours.Text, 0, 23, out var hour)
                    || !TryParseNumber(txtMinutes.Text, 0, 59, out var minute)
                    || !TryParseNumber(txtSeconds.Text, 0, 59, out var second))
                {
                    SetObjectIdInvalid($"Invalid {(UseUtcDateParts() ? "UTC" : "local")} date/time fields.");
                    return;
                }

                DateTime dateTime;
                try
                {
                    var kind = UseUtcDateParts() ? DateTimeKind.Utc : DateTimeKind.Local;
                    dateTime = new DateTime(year, month, day, hour, minute, second, kind);
                }
                catch
                {
                    SetObjectIdInvalid($"Invalid {(UseUtcDateParts() ? "UTC" : "local")} date/time combination.");
                    return;
                }

                var utc = UseUtcDateParts()
                    ? new DateTimeOffset(dateTime)
                    : new DateTimeOffset(dateTime).ToUniversalTime();
                PopulateFromUtc(utc, GetSuffixFromCurrentObjectId());
            }
            finally
            {
                isUpdating = false;
            }
        }

        void RefreshDisplayedFromCurrentState()
        {
            if (TryNormalizeMongoObjectId(txtObjectIdRaw.Text, out var objectId)
                && TryGetUtcFromObjectId(objectId, out _, out var unixTimestamp))
            {
                isUpdating = true;
                try
                {
                    PopulateFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp), objectId[8..]);
                }
                finally
                {
                    isUpdating = false;
                }
            }
        }

        void ConvertFromUtcTimestampField()
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                if (!long.TryParse((txtUtcTimestamp.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimestamp))
                {
                    SetObjectIdInvalid("Invalid UTC timestamp value.");
                    return;
                }

                DateTimeOffset utc;
                try
                {
                    utc = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
                }
                catch
                {
                    SetObjectIdInvalid("UTC timestamp is outside supported range.");
                    return;
                }

                PopulateFromUtc(utc, GetSuffixFromCurrentObjectId());
            }
            finally
            {
                isUpdating = false;
            }
        }

        void ConvertFromUtcDateTimeField()
        {
            if (isUpdating)
                return;

            isUpdating = true;
            try
            {
                if (!TryParseUtcIsoDateTime(txtUtcDateTime.Text, out var utc))
                {
                    SetObjectIdInvalid("Invalid UTC DateTime. Use format yyyy-MM-ddTHH:mm:ss.fffZ.");
                    return;
                }

                PopulateFromUtc(utc, GetSuffixFromCurrentObjectId());
            }
            finally
            {
                isUpdating = false;
            }
        }

        void WireCommitHandlers(TextBox box, Action onCommit)
        {
            box.TextChanged += (_, _) => onCommit();
            box.LostFocus += (_, _) => onCommit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    onCommit();
                }
            };
        }

        txtObjectIdRaw.TextChanged += (_, _) => ConvertFromObjectIdInput(txtObjectIdRaw.Text, sourceIsRaw: true);
        txtObjectIdWrapped.TextChanged += (_, _) => ConvertFromObjectIdInput(txtObjectIdWrapped.Text, sourceIsRaw: false);
        WireCommitHandlers(txtYear, ConvertFromDatePartFields);
        WireCommitHandlers(txtMonth, ConvertFromDatePartFields);
        WireCommitHandlers(txtDay, ConvertFromDatePartFields);
        WireCommitHandlers(txtHours, ConvertFromDatePartFields);
        WireCommitHandlers(txtMinutes, ConvertFromDatePartFields);
        WireCommitHandlers(txtSeconds, ConvertFromDatePartFields);
        WireCommitHandlers(txtUtcTimestamp, ConvertFromUtcTimestampField);
        WireCommitHandlers(txtUtcDateTime, ConvertFromUtcDateTimeField);
        cmbTimeMode.SelectionChanged += (_, _) =>
        {
            if (isUpdating)
                return;
            UpdateDatePartLabels();
            RefreshDisplayedFromCurrentState();
        };
        btnClose.Click += (_, _) => dlg.Close();

        // Initial sample from request.
        UpdateDatePartLabels();
        txtObjectIdRaw.Text = "69d59a800000000000000000";

        dlg.Content = root;
        dlg.ShowDialog();
    }
}

using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static string BuildBase64InfoText()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var sb = new StringBuilder();

        sb.AppendLine("Why it is called Base64");
        sb.AppendLine("- Base64 uses an alphabet of 64 symbols.");
        sb.AppendLine("- 64 = 2^6, so each Base64 character encodes 6 bits.");
        sb.AppendLine("- 3 input bytes = 24 bits -> 4 Base64 chars (4 * 6 = 24).");
        sb.AppendLine();
        sb.AppendLine("Padding");
        sb.AppendLine("- Padding depends on input bytes mod 3.");
        sb.AppendLine("- remainder 0 -> no '='");
        sb.AppendLine("- remainder 1 -> '=='");
        sb.AppendLine("- remainder 2 -> '='");
        sb.AppendLine();
        sb.AppendLine("Base64 index table (value -> 6-bit -> char)");

        for (var i = 0; i < alphabet.Length; i++)
        {
            var bits = Convert.ToString(i, 2).PadLeft(6, '0');
            sb.Append($"{i,2} -> {bits} -> {alphabet[i]}");
            if (i < alphabet.Length - 1)
                sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Base64URL");
        sb.AppendLine("- Same 6-bit values.");
        sb.AppendLine("- Only chars 62 and 63 differ:");
        sb.AppendLine("  62: '+' -> '-'");
        sb.AppendLine("  63: '/' -> '_'");
        sb.AppendLine("- '=' padding is less common in URLs, but valid.");

        return sb.ToString();
    }

    private static string BuildBase64CalculationText(string plainText, bool outputAsBase64Url)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var input = plainText ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(input);
        var sb = new StringBuilder();

        static string ShowChar(char ch)
        {
            return ch switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => ch.ToString()
            };
        }

        sb.AppendLine("How this value is calculated");
        sb.AppendLine($"Input text: \"{input}\"");
        sb.AppendLine($"UTF-8 bytes: {bytes.Length}");
        sb.AppendLine($"Mode: {(outputAsBase64Url ? "Base64URL" : "Base64")}");
        sb.AppendLine();

        if (bytes.Length == 0)
        {
            sb.AppendLine("No bytes to encode. Output is empty.");
            return sb.ToString();
        }

        sb.AppendLine("Input characters -> UTF-8 bytes:");
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            var charBytes = Encoding.UTF8.GetBytes(new[] { ch });
            var byteList = string.Join(" ", Array.ConvertAll(charBytes, b => b.ToString()));
            var bitList = string.Join(" ", Array.ConvertAll(charBytes, b => Convert.ToString(b, 2).PadLeft(8, '0')));
            sb.AppendLine($"  '{ShowChar(ch)}' -> [{byteList}] -> [{bitList}]");
        }
        sb.AppendLine();

        sb.AppendLine("Per 3-byte block:");
        var blockCount = (bytes.Length + 2) / 3;
        for (var block = 0; block < blockCount; block++)
        {
            var i = block * 3;
            var b0 = bytes[i];
            var hasB1 = i + 1 < bytes.Length;
            var hasB2 = i + 2 < bytes.Length;
            var b1 = hasB1 ? bytes[i + 1] : (byte)0;
            var b2 = hasB2 ? bytes[i + 2] : (byte)0;

            var twentyFourBits = (b0 << 16) | (b1 << 8) | b2;
            var s0 = (twentyFourBits >> 18) & 0x3F;
            var s1 = (twentyFourBits >> 12) & 0x3F;
            var s2 = (twentyFourBits >> 6) & 0x3F;
            var s3 = twentyFourBits & 0x3F;

            var c0 = alphabet[s0];
            var c1 = alphabet[s1];
            var c2 = hasB1 ? alphabet[s2] : '=';
            var c3 = hasB2 ? alphabet[s3] : '=';

            sb.AppendLine($"Block {block + 1}:");
            sb.AppendLine(
                $"  bytes: {b0} ({Convert.ToString(b0, 2).PadLeft(8, '0')})" +
                (hasB1 ? $", {b1} ({Convert.ToString(b1, 2).PadLeft(8, '0')})" : ", [pad 00000000]") +
                (hasB2 ? $", {b2} ({Convert.ToString(b2, 2).PadLeft(8, '0')})" : ", [pad 00000000]"));
            sb.AppendLine($"  24 bits: {Convert.ToString(twentyFourBits, 2).PadLeft(24, '0')}");
            sb.AppendLine(
                $"  sextets: {Convert.ToString(s0, 2).PadLeft(6, '0')}({s0} -> {c0}), " +
                $"{Convert.ToString(s1, 2).PadLeft(6, '0')}({s1} -> {c1}), " +
                $"{Convert.ToString(s2, 2).PadLeft(6, '0')}({s2} -> {(hasB1 ? c2.ToString() : "'='")}), " +
                $"{Convert.ToString(s3, 2).PadLeft(6, '0')}({s3} -> {(hasB2 ? c3.ToString() : "'='")})");
        }

        var base64 = Convert.ToBase64String(bytes);
        var base64Url = ToBase64Url(base64);
        sb.AppendLine();
        sb.AppendLine($"Base64 output: {base64}");
        sb.AppendLine($"Base64URL output: {base64Url}");
        sb.AppendLine($"Selected mode output: {(outputAsBase64Url ? base64Url : base64)}");

        return sb.ToString();
    }

    private static string NormalizeBase64Input(string? text, bool isBase64Url = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                if (isBase64Url)
                {
                    if (c == '-')
                    {
                        sb.Append('+');
                        continue;
                    }
                    if (c == '_')
                    {
                        sb.Append('/');
                        continue;
                    }
                }
                sb.Append(c);
            }
        }

        var s = sb.ToString();
        var mod = s.Length % 4;
        if (mod == 0)
            return s;
        if (mod == 1)
            return s;

        return s + new string('=', 4 - mod);
    }

    private static string ToBase64Url(string base64)
    {
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool IsValidBase64Syntax(string text, bool isBase64Url)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
            return false;

        if (isBase64Url)
        {
            var paddingStart = text.IndexOf('=');
            if (paddingStart >= 0)
            {
                for (var i = paddingStart; i < text.Length; i++)
                {
                    if (text[i] != '=')
                        return false;
                }

                var paddingCount = text.Length - paddingStart;
                if (paddingCount > 2)
                    return false;
            }

            foreach (var c in text)
            {
                var isLetterOrDigit = (c >= 'A' && c <= 'Z') ||
                                      (c >= 'a' && c <= 'z') ||
                                      (c >= '0' && c <= '9');
                if (!isLetterOrDigit && c != '-' && c != '_' && c != '=')
                    return false;
            }

            return text.Length % 4 != 1;
        }

        var base64PaddingStart = text.IndexOf('=');
        if (base64PaddingStart >= 0)
        {
            for (var i = base64PaddingStart; i < text.Length; i++)
            {
                if (text[i] != '=')
                    return false;
            }

            var paddingCount = text.Length - base64PaddingStart;
            if (paddingCount > 2)
                return false;
        }

        foreach (var c in text)
        {
            var isLetterOrDigit = (c >= 'A' && c <= 'Z') ||
                                  (c >= 'a' && c <= 'z') ||
                                  (c >= '0' && c <= '9');
            if (!isLetterOrDigit && c != '+' && c != '/' && c != '=')
                return false;
        }

        return text.Length % 4 != 1;
    }

    private static bool IsValidBase64Like(string? text, bool isBase64Url)
    {
        if (string.IsNullOrEmpty(text) || !IsValidBase64Syntax(text, isBase64Url))
            return false;

        var normalized = NormalizeBase64Input(text, isBase64Url);
        if (string.IsNullOrEmpty(normalized))
            return false;

        try
        {
            _ = Convert.FromBase64String(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ShowBase64Dialog()
    {
        var dlg = new Window
        {
            Title = "Base64",
            Width = 1040,
            Height = 760,
            MinWidth = 680,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var sharedInputPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var sharedHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        sharedHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sharedHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lblSharedInput = new TextBlock
        {
            Text = "Quick Paste (auto-detect Base64 / Base64URL / text)",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lblSharedInput, 0);
        sharedHeader.Children.Add(lblSharedInput);
        var btnInfo = new Button
        {
            Content = "i",
            Width = 24,
            Height = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Base64 info"
        };
        btnInfo.Click += (_, _) =>
        {
            MessageBox.Show(
                dlg,
                BuildBase64InfoText(),
                "Base64 Information",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };
        Grid.SetColumn(btnInfo, 1);
        sharedHeader.Children.Add(btnInfo);
        var lblPaddingNote = new TextBlock
        {
            Text = "Note: Base64URL padding ('=') is less common, but valid (RFC 4648).",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        var txtSharedInput = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 58,
            MaxHeight = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap
        };
        sharedInputPanel.Children.Add(sharedHeader);
        sharedInputPanel.Children.Add(lblPaddingNote);
        sharedInputPanel.Children.Add(txtSharedInput);
        DockPanel.SetDock(sharedInputPanel, Dock.Top);
        root.Children.Add(sharedInputPanel);

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var mathGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        mathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        mathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var encodeMathText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        var encodeMathFrame = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = encodeMathText
        };
        Grid.SetColumn(encodeMathFrame, 0);
        mathGrid.Children.Add(encodeMathFrame);
        var decodeMathText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        var decodeMathFrame = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = decodeMathText
        };
        Grid.SetColumn(decodeMathFrame, 2);
        mathGrid.Children.Add(decodeMathFrame);
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
        bottom.Children.Add(mathGrid);
        bottom.Children.Add(status);
        bottom.Children.Add(closeRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        static TextBox CreateBase64TextBox() => new()
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 100
        };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var encodeColumn = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var encodeTitle = new TextBlock
        {
            Text = "Encode",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(encodeTitle, 0);
        encodeColumn.Children.Add(encodeTitle);

        var encodeModeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        encodeModeRow.Children.Add(new TextBlock
        {
            Text = "Mode:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var rbEncodeBase64 = new RadioButton
        {
            Content = "Base64",
            IsChecked = true,
            Margin = new Thickness(0, 0, 10, 0),
            GroupName = "EncodeMode"
        };
        var rbEncodeBase64Url = new RadioButton
        {
            Content = "Base64URL",
            GroupName = "EncodeMode"
        };
        encodeModeRow.Children.Add(rbEncodeBase64);
        encodeModeRow.Children.Add(rbEncodeBase64Url);
        Grid.SetRow(encodeModeRow, 1);
        encodeColumn.Children.Add(encodeModeRow);

        var encodeInputHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        encodeInputHeader.Children.Add(new TextBlock
        {
            Text = "Input",
            FontWeight = FontWeights.SemiBold
        });
        encodeInputHeader.Children.Add(new TextBlock
        {
            Text = "Base64 [A-Za-z0-9+/=]  |  Base64URL [A-Za-z0-9_-] (+ optional trailing '=').",
            Foreground = Brushes.DimGray,
            FontSize = 11
        });
        Grid.SetRow(encodeInputHeader, 2);
        encodeColumn.Children.Add(encodeInputHeader);

        var txtEncodeIn = CreateBase64TextBox();
        Grid.SetRow(txtEncodeIn, 3);
        encodeColumn.Children.Add(txtEncodeIn);

        var btnEncode = new Button
        {
            Content = "Encode",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 8, 0, 8)
        };
        var btnExplainEncode = new Button
        {
            Content = "Explain encoding",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 8, 0, 8)
        };
        var encodeButtonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        encodeButtonsRow.Children.Add(btnEncode);
        encodeButtonsRow.Children.Add(btnExplainEncode);
        Grid.SetRow(encodeButtonsRow, 4);
        encodeColumn.Children.Add(encodeButtonsRow);

        var lblEncodeOut = new TextBlock
        {
            Text = "Output",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblEncodeOut, 5);
        encodeColumn.Children.Add(lblEncodeOut);

        var txtEncodeOut = CreateBase64TextBox();
        Grid.SetRow(txtEncodeOut, 6);
        encodeColumn.Children.Add(txtEncodeOut);

        var lblEncodeIncludingPadding = new TextBlock
        {
            Text = "Including padding",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        };
        Grid.SetRow(lblEncodeIncludingPadding, 7);
        encodeColumn.Children.Add(lblEncodeIncludingPadding);

        var txtEncodeIncludingPadding = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetRow(txtEncodeIncludingPadding, 8);
        encodeColumn.Children.Add(txtEncodeIncludingPadding);

        var columnSeparator = new Border
        {
            Width = 1,
            Background = Brushes.Gainsboro,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(columnSeparator, 1);

        var decodeColumn = new Grid();
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var decodeTitle = new TextBlock
        {
            Text = "Decode",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(decodeTitle, 0);
        decodeColumn.Children.Add(decodeTitle);

        var decodeModeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        decodeModeRow.Children.Add(new TextBlock
        {
            Text = "Mode:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var rbDecodeBase64 = new RadioButton
        {
            Content = "Base64",
            IsChecked = true,
            Margin = new Thickness(0, 0, 10, 0),
            GroupName = "DecodeMode"
        };
        var rbDecodeBase64Url = new RadioButton
        {
            Content = "Base64URL",
            GroupName = "DecodeMode"
        };
        decodeModeRow.Children.Add(rbDecodeBase64);
        decodeModeRow.Children.Add(rbDecodeBase64Url);
        Grid.SetRow(decodeModeRow, 1);
        decodeColumn.Children.Add(decodeModeRow);

        var decodeInputHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        decodeInputHeader.Children.Add(new TextBlock
        {
            Text = "Input",
            FontWeight = FontWeights.SemiBold
        });
        decodeInputHeader.Children.Add(new TextBlock
        {
            Text = "Base64 [A-Za-z0-9+/=]  |  Base64URL [A-Za-z0-9_-] (+ optional trailing '=').",
            Foreground = Brushes.DimGray,
            FontSize = 11
        });
        Grid.SetRow(decodeInputHeader, 2);
        decodeColumn.Children.Add(decodeInputHeader);

        var txtDecodeIn = CreateBase64TextBox();
        Grid.SetRow(txtDecodeIn, 3);
        decodeColumn.Children.Add(txtDecodeIn);

        var btnDecode = new Button
        {
            Content = "Decode",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(btnDecode, 4);
        decodeColumn.Children.Add(btnDecode);

        var lblDecodeOut = new TextBlock
        {
            Text = "Output",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblDecodeOut, 5);
        decodeColumn.Children.Add(lblDecodeOut);

        var txtDecodeOut = CreateBase64TextBox();
        Grid.SetRow(txtDecodeOut, 6);
        decodeColumn.Children.Add(txtDecodeOut);

        var lblDecodeIncludingPadding = new TextBlock
        {
            Text = "Including padding",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        };
        Grid.SetRow(lblDecodeIncludingPadding, 7);
        decodeColumn.Children.Add(lblDecodeIncludingPadding);

        var txtDecodeIncludingPadding = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetRow(txtDecodeIncludingPadding, 8);
        decodeColumn.Children.Add(txtDecodeIncludingPadding);

        Grid.SetColumn(encodeColumn, 0);
        mainGrid.Children.Add(encodeColumn);
        mainGrid.Children.Add(columnSeparator);
        Grid.SetColumn(decodeColumn, 2);
        mainGrid.Children.Add(decodeColumn);

        root.Children.Add(mainGrid);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        void UpdatePaddingMathCards()
        {
            var encodeBytes = Encoding.UTF8.GetByteCount(txtEncodeIn.Text ?? string.Empty);
            var encodeRemainder = encodeBytes % 3;
            var encodePaddingCount = (3 - encodeRemainder) % 3;
            var encodeEncodedChars = ((encodeBytes + 2) / 3) * 4;
            encodeMathText.Text =
                "Base64 Encode Padding Math\n" +
                $"input_bytes = {encodeBytes}\n" +
                $"remainder = input_bytes % 3 = {encodeRemainder}\n" +
                $"padding '=' count = (3 - remainder) % 3 = {encodePaddingCount}\n" +
                $"encoded_chars_with_padding = ceil(input_bytes / 3) * 4 = {encodeEncodedChars}";

            var rawDecode = txtDecodeIn.Text ?? string.Empty;
            var decodeCharsTotal = 0;
            var trailingEquals = 0;
            for (var i = 0; i < rawDecode.Length; i++)
            {
                if (!char.IsWhiteSpace(rawDecode[i]))
                    decodeCharsTotal++;
            }

            for (var i = rawDecode.Length - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(rawDecode[i]))
                    continue;
                if (rawDecode[i] == '=')
                    trailingEquals++;
                else
                    break;
            }

            var decodeCharsNoPadding = Math.Max(0, decodeCharsTotal - trailingEquals);
            var decodeRemainder = decodeCharsNoPadding % 4;
            var missingPadding = decodeRemainder == 0 ? 0 : 4 - decodeRemainder;
            var decodeWarning = decodeRemainder == 1
                ? "len % 4 == 1 => impossible (you cannot fix this with '=')."
                : $"would add {missingPadding} '=' to align to multiple of 4.";
            decodeMathText.Text =
                "Decode Length/Padding Check\n" +
                $"encoded_chars_total = {decodeCharsTotal}\n" +
                $"existing_trailing_equals = {trailingEquals} (max valid is 2)\n" +
                $"encoded_chars_without_padding = {decodeCharsNoPadding}\n" +
                $"remainder = encoded_chars_without_padding % 4 = {decodeRemainder}\n" +
                $"{decodeWarning}\n" +
                $"normalized_length = encoded_chars_without_padding + missing_padding";
        }

        static string ToBase64UrlPadded(string base64)
        {
            return base64.Replace('+', '-').Replace('/', '_');
        }

        void ClearEncodePaddedOutputs()
        {
            txtEncodeIncludingPadding.Text = string.Empty;
        }

        void ClearDecodePaddedOutputs()
        {
            txtDecodeIncludingPadding.Text = string.Empty;
        }

        var suppressModeRefresh = false;

        bool TryEncode(bool setStatusMessage)
        {
            try
            {
                var plain = txtEncodeIn.Text ?? string.Empty;
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
                var useBase64Url = rbEncodeBase64Url.IsChecked == true;
                txtEncodeIncludingPadding.Text = useBase64Url ? ToBase64UrlPadded(base64) : base64;
                txtEncodeOut.Text = useBase64Url ? ToBase64Url(base64) : base64;
                if (setStatusMessage)
                    SetStatus(useBase64Url ? "Encode: OK (Base64URL, UTF-8)." : "Encode: OK (Base64, UTF-8).");
                return true;
            }
            catch (Exception ex)
            {
                ClearEncodePaddedOutputs();
                if (setStatusMessage)
                    SetStatus($"Encode: {ex.Message}", Brushes.IndianRed);
                return false;
            }
        }

        bool TryDecode(bool setStatusMessage)
        {
            bool TryDecodeBytes(string source, bool decodeAsBase64Url, out byte[] bytes)
            {
                bytes = [];
                try
                {
                    var normalized = NormalizeBase64Input(source, decodeAsBase64Url);
                    if (string.IsNullOrEmpty(normalized))
                        return false;

                    bytes = Convert.FromBase64String(normalized);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                var rawDecode = txtDecodeIn.Text ?? string.Empty;
                var useBase64Url = rbDecodeBase64Url.IsChecked == true;
                if (string.IsNullOrEmpty(rawDecode))
                {
                    txtDecodeOut.Text = string.Empty;
                    ClearDecodePaddedOutputs();
                    if (setStatusMessage)
                        SetStatus("Decode: nothing to decode.");
                    return true;
                }

                var isStrictValid = IsValidBase64Syntax(rawDecode, useBase64Url);
                if (!isStrictValid)
                {
                    var decoded = TryDecodeBytes(rawDecode, useBase64Url, out var strictModeBytes);
                    if (!decoded)
                    {
                        // Fallback: try the opposite flavor so we can show best-effort output.
                        decoded = TryDecodeBytes(rawDecode, !useBase64Url, out strictModeBytes);
                    }

                    if (!decoded)
                    {
                        txtDecodeOut.Text = string.Empty;
                        ClearDecodePaddedOutputs();
                        if (setStatusMessage)
                            SetStatus(useBase64Url
                                ? "Decode: invalid Base64URL input (allowed: A-Z a-z 0-9 - _). Could not decode."
                                : "Decode: invalid Base64 input (allowed: A-Z a-z 0-9 + / =). Could not decode.",
                                Brushes.IndianRed);
                        return false;
                    }

                    txtDecodeOut.Text = Encoding.UTF8.GetString(strictModeBytes);
                    var paddedBase64Fallback = Convert.ToBase64String(strictModeBytes);
                    txtDecodeIncludingPadding.Text = useBase64Url
                        ? ToBase64UrlPadded(paddedBase64Fallback)
                        : paddedBase64Fallback;
                    if (setStatusMessage)
                        SetStatus(useBase64Url
                            ? "Decode: invalid Base64URL input (allowed: A-Z a-z 0-9 - _). Showing best-effort decode."
                            : "Decode: invalid Base64 input (allowed: A-Z a-z 0-9 + / =). Showing best-effort decode.",
                            Brushes.IndianRed);
                    return false;
                }

                var bytes = Convert.FromBase64String(NormalizeBase64Input(rawDecode, useBase64Url));
                txtDecodeOut.Text = Encoding.UTF8.GetString(bytes);
                var paddedBase64 = Convert.ToBase64String(bytes);
                txtDecodeIncludingPadding.Text = useBase64Url ? ToBase64UrlPadded(paddedBase64) : paddedBase64;
                if (setStatusMessage)
                    SetStatus(useBase64Url ? "Decode: OK (Base64URL, UTF-8)." : "Decode: OK (Base64, UTF-8).");
                return true;
            }
            catch (FormatException)
            {
                txtDecodeOut.Text = string.Empty;
                ClearDecodePaddedOutputs();
                if (setStatusMessage)
                    SetStatus("Decode: invalid Base64/Base64URL input.", Brushes.IndianRed);
                return false;
            }
            catch (Exception ex)
            {
                txtDecodeOut.Text = string.Empty;
                ClearDecodePaddedOutputs();
                if (setStatusMessage)
                    SetStatus($"Decode: {ex.Message}", Brushes.IndianRed);
                return false;
            }
        }

        void RefreshAllForModeChange()
        {
            if (suppressModeRefresh)
                return;

            var encodeOk = TryEncode(false);
            var decodeOk = TryDecode(false);

            if (encodeOk && decodeOk)
                SetStatus("Mode changed: encode/decode outputs refreshed.");
            else if (encodeOk)
                SetStatus("Mode changed: encode refreshed; decode has invalid input.", Brushes.IndianRed);
            else if (decodeOk)
                SetStatus("Mode changed: decode refreshed; encode failed.", Brushes.IndianRed);
            else
                SetStatus("Mode changed: unable to refresh outputs from current inputs.", Brushes.IndianRed);
        }

        rbEncodeBase64.Checked += (_, _) => RefreshAllForModeChange();
        rbEncodeBase64Url.Checked += (_, _) => RefreshAllForModeChange();
        rbDecodeBase64.Checked += (_, _) => RefreshAllForModeChange();
        rbDecodeBase64Url.Checked += (_, _) => RefreshAllForModeChange();
        txtEncodeIn.TextChanged += (_, _) =>
        {
            _ = TryEncode(false);
            UpdatePaddingMathCards();
        };
        txtDecodeIn.TextChanged += (_, _) =>
        {
            _ = TryDecode(false);
            UpdatePaddingMathCards();
        };

        txtSharedInput.TextChanged += (_, _) =>
        {
            var raw = txtSharedInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                txtEncodeIn.Text = string.Empty;
                txtDecodeIn.Text = string.Empty;
                txtEncodeOut.Text = string.Empty;
                txtDecodeOut.Text = string.Empty;
                ClearEncodePaddedOutputs();
                ClearDecodePaddedOutputs();
                SetStatus("Quick paste: input cleared.");
                return;
            }

            var isBase64 = IsValidBase64Like(raw, isBase64Url: false);
            var isBase64Url = !isBase64 && IsValidBase64Like(raw, isBase64Url: true);

            txtEncodeIn.Text = raw;

            if (isBase64)
            {
                txtDecodeIn.Text = raw;
                suppressModeRefresh = true;
                rbEncodeBase64.IsChecked = true;
                rbDecodeBase64.IsChecked = true;
                suppressModeRefresh = false;
                _ = TryEncode(false);
                _ = TryDecode(false);
                SetStatus("Quick paste: detected Base64, copied to both inputs, and auto-ran encode/decode.");
                return;
            }

            if (isBase64Url)
            {
                txtDecodeIn.Text = raw;
                suppressModeRefresh = true;
                rbEncodeBase64Url.IsChecked = true;
                rbDecodeBase64Url.IsChecked = true;
                suppressModeRefresh = false;
                _ = TryEncode(false);
                _ = TryDecode(false);
                SetStatus("Quick paste: detected Base64URL, copied to both inputs, and auto-ran encode/decode.");
                return;
            }

            txtDecodeIn.Text = string.Empty;
            txtDecodeOut.Text = string.Empty;
            ClearDecodePaddedOutputs();
            _ = TryEncode(false);
            SetStatus("Quick paste: treated as text, copied to encode input only, and auto-ran encode.");
        };

        btnEncode.Click += (_, _) =>
        {
            _ = TryEncode(true);
        };

        btnExplainEncode.Click += (_, _) =>
        {
            var useBase64Url = rbEncodeBase64Url.IsChecked == true;
            MessageBox.Show(
                dlg,
                BuildBase64CalculationText(txtEncodeIn.Text ?? string.Empty, useBase64Url),
                "Encode Calculation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };

        btnDecode.Click += (_, _) =>
        {
            _ = TryDecode(true);
        };

        btnClose.Click += (_, _) => dlg.Close();

        UpdatePaddingMathCards();
        dlg.Content = root;
        dlg.ShowDialog();
    }
}

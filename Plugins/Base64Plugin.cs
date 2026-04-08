using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static string NormalizeBase64Input(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }

        var s = sb.ToString();
        var mod = s.Length % 4;
        if (mod == 0)
            return s;
        if (mod == 1)
            return s;

        return s + new string('=', 4 - mod);
    }

    private void ShowBase64Dialog()
    {
        var dlg = new Window
        {
            Title = "Base64",
            Width = 880,
            Height = 520,
            MinWidth = 560,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
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
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        encodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var encodeTitle = new TextBlock
        {
            Text = "Encode",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(encodeTitle, 0);
        encodeColumn.Children.Add(encodeTitle);

        var lblEncodeIn = new TextBlock
        {
            Text = "Input",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblEncodeIn, 1);
        encodeColumn.Children.Add(lblEncodeIn);

        var txtEncodeIn = CreateBase64TextBox();
        Grid.SetRow(txtEncodeIn, 2);
        encodeColumn.Children.Add(txtEncodeIn);

        var btnEncode = new Button
        {
            Content = "Encode",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(btnEncode, 3);
        encodeColumn.Children.Add(btnEncode);

        var lblEncodeOut = new TextBlock
        {
            Text = "Output",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblEncodeOut, 4);
        encodeColumn.Children.Add(lblEncodeOut);

        var txtEncodeOut = CreateBase64TextBox();
        Grid.SetRow(txtEncodeOut, 5);
        encodeColumn.Children.Add(txtEncodeOut);

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
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        decodeColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var decodeTitle = new TextBlock
        {
            Text = "Decode",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(decodeTitle, 0);
        decodeColumn.Children.Add(decodeTitle);

        var lblDecodeIn = new TextBlock
        {
            Text = "Input",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblDecodeIn, 1);
        decodeColumn.Children.Add(lblDecodeIn);

        var txtDecodeIn = CreateBase64TextBox();
        Grid.SetRow(txtDecodeIn, 2);
        decodeColumn.Children.Add(txtDecodeIn);

        var btnDecode = new Button
        {
            Content = "Decode",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(btnDecode, 3);
        decodeColumn.Children.Add(btnDecode);

        var lblDecodeOut = new TextBlock
        {
            Text = "Output",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(lblDecodeOut, 4);
        decodeColumn.Children.Add(lblDecodeOut);

        var txtDecodeOut = CreateBase64TextBox();
        Grid.SetRow(txtDecodeOut, 5);
        decodeColumn.Children.Add(txtDecodeOut);

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

        btnEncode.Click += (_, _) =>
        {
            try
            {
                var plain = txtEncodeIn.Text ?? string.Empty;
                txtEncodeOut.Text = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
                SetStatus("Encode: OK (UTF-8).");
            }
            catch (Exception ex)
            {
                SetStatus($"Encode: {ex.Message}", Brushes.IndianRed);
            }
        };

        btnDecode.Click += (_, _) =>
        {
            try
            {
                var b64 = NormalizeBase64Input(txtDecodeIn.Text);
                if (string.IsNullOrEmpty(b64))
                {
                    txtDecodeOut.Text = string.Empty;
                    SetStatus("Decode: nothing to decode.");
                    return;
                }

                var bytes = Convert.FromBase64String(b64);
                txtDecodeOut.Text = Encoding.UTF8.GetString(bytes);
                SetStatus("Decode: OK (UTF-8).");
            }
            catch (FormatException)
            {
                SetStatus("Decode: invalid Base64.", Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                SetStatus($"Decode: {ex.Message}", Brushes.IndianRed);
            }
        };

        btnClose.Click += (_, _) => dlg.Close();

        dlg.Content = root;
        dlg.ShowDialog();
    }
}

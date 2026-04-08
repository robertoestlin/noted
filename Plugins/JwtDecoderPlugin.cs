using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static byte[] Base64UrlDecodeBytes(string segment)
    {
        var s = segment.Trim().Replace('-', '+').Replace('_', '/');
        var mod = s.Length % 4;
        if (mod == 1)
            throw new FormatException("Invalid Base64url segment length.");

        if (mod > 0)
            s += new string('=', 4 - mod);

        return Convert.FromBase64String(s);
    }

    private static string TryPrettyPrintJson(string utf8Text)
    {
        try
        {
            using var doc = JsonDocument.Parse(utf8Text);
            return JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return utf8Text;
        }
    }

    private const string JwtIoHomeUrl = "https://jwt.io/";
    private const string JwtIoIntroductionUrl = "https://www.jwt.io/introduction#what-is-json-web-token";

    private const string JwtSampleMongoDbAtlas =
        "eyJhbGciOiJFUzUxMiIsInR5cCI6IkpXVCIsImtpZCI6Im1yay01ZzFkZDU1Y2MwZDc1YzdhYTAxZzhkMjkxMDgxY2czYyJ9.eyJpc3MiOiJodHRwczovL2Nsb3VkLm1vbmdvZGIuY29tIiwiYXVkIjoiYXBpOi8vYWRtaW4iLCJzdWIiOiJtZGJfc2FfaWRfNzBlNGY5MmU5NGQ4OGRmZzk1MTM5NjU1IiwiaWF0IjoxNzc1NTAwMjMxLCJuYmYiOjE3NzU1MDAyMzEsImV4cCI6MTc3NTUwMzgzMSwianRpIjoiMjNiNDI4MjctNjRmZS01OTQwLTkyZ2UtNzVlOTFmMmY0YmUxIiwiYWN0b3JJZCI6Im1kYl9zYV9pZF83MGU0ZjkyZTk0ZDg4ZGZmOTUxMzk2NTUiLCJzZXNzaW9uU3ViIjoibWRiX3NhX2lkXzcwZTRmOTJlOTRkODhkZmY5NTEzOTY1NSIsInNlc3Npb25JZCI6IjEyM2M4OWQ5LTA0OTMtNTE1Ny1jNGMzLWc3NmYyZTQxOTU1ZCIsImNpZCI6Im1kYl9zYV9pZF83MGU0ZjkyZTk0ZDg4ZGZmOTUxMzk2NTUifQ.ANFfhFi0c7tpCF3OZfxhUWTUtXlERO0eDFToZKhQZZa5KWdKQ-ccjfntOP3EdVoapUfLs-t7ryQbJqIo7lK4bFf6ARkJwRhxHdhAGo--yPGKzi3j-hDgiq8tHvPotDOAsF51ZxM9uIbqmG04KbyrKg5MMFFoTTbl5wwp9G_6-EI_t_Pm";

    private void ShowJwtDecoderDialog()
    {
        var dlg = new Window
        {
            Title = "JWT Decoder",
            Width = 920,
            Height = 720,
            MinWidth = 560,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
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

        static TextBox CreateOutputBox() => new()
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = true,
            MinHeight = 140
        };

        var lblStructure = new TextBlock
        {
            Text = "A JWT has three parts: Header, Payload, and Signature — xxxxx.yyyyy.zzzzz (each segment is Base64url-encoded).",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var lblIn = new TextBlock
        {
            Text = "JWT (paste compact token; optional \"Bearer \" prefix is stripped)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var txtJwt = new TextBox
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var btnDecode = new Button
        {
            Content = "Decode",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 100
        };
        var btnPasteSample = new Button
        {
            Content = "Paste sample JWT",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnRow.Children.Add(btnDecode);
        btnRow.Children.Add(btnPasteSample);

        var splitGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerCol = new StackPanel();
        headerCol.Children.Add(new TextBlock
        {
            Text = "Header",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtHeader = CreateOutputBox();
        headerCol.Children.Add(txtHeader);
        Grid.SetColumn(headerCol, 0);
        splitGrid.Children.Add(headerCol);

        var gutter = new Border { Width = 8 };
        Grid.SetColumn(gutter, 1);
        splitGrid.Children.Add(gutter);

        var payloadCol = new StackPanel();
        payloadCol.Children.Add(new TextBlock
        {
            Text = "Payload",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtPayload = CreateOutputBox();
        payloadCol.Children.Add(txtPayload);
        Grid.SetColumn(payloadCol, 2);
        splitGrid.Children.Add(payloadCol);

        var lblSig = new TextBlock
        {
            Text = "Signature (Base64url, not verified)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var txtSignature = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 56,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var lblNote = new TextBlock
        {
            Text = "This tool only decodes segments. It does not verify the signature or check expiry.",
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0)
        };

        var jwtDocLine = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            FontSize = 11
        };
        jwtDocLine.Inlines.Add("Documentation: ");
        var linkJwtIo = new Hyperlink(new Run("jwt.io"))
        {
            NavigateUri = new Uri(JwtIoHomeUrl)
        };
        linkJwtIo.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri!.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        jwtDocLine.Inlines.Add(linkJwtIo);
        jwtDocLine.Inlines.Add(new Run(" · "));
        var linkIntro = new Hyperlink(new Run("Introduction"))
        {
            NavigateUri = new Uri(JwtIoIntroductionUrl)
        };
        linkIntro.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri!.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        jwtDocLine.Inlines.Add(linkIntro);

        var mainStack = new StackPanel();
        mainStack.Children.Add(lblStructure);
        mainStack.Children.Add(lblIn);
        mainStack.Children.Add(txtJwt);
        mainStack.Children.Add(btnRow);
        mainStack.Children.Add(splitGrid);
        mainStack.Children.Add(lblSig);
        mainStack.Children.Add(txtSignature);
        mainStack.Children.Add(lblNote);
        mainStack.Children.Add(jwtDocLine);

        root.Children.Add(mainStack);

        btnClose.Click += (_, _) => dlg.Close();

        void DecodeJwt()
        {
            txtHeader.Text = string.Empty;
            txtPayload.Text = string.Empty;
            txtSignature.Text = string.Empty;

            var raw = (txtJwt.Text ?? string.Empty).Trim();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring("Bearer ".Length).Trim();

            if (string.IsNullOrEmpty(raw))
            {
                SetStatus("Paste a JWT to decode.", Brushes.IndianRed);
                return;
            }

            var parts = raw.Split('.');
            if (parts.Length < 2)
            {
                SetStatus("A JWT must have at least a header and payload (dot-separated segments).", Brushes.IndianRed);
                return;
            }

            try
            {
                var headerJson = Encoding.UTF8.GetString(Base64UrlDecodeBytes(parts[0]));
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecodeBytes(parts[1]));

                txtHeader.Text = TryPrettyPrintJson(headerJson);
                txtPayload.Text = TryPrettyPrintJson(payloadJson);

                txtSignature.Text = parts.Length >= 3
                    ? parts[2]
                    : "(no signature segment)";

                SetStatus(parts.Length >= 3
                    ? "Decoded header and payload. Signature shown as raw Base64url (not verified)."
                    : "Decoded header and payload (two segments only).");
            }
            catch (FormatException ex)
            {
                SetStatus(ex.Message, Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                SetStatus($"Decode failed: {ex.Message}", Brushes.IndianRed);
            }
        }

        btnDecode.Click += (_, _) => DecodeJwt();

        btnPasteSample.Click += (_, _) =>
        {
            txtJwt.Text = JwtSampleMongoDbAtlas;
            DecodeJwt();
        };

        dlg.Content = root;
        dlg.ShowDialog();
    }
}

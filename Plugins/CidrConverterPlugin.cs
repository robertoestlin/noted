using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static bool TryParseIpv4(string? text, out uint value)
    {
        value = 0;
        var parts = (text ?? string.Empty).Trim().Split('.');
        if (parts.Length != 4)
            return false;

        uint parsed = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var octet))
                return false;
            parsed = (parsed << 8) | octet;
        }

        value = parsed;
        return true;
    }

    private static string ToIpv4String(uint value)
        => string.Create(CultureInfo.InvariantCulture, $"{(value >> 24) & 255}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}");

    private static uint PrefixToMask(int prefixLength)
    {
        if (prefixLength <= 0)
            return 0;
        if (prefixLength >= 32)
            return 0xFFFFFFFFu;
        return 0xFFFFFFFFu << (32 - prefixLength);
    }

    private static bool TryMaskToPrefix(uint mask, out int prefixLength)
    {
        prefixLength = 0;
        var sawZero = false;
        for (var bit = 31; bit >= 0; bit--)
        {
            var isSet = ((mask >> bit) & 1u) == 1u;
            if (isSet)
            {
                if (sawZero)
                    return false;
                prefixLength++;
            }
            else
            {
                sawZero = true;
            }
        }

        return true;
    }

    private static bool TryParsePrefixOrMask(string? value, out int prefixLength)
    {
        prefixLength = -1;
        var trimmed = (value ?? string.Empty).Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefix))
        {
            if (prefix is < 0 or > 32)
                return false;
            prefixLength = prefix;
            return true;
        }

        if (!TryParseIpv4(trimmed, out var mask))
            return false;

        return TryMaskToPrefix(mask, out prefixLength);
    }

    private static bool TryParseCidr(string? cidrText, out uint ip, out int prefixLength)
    {
        ip = 0;
        prefixLength = -1;
        var raw = (cidrText ?? string.Empty).Trim();
        var slashIndex = raw.IndexOf('/');
        if (slashIndex <= 0 || slashIndex == raw.Length - 1 || raw.IndexOf('/', slashIndex + 1) >= 0)
            return false;

        var ipPart = raw[..slashIndex];
        var prefixPart = raw[(slashIndex + 1)..];
        if (!TryParseIpv4(ipPart, out ip))
            return false;
        if (!int.TryParse(prefixPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out prefixLength))
            return false;
        return prefixLength is >= 0 and <= 32;
    }

    private void ShowCidrConverterDialog()
    {
        var dlg = new Window
        {
            Title = "CIDR Converter",
            Width = 920,
            Height = 860,
            MinWidth = 820,
            MinHeight = 820,
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

        static TextBlock Label(string text, Thickness? margin = null) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = margin ?? new Thickness(0, 0, 0, 4)
        };

        static TextBox ReadOnlyField()
            => new()
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Margin = new Thickness(0, 0, 0, 8)
            };

        var content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var panel = new StackPanel();
        content.Content = panel;

        var intro = new TextBlock
        {
            Text = "Convert between CIDR, subnet mask, and IPv4 network ranges.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(intro);

        panel.Children.Add(Label("CIDR input (e.g., 192.168.10.14/24)"));
        var txtCidrIn = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtCidrIn);

        var btnFromCidr = new Button
        {
            Content = "Convert CIDR",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 120,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(btnFromCidr);

        var cidrGrid = new Grid();
        cidrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cidrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 9; i++)
            cidrGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(cidrGrid);

        TextBox AddCidrOutput(string labelText, int row)
        {
            var label = Label(labelText, new Thickness(0, row == 0 ? 0 : 2, 8, 8));
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            cidrGrid.Children.Add(label);

            var box = ReadOnlyField();
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            cidrGrid.Children.Add(box);
            return box;
        }

        var txtIp = AddCidrOutput("IP", 0);
        var txtPrefix = AddCidrOutput("Prefix", 1);
        var txtSubnetMask = AddCidrOutput("Subnet mask", 2);
        var txtWildcard = AddCidrOutput("Wildcard mask", 3);
        var txtNetwork = AddCidrOutput("Network address", 4);
        var txtBroadcast = AddCidrOutput("Broadcast address", 5);
        var txtHostRange = AddCidrOutput("Usable host range", 6);
        var txtAddressCount = AddCidrOutput("Total address count", 7);
        var txtUsableCount = AddCidrOutput("Usable host count", 8);

        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 10) });
        panel.Children.Add(Label("Build CIDR from IPv4 + prefix or subnet mask"));

        panel.Children.Add(Label("IPv4 address"));
        var txtIpIn = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtIpIn);

        panel.Children.Add(Label("Prefix length or subnet mask (e.g., 24 or 255.255.255.0)"));
        var txtMaskOrPrefixIn = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtMaskOrPrefixIn);

        var btnToCidr = new Button
        {
            Content = "Build CIDR",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(btnToCidr);

        panel.Children.Add(Label("CIDR result"));
        var txtCidrOut = ReadOnlyField();
        panel.Children.Add(txtCidrOut);

        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 10) });
        panel.Children.Add(Label("IP in CIDR range?"));

        panel.Children.Add(Label("IP to check"));
        var txtRangeIpIn = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(txtRangeIpIn);

        var btnCheckRange = new Button
        {
            Content = "Check IP in Range",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(btnCheckRange);

        var txtRangeResult = new TextBlock
        {
            Text = "Uses the CIDR input above.",
            Foreground = Brushes.DimGray,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(txtRangeResult);

        void PopulateFromNetwork(uint ip, int prefixLength)
        {
            var mask = PrefixToMask(prefixLength);
            var network = ip & mask;
            var broadcast = network | ~mask;
            var wildcard = ~mask;
            var totalAddresses = 1UL << (32 - prefixLength);
            var usableHosts = prefixLength switch
            {
                32 => 1UL,
                31 => 2UL,
                _ => totalAddresses - 2
            };

            string hostRange;
            if (prefixLength == 32)
            {
                hostRange = $"{ToIpv4String(ip)} (single host)";
            }
            else if (prefixLength == 31)
            {
                hostRange = $"{ToIpv4String(network)} - {ToIpv4String(broadcast)}";
            }
            else
            {
                hostRange = $"{ToIpv4String(network + 1)} - {ToIpv4String(broadcast - 1)}";
            }

            txtIp.Text = ToIpv4String(ip);
            txtPrefix.Text = $"/{prefixLength}";
            txtSubnetMask.Text = ToIpv4String(mask);
            txtWildcard.Text = ToIpv4String(wildcard);
            txtNetwork.Text = ToIpv4String(network);
            txtBroadcast.Text = ToIpv4String(broadcast);
            txtHostRange.Text = hostRange;
            txtAddressCount.Text = totalAddresses.ToString(CultureInfo.InvariantCulture);
            txtUsableCount.Text = usableHosts.ToString(CultureInfo.InvariantCulture);
            txtCidrOut.Text = $"{ToIpv4String(network)}/{prefixLength}";
        }

        bool TryPopulateFromCidrInput(bool updateStatusOnFailure)
        {
            if (!TryParseCidr(txtCidrIn.Text, out var ip, out var prefixLength))
            {
                if (updateStatusOnFailure)
                    SetStatus("Enter a valid CIDR like 10.0.2.15/24.", Brushes.IndianRed);
                return false;
            }

            PopulateFromNetwork(ip, prefixLength);
            SetStatus("CIDR converted.");
            return true;
        }

        btnFromCidr.Click += (_, _) =>
        {
            _ = TryPopulateFromCidrInput(updateStatusOnFailure: true);
        };

        txtCidrIn.TextChanged += (_, _) =>
        {
            _ = TryPopulateFromCidrInput(updateStatusOnFailure: false);
        };

        btnToCidr.Click += (_, _) =>
        {
            if (!TryParseIpv4(txtIpIn.Text, out var ip))
            {
                SetStatus("Enter a valid IPv4 address.", Brushes.IndianRed);
                return;
            }

            if (!TryParsePrefixOrMask(txtMaskOrPrefixIn.Text, out var prefixLength))
            {
                SetStatus("Enter a valid prefix length (0-32) or contiguous subnet mask.", Brushes.IndianRed);
                return;
            }

            var canonicalCidr = $"{ToIpv4String(ip & PrefixToMask(prefixLength))}/{prefixLength}";
            txtCidrOut.Text = canonicalCidr;
            txtCidrIn.Text = canonicalCidr;
            PopulateFromNetwork(ip, prefixLength);
            SetStatus("CIDR built from input.");
        };

        btnCheckRange.Click += (_, _) =>
        {
            var cidrText = string.IsNullOrWhiteSpace(txtCidrIn.Text)
                ? txtCidrOut.Text
                : txtCidrIn.Text;

            if (!TryParseCidr(cidrText, out var cidrIp, out var prefixLength))
            {
                txtRangeResult.Text = "Invalid CIDR range.";
                txtRangeResult.Foreground = Brushes.IndianRed;
                SetStatus("Enter a valid CIDR in the input above.", Brushes.IndianRed);
                return;
            }

            if (!TryParseIpv4(txtRangeIpIn.Text, out var testIp))
            {
                txtRangeResult.Text = "Invalid IPv4 address.";
                txtRangeResult.Foreground = Brushes.IndianRed;
                SetStatus("Enter a valid IPv4 address to test.", Brushes.IndianRed);
                return;
            }

            var mask = PrefixToMask(prefixLength);
            var network = cidrIp & mask;
            var isInRange = (testIp & mask) == network;

            txtRangeResult.Text = isInRange
                ? "In range"
                : "Not in range";
            txtRangeResult.Foreground = isInRange
                ? Brushes.ForestGreen
                : Brushes.IndianRed;
            SetStatus("CIDR membership checked.");
        };

        btnClose.Click += (_, _) => dlg.Close();
        root.Children.Add(content);
        dlg.Content = root;
        dlg.ShowDialog();
    }
}

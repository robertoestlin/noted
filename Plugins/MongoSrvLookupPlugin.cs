using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DnsClient;
using DnsClient.Protocol;

namespace Noted;

public partial class MainWindow
{
    private static bool TryRedactMongoConnectionStringPassword(string? input, out string redacted)
    {
        redacted = (input ?? string.Empty).Trim();
        var text = redacted;

        const string mongo = "mongodb://";
        const string mongoSrv = "mongodb+srv://";
        var hasMongoScheme = text.StartsWith(mongo, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(mongoSrv, StringComparison.OrdinalIgnoreCase);
        if (!hasMongoScheme)
            return false;

        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;
        var authorityStart = schemeEnd + 3;
        var authorityEnd = text.IndexOfAny(['/', '?'], authorityStart);
        if (authorityEnd < 0)
            authorityEnd = text.Length;

        var authority = text.Substring(authorityStart, authorityEnd - authorityStart);
        var atIndex = authority.LastIndexOf('@');
        if (atIndex <= 0)
            return false;

        var userInfo = authority[..atIndex];
        var hostInfo = authority[(atIndex + 1)..];
        var colonIndex = userInfo.IndexOf(':');
        if (colonIndex < 0)
            return false;

        var username = userInfo[..colonIndex];
        var redactedUserInfo = $"{username}:PWD_REDACTED";
        var rebuiltAuthority = $"{redactedUserInfo}@{hostInfo}";
        redacted = string.Concat(text.AsSpan(0, authorityStart), rebuiltAuthority, text.AsSpan(authorityEnd));
        return !string.Equals(redacted, text, StringComparison.Ordinal);
    }

    private static bool TryNormalizeMongoSrvLookupInput(string? input, out string srvRecord)
    {
        srvRecord = string.Empty;
        var text = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            var hostPart = text["mongodb+srv://".Length..];
            var slashIndex = hostPart.IndexOf('/');
            if (slashIndex >= 0)
                hostPart = hostPart[..slashIndex];
            var atIndex = hostPart.LastIndexOf('@');
            if (atIndex >= 0)
                hostPart = hostPart[(atIndex + 1)..];
            hostPart = hostPart.Trim();
            if (string.IsNullOrWhiteSpace(hostPart))
                return false;
            text = $"_mongodb._tcp.{hostPart}";
        }

        if (text.EndsWith("."))
            text = text[..^1];

        if (!text.StartsWith("_mongodb._tcp.", StringComparison.OrdinalIgnoreCase))
        {
            var host = text;
            if (host.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
            {
                host = host["mongodb://".Length..];
                var slashIndex = host.IndexOf('/');
                if (slashIndex >= 0)
                    host = host[..slashIndex];
                var atIndex = host.LastIndexOf('@');
                if (atIndex >= 0)
                    host = host[(atIndex + 1)..];
                var commaIndex = host.IndexOf(',');
                if (commaIndex >= 0)
                    host = host[..commaIndex];
                var colonIndex = host.LastIndexOf(':');
                if (colonIndex >= 0 && colonIndex < host.Length - 1 && int.TryParse(host[(colonIndex + 1)..], out _))
                    host = host[..colonIndex];
                host = host.Trim();
            }

            // Accept plain cluster host input like: cluster1-pl-0.odjn3h.mongodb.net
            if (!string.IsNullOrWhiteSpace(host))
                text = $"_mongodb._tcp.{host}";
        }

        if (!text.StartsWith("_mongodb._tcp.", StringComparison.OrdinalIgnoreCase))
            return false;

        srvRecord = text;
        return true;
    }

    private void ShowMongoSrvLookupDialog()
    {
        var dlg = new Window
        {
            Title = "MongoDB SRV DNS Lookup",
            Width = 860,
            Height = 560,
            MinWidth = 700,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel();

        var topHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topHeader.Children.Add(new TextBlock
        {
            Text = "Cluster host or connection string",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var btnInfo = new Button
        {
            Content = "i",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Bold,
            ToolTip = "Examples"
        };
        Grid.SetColumn(btnInfo, 1);
        topHeader.Children.Add(btnInfo);
        top.Children.Add(topHeader);
        var txtInput = new TextBox
        {
            Text = string.Empty,
            Margin = new Thickness(0, 0, 0, 8)
        };
        top.Children.Add(txtInput);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var btnLookup = new Button
        {
            Content = "Lookup SRV",
            Padding = new Thickness(14, 5, 14, 5),
            MinWidth = 110
        };
        buttonRow.Children.Add(btnLookup);
        top.Children.Add(buttonRow);

        var txtOutput = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New")
        };

        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        };

        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true,
            IsDefault = true
        };
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        bottom.Children.Add(btnClose);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(status);
        root.Children.Add(txtOutput);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        bool lookupOnNextTextChange = false;

        async Task RunLookupAsync()
        {
            btnLookup.IsEnabled = false;

            if (TryRedactMongoConnectionStringPassword(txtInput.Text, out var redactedInput))
                txtInput.Text = redactedInput;

            if (!TryNormalizeMongoSrvLookupInput(txtInput.Text, out var query))
            {
                SetStatus("Enter a valid _mongodb._tcp.<cluster-host> value or mongodb+srv:// URI.", Brushes.IndianRed);
                txtOutput.Text = string.Empty;
                return;
            }

            SetStatus("Looking up DNS SRV records...");
            txtOutput.Text = string.Empty;

            try
            {
                var lookup = new LookupClient();
                var result = await lookup.QueryAsync(query, QueryType.SRV).ConfigureAwait(true);
                var records = result.Answers.SrvRecords()
                    .OrderBy(record => record.Priority)
                    .ThenBy(record => record.Weight)
                    .ThenBy(record => record.Port)
                    .ToList();

                var sb = new StringBuilder();
                if (records.Count == 0)
                {
                    sb.AppendLine("No SRV records found.");
                    txtOutput.Text = sb.ToString();
                    SetStatus("No SRV records returned.", Brushes.IndianRed);
                    return;
                }

                foreach (var record in records)
                {
                    sb.AppendLine($"{query} service = {record.Priority} {record.Weight} {record.Port} {record.Target}");
                }

                var ports = records
                    .Select(record => record.Port)
                    .Distinct()
                    .OrderBy(port => port)
                    .ToList();
                if (ports.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Ports found: {string.Join(", ", ports)}");
                }

                var nameservers = result.Authorities.NsRecords()
                    .Select(ns => ns.NSDName.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (nameservers.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Authoritative nameservers:");
                    foreach (var ns in nameservers)
                        sb.AppendLine(ns);
                }

                txtOutput.Text = sb.ToString();
                SetStatus($"Found {records.Count} SRV record(s).");
            }
            catch (DnsResponseException ex)
            {
                txtOutput.Text = string.Empty;
                SetStatus($"DNS lookup failed: {ex.Code}", Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                txtOutput.Text = string.Empty;
                SetStatus($"Lookup failed: {ex.Message}", Brushes.IndianRed);
            }
            finally
            {
                btnLookup.IsEnabled = true;
            }
        }

        btnLookup.Click += async (_, _) => await RunLookupAsync().ConfigureAwait(true);
        btnInfo.Click += (_, _) =>
        {
            var scheme = "mongodb+srv";
            var schemeSeparator = "://";
            var sampleUser = "sampleuser";
            var samplePass = "samplepass";
            var host = "cluster1-pl-0.abcd1e.mongodb.net";
            var sampleConnectionString = $"{scheme}{schemeSeparator}{sampleUser}:{samplePass}@{host}";
            var examplesWindow = new Window
            {
                Title = "MongoDB SRV Lookup - Examples",
                Width = 680,
                Height = 230,
                MinWidth = 640,
                MinHeight = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg,
                ResizeMode = ResizeMode.NoResize
            };

            var examplesRoot = new DockPanel { Margin = new Thickness(12) };
            examplesWindow.Content = examplesRoot;

            var btnExamplesOk = new Button
            {
                Content = "OK",
                Width = 90,
                IsDefault = true,
                IsCancel = true
            };
            var examplesFooter = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            examplesFooter.Children.Add(btnExamplesOk);
            DockPanel.SetDock(examplesFooter, Dock.Bottom);
            examplesRoot.Children.Add(examplesFooter);

            var examplesText = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas, Courier New"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Text =
                    "Examples:\n"
                    + $"-{sampleConnectionString}\n"
                    + $"-{host}\n\n"
                    + "If you paste a connection string with a password, the password is automatically redacted to PWD_REDACTED."
            };
            examplesRoot.Children.Add(examplesText);

            btnExamplesOk.Click += (_, _) => examplesWindow.Close();
            examplesWindow.ShowDialog();
        };
        DataObject.AddPastingHandler(txtInput, (_, _) => lookupOnNextTextChange = true);
        txtInput.TextChanged += async (_, _) =>
        {
            var shouldLookup = lookupOnNextTextChange;
            lookupOnNextTextChange = false;
            if (!shouldLookup)
                return;

            if (TryRedactMongoConnectionStringPassword(txtInput.Text, out var redactedInput))
            {
                txtInput.Text = redactedInput;
                txtInput.CaretIndex = txtInput.Text.Length;
            }

            await RunLookupAsync().ConfigureAwait(true);
        };
        txtInput.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;
            e.Handled = true;
            await RunLookupAsync().ConfigureAwait(true);
        };
        btnClose.Click += (_, _) => dlg.Close();

        dlg.Content = root;
        dlg.ShowDialog();
    }
}

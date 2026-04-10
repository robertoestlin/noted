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
    private static bool TryNormalizeTxtLookupInput(string? input, out string host)
    {
        host = string.Empty;
        var text = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            text = uri.Host;

        var slashIndex = text.IndexOfAny(['/', '?', '#']);
        if (slashIndex >= 0)
            text = text[..slashIndex];

        if (text.EndsWith("."))
            text = text[..^1];

        host = text.Trim();
        return !string.IsNullOrWhiteSpace(host);
    }

    private static string BuildTxtLookupTable(string queryHost, System.Collections.Generic.List<TxtRecord> records)
    {
        const string typeLabel = "TXT";
        const string typeHeader = "Type";
        const string hostHeader = "Domain Name";
        const string ttlHeader = "TTL";
        const string valueHeader = "Record";

        var hostWidth = Math.Max(hostHeader.Length, queryHost.Length) + 2;
        var ttlWidth = Math.Max(ttlHeader.Length, 3) + 2;
        var typeWidth = Math.Max(typeHeader.Length, typeLabel.Length) + 1;

        var sb = new StringBuilder();
        sb.AppendLine($"{typeHeader.PadRight(typeWidth)}{hostHeader.PadRight(hostWidth)}{ttlHeader.PadRight(ttlWidth)}{valueHeader}");
        sb.AppendLine($"{new string('-', typeWidth)}{new string('-', hostWidth)}{new string('-', ttlWidth)}{new string('-', valueHeader.Length + 24)}");

        foreach (var record in records)
        {
            var value = string.Join(string.Empty, record.Text);
            sb.AppendLine($"{typeLabel.PadRight(typeWidth)}{queryHost.PadRight(hostWidth)}{record.InitialTimeToLive.ToString().PadRight(ttlWidth)}{value}");
        }

        return sb.ToString();
    }

    private void ShowTxtLookupDialog()
    {
        var dlg = new Window
        {
            Title = "TXT DNS Lookup",
            Width = 980,
            Height = 620,
            MinWidth = 760,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel();

        top.Children.Add(new TextBlock
        {
            Text = "Domain or host",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtInput = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        top.Children.Add(txtInput);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var btnLookup = new Button
        {
            Content = "Find TXT Records",
            Padding = new Thickness(14, 5, 14, 5),
            MinWidth = 140
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

        async Task RunLookupAsync()
        {
            btnLookup.IsEnabled = false;

            if (!TryNormalizeTxtLookupInput(txtInput.Text, out var queryHost))
            {
                SetStatus("Enter a valid domain or host.", Brushes.IndianRed);
                txtOutput.Text = string.Empty;
                btnLookup.IsEnabled = true;
                return;
            }

            txtInput.Text = queryHost;
            txtInput.CaretIndex = txtInput.Text.Length;
            txtOutput.Text = string.Empty;
            SetStatus("Looking up DNS TXT records...");

            try
            {
                var lookup = new LookupClient();
                var result = await lookup.QueryAsync(queryHost, QueryType.TXT).ConfigureAwait(true);
                var records = result.Answers.TxtRecords()
                    .OrderBy(record => record.DomainName.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(record => string.Join(string.Empty, record.Text), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (records.Count == 0)
                {
                    txtOutput.Text = "No TXT records found.";
                    SetStatus("No TXT records returned.", Brushes.IndianRed);
                    return;
                }

                txtOutput.Text = BuildTxtLookupTable(queryHost, records);
                SetStatus($"Found {records.Count} TXT record(s).");
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

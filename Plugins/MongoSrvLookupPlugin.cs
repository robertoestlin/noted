using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using DnsClient;
using DnsClient.Protocol;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const int MongoSrvLookupHistoryLimit = 75;
    private List<MongoSrvLookupHistoryEntry> _mongoSrvLookupHistory = [];
    private static bool TryRedactMongoConnectionStringPassword(
        string? input,
        out string redacted,
        string passwordPlaceholder = "PWD_REDACTED")
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
        var redactedUserInfo = $"{username}:{passwordPlaceholder}";
        var rebuiltAuthority = $"{redactedUserInfo}@{hostInfo}";
        redacted = string.Concat(text.AsSpan(0, authorityStart), rebuiltAuthority, text.AsSpan(authorityEnd));
        return !string.Equals(redacted, text, StringComparison.Ordinal);
    }

    private const string MongoSrvLookupHistoryStoredPasswordPlaceholder = "redacted";

    private static string SanitizeMongoSrvLookupTextForPersistentHistory(string? text)
    {
        var t = text ?? string.Empty;
        if (TryRedactMongoConnectionStringPassword(t, out var mongoRedacted,
                MongoSrvLookupHistoryStoredPasswordPlaceholder))
            t = mongoRedacted;
        else
            t = t.Replace("PWD_REDACTED", MongoSrvLookupHistoryStoredPasswordPlaceholder, StringComparison.Ordinal);
        return t;
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

    private static string TrimDnsFqdn(string name) =>
        (name ?? string.Empty).Trim().TrimEnd('.');

    private static string FormatPrivateLinkIps(IPAddress[] addresses)
    {
        if (addresses is not { Length: > 0 })
            return "(could not resolve)";

        return string.Join(
            ", ",
            addresses
                .Distinct()
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ThenBy(a => a.ToString(), StringComparer.Ordinal)
                .Select(a => a.ToString()));
    }

    private static async Task<string> RunNslookupAsync(string host, CancellationToken cancellationToken = default)
    {
        host = TrimDnsFqdn(host);
        if (string.IsNullOrEmpty(host))
            return string.Empty;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nslookup",
                    ArgumentList = { host },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var readOut = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readErr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await readOut.ConfigureAwait(false);
            var stderr = await readErr.ConfigureAwait(false);
            var combined = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
                combined.Append(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (combined.Length > 0)
                    combined.AppendLine();
                combined.Append(stderr);
            }
            var text = combined.ToString().TrimEnd();
            return string.IsNullOrEmpty(text) ? "(nslookup produced no output.)" : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"nslookup failed: {ex.Message}";
        }
    }

    private static string TruncateMongoSrvLookupOneLine(string? text, int maxLen)
    {
        var s = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (s.Length <= maxLen)
            return s;
        return s[..(maxLen - 1)] + "…";
    }

    private static string FriendlyMongoSrvLookupQuerySubtitle(string? srvQuery)
    {
        var q = (srvQuery ?? string.Empty).Trim();
        const string prefix = "_mongodb._tcp.";
        if (q.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            q = q[prefix.Length..];
        q = q.TrimEnd('.').Trim();
        return TruncateMongoSrvLookupOneLine(q, 48);
    }

    private static string BuildMongoSrvLookupHistoryListLabel(MongoSrvLookupHistoryEntry entry)
    {
        var when = entry.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var hint = FriendlyMongoSrvLookupQuerySubtitle(entry.SrvQuery);
        if (string.IsNullOrWhiteSpace(hint))
            hint = TruncateMongoSrvLookupOneLine(entry.InputText, 42);
        return $"{when} — {hint}";
    }

    private void PushMongoSrvLookupHistory(string inputText, string srvQuery, string responseText, bool success)
    {
        _mongoSrvLookupHistory.Insert(0, new MongoSrvLookupHistoryEntry
        {
            CreatedUtc = DateTime.UtcNow,
            InputText = SanitizeMongoSrvLookupTextForPersistentHistory(inputText),
            SrvQuery = srvQuery ?? string.Empty,
            ResponseText = SanitizeMongoSrvLookupTextForPersistentHistory(responseText),
            Success = success
        });
        PersistMongoSrvLookupHistory();
    }

    private sealed class MongoSrvLookupHistoryListItem
    {
        public MongoSrvLookupHistoryEntry Entry { get; init; } = new();

        public override string ToString() => BuildMongoSrvLookupHistoryListLabel(Entry);
    }

    private void ShowMongoSrvLookupDialog()
    {
        var dlg = new Window
        {
            Title = "MongoDB SRV DNS Lookup",
            Width = 940,
            Height = 560,
            MinWidth = 820,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var historySelSync = false;

        var outerGrid = new Grid { Margin = new Thickness(12) };
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(256) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var historyHeader = new TextBlock
        {
            Text = "Lookup history",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var lstHistory = new ListBox
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };

        static ListBoxItem? FindMongoSrvAncestorListBoxItem(DependencyObject? source)
        {
            while (source != null && source is not ListBoxItem)
                source = VisualTreeHelper.GetParent(source);
            return source as ListBoxItem;
        }

        lstHistory.ContextMenu = new ContextMenu();

        lstHistory.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (FindMongoSrvAncestorListBoxItem(e.OriginalSource as DependencyObject)?.Content is MongoSrvLookupHistoryListItem clicked)
                lstHistory.SelectedItem = clicked;
        };

        void RefreshMongoSrvLookupHistoryUi(bool selectLatestEntry)
        {
            historySelSync = true;
            lstHistory.Items.Clear();
            foreach (var entry in _mongoSrvLookupHistory)
                lstHistory.Items.Add(new MongoSrvLookupHistoryListItem { Entry = entry });

            historySelSync = false;
            if (selectLatestEntry && lstHistory.Items.Count > 0)
                lstHistory.SelectedIndex = 0;
        }

        var historyPane = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(historyHeader, Dock.Top);
        historyPane.Children.Add(historyHeader);
        historyPane.Children.Add(lstHistory);
        Grid.SetColumn(historyPane, 0);
        outerGrid.Children.Add(historyPane);

        var root = new DockPanel();
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

        const string atlasPrivateEndpointDocs =
            "https://www.mongodb.com/docs/atlas/troubleshoot-private-endpoints/#std-label-pl-troubleshooting";
        var docsFooter = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var docsLink = new Hyperlink(new Run("Atlas private endpoint troubleshooting"))
        {
            NavigateUri = new Uri(atlasPrivateEndpointDocs)
        };
        docsLink.RequestNavigate += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            }
            catch
            {
                // ignore — e.g. no default browser
            }

            e.Handled = true;
        };
        docsFooter.Inlines.Add(docsLink);

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
        DockPanel.SetDock(docsFooter, Dock.Bottom);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(docsFooter);
        root.Children.Add(status);
        root.Children.Add(txtOutput);
        Grid.SetColumn(root, 1);
        outerGrid.Children.Add(root);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        void ApplyMongoSrvHistoryListItemToEditor(MongoSrvLookupHistoryListItem item)
        {
            txtInput.Text = item.Entry.InputText;
            txtOutput.Text = item.Entry.ResponseText;
            SetStatus(item.Entry.Success ? "Saved lookup (history)." : "Saved lookup (history; had errors).",
                item.Entry.Success ? Brushes.DimGray : Brushes.IndianRed);
        }

        bool lookupOnNextTextChange = false;

        lstHistory.SelectionChanged += (_, _) =>
        {
            if (historySelSync || lstHistory.SelectedItem is not MongoSrvLookupHistoryListItem item)
                return;

            ApplyMongoSrvHistoryListItemToEditor(item);
        };

        lstHistory.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (historySelSync)
                return;
            if (FindMongoSrvAncestorListBoxItem(e.OriginalSource as DependencyObject)?.Content is not MongoSrvLookupHistoryListItem clicked)
                return;

            lstHistory.SelectedItem = clicked;
            ApplyMongoSrvHistoryListItemToEditor(clicked);
        };

        var ctxDeleteHistoryItem = new MenuItem { Header = "Delete" };
        ctxDeleteHistoryItem.Click += (_, _) =>
        {
            if (lstHistory.SelectedItem is not MongoSrvLookupHistoryListItem listItem)
                return;

            var deletedEntry = listItem.Entry;
            var deleteIx = _mongoSrvLookupHistory.IndexOf(deletedEntry);
            if (deleteIx < 0 || !_mongoSrvLookupHistory.Remove(deletedEntry))
                return;

            PersistMongoSrvLookupHistory();
            RefreshMongoSrvLookupHistoryUi(false);

            if (_mongoSrvLookupHistory.Count == 0)
            {
                lstHistory.SelectedIndex = -1;
                txtInput.Clear();
                txtOutput.Clear();
            }
            else
                lstHistory.SelectedIndex = Math.Min(deleteIx, _mongoSrvLookupHistory.Count - 1);

            SetStatus("History entry deleted.");
        };
        lstHistory.ContextMenu.Items.Add(ctxDeleteHistoryItem);

        async Task RunLookupAsync()
        {
            btnLookup.IsEnabled = false;

            if (TryRedactMongoConnectionStringPassword(txtInput.Text, out var redactedInput))
                txtInput.Text = redactedInput;

            var inputSnap = txtInput.Text.Trim();

            if (!TryNormalizeMongoSrvLookupInput(txtInput.Text, out var query))
            {
                SetStatus("Enter a valid _mongodb._tcp.<cluster-host> value or mongodb+srv:// URI.", Brushes.IndianRed);
                txtOutput.Text = string.Empty;
                btnLookup.IsEnabled = true;
                return;
            }

            SetStatus("Looking up DNS SRV records...");
            txtOutput.Text = string.Empty;

            try
            {
                var responseText = string.Empty;
                var succeeded = false;
                Brush? statusBrush = Brushes.DimGray;
                var statusFinal = string.Empty;

                try
                {
                    using var nslookupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
                        responseText = sb.ToString();
                        succeeded = false;
                        statusBrush = Brushes.IndianRed;
                        statusFinal = "No SRV records returned.";
                    }
                    else
                    {
                        foreach (var record in records)
                            sb.AppendLine($"{query} service = {record.Priority} {record.Weight} {record.Port} {record.Target}");

                        var ports = records
                            .Select(record => record.Port)
                            .Distinct()
                            .OrderBy(port => port)
                            .ToList();

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

                        var firstTargetHost = TrimDnsFqdn(records[0].Target.Value);
                        if (!string.IsNullOrEmpty(firstTargetHost))
                        {
                            sb.AppendLine();
                            sb.AppendLine($"--- nslookup {firstTargetHost} ---");
                            SetStatus($"Found {records.Count} SRV record(s). Running nslookup…");
                            var nslookupOut = await RunNslookupAsync(firstTargetHost, nslookupCts.Token).ConfigureAwait(true);
                            sb.Append(nslookupOut);
                        }

                        string privateLinkIpText;
                        if (string.IsNullOrEmpty(firstTargetHost))
                            privateLinkIpText = "(n/a)";
                        else
                        {
                            try
                            {
                                var addrs = await Dns.GetHostAddressesAsync(firstTargetHost, nslookupCts.Token)
                                    .ConfigureAwait(true);
                                privateLinkIpText = FormatPrivateLinkIps(addrs);
                            }
                            catch
                            {
                                privateLinkIpText = "(could not resolve)";
                            }
                        }

                        var portsText = ports.Count > 0 ? string.Join(", ", ports) : "—";
                        sb.AppendLine();
                        sb.AppendLine();
                        sb.AppendLine("--- Summary ---");
                        sb.AppendLine($"Private Link IP: {privateLinkIpText}");
                        sb.AppendLine($"Ports: {portsText}");

                        responseText = sb.ToString();
                        succeeded = true;
                        statusFinal = $"Found {records.Count} SRV record(s).";
                    }
                }
                catch (OperationCanceledException)
                {
                    responseText = "Lookup timed out or was canceled.";
                    succeeded = false;
                    statusBrush = Brushes.IndianRed;
                    statusFinal = responseText;
                }
                catch (DnsResponseException ex)
                {
                    responseText = $"DNS lookup failed: {ex.Code}";
                    succeeded = false;
                    statusBrush = Brushes.IndianRed;
                    statusFinal = responseText;
                }
                catch (Exception ex)
                {
                    responseText = $"Lookup failed: {ex.Message}";
                    succeeded = false;
                    statusBrush = Brushes.IndianRed;
                    statusFinal = responseText;
                }

                txtOutput.Text = responseText;
                SetStatus(statusFinal, statusBrush);
                PushMongoSrvLookupHistory(inputSnap, query, responseText, succeeded);
                RefreshMongoSrvLookupHistoryUi(true);
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

        RefreshMongoSrvLookupHistoryUi(false);
        txtInput.Clear();
        txtOutput.Clear();
        SetStatus(string.Empty);

        dlg.Content = outerGrid;
        dlg.ShowDialog();
    }
}

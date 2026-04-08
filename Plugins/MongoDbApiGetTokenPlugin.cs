using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private const string DefaultMongoDbAtlasTokenUrl = "https://cloud.mongodb.com/api/oauth/token";
    private const string DefaultMongoDbAtlasRevokeUrl = "https://cloud.mongodb.com/api/oauth/revoke";
    private const string MongoDbAtlasGenerateTokenDocsUrl = "https://www.mongodb.com/docs/atlas/api/service-accounts/generate-oauth2-token/";

    private static string ResolveMongoDbOAuthRevokeUrl(string tokenEndpointUrl)
    {
        var t = (tokenEndpointUrl ?? string.Empty).Trim();
        if (t.EndsWith("/oauth/token", StringComparison.OrdinalIgnoreCase))
            return string.Concat(t.AsSpan(0, t.Length - "/token".Length), "/revoke");

        return DefaultMongoDbAtlasRevokeUrl;
    }

    private const string IfconfigMeIpUrl = "https://ifconfig.me/ip";
    private const string MongoDbPublicIpLabelPrefix = "Your IP: ";

    private void ShowMongoDbApiGetTokenDialog()
    {
        var dlg = new Window
        {
            Title = "MongoDB API Get Token",
            Width = 640,
            Height = 628,
            MinWidth = 480,
            MinHeight = 600,
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

        var form = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < 6; i++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static TextBlock Label(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var lblUrl = Label("Token URL");
        Grid.SetRow(lblUrl, 0);
        form.Children.Add(lblUrl);

        var txtTokenUrl = new TextBox
        {
            Text = DefaultMongoDbAtlasTokenUrl,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(txtTokenUrl, 1);
        form.Children.Add(txtTokenUrl);

        var lblClientId = Label("Client ID");
        Grid.SetRow(lblClientId, 2);
        form.Children.Add(lblClientId);

        var txtClientId = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(txtClientId, 3);
        form.Children.Add(txtClientId);

        var lblSecret = Label("Client Secret");
        Grid.SetRow(lblSecret, 4);
        form.Children.Add(lblSecret);

        var pwdSecret = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(pwdSecret, 5);
        form.Children.Add(pwdSecret);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var btnGetToken = new Button
        {
            Content = "Get token",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 110
        };
        var btnRevokeToken = new Button
        {
            Content = "Revoke token",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnRow.Children.Add(btnGetToken);
        btnRow.Children.Add(btnRevokeToken);

        var lblAccess = Label("Access token (use as Authorization: Bearer … for Atlas Admin API)");
        var txtAccessToken = new TextBox
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 88,
            MaxHeight = 88
        };

        string? ipForClipboard = null;

        var ipSection = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var ipLineGrid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        ipLineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ipLineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ipLineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtIpPrefix = new TextBlock
        {
            Text = MongoDbPublicIpLabelPrefix,
            FontFamily = new FontFamily("Consolas, Courier New"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var txtIpValue = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Consolas, Courier New"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var btnCopyIp = new Button
        {
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 30,
            MinHeight = 26,
            Visibility = Visibility.Collapsed,
            ToolTip = "Copy IP",
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Content = "\uE8C8",
            FontSize = 14
        };

        Grid.SetColumn(txtIpPrefix, 0);
        Grid.SetColumn(txtIpValue, 1);
        Grid.SetColumn(btnCopyIp, 2);
        ipLineGrid.Children.Add(txtIpPrefix);
        ipLineGrid.Children.Add(txtIpValue);
        ipLineGrid.Children.Add(btnCopyIp);

        var btnGetPublicIp = new Button
        {
            Content = "Get your IP for API Access List",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 6, 16, 6)
        };
        ipSection.Children.Add(btnGetPublicIp);
        ipSection.Children.Add(ipLineGrid);

        var docLine = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        docLine.Inlines.Add("Documentation: ");
        var docHyperlink = new Hyperlink(new Run(MongoDbAtlasGenerateTokenDocsUrl))
        {
            NavigateUri = new Uri(MongoDbAtlasGenerateTokenDocsUrl)
        };
        docHyperlink.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri!.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        docLine.Inlines.Add(docHyperlink);

        var mainStack = new StackPanel();
        mainStack.Children.Add(form);
        mainStack.Children.Add(btnRow);
        mainStack.Children.Add(lblAccess);
        mainStack.Children.Add(txtAccessToken);
        mainStack.Children.Add(docLine);
        mainStack.Children.Add(ipSection);

        root.Children.Add(mainStack);

        btnClose.Click += (_, _) => dlg.Close();

        btnCopyIp.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(ipForClipboard))
                return;
            try
            {
                Clipboard.SetText(ipForClipboard);
            }
            catch
            {
                // ignore clipboard failures
            }
        };

        btnGetPublicIp.Click += async (_, _) =>
        {
            btnGetPublicIp.IsEnabled = false;
            ipLineGrid.Visibility = Visibility.Visible;
            ipForClipboard = null;
            txtIpValue.Text = "";
            btnCopyIp.Visibility = Visibility.Collapsed;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                using var response = await http.GetAsync(new Uri(IfconfigMeIpUrl)).ConfigureAwait(true);
                response.EnsureSuccessStatusCode();
                var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(true)).Trim();
                var line = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? body;
                if (string.IsNullOrWhiteSpace(line))
                {
                    txtIpValue.Text = "(empty response)";
                    return;
                }

                ipForClipboard = line;
                txtIpValue.Text = line;
                btnCopyIp.Visibility = Visibility.Visible;
            }
            catch (HttpRequestException)
            {
                txtIpValue.Text = "(couldn't fetch)";
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ex.CancellationToken.IsCancellationRequested)
            {
                txtIpValue.Text = "(timed out)";
            }
            catch (Exception)
            {
                txtIpValue.Text = "(couldn't fetch)";
            }
            finally
            {
                btnGetPublicIp.IsEnabled = true;
            }
        };

        btnGetToken.Click += async (_, _) =>
        {
            var urlText = (txtTokenUrl.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(urlText))
            {
                SetStatus("Enter a token URL.", Brushes.IndianRed);
                return;
            }

            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var tokenUri) ||
                (tokenUri.Scheme != Uri.UriSchemeHttp && tokenUri.Scheme != Uri.UriSchemeHttps))
            {
                SetStatus("Token URL must be a valid http(s) URL.", Brushes.IndianRed);
                return;
            }

            var clientId = (txtClientId.Text ?? string.Empty).Trim();
            var clientSecret = pwdSecret.Password ?? string.Empty;
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                SetStatus("Enter client ID and client secret.", Brushes.IndianRed);
                return;
            }

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            btnGetToken.IsEnabled = false;
            btnRevokeToken.IsEnabled = false;
            txtAccessToken.Text = string.Empty;
            SetStatus("Requesting token…");

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                });

                using var response = await http.SendAsync(request).ConfigureAwait(true);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var rootEl = doc.RootElement;

                if (rootEl.TryGetProperty("access_token", out var accessTokenEl))
                {
                    var token = accessTokenEl.GetString() ?? string.Empty;
                    txtAccessToken.Text = token;

                    var detail = response.IsSuccessStatusCode ? "OK." : $"HTTP {(int)response.StatusCode}.";
                    if (rootEl.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var sec))
                        SetStatus($"{detail} Expires in {sec} seconds.");
                    else
                        SetStatus($"{detail} Copy the access token for Authorization: Bearer … on Atlas Admin API requests.");
                    return;
                }

                txtAccessToken.Text = string.Empty;
                var err = rootEl.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
                if (string.IsNullOrEmpty(err) && rootEl.TryGetProperty("error", out var er))
                    err = er.GetString();
                if (string.IsNullOrEmpty(err))
                    err = body.Length > 500 ? body.Substring(0, 500) + "…" : body;

                SetStatus(
                    $"Request failed ({(int)response.StatusCode}): {err}",
                    Brushes.IndianRed);
            }
            catch (HttpRequestException ex)
            {
                txtAccessToken.Text = string.Empty;
                SetStatus($"Network error: {ex.Message}", Brushes.IndianRed);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ex.CancellationToken.IsCancellationRequested)
            {
                txtAccessToken.Text = string.Empty;
                SetStatus("Request timed out.", Brushes.IndianRed);
            }
            catch (JsonException ex)
            {
                txtAccessToken.Text = string.Empty;
                SetStatus($"Invalid JSON response: {ex.Message}", Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                txtAccessToken.Text = string.Empty;
                SetStatus(ex.Message, Brushes.IndianRed);
            }
            finally
            {
                btnGetToken.IsEnabled = true;
                btnRevokeToken.IsEnabled = true;
            }
        };

        btnRevokeToken.Click += async (_, _) =>
        {
            var accessToken = (txtAccessToken.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(accessToken))
            {
                SetStatus("No access token to revoke.", Brushes.IndianRed);
                return;
            }

            var urlText = (txtTokenUrl.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(urlText))
            {
                SetStatus("Enter a token URL.", Brushes.IndianRed);
                return;
            }

            if (!Uri.TryCreate(ResolveMongoDbOAuthRevokeUrl(urlText), UriKind.Absolute, out var revokeUri) ||
                (revokeUri.Scheme != Uri.UriSchemeHttp && revokeUri.Scheme != Uri.UriSchemeHttps))
            {
                SetStatus("Could not determine a valid revoke URL.", Brushes.IndianRed);
                return;
            }

            var clientId = (txtClientId.Text ?? string.Empty).Trim();
            var clientSecret = pwdSecret.Password ?? string.Empty;
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                SetStatus("Enter client ID and client secret.", Brushes.IndianRed);
                return;
            }

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            btnGetToken.IsEnabled = false;
            btnRevokeToken.IsEnabled = false;
            SetStatus("Revoking token…");

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                using var request = new HttpRequestMessage(HttpMethod.Post, revokeUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = accessToken,
                    ["token_type_hint"] = "access_token"
                });

                using var response = await http.SendAsync(request).ConfigureAwait(true);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

                if (response.IsSuccessStatusCode)
                {
                    // RFC 7009: revoke endpoints return HTTP 200 both when a token is revoked and when the
                    // token is unknown, invalid, or already revoked — the response does not distinguish them.
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var okDoc = JsonDocument.Parse(body);
                            if (okDoc.RootElement.TryGetProperty("error", out var errEl))
                            {
                                var errDesc = okDoc.RootElement.TryGetProperty("error_description", out var descEl)
                                    ? descEl.GetString()
                                    : errEl.GetString();
                                SetStatus(
                                    string.IsNullOrEmpty(errDesc)
                                        ? "Revoke failed (error in response body)."
                                        : $"Revoke failed: {errDesc}",
                                    Brushes.IndianRed);
                                return;
                            }
                        }
                        catch (JsonException)
                        {
                            // Success with unexpected non-JSON body — still treat as accepted.
                        }
                    }

                    SetStatus(
                        $"HTTP {(int)response.StatusCode}: revoke request accepted. " +
                        "The Atlas/OAuth revoke API does not indicate whether this token was valid — " +
                        "invalid, unknown, or already-revoked tokens receive the same success response (RFC 7009).");
                    return;
                }

                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var rootEl = doc.RootElement;
                var err = rootEl.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
                if (string.IsNullOrEmpty(err) && rootEl.TryGetProperty("error", out var er))
                    err = er.GetString();
                if (string.IsNullOrEmpty(err))
                    err = body.Length > 500 ? body.Substring(0, 500) + "…" : body;

                SetStatus(
                    $"Revoke failed ({(int)response.StatusCode}): {err}",
                    Brushes.IndianRed);
            }
            catch (HttpRequestException ex)
            {
                SetStatus($"Network error: {ex.Message}", Brushes.IndianRed);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ex.CancellationToken.IsCancellationRequested)
            {
                SetStatus("Revoke request timed out.", Brushes.IndianRed);
            }
            catch (JsonException ex)
            {
                SetStatus($"Invalid JSON response: {ex.Message}", Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, Brushes.IndianRed);
            }
            finally
            {
                btnGetToken.IsEnabled = true;
                btnRevokeToken.IsEnabled = true;
            }
        };

        dlg.Content = root;
        dlg.ShowDialog();
    }
}

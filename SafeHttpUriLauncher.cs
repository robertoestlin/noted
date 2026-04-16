using System.Diagnostics;

namespace Noted;

/// <summary>
/// Opens a small set of absolute URIs via the shell (default browser / mail client / FTP handler).
/// Blocks schemes such as file, ms-settings, and other custom protocols.
/// Used for WPF <see cref="System.Windows.Documents.Hyperlink"/> and AvalonEdit Ctrl+click links.
/// </summary>
internal static class SafeHttpUriLauncher
{
    public static bool TryOpenInDefaultBrowser(Uri? uri)
        => TryOpenHyperlinkUri(uri);

    /// <summary>
    /// Allows http, https, ftp (AvalonEdit link regex), and mailto (AvalonEdit email links).
    /// </summary>
    public static bool TryOpenHyperlinkUri(Uri? uri)
    {
        if (!TryGetShellSafeUriString(uri, out string launchUri))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(launchUri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetShellSafeUriString(Uri? uri, out string launchUri)
    {
        launchUri = string.Empty;
        if (uri is null || !uri.IsAbsoluteUri)
            return false;

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(uri.Host))
                return false;
            launchUri = uri.AbsoluteUri;
            return true;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.IsWellFormedUriString(uri.OriginalString, UriKind.Absolute))
                return false;
            launchUri = uri.AbsoluteUri;
            return true;
        }

        return false;
    }
}

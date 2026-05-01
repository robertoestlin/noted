using System.Diagnostics;
using System.IO;
using Noted.Models;

namespace Noted;

/// <summary>
/// Opens a small set of absolute URIs via the shell (default browser / mail client / FTP handler).
/// Blocks schemes such as file, ms-settings, and other custom protocols.
/// Used for WPF <see cref="System.Windows.Documents.Hyperlink"/> and AvalonEdit Ctrl+click links.
/// </summary>
internal static class SafeHttpUriLauncher
{
    /// <summary>When set, http(s) URLs may be opened with a specific browser instead of the system default.</summary>
    public static Func<ExternalBrowserChoice>? GetPreferredExternalBrowser { get; set; }

    public static bool TryOpenInDefaultBrowser(Uri? uri)
        => TryOpenHyperlinkUri(uri);

    /// <summary>
    /// Allows http, https, ftp (AvalonEdit link regex), and mailto (AvalonEdit email links).
    /// </summary>
    public static bool TryOpenHyperlinkUri(Uri? uri)
    {
        if (!TryGetShellSafeUriString(uri, out string launchUri))
            return false;

        if (uri is not null
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var choice = GetPreferredExternalBrowser?.Invoke() ?? ExternalBrowserChoice.Default;
            if (choice != ExternalBrowserChoice.Default && TryLaunchHttpUrlWithSpecificBrowser(choice, launchUri))
                return true;
        }

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

    private static bool TryLaunchHttpUrlWithSpecificBrowser(ExternalBrowserChoice choice, string url)
    {
        var exe = ResolveBrowserExecutable(choice);
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return false;

        try
        {
            var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = true };
            psi.ArgumentList.Add(url);
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveBrowserExecutable(ExternalBrowserChoice choice)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return choice switch
        {
            ExternalBrowserChoice.Chrome => FindFirstExistingFile(
                Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe")),
            ExternalBrowserChoice.Edge => FindFirstExistingFile(
                Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe")),
            ExternalBrowserChoice.Firefox => FindFirstExistingFile(
                Path.Combine(programFiles, @"Mozilla Firefox\firefox.exe"),
                Path.Combine(programFilesX86, @"Mozilla Firefox\firefox.exe")),
            _ => null
        };
    }

    private static string? FindFirstExistingFile(params string[] paths)
    {
        foreach (var p in paths)
        {
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
                return p;
        }

        return null;
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

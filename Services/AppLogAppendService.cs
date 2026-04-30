using System.Globalization;
using System.IO;

namespace Noted.Services;

public static class AppLogAppendService
{
    public static void AppendLine(string backupFolder, string appLogFileName, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(backupFolder))
            return;

        try
        {
            Directory.CreateDirectory(backupFolder);
            var logPath = Path.Combine(backupFolder, appLogFileName);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            File.AppendAllText(logPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch
        {
            // best-effort
        }
    }
}

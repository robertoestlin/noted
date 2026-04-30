using System.IO;
using System.Text.Json;

namespace Noted.Models;

public static class SessionStateMigration
{
    /// <summary>Pre-split <c>settings.json</c> stored main window bounds at the root (<c>Left</c>, …).</summary>
    public static bool JsonLooksLikeLegacyCombinedSettings(string effectiveSettingsJsonPath)
    {
        try
        {
            if (!File.Exists(effectiveSettingsJsonPath))
                return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(effectiveSettingsJsonPath));
            return doc.RootElement.TryGetProperty("Left", out _);
        }
        catch
        {
            return false;
        }
    }

    public static LegacyCombinedSettings? TryReadLegacyCombinedSettings(string effectiveSettingsJsonPath)
    {
        try
        {
            if (!File.Exists(effectiveSettingsJsonPath))
                return null;
            var json = File.ReadAllText(effectiveSettingsJsonPath);
            return JsonSerializer.Deserialize<LegacyCombinedSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    public static NotedSessionState FromLegacy(LegacyCombinedSettings legacy)
    {
        var s = new NotedSessionState
        {
            Left = legacy.Left,
            Top = legacy.Top,
            Width = legacy.Width <= 0 ? 1100 : legacy.Width,
            Height = legacy.Height <= 0 ? 700 : legacy.Height,
            Maximized = legacy.Maximized,
            ActiveTabIndex = legacy.ActiveTabIndex,
            LastCloudCopyUtc = legacy.LastCloudCopyUtc,
            AlarmPopupLeft = legacy.AlarmPopupLeft,
            AlarmPopupTop = legacy.AlarmPopupTop,
            PluginAlarmsSnoozedUntilLocal = legacy.PluginAlarmsSnoozedUntilLocal
        };
        if (legacy.Standup != null)
        {
            s.StandupWindowLeft = legacy.Standup.WindowLeft;
            s.StandupWindowTop = legacy.Standup.WindowTop;
            s.StandupWindowWidth = legacy.Standup.WindowWidth;
            s.StandupWindowHeight = legacy.Standup.WindowHeight;
            s.StandupWindowMaximized = legacy.Standup.WindowMaximized;
        }

        return s;
    }
}

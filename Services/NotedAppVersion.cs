using System.Reflection;

namespace Noted.Services;

/// <summary>Running build version for upgrade checks and persistence (see <c>LastNotedVersion</c>).</summary>
public static class NotedAppVersion
{
    public static string Current { get; } = ReadVersion();

    static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Informational often includes "+hash"; keep semver-ish prefix for folders/logs.
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus].Trim() : info.Trim();
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}

using System.Globalization;
using System.Windows.Media;
using Noted.Models;

namespace Noted.Services;

public sealed class UserProfileService
{
    private readonly ColorThemeService _colorThemeService = new();

    public List<UserProfile> NormalizeUsers(IEnumerable<UserProfile>? users)
    {
        if (users == null)
            return [];

        var byName = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Name))
                continue;

            var name = user.Name.Trim();
            var color = NormalizeUserColor(user.Color, fallbackSeed: name);
            byName[name] = new UserProfile { Name = name, Color = color };
        }

        return byName.Values
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<UserProfile> BuildUsersFromLegacyNames(IEnumerable<string>? userNames)
    {
        if (userNames == null)
            return [];

        return NormalizeUsers(userNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new UserProfile
            {
                Name = name.Trim(),
                Color = ColorToHex(DeterministicUserColor(name.Trim()))
            }));
    }

    public Color ResolveUserColor(IReadOnlyCollection<UserProfile> users, string person)
    {
        var user = users.FirstOrDefault(user => string.Equals(user.Name, person, StringComparison.OrdinalIgnoreCase));
        if (user != null && _colorThemeService.TryParseColor(user.Color, out var parsed))
            return parsed;

        return DeterministicUserColor(person);
    }

    public Color RandomUserColor()
    {
        double hue = Random.Shared.NextDouble() * 360.0;
        double saturation = 0.45 + (Random.Shared.NextDouble() * 0.30);
        double value = 0.78 + (Random.Shared.NextDouble() * 0.18);
        return ColorFromHsv(hue, saturation, value);
    }

    private string NormalizeUserColor(string? input, string fallbackSeed)
    {
        if (_colorThemeService.TryParseColor(input, out var parsed))
            return ColorToHex(parsed);

        return ColorToHex(DeterministicUserColor(fallbackSeed));
    }

    private static Color DeterministicUserColor(string seed)
    {
        int hash = Math.Abs((seed ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase));
        double hue = hash % 360;
        return ColorFromHsv(hue, 0.50, 0.88);
    }

    private static string ColorToHex(Color color)
        => string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double c = value * saturation;
        double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        double m = value - c;

        double r = 0, g = 0, b = 0;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}

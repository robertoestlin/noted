using System.Windows.Media;

namespace Noted.Services;

public sealed class ColorThemeService
{
    public bool TryParseColor(string? input, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            var parsed = ColorConverter.ConvertFromString(input.Trim());
            if (parsed is Color converted)
            {
                color = converted;
                return true;
            }
        }
        catch
        {
            // Caller handles validation messaging.
        }

        return false;
    }

    public Color MigrateHighlightedLineColor(Color color, Color currentDefault)
    {
        if (color == Color.FromRgb(255, 196, 128))
            return currentDefault;
        if (color == Color.FromRgb(255, 105, 180))
            return currentDefault;
        if (color == Color.FromRgb(255, 182, 193))
            return currentDefault;
        return color;
    }

    public Color MigrateSelectedHighlightedLineColor(Color color, Color currentDefault)
    {
        if (color == Color.FromRgb(255, 160, 96))
            return currentDefault;
        if (color == Color.FromRgb(255, 105, 180))
            return currentDefault;
        if (color == Color.FromRgb(255, 182, 193))
            return currentDefault;
        return color;
    }
}

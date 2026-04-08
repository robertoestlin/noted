using System.Windows.Input;

namespace Noted.Services;

public sealed class ShortcutService
{
    public bool TryParseKeyGesture(string? input, out KeyGesture gesture)
    {
        gesture = null!;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            var converter = new KeyGestureConverter();
            var parsed = converter.ConvertFromInvariantString(input.Trim());
            if (parsed is KeyGesture keyGesture && keyGesture.Key != Key.None)
            {
                gesture = keyGesture;
                return true;
            }
        }
        catch
        {
            // Invalid gesture text.
        }

        return false;
    }
}

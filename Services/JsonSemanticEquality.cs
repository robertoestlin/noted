using System.Text.Json;

namespace Noted.Services;

/// <summary>
/// Compares parsed JSON for logical equality: object property order is ignored; array order and values are preserved.
/// </summary>
public static class JsonSemanticEquality
{
    public static bool AreEqual(string existingJson, string newJson)
    {
        using var a = JsonDocument.Parse(existingJson, MinimalDocumentOptions);
        using var b = JsonDocument.Parse(newJson, MinimalDocumentOptions);
        return ElementsEqual(a.RootElement, b.RootElement);
    }

    static readonly JsonDocumentOptions MinimalDocumentOptions =
        new() { CommentHandling = JsonCommentHandling.Disallow };

    static bool ElementsEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;

        return a.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.String => string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => NumbersEqual(a, b),
            JsonValueKind.Array => ArraysEqual(a, b),
            JsonValueKind.Object => ObjectsEqual(a, b),
            _ => string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal)
        };
    }

    static bool NumbersEqual(JsonElement a, JsonElement b)
    {
        if (a.TryGetInt64(out var la) && b.TryGetInt64(out var lb))
            return la == lb;
        if (a.TryGetUInt64(out var ua) && b.TryGetUInt64(out var ub))
            return ua == ub;
        try
        {
            return a.GetDecimal() == b.GetDecimal();
        }
        catch
        {
            return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
        }
    }

    static bool ArraysEqual(JsonElement a, JsonElement b)
    {
        var len = a.GetArrayLength();
        if (len != b.GetArrayLength())
            return false;
        for (var i = 0; i < len; i++)
        {
            if (!ElementsEqual(a[i], b[i]))
                return false;
        }

        return true;
    }

    static bool ObjectsEqual(JsonElement a, JsonElement b)
    {
        var aCount = 0;
        foreach (var _ in a.EnumerateObject())
            aCount++;
        var bCount = 0;
        foreach (var _ in b.EnumerateObject())
            bCount++;
        if (aCount != bCount)
            return false;

        foreach (var prop in a.EnumerateObject())
        {
            if (!b.TryGetProperty(prop.Name, out var bEl))
                return false;
            if (!ElementsEqual(prop.Value, bEl))
                return false;
        }

        return true;
    }
}

using System.Text.Json;

namespace SportsDeepLinks.Core.Extraction;

/// <summary>
/// Small null-safe JsonElement accessors so extraction code can read Apple's loosely-typed
/// JSON the same way the Python source reads dicts (`.get(x) or .get(y)`, tolerant of missing
/// keys/wrong shapes) without a try/catch at every call site.
/// </summary>
public static class JsonHelpers
{
    public static JsonElement GetObjectOrDefault(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return default;
    }

    public static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    /// <summary>Returns the first non-null/non-empty string across the given property names.</summary>
    public static string? GetFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var value = GetString(element, name);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    public static long? GetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            _ => null,
        };
    }
}

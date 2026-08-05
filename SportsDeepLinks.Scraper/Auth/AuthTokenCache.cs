using System.Text.Json;

namespace SportsDeepLinks.Scraper.Auth;

public record AuthTokens(string Utscf, string Utsk, long Timestamp);

/// <summary>
/// JSON file cache for Apple TV's rotating utscf/utsk session tokens, matching the
/// {"utscf":..., "utsk":..., "timestamp":...} shape of data/apple_uts_auth.json in the
/// Python source. Avoids re-launching a browser on every run.
/// </summary>
public static class AuthTokenCache
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "apple_uts_auth.json");

    public static AuthTokens? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var utscf = root.TryGetProperty("utscf", out var u1) ? u1.GetString() : null;
            var utsk = root.TryGetProperty("utsk", out var u2) ? u2.GetString() : null;
            var timestamp = root.TryGetProperty("timestamp", out var t) && t.TryGetInt64(out var ts) ? ts : 0;

            if (string.IsNullOrEmpty(utscf) || string.IsNullOrEmpty(utsk))
            {
                return null;
            }

            // Tokens are stored URL-encoded, matching Python's urllib.parse.unquote on load.
            return new AuthTokens(Uri.UnescapeDataString(utscf), Uri.UnescapeDataString(utsk), timestamp);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AuthTokens tokens, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(new
        {
            utscf = tokens.Utscf,
            utsk = tokens.Utsk,
            timestamp = tokens.Timestamp,
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }
}

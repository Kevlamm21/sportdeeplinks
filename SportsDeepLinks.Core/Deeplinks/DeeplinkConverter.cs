using System.Text.RegularExpressions;

namespace SportsDeepLinks.Core.Deeplinks;

/// <summary>
/// Converts app-scheme deep links (sportscenter://, pplus://, aiv://, ...) to their HTTPS
/// equivalents. Pure string/regex logic, no network calls.
///
/// Direct port of bin/deeplink_converter.py from the FruitDeepLinks Python project. Provider
/// converters, dispatch order, and the ESPN priority-tier logic are preserved exactly, since
/// subtle reordering here silently changes which deep link gets produced.
/// </summary>
public static class DeeplinkConverter
{
    private static readonly Regex SchemeRegex = new(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):", RegexOptions.Compiled);
    private static readonly Regex UuidRegex = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EspnPlayableIdUuid = new(
        @":([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HttpsSchemeRegex = new(@"^https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LastResortRegex = new(
        @"^[a-zA-Z][a-zA-Z0-9+.\-]*://(www\.[^/]+/.+)$",
        RegexOptions.Compiled);
    private static readonly Regex CbsSportsLetId = new(@"/watch/(LET-\d+)", RegexOptions.Compiled);
    private static readonly Regex MaxSportEventId = new(
        @"play\.hbomax\.com/sport/([0-9a-f\-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string Scheme(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "";
        }

        var m = SchemeRegex.Match(url);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
    }

    /// <summary>
    /// Minimal query-string parse: returns the first value seen per key (matches the Python
    /// callers' `(qs.get("x") or [None])[0]` pattern, which always takes the first occurrence).
    /// </summary>
    private static Dictionary<string, string> ParseQuery(string url)
    {
        var result = new Dictionary<string, string>();
        var qIndex = url.IndexOf('?');
        if (qIndex < 0)
        {
            return result;
        }

        var query = url[(qIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            var value = eq >= 0 ? pair[(eq + 1)..] : "";
            key = Uri.UnescapeDataString(key);
            value = Uri.UnescapeDataString(value);
            if (!result.ContainsKey(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    // ----------------------------
    // Provider converters
    // ----------------------------

    /// <summary>
    /// aiv://aiv/detail?gti=&lt;GTI&gt;&amp;action=watch&amp;type=live...
    ///   -&gt; https://app.primevideo.com/detail?gti=&lt;GTI&gt;
    /// </summary>
    public static string? ConvertAmazonPrime(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("aiv://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var qs = ParseQuery(punchoutUrl);
        return qs.TryGetValue("gti", out var gti) && !string.IsNullOrEmpty(gti)
            ? $"https://app.primevideo.com/detail?gti={gti}"
            : null;
    }

    /// <summary>
    /// ESPN (SportsCenter scheme). Priority (best to worst):
    ///   1. espnGraphId from ESPN Watch Graph API (most reliable for ADBTuner)
    ///   2. playID from sportscenter:// URL
    ///   3. playableId extraction from tvs.sbd pattern
    ///   4. Fallback to ESPN Watch landing page
    /// </summary>
    public static string? ConvertEspn(string? punchoutUrl, string? playableId = null, string? espnGraphId = null)
    {
        // Priority 1: ESPN Graph ID, format "espn-watch:{playID}:{hash}". This check happens
        // before validating punchoutUrl at all, so it can succeed with no punchout URL.
        if (!string.IsNullOrEmpty(espnGraphId))
        {
            var parts = espnGraphId.Split(':');
            if (parts.Length >= 2 && UuidRegex.IsMatch(parts[1]))
            {
                return $"https://www.espn.com/watch/player/_/id/{parts[1]}";
            }
        }

        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("sportscenter://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var qs = ParseQuery(punchoutUrl);

        // Priority 2: playID/playId/playid in the URL (check all three casings explicitly,
        // matching qs.get("playID") or qs.get("playId") or qs.get("playid")).
        string? playId = null;
        if (qs.TryGetValue("playID", out var p1)) playId = p1;
        else if (qs.TryGetValue("playId", out var p2)) playId = p2;
        else if (qs.TryGetValue("playid", out var p3)) playId = p3;

        if (!string.IsNullOrEmpty(playId) && UuidRegex.IsMatch(playId))
        {
            return $"https://www.espn.com/watch/player/_/id/{playId}";
        }

        // Priority 3: channel-based punchout (playChannel=espn1) - pull the UUID out of the
        // Apple playable_id pattern "tvs.sbd.30061:<UUID>:<suffix>".
        if (!string.IsNullOrEmpty(playableId))
        {
            var m = EspnPlayableIdUuid.Match(playableId);
            if (m.Success)
            {
                return $"https://www.espn.com/watch/player/_/id/{m.Groups[1].Value}";
            }
        }

        return "https://www.espn.com/watch/";
    }

    /// <summary>
    /// pplus://www.paramountplus.com/live-tv/stream/&lt;slug&gt;/&lt;uuid&gt;/
    ///   -&gt; https://www.paramountplus.com/live-tv/stream/&lt;slug&gt;/&lt;uuid&gt;/
    /// </summary>
    public static string? ConvertParamountPlus(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("pplus://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "https://" + punchoutUrl["pplus://".Length..].TrimStart('/');
    }

    /// <summary>
    /// cbstve://www.cbs.com/live-tv/stream/sports/&lt;uuid&gt;/
    ///   -&gt; https://www.cbs.com/live-tv/stream/sports/&lt;uuid&gt;/
    /// </summary>
    public static string? ConvertCbsTve(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("cbstve://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "https://" + punchoutUrl["cbstve://".Length..].TrimStart('/');
    }

    /// <summary>
    /// open.dazn.com://media/open/&lt;id&gt; -&gt; https://open.dazn.com/media/open/&lt;id&gt;
    /// </summary>
    public static string? ConvertDazn(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        const string scheme = "open.dazn.com://";
        if (punchoutUrl.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return "https://open.dazn.com/" + punchoutUrl[scheme.Length..].TrimStart('/');
        }

        return null;
    }

    /// <summary>
    /// vixapp://live/transmission-matchid-XXXX?play
    ///   -&gt; https://vix.com/&lt;locale&gt;/live/transmission-matchid-XXXX?play
    /// </summary>
    public static string? ConvertVix(string? punchoutUrl, string locale = "es-es")
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("vixapp://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tail = punchoutUrl["vixapp://".Length..].TrimStart('/');
        if (!tail.StartsWith("live/", StringComparison.Ordinal))
        {
            tail = "live/" + tail;
        }

        return $"https://vix.com/{locale}/" + tail;
    }

    /// <summary>
    /// fsapp://live/FS1?eventId=... -&gt; https://www.foxsports.com/live/fs1?eventId=...
    /// foxone://channel/fs1         -&gt; https://www.foxsports.com/live/fs1
    /// </summary>
    public static string? ConvertFox(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        const string fsappPrefix = "fsapp://live/";
        if (punchoutUrl.StartsWith(fsappPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var tail = punchoutUrl[fsappPrefix.Length..];
            var qIdx = tail.IndexOf('?');
            var channel = (qIdx >= 0 ? tail[..qIdx] : tail).Trim('/').ToLowerInvariant();
            var q = qIdx >= 0 ? "?" + tail[(qIdx + 1)..] : "";
            return $"https://www.foxsports.com/live/{channel}{q}";
        }

        const string foxonePrefix = "foxone://channel/";
        if (punchoutUrl.StartsWith(foxonePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var channel = punchoutUrl[foxonePrefix.Length..].Trim('/').ToLowerInvariant();
            return $"https://www.foxsports.com/live/{channel}";
        }

        return null;
    }

    /// <summary>
    /// watchtnt://play?... -&gt; https://www.tntdrama.com/watchtnt?...
    /// watchtru://play?... -&gt; https://www.trutv.com/watchtrutv?...
    /// </summary>
    public static string? ConvertTurner(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        const string tnt = "watchtnt://play";
        if (punchoutUrl.StartsWith(tnt, StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.tntdrama.com/watchtnt" + punchoutUrl[tnt.Length..];
        }

        const string tru = "watchtru://play";
        if (punchoutUrl.StartsWith(tru, StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.trutv.com/watchtrutv" + punchoutUrl[tru.Length..];
        }

        return null;
    }

    /// <summary>
    /// gametime://game/0022500409?x-source=... -&gt; https://www.nba.com/game/0022500409
    /// </summary>
    public static string? ConvertNbaGametime(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("gametime://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = punchoutUrl.Contains("://") ? punchoutUrl[(punchoutUrl.IndexOf("://", StringComparison.Ordinal) + 3)..] : punchoutUrl;
        var qIdx = path.IndexOf('?');
        if (qIdx >= 0)
        {
            path = path[..qIdx];
        }

        const string gamePrefix = "game/";
        if (path.StartsWith(gamePrefix, StringComparison.Ordinal))
        {
            var gameId = path[gamePrefix.Length..].Trim('/');
            if (gameId.Length > 0)
            {
                return $"https://www.nba.com/game/{gameId}";
            }
        }

        return null;
    }

    /// <summary>
    /// nbcsportstve://watch/12013522 -&gt; https://www.nbcsports.com/watch/schedule
    /// (the naive rewrite to /watch/{id} 404s; fall back to the schedule hub, per the Python source's note)
    /// </summary>
    public static string? ConvertNbcSports(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("nbcsportstve://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "https://www.nbcsports.com/watch/schedule";
    }

    /// <summary>
    /// nflctv://livestream/&lt;uuid&gt; -&gt; no stable public event-level URL identified; falls back to NFL+ landing.
    /// </summary>
    public static string? ConvertNflCtv(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("nflctv://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "https://www.nfl.com/plus/";
    }

    /// <summary>
    /// cbssportsapp://home/watch/LET-211531296?source=tvapp
    ///   -&gt; https://www.cbssports.com/watch/&lt;path&gt;/&lt;LET-...&gt;
    /// </summary>
    public static string? ConvertCbsSports(string? punchoutUrl, string? league = null)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("cbssportsapp://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var m = CbsSportsLetId.Match(punchoutUrl);
        if (!m.Success)
        {
            return null;
        }

        var letId = m.Groups[1].Value;
        if (string.IsNullOrEmpty(league))
        {
            return $"https://www.cbssports.com/watch/{letId}";
        }

        var path = CbsSportsLeagueMap.PathFor(league);
        return $"https://www.cbssports.com/watch/{path}/{letId}";
    }

    /// <summary>
    /// (Best guess, not validated) peacock://event/&lt;id&gt; -&gt; https://www.peacocktv.com/watch/playback/event/&lt;id&gt;
    /// </summary>
    public static string? ConvertPeacock(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("peacock://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        const string eventPrefix = "peacock://event/";
        if (punchoutUrl.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var eventId = punchoutUrl[eventPrefix.Length..];
            return $"https://www.peacocktv.com/watch/playback/event/{eventId}";
        }

        return null;
    }

    /// <summary>
    /// marquee://video/... -&gt; no known HTTP equivalent; falls back to the Marquee watch landing page.
    /// </summary>
    public static string? ConvertMarquee(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl) || !punchoutUrl.StartsWith("marquee://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "https://www.marqueesportsnetwork.com/watch";
    }

    /// <summary>
    /// mlbatbat://mlbtv?gamepk=831545 -&gt; https://www.mlb.com/tv/g831545
    /// </summary>
    public static string? ConvertMlb(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        var qs = ParseQuery(punchoutUrl);
        return qs.TryGetValue("gamepk", out var gamepk) && !string.IsNullOrEmpty(gamepk)
            ? $"https://www.mlb.com/tv/g{gamepk}"
            : null;
    }

    /// <summary>
    /// Max (HBO Max) - convert the sport landing page to the direct video player.
    /// https://play.hbomax.com/sport/{event-id}?utm_source=...
    ///   -&gt; https://play.hbomax.com/video/watch-sport/{event-id}
    /// </summary>
    public static string? ConvertMax(string? punchoutUrl)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        if (!punchoutUrl.StartsWith("https://play.hbomax.com/sport/", StringComparison.Ordinal))
        {
            return punchoutUrl.Contains("watch-sport", StringComparison.Ordinal) ? punchoutUrl : null;
        }

        var m = MaxSportEventId.Match(punchoutUrl);
        if (!m.Success)
        {
            return null;
        }

        return $"https://play.hbomax.com/video/watch-sport/{m.Groups[1].Value}";
    }

    // ----------------------------
    // Public API
    // ----------------------------

    public static string? GenerateHttpDeeplink(
        string? punchoutUrl,
        string? provider = null,
        string? playableId = null,
        string? league = null,
        string? vixLocale = null,
        string? espnGraphId = null)
    {
        if (string.IsNullOrEmpty(punchoutUrl))
        {
            return null;
        }

        // Special case: Max URLs need transformation even though they're already HTTPS -
        // this check must come before the generic "already https? keep as-is" check below.
        if (punchoutUrl.Contains("play.hbomax.com/sport/", StringComparison.Ordinal))
        {
            return ConvertMax(punchoutUrl);
        }

        if (HttpsSchemeRegex.IsMatch(punchoutUrl))
        {
            return punchoutUrl;
        }

        var prov = (provider ?? Scheme(punchoutUrl)).ToLowerInvariant();

        switch (prov)
        {
            case "aiv":
            case "amazon prime video":
            case "prime video":
                return ConvertAmazonPrime(punchoutUrl);

            case "sportscenter":
            case "espn":
            case "espn+":
                return ConvertEspn(punchoutUrl, playableId, espnGraphId);

            case "pplus":
            case "paramount":
            case "paramount+":
                return ConvertParamountPlus(punchoutUrl);

            case "cbstve":
            case "cbs":
                return ConvertCbsTve(punchoutUrl);

            case "open.dazn.com":
            case "dazn":
                return ConvertDazn(punchoutUrl);

            case "vixapp":
            case "vix":
                return ConvertVix(punchoutUrl, vixLocale ?? "es-es");

            case "fsapp":
            case "foxone":
            case "fox sports":
                return ConvertFox(punchoutUrl);

            case "watchtnt":
            case "watchtru":
                return ConvertTurner(punchoutUrl);

            case "gametime":
            case "nba":
                return ConvertNbaGametime(punchoutUrl);

            case "nbcsportstve":
            case "nbcsports":
                return ConvertNbcSports(punchoutUrl);

            case "cbssportsapp":
            case "cbs sports":
                return ConvertCbsSports(punchoutUrl, league);

            case "nflctv":
            case "nfl":
                return ConvertNflCtv(punchoutUrl);

            case "marquee":
            case "marquee sports network":
                return ConvertMarquee(punchoutUrl);

            case "mlbatbat":
            case "mlbtv":
            case "mlb":
                return ConvertMlb(punchoutUrl);

            case "peacock":
                return ConvertPeacock(punchoutUrl);

            case "max":
            case "hbo max":
            case "hbomax":
            case "https":
                if (punchoutUrl.Contains("play.hbomax.com", StringComparison.Ordinal))
                {
                    return ConvertMax(punchoutUrl);
                }
                break;
        }

        // Last resort: scheme://www.domain/... -> https://www.domain/...
        var lastResort = LastResortRegex.Match(punchoutUrl);
        return lastResort.Success ? "https://" + lastResort.Groups[1].Value : null;
    }

    /// <summary>
    /// Generate a working sportscenter:// deeplink for ADBTuner using an ESPN Watch Graph ID.
    /// Inverse of the ESPN Graph-ID priority tier in <see cref="ConvertEspn"/>.
    /// </summary>
    public static string? GenerateEspnSchemeDeeplink(string? espnGraphId, string? fallbackUrl)
    {
        if (string.IsNullOrEmpty(espnGraphId))
        {
            return fallbackUrl;
        }

        var parts = espnGraphId.Split(':');
        if (parts.Length >= 2 && UuidRegex.IsMatch(parts[1]))
        {
            return $"sportscenter://x-callback-url/showWatchStream?playID={parts[1]}";
        }

        return fallbackUrl;
    }

    /// <summary>
    /// Port of logical_service_mapper.py::extract_gti_from_deeplink() - a slightly better
    /// Amazon GTI extractor than the inline parse in ConvertAmazonPrime, since it also
    /// recognizes the event-specific "broadcast=" query param before the show-page "gti=" one.
    /// </summary>
    public static string? ExtractGtiFromDeeplink(string? deeplink)
    {
        if (string.IsNullOrEmpty(deeplink))
        {
            return null;
        }

        var broadcastMatch = Regex.Match(deeplink, @"broadcast=(amzn1\.dv\.gti\.[0-9a-f-]{36})", RegexOptions.IgnoreCase);
        if (broadcastMatch.Success)
        {
            return broadcastMatch.Groups[1].Value;
        }

        var mainMatch = Regex.Match(deeplink, @"[?&]gti=(amzn1\.dv\.gti\.[0-9a-f-]{36})", RegexOptions.IgnoreCase);
        return mainMatch.Success ? mainMatch.Groups[1].Value : null;
    }
}

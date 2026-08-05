using System.Text.RegularExpressions;

namespace SportsDeepLinks.Core.Deeplinks;

/// <summary>
/// Maps a league display name to the path segment CBS Sports uses in its /watch/ URLs.
/// Port of deeplink_converter.py's _CBS_LEAGUE_TO_PATH + _slugify().
/// </summary>
public static class CbsSportsLeagueMap
{
    private static readonly Dictionary<string, string> LeagueToPath = new()
    {
        ["College Basketball"] = "college-basketball",
        ["Men's College Basketball"] = "college-basketball",
        ["Women's College Basketball"] = "womens-college-basketball",
        ["Conference League"] = "uefa-conference-league",
        ["Women's Champions League"] = "uefa-womens-champions-league",
        ["EFL Cup"] = "carabao-cup",
        ["EFL Championship"] = "efl",
        ["England League One"] = "efl",
        ["England League Two"] = "efl",
        ["Scottish Premiership"] = "scottish-professional-football-league",
        ["Serie A"] = "serie-a",
        ["Italy Supercoppa Italiana"] = "serie-a",
        ["Major Arena Soccer League"] = "soccer",
    };

    private static readonly Regex CurlyQuoteOrApostrophe = new("[’']", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static string PathFor(string? league)
    {
        if (string.IsNullOrEmpty(league))
        {
            return "";
        }

        if (LeagueToPath.TryGetValue(league, out var path))
        {
            return path;
        }

        return Slugify(league);
    }

    public static string Slugify(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        var lower = s.ToLowerInvariant();
        lower = CurlyQuoteOrApostrophe.Replace(lower, "");
        lower = NonAlphaNumeric.Replace(lower, "-");
        return lower.Trim('-');
    }
}

namespace SportsDeepLinks.Scraper.Discovery;

/// <summary>Port of apple_scraper_db.py::default_terms()/ensure_all_first().</summary>
public static class SearchTerms
{
    private static readonly string[] Terms =
    {
        "soccer", "nba", "nhl", "mlb", "nfl", "mls",
        "champions league", "ligue 1", "formula 1", "cricket",
        "espn", "cbs sports", "fox sports", "paramount+", "prime video", "peacock", "dazn",
        "women's college basketball", "men's college basketball",
    };

    /// <summary>Default search terms, with "all" always forced first.</summary>
    public static IReadOnlyList<string> Default { get; } = new[] { "all" }.Concat(Terms).ToArray();
}

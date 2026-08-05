namespace SportsDeepLinks.Scraper.Amazon;

/// <summary>
/// Page-content heuristics for deciding whether an Amazon detail-page response is usable,
/// a stale/404 GTI, a geo-unavailable title, or an inconclusive redirect/shell page that needs
/// the Playwright fallback. Port of amazon2.py's _looks_unavailable_page, _looks_stale_404,
/// _looks_shell_page, and _looks_blank_unusable_page (the latter two combined here since both
/// are only ever consulted together and only change the fallback *reason*, which this port
/// doesn't surface without the debug-CSV export amazon2.py otherwise writes it to).
/// </summary>
public static class AmazonPageClassifier
{
    private static readonly string[] UnavailableMarkers =
    {
        "currently unavailable to watch in your location",
        "currently unavailable to watch",
        "video is currently unavailable",
        "unavailable to watch in your location",
        "this video is currently unavailable",
    };

    private static readonly string[] Hard404Markers =
    {
        "sorry, we couldn't find",
        "sorry! we couldn't find that page",
        "page not found",
        "looking for something?",
        "dogs of amazon",
    };

    public static bool LooksUnavailable(string pageText, string title)
    {
        var visible = $"{title}\n{pageText}".ToLowerInvariant();
        return UnavailableMarkers.Any(marker => visible.Contains(marker, StringComparison.Ordinal));
    }

    public static bool LooksStale404(
        int statusCode, string title, string pageText, string? benefitId, string? entitlement, string? channelId)
    {
        if (statusCode is 404 or 410)
        {
            return true;
        }

        var visible = $"{title}\n{pageText}".ToLowerInvariant();
        var hasValidSignals =
            !string.IsNullOrWhiteSpace(benefitId) ||
            !string.IsNullOrWhiteSpace(entitlement) ||
            !string.IsNullOrWhiteSpace(channelId);

        return Hard404Markers.Any(marker => visible.Contains(marker, StringComparison.Ordinal)) && !hasValidSignals;
    }

    public static bool LooksShellOrBlank(
        string finalUrl, string title, string pageText, string? benefitId, string? entitlement, string? channelId)
    {
        var hasRealSignal =
            !string.IsNullOrEmpty(benefitId) ||
            !string.IsNullOrEmpty(entitlement) ||
            (!string.IsNullOrEmpty(channelId) && channelId is not ("aiv_amazon_error" or "aiv_aggregator"));

        if (hasRealSignal)
        {
            return false;
        }

        var visible = pageText.ToLowerInvariant();
        var final = finalUrl.ToLowerInvariant();
        var titleNorm = title.Trim().ToLowerInvariant();

        if (visible.Contains("continue shopping", StringComparison.Ordinal))
        {
            return true;
        }

        if (titleNorm == "amazon.com" && (visible.Contains("continue shopping", StringComparison.Ordinal) || final.Contains("/gp/video/detail/", StringComparison.Ordinal)))
        {
            return true;
        }

        if (final.Contains("/dp/", StringComparison.Ordinal)) return true;
        if (final.Contains("/gp/video/offers/", StringComparison.Ordinal)) return true;
        if (visible.Contains("watch with a free trial", StringComparison.Ordinal) || visible.Contains("subscribe and watch", StringComparison.Ordinal)) return true;

        return false;
    }
}

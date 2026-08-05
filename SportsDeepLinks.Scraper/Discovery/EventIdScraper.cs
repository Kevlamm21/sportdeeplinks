using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SportsDeepLinks.Scraper.Discovery;

/// <summary>
/// Discovers Apple UMC event IDs by loading each search term's page, scrolling to force
/// lazy-loaded tiles to render, and regexing the rendered HTML - port of
/// apple_scraper_db.py::scrape_search_term()/get_event_ids_from_page(). Apple returns event
/// *data* as JSON (see AppleTvApiClient), but the discovery step only has rendered HTML to
/// work with, so this still needs the live browser page rather than a plain HttpClient.
/// </summary>
public static class EventIdScraper
{
    private const string SearchUrlTemplate = "https://tv.apple.com/us/collection/sports/uts.col.search.SE?searchTerm={0}";
    private static readonly Regex EventIdRegex = new(@"umc\.cse\.[a-z0-9]+", RegexOptions.Compiled);

    public static async Task<HashSet<string>> DiscoverAsync(
        IPage page,
        IEnumerable<string> searchTerms,
        Action<string, int>? onTermScraped = null)
    {
        var ids = new HashSet<string>();

        foreach (var term in searchTerms)
        {
            var url = string.Format(SearchUrlTemplate, Uri.EscapeDataString(term));
            await page.GotoAsync(url);
            await page.WaitForTimeoutAsync(600);
            await PlaywrightScrollHelper.AutoScrollAsync(page, steps: 24, delayMs: 200);

            var html = await page.ContentAsync();
            var before = ids.Count;
            foreach (Match m in EventIdRegex.Matches(html))
            {
                ids.Add(m.Value);
            }

            onTermScraped?.Invoke(term, ids.Count - before);
        }

        return ids;
    }
}

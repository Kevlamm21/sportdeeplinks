using AngleSharp;

namespace SportsDeepLinks.Scraper.Amazon;

public record ParsedAmazonPage(string Title, string PageText, string EntitlementText, IReadOnlyList<string> Hrefs);

/// <summary>
/// Extracts title, visible body text, an "entitlement message" (the text Amazon shows
/// explaining which subscription is required), and outbound links from an Amazon Video detail
/// page. Port of amazon2.py's BeautifulSoup-based extraction (_extract_entitlement_text_from_html,
/// title/page_text parsing) using AngleSharp, .NET's closest equivalent to BeautifulSoup.
/// </summary>
public static class AmazonHtmlParser
{
    private static readonly string[] EntitlementSelectors =
    {
        "[data-automation-id='entitlement-message']",
        "[data-testid='entitlement-message']",
        "#entitlement-message",
    };

    private static readonly IBrowsingContext Context = BrowsingContext.New(Configuration.Default);

    public static async Task<ParsedAmazonPage> ParseAsync(string html)
    {
        using var document = await Context.OpenAsync(req => req.Content(html ?? ""));

        var title = document.Title?.Trim() ?? "";

        var entitlement = "";
        foreach (var selector in EntitlementSelectors)
        {
            var text = document.QuerySelector(selector)?.TextContent?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                entitlement = text;
                break;
            }
        }

        var pageText = document.Body?.TextContent?.Trim() ?? "";
        if (pageText.Length > 20000)
        {
            pageText = pageText[..20000];
        }

        var hrefs = document.QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href") ?? "")
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();

        return new ParsedAmazonPage(title, pageText, entitlement, hrefs);
    }
}

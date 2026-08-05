using Microsoft.Playwright;

namespace SportsDeepLinks.Scraper.Amazon;

public enum AmazonScrapeStatus { Success, Stale, Unavailable, Error }

public record AmazonChannelResult(string Gti, AmazonScrapeStatus Status, string? ChannelId, string? ChannelName, string? BenefitId);

/// <summary>
/// Resolves which Amazon subscription (NBA League Pass, DAZN, Peacock, Max, ...) a given GTI
/// requires. Port of amazon2.py's two-tier strategy: try a plain HTTP GET first (Python uses
/// curl_cffi to impersonate a Chrome TLS fingerprint, which plain .NET HttpClient cannot
/// replicate - expect more GTIs to need the Playwright fallback here than in the Python
/// original), then fall back to a stealthed headless Chromium page only when the HTTP response
/// is inconclusive.
/// </summary>
public sealed class AmazonChannelClient : IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";

    private const string StealthInitScript = """
        Object.defineProperty(navigator, 'webdriver', {get: () => undefined});
        Object.defineProperty(navigator, 'platform', {get: () => 'Linux x86_64'});
        Object.defineProperty(navigator, 'language', {get: () => 'en-US'});
        Object.defineProperty(navigator, 'languages', {get: () => ['en-US', 'en']});
        window.chrome = { runtime: {} };
        """;

    private static readonly string[] EntitlementSelectors =
    {
        "[data-automation-id='entitlement-message']",
        "[data-testid='entitlement-message']",
        "#entitlement-message",
    };

    private readonly HttpClient _http;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public AmazonChannelClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public static string GtiToUrl(string gti) => $"https://www.amazon.com/gp/video/detail/{gti}";

    public async Task<AmazonChannelResult> ResolveAsync(string gti)
    {
        var httpResult = await TryHttpProbeAsync(gti);
        return httpResult ?? await ResolveViaBrowserAsync(gti);
    }

    private async Task<AmazonChannelResult?> TryHttpProbeAsync(string gti)
    {
        var url = GtiToUrl(gti);

        try
        {
            using var response = await _http.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            var parsed = await AmazonHtmlParser.ParseAsync(html);
            var benefitId = BenefitMap.ParseBenefitId(finalUrl)
                ?? BenefitMap.ParseBenefitId(html)
                ?? BenefitMap.FindKnownBenefitIdInHtml(html)
                ?? FindBenefitIdInHrefs(parsed.Hrefs);

            return Classify(gti, (int)response.StatusCode, finalUrl, parsed, benefitId);
        }
        catch
        {
            return null; // network/impersonation failure -> browser fallback
        }
    }

    private async Task<AmazonChannelResult> ResolveViaBrowserAsync(string gti)
    {
        var url = GtiToUrl(gti);
        await EnsureBrowserAsync();

        await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgent,
            Locale = "en-US",
            TimezoneId = "America/New_York",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
        });
        await context.AddInitScriptAsync(StealthInitScript);
        var page = await context.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000,
            });
            var statusCode = response?.Status ?? 0;

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 });
            }
            catch
            {
                // networkidle timing out is fine - domcontentloaded already succeeded.
            }

            await page.WaitForTimeoutAsync(250);

            var html = await page.ContentAsync();
            var finalUrl = page.Url;
            var title = await page.TitleAsync();

            var parsed = await AmazonHtmlParser.ParseAsync(html);
            var liveEntitlement = await TryExtractLiveEntitlementAsync(page);
            var effectiveParsed = parsed with
            {
                Title = title,
                EntitlementText = string.IsNullOrEmpty(liveEntitlement) ? parsed.EntitlementText : liveEntitlement,
            };

            var benefitId = BenefitMap.ParseBenefitId(finalUrl)
                ?? FindBenefitIdInHrefs(parsed.Hrefs)
                ?? BenefitMap.ParseBenefitId(html);

            return Classify(gti, statusCode, finalUrl, effectiveParsed, benefitId)
                ?? new AmazonChannelResult(gti, AmazonScrapeStatus.Error, null, null, benefitId);
        }
        catch
        {
            return new AmazonChannelResult(gti, AmazonScrapeStatus.Error, null, null, null);
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }

    private static async Task<string?> TryExtractLiveEntitlementAsync(IPage page)
    {
        foreach (var selector in EntitlementSelectors)
        {
            try
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() > 0)
                {
                    var text = (await locator.First.InnerTextAsync()).Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
            catch
            {
                // Selector not present/detached mid-navigation - try the next one.
            }
        }

        return null;
    }

    private static string? FindBenefitIdInHrefs(IReadOnlyList<string> hrefs)
    {
        foreach (var href in hrefs)
        {
            var id = BenefitMap.ParseBenefitId(href);
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Classification order mirrors amazon2.py::_build_scrape_result exactly: unavailable ->
    /// stale/404 -> shell/blank (inconclusive, null return) -> "no signals at all" (inconclusive)
    /// -> success. A null return means the caller should fall back to the browser.
    /// </summary>
    private static AmazonChannelResult? Classify(
        string gti, int statusCode, string finalUrl, ParsedAmazonPage parsed, string? benefitId)
    {
        var (displayName, logicalServiceId, unknownReason) =
            BenefitMap.Classify(benefitId, parsed.EntitlementText, parsed.PageText);

        if (AmazonPageClassifier.LooksUnavailable(parsed.PageText, parsed.Title))
        {
            return new AmazonChannelResult(gti, AmazonScrapeStatus.Unavailable, null, null, benefitId);
        }

        if (AmazonPageClassifier.LooksStale404(statusCode, parsed.Title, parsed.PageText, benefitId, parsed.EntitlementText, logicalServiceId))
        {
            return new AmazonChannelResult(gti, AmazonScrapeStatus.Stale, null, null, benefitId);
        }

        if (AmazonPageClassifier.LooksShellOrBlank(finalUrl, parsed.Title, parsed.PageText, benefitId, parsed.EntitlementText, logicalServiceId))
        {
            return null;
        }

        var hasNoHttpSignalsAtAll =
            !string.IsNullOrEmpty(unknownReason) &&
            string.IsNullOrEmpty(benefitId) &&
            string.IsNullOrEmpty(parsed.EntitlementText) &&
            string.IsNullOrWhiteSpace(parsed.PageText);

        if (hasNoHttpSignalsAtAll)
        {
            return null;
        }

        return new AmazonChannelResult(gti, AmazonScrapeStatus.Success, logicalServiceId, displayName, benefitId);
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browser != null)
        {
            return;
        }

        _playwright ??= await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--disable-dev-shm-usage", "--no-sandbox", "--disable-blink-features=AutomationControlled" },
        });
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}

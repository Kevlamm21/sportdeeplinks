using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SportsDeepLinks.Scraper.Auth;

public record CapturedSession(AuthTokens Tokens, IReadOnlyList<BrowserContextCookiesResult> Cookies);

/// <summary>
/// Establishes a real Apple TV browser session and captures the utscf/utsk query-string
/// tokens their JSON API requires, plus session cookies. Port of the auth-capture half of
/// apple_scraper_db.py, but using Playwright's native request events instead of scraping
/// Chrome's CDP performance log with a regex (the approach Python's Selenium bindings need).
///
/// Owns the browser/context/page for the lifetime of a scrape run, since event discovery
/// (Phase 4) needs the same session to keep working - only per-event JSON fetches (Phase 5)
/// can drop to a plain HttpClient.
/// </summary>
public sealed class AppleAuthTokenCapture : IAsyncDisposable
{
    private const string SearchUrl = "https://tv.apple.com/us/collection/sports/uts.col.search.SE?searchTerm=all";
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private static readonly Regex UtscfRegex = new("utscf=([^&]+)", RegexOptions.Compiled);
    private static readonly Regex UtskRegex = new("utsk=([^&]+)", RegexOptions.Compiled);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowserContext? Context { get; private set; }
    public IPage? Page { get; private set; }

    /// <summary>
    /// Launches the browser, navigates to the sports search page, and returns tokens+cookies -
    /// from cache if present, otherwise captured live from the first XHR containing "utscf=".
    /// </summary>
    public async Task<CapturedSession> CaptureAsync(
        bool headless = true,
        string? cachePath = null,
        TimeSpan? timeout = null)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            // --no-sandbox is required in most container runtimes, which don't grant the
            // extra namespace/user privileges Chromium's sandbox otherwise needs.
            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage" },
        });
        Context = await _browser.NewContextAsync(new BrowserNewContextOptions { UserAgent = UserAgent });
        Page = await Context.NewPageAsync();

        var cached = AuthTokenCache.Load(cachePath);
        if (cached != null)
        {
            await Page.GotoAsync(SearchUrl);
            return new CapturedSession(cached, await Context.CookiesAsync());
        }

        var tcs = new TaskCompletionSource<AuthTokens>(TaskCreationOptions.RunContinuationsAsynchronously);
        Page.Request += (_, request) => TryCaptureTokens(request.Url, tcs);

        await Page.GotoAsync(SearchUrl);
        await PlaywrightScrollHelper.AutoScrollAsync(Page);

        // The utscf/utsk tokens only appear on the XHR Apple's client-side router fires when
        // opening an event's detail view - scrolling the search results alone never triggers
        // it, so a tile has to actually be clicked (confirmed by probing the live page: the
        // search results page's own requests never carry the tokens, only the sporting-event
        // detail fetch does).
        await ClickFirstSportingEventTileAsync(Page);

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        await using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                "Timed out waiting for Apple TV auth tokens (utscf/utsk) to appear in network traffic.")));

        var tokens = await tcs.Task;
        AuthTokenCache.Save(tokens, cachePath);

        return new CapturedSession(tokens, await Context.CookiesAsync());
    }

    /// <summary>
    /// Clicks the first "/sporting-event/" tile on the search results page, retrying a couple of
    /// candidates in case the first is obscured/animating. This is what actually fires the
    /// token-bearing XHR - a plain page load/scroll never does.
    /// </summary>
    private static async Task ClickFirstSportingEventTileAsync(IPage page, int maxAttempts = 3)
    {
        var locator = page.Locator("a[href*='/sporting-event/']");
        var count = await locator.CountAsync();

        for (var i = 0; i < Math.Min(count, maxAttempts); i++)
        {
            try
            {
                await locator.Nth(i).ClickAsync(new LocatorClickOptions { Timeout = 10000 });
                return;
            }
            catch
            {
                // Try the next candidate tile.
            }
        }
    }

    private static void TryCaptureTokens(string url, TaskCompletionSource<AuthTokens> tcs)
    {
        if (tcs.Task.IsCompleted || !url.Contains("utscf="))
        {
            return;
        }

        var utscfMatch = UtscfRegex.Match(url);
        var utskMatch = UtskRegex.Match(url);
        if (!utscfMatch.Success || !utskMatch.Success)
        {
            return;
        }

        var utscf = Uri.UnescapeDataString(utscfMatch.Groups[1].Value);
        var utsk = Uri.UnescapeDataString(utskMatch.Groups[1].Value);
        tcs.TrySetResult(new AuthTokens(utscf, utsk, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}

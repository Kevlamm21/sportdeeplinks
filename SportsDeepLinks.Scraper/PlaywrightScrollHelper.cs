using Microsoft.Playwright;

namespace SportsDeepLinks.Scraper;

/// <summary>
/// Manual scroll-and-wait loop to force Apple TV's lazily-loaded tiles to render, matching
/// apple_scraper_db.py::auto_scroll() (Playwright has no built-in "wait for infinite scroll"
/// primitive, so this mirrors the fixed-step approach rather than trying to detect completion).
/// </summary>
public static class PlaywrightScrollHelper
{
    public static async Task AutoScrollAsync(IPage page, int steps = 24, int delayMs = 200)
    {
        for (var i = 0; i < steps; i++)
        {
            await page.EvaluateAsync("window.scrollBy(0, 300);");
            await page.WaitForTimeoutAsync(delayMs);
        }
    }
}

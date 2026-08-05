using System.Net;
using System.Text.Json;
using Microsoft.Playwright;

namespace SportsDeepLinks.Scraper.Client;

/// <summary>
/// Fetches one event's full JSON from Apple TV's sporting-events API using a plain HttpClient
/// seeded with the browser session's cookies + auth tokens - port of the "requests" half of
/// apple_scraper_db.py::HybridAPIClient. v1 has no Selenium/Playwright-script fetch fallback on
/// failure (that's a later robustness enhancement); a failed fetch just returns null so the
/// caller can log and skip the event.
/// </summary>
public sealed class AppleTvApiClient : IDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly string _utscf;
    private readonly string _utsk;

    public AppleTvApiClient(string utscf, string utsk, IEnumerable<BrowserContextCookiesResult> cookies)
    {
        _utscf = utscf;
        _utsk = utsk;

        var cookieContainer = new CookieContainer();
        foreach (var cookie in cookies)
        {
            try
            {
                cookieContainer.Add(new System.Net.Cookie(
                    cookie.Name,
                    cookie.Value,
                    string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                    string.IsNullOrEmpty(cookie.Domain) ? ".apple.com" : cookie.Domain));
            }
            catch
            {
                // A handful of browser cookies (host-only, odd domain formats) can be rejected
                // by CookieContainer's stricter validation - skip rather than fail the whole client.
            }
        }

        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.Add("Referer", "https://tv.apple.com/us");
    }

    /// <summary>
    /// Fetches one event's full JSON. Success criteria mirror HybridAPIClient exactly: HTTP 200,
    /// JSON content-type, and the parsed body has a non-null "data" property. Caller owns
    /// disposing the returned JsonDocument.
    /// </summary>
    public async Task<JsonDocument?> FetchEventAsync(string eventId)
    {
        var url =
            $"https://tv.apple.com/api/uts/v3/sporting-events/{eventId}" +
            "?caller=web&locale=en-US&pfm=web&sf=143441&v=90" +
            $"&utscf={_utscf}&utsk={_utsk}";

        try
        {
            using var response = await _http.GetAsync(url);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var doc = await JsonDocument.ParseAsync(stream);

            var hasData = doc.RootElement.ValueKind == JsonValueKind.Object &&
                          doc.RootElement.TryGetProperty("data", out var data) &&
                          data.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

            if (!hasData)
            {
                doc.Dispose();
                return null;
            }

            return doc;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

using System.Net.Http.Json;
using System.Text.Json;

namespace SportsDeepLinks.Scraper.Espn;

/// <summary>
/// Queries ESPN's own Watch Graph GraphQL API for a day's "airings" - the source of the more
/// reliable ESPN Watch Graph ID that DeeplinkConverter's ESPN tier-1 priority consumes. Direct
/// port of bin/fruit_ingest_espn_graph.py's request shape (URL, static API key, GraphQL query,
/// and spoofed browser headers) - the GraphQL query text is copied verbatim.
/// </summary>
public sealed class EspnGraphClient : IDisposable
{
    private const string ApiBase = "https://watch.graph.api.espn.com/api";
    private const string ApiKey = "0dbf88e8-cc6d-41da-aa83-18b5c630bc5c";
    private const string Features = "pbov7";

    private const string GqlQuery = """
        query Airings(
          $countryCode: String!, $deviceType: DeviceType!, $tz: String!,
          $day: String!, $limit: Int
        ) {
          airings(
            countryCode: $countryCode, deviceType: $deviceType, tz: $tz,
            day: $day, limit: $limit
          ) {
            id airingId simulcastAiringId name shortName type
            startDateTime endDateTime
            feedName
            feedType
            network { id name shortName }
            league  { id name abbreviation }
            sport   { id name abbreviation }
            packages { name }
            category { name }
            subcategory { name }
            competition { id }
            image { url }
            purchaseImage { url }
            program { id code categoryCode isStudio }
            language
            isReAir
          }
        }
        """;

    private readonly HttpClient _http;

    public EspnGraphClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        _http.DefaultRequestHeaders.Add("Origin", "https://www.espn.com");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.espn.com/");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <summary>
    /// Fetches one day's airings. Returns the raw "airings" JSON array elements (caller decides
    /// how much of the payload to use - Phase 6 only needs "program.id" and the playback id
    /// fields, but the full airing metadata is available if useful elsewhere).
    /// </summary>
    public async Task<List<JsonElement>> FetchAiringsAsync(
        string dayIso,
        string timeZoneId = "America/New_York",
        string countryCode = "US",
        string deviceType = "DESKTOP",
        int limit = 2000)
    {
        var url = $"{ApiBase}?apiKey={ApiKey}&features={Features}";
        var payload = new
        {
            query = GqlQuery,
            variables = new { countryCode, deviceType, tz = timeZoneId, day = dayIso, limit },
            operationName = "Airings",
        };

        using var response = await _http.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        var airings = new List<JsonElement>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("airings", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var airing in arr.EnumerateArray())
            {
                airings.Add(airing.Clone());
            }
        }

        return airings;
    }

    public void Dispose() => _http.Dispose();
}

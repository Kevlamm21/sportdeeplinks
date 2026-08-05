using System.Text.Json;
using SportsDeepLinks.Core.Extraction;
using SportsDeepLinks.Core.Models;
using SportsDeepLinks.Scraper.Amazon;
using SportsDeepLinks.Scraper.Auth;
using SportsDeepLinks.Scraper.Client;
using SportsDeepLinks.Scraper.Discovery;
using SportsDeepLinks.Scraper.Espn;

var headless = !args.Contains("--headed");
var skipEspn = args.Contains("--skip-espn");
var skipAmazon = args.Contains("--skip-amazon");
var outputPath = GetArgValue(args, "--output") ?? Path.Combine("out", "events.json");
var eventLimit = int.TryParse(GetArgValue(args, "--limit"), out var parsedLimit) ? parsedLimit : (int?)null;

Console.WriteLine("=== SportsDeepLinks Scraper ===");

Console.WriteLine("[1/6] Capturing Apple TV auth session...");
await using var auth = new AppleAuthTokenCapture();
var session = await auth.CaptureAsync(headless: headless);
Console.WriteLine($"      tokens captured, cookies={session.Cookies.Count}");

using var appleClient = new AppleTvApiClient(session.Tokens.Utscf, session.Tokens.Utsk, session.Cookies);

Console.WriteLine("[2/6] Discovering event ids...");
var eventIds = (await EventIdScraper.DiscoverAsync(
        auth.Page!,
        SearchTerms.Default,
        (term, added) => Console.WriteLine($"      {term}: +{added} new ids")))
    .ToList();
Console.WriteLine($"      total discovered: {eventIds.Count}");

if (eventLimit is int limit)
{
    eventIds = eventIds.Take(limit).ToList();
}

var espnGraphLookup = new Dictionary<string, string>();
if (!skipEspn)
{
    Console.WriteLine("[3/6] Fetching ESPN Watch Graph enrichment...");
    try
    {
        using var espnClient = new EspnGraphClient();
        var dayIso = DateTime.Now.ToString("yyyy-MM-dd");
        var airings = await espnClient.FetchAiringsAsync(dayIso);
        espnGraphLookup = EspnGraphEnrichment.BuildProgramIdLookup(airings);
        Console.WriteLine($"      airings={airings.Count}, program-id lookups={espnGraphLookup.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      ESPN enrichment failed, continuing without it: {ex.Message}");
    }
}
else
{
    Console.WriteLine("[3/6] Skipping ESPN enrichment (--skip-espn)");
}

Console.WriteLine("[4/6] Fetching + extracting events...");
var events = new List<SportingEvent>();
for (var i = 0; i < eventIds.Count; i++)
{
    var eventId = eventIds[i];
    using var raw = await appleClient.FetchEventAsync(eventId);
    if (raw == null)
    {
        Console.WriteLine($"      [{i + 1}/{eventIds.Count}] {eventId}: fetch failed, skipping");
        continue;
    }

    var normalized = AppleEventNormalizer.Normalize(eventId, raw.RootElement);
    var playables = PlayableExtractor.ExtractPlayables(normalized, espnGraphLookup);

    events.Add(new SportingEvent
    {
        Id = normalized.Id,
        Title = normalized.Title,
        SportName = normalized.SportName,
        LeagueName = normalized.LeagueName,
        StartTimeMs = normalized.StartTimeMs,
        EndTimeMs = normalized.EndTimeMs,
        Playables = playables,
    });

    if ((i + 1) % 10 == 0 || i + 1 == eventIds.Count)
    {
        Console.WriteLine($"      [{i + 1}/{eventIds.Count}] events processed, {events.Count} kept so far");
    }
}

if (!skipAmazon)
{
    Console.WriteLine("[5/6] Resolving Amazon channel identification...");
    var allPlayables = events.SelectMany(e => e.Playables).ToList();
    var gtis = GtiExtractor.ExtractGtis(allPlayables).ToList();
    Console.WriteLine($"      found {gtis.Count} distinct Amazon GTIs");

    if (gtis.Count > 0)
    {
        await using var amazonClient = new AmazonChannelClient();
        var resolved = new Dictionary<string, AmazonChannelResult>();

        for (var g = 0; g < gtis.Count; g++)
        {
            var gti = gtis[g];
            var result = await amazonClient.ResolveAsync(gti);
            resolved[gti] = result;
            Console.WriteLine($"      [{g + 1}/{gtis.Count}] {gti}: {result.Status} -> {result.ChannelName ?? "(none)"}");
        }

        foreach (var playable in allPlayables)
        {
            var gti = GtiExtractor.ExtractGtiFromPlayable(playable);
            if (gti != null && resolved.TryGetValue(gti, out var match) && match.Status == AmazonScrapeStatus.Success)
            {
                playable.AmazonChannelId = match.ChannelId;
                playable.AmazonChannelName = match.ChannelName;
            }
        }
    }
}
else
{
    Console.WriteLine("[5/6] Skipping Amazon channel identification (--skip-amazon)");
}

Console.WriteLine($"[6/6] Writing {events.Count} events to {outputPath}...");
var outputDir = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(outputDir))
{
    Directory.CreateDirectory(outputDir);
}

var json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(outputPath, json);

Console.WriteLine("Done.");

static string? GetArgValue(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

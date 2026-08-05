using System.Text.Json;
using SportsDeepLinks.Core.Deeplinks;
using SportsDeepLinks.Core.Models;

namespace SportsDeepLinks.Core.Extraction;

/// <summary>
/// Extracts ADB (deep-link) data per playable. Direct port of
/// fruit_import_appletv.py::extract_playables() - the entry point for turning Apple's raw
/// playable JSON into usable HTTPS deep links. Lane-priority fields (logical_service, priority)
/// are deliberately omitted, since that machinery exists only to order lane assignment.
/// </summary>
public static class PlayableExtractor
{
    /// <param name="espnGraphIdsByExternalId">
    /// Optional lookup of ESPN Watch Graph IDs (format "espn-watch:{playID}:{hash}") keyed by
    /// the Apple playable's "externalId" field - the same join key fruit_enrich_espn.py uses to
    /// match Apple-scraped ESPN playables against ESPN's own GraphQL "Airings" data (see the
    /// Phase 6 ESPN Watch Graph client). Pass null/empty if ESPN enrichment wasn't run.
    /// </param>
    public static List<Playable> ExtractPlayables(
        NormalizedAppleEvent evt,
        IReadOnlyDictionary<string, string>? espnGraphIdsByExternalId = null)
    {
        var result = new List<Playable>();

        foreach (var playableJson in evt.Playables.Values)
        {
            if (playableJson.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var playableId = JsonHelpers.GetFirstString(playableJson, "id", "playableId");
            if (string.IsNullOrEmpty(playableId))
            {
                continue;
            }

            var punchout = JsonHelpers.GetObjectOrDefault(playableJson, "punchoutUrls");
            var deeplinkPlay = JsonHelpers.GetString(punchout, "play") ?? JsonHelpers.GetString(playableJson, "deeplink_play");
            var deeplinkOpen = JsonHelpers.GetString(punchout, "open") ?? JsonHelpers.GetString(playableJson, "deeplink_open");
            var playableUrl = JsonHelpers.GetFirstString(playableJson, "playable_url", "url", "playableUrl");

            deeplinkPlay = ApplyEspnFixups(deeplinkPlay, playableJson);

            var url = deeplinkPlay ?? deeplinkOpen ?? playableUrl ?? "";
            string? provider = null;
            if (url.Contains("://", StringComparison.Ordinal))
            {
                provider = url.Split("://", 2)[0];
            }
            else if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
            {
                provider = "https";
            }

            var serviceName = JsonHelpers.GetFirstString(playableJson, "serviceName", "serviceDisplayName", "providerName");

            string? espnGraphId = null;
            var externalId = JsonHelpers.GetString(playableJson, "externalId");
            if (!string.IsNullOrEmpty(externalId) && espnGraphIdsByExternalId != null)
            {
                espnGraphIdsByExternalId.TryGetValue(externalId, out espnGraphId);
            }

            var httpDeeplinkUrl = ComputeHttpDeeplinkUrl(deeplinkPlay ?? deeplinkOpen, provider, playableId, espnGraphId);

            result.Add(new Playable
            {
                EventId = evt.Id,
                PlayableId = playableId,
                Provider = provider,
                ServiceName = serviceName,
                DeeplinkPlay = deeplinkPlay,
                DeeplinkOpen = deeplinkOpen,
                PlayableUrl = playableUrl,
                HttpDeeplinkUrl = httpDeeplinkUrl,
            });
        }

        return result;
    }

    /// <summary>
    /// ESPN-only fixup applied to deeplink_play before deep-link conversion: rewrite the
    /// channel-based "playChannel=" form to the "playID=" form using externalId, then strip any
    /// "x-source=" tracking param (needed for cross-platform/ADBTuner compatibility).
    /// </summary>
    private static string? ApplyEspnFixups(string? deeplinkPlay, JsonElement playableJson)
    {
        if (string.IsNullOrEmpty(deeplinkPlay) || !deeplinkPlay.Contains("sportscenter://", StringComparison.Ordinal))
        {
            return deeplinkPlay;
        }

        if (deeplinkPlay.Contains("playChannel=", StringComparison.Ordinal))
        {
            var externalId = JsonHelpers.GetString(playableJson, "externalId");
            if (!string.IsNullOrEmpty(externalId))
            {
                var lastColon = externalId.LastIndexOf(':');
                var playId = lastColon >= 0 ? externalId[(lastColon + 1)..] : externalId;
                deeplinkPlay = $"sportscenter://x-callback-url/showWatchStream?playID={playId}";
            }
        }

        if (deeplinkPlay.Contains("x-source=", StringComparison.Ordinal))
        {
            var idx = deeplinkPlay.IndexOf("x-source=", StringComparison.Ordinal);
            deeplinkPlay = deeplinkPlay[..idx].TrimEnd('&', '?');
        }

        return deeplinkPlay;
    }

    private static string? ComputeHttpDeeplinkUrl(string? deeplink, string? providerHint, string playableId, string? espnGraphId)
    {
        if (string.IsNullOrEmpty(deeplink))
        {
            return null;
        }

        if (deeplink.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            deeplink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return deeplink;
        }

        return DeeplinkConverter.GenerateHttpDeeplink(deeplink, providerHint, playableId, espnGraphId: espnGraphId);
    }
}

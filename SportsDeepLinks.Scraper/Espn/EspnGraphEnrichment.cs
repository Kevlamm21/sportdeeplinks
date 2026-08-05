using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SportsDeepLinks.Core.Extraction;

namespace SportsDeepLinks.Scraper.Espn;

/// <summary>
/// Builds a lookup of Apple playable "externalId" -> ESPN Watch Graph ID from a batch of
/// airings fetched via <see cref="EspnGraphClient"/>, ready to hand to
/// PlayableExtractor.ExtractPlayables' espnGraphIdsByExternalId parameter.
///
/// Port of fruit_enrich_espn.py's join logic: Apple's playable.externalId matches an airing's
/// nested "program.id" field, and the usable playback id is the airing's own id (falling back
/// to airingId, then simulcastAiringId) - the same fields fruit_ingest_espn_graph.py's
/// espn_player_url() uses to build "https://www.espn.com/watch/player/_/id/{id}".
///
/// Note: fruit_enrich_espn.py itself stores just the bare playback UUID in its espn_graph_id
/// column (explicitly stripping the "espn-watch:" prefix before saving), which doesn't actually
/// satisfy the "espn-watch:{playID}:{hash}" format ConvertEspn's tier-1 check expects (a bare
/// UUID has no ':' to split on). That looks like a latent inconsistency in the source project.
/// This port instead always builds the full "espn-watch:{playbackId}:{hash}" form via
/// BuildGraphId(), matching the documented/tested contract in DeeplinkConverter.
/// </summary>
public static class EspnGraphEnrichment
{
    public static Dictionary<string, string> BuildProgramIdLookup(IEnumerable<JsonElement> airings)
    {
        var lookup = new Dictionary<string, string>();

        foreach (var airing in airings)
        {
            var program = JsonHelpers.GetObjectOrDefault(airing, "program");
            var programId = JsonHelpers.GetString(program, "id");
            if (string.IsNullOrEmpty(programId))
            {
                continue;
            }

            var playbackId = JsonHelpers.GetFirstString(airing, "id", "airingId", "simulcastAiringId");
            if (string.IsNullOrEmpty(playbackId))
            {
                continue;
            }

            lookup[programId] = BuildGraphId("espn-watch", playbackId);
        }

        return lookup;
    }

    /// <summary>Port of fruit_ingest_espn_graph.py::stable_event_id() - "{source}:{id}:{hash32}".</summary>
    public static string BuildGraphId(string source, string playbackId)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{playbackId}"));
        var hash32 = Convert.ToHexStringLower(hashBytes)[..32];
        return $"{source}:{playbackId}:{hash32}";
    }
}

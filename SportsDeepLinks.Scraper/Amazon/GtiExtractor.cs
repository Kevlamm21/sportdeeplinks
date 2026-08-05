using System.Text.RegularExpressions;
using SportsDeepLinks.Core.Models;

namespace SportsDeepLinks.Scraper.Amazon;

/// <summary>
/// Pulls Amazon GTIs (amzn1.dv.gti.&lt;uuid&gt;) out of already-extracted Playables, so the
/// Amazon channel-identification scrape has something to work from without a database - port of
/// the regex half of amazon2.py::extract_gtis() (the SQL/DB selection logic is skipped since v1
/// has no persistent DB to query).
/// </summary>
public static class GtiExtractor
{
    private static readonly Regex GtiRegex = new(@"(amzn1\.dv\.gti\.[0-9a-fA-F-]{36})", RegexOptions.Compiled);

    public static HashSet<string> ExtractGtis(IEnumerable<Playable> playables)
    {
        var gtis = new HashSet<string>();

        foreach (var playable in playables)
        {
            var gti = ExtractGtiFromPlayable(playable);
            if (gti != null)
            {
                gtis.Add(gti);
            }
        }

        return gtis;
    }

    /// <summary>Returns the first GTI found across an "aiv" playable's deep-link/URL fields, if any.</summary>
    public static string? ExtractGtiFromPlayable(Playable playable)
    {
        if (!string.Equals(playable.Provider, "aiv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var field in new[] { playable.DeeplinkPlay, playable.DeeplinkOpen, playable.PlayableUrl })
        {
            if (string.IsNullOrEmpty(field))
            {
                continue;
            }

            var m = GtiRegex.Match(field);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return null;
    }
}

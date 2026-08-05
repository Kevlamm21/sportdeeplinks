using System.Text.RegularExpressions;

namespace SportsDeepLinks.Scraper.Amazon;

/// <summary>
/// Maps an Amazon "benefitId" (or its own page text, when no benefitId is present) to a
/// display name + canonical logical service id. Port of amazon2.py's BENEFIT_MAP, TEXT_INFER,
/// BENEFIT_RE_LIST, and _normalize().
/// </summary>
public static class BenefitMap
{
    public static readonly IReadOnlyDictionary<string, (string DisplayName, string LogicalServiceId)> Known =
        new Dictionary<string, (string, string)>
        {
            ["prime_included"] = ("Prime Exclusive", "aiv_prime"),
            ["daznus"] = ("DAZN", "aiv_dazn"),
            ["peacockus"] = ("Peacock", "aiv_peacock"),
            ["maxliveeventsus"] = ("Max", "aiv_max"),
            ["vixplusus"] = ("ViX Premium", "aiv_vix_premium"),
            ["vixus"] = ("ViX", "aiv_vix"),
            ["tennischannelus"] = ("Tennis Channel", "aiv_tennis_channel"),
            ["willowtv"] = ("Willow TV", "aiv_willow"),
            ["wnbalp"] = ("WNBA League Pass", "aiv_wnba_league_pass"),
            ["FSNOHIFSOH3"] = ("FanDuel Sports Network", "aiv_fanduel"),
            // Subscriber Product IDs (SPIDs) - Amazon's internal subscription identifiers.
            ["amzn1.dv.spid.8cc2a36e-cd1b-d2cb-0e3b-b9ddce868f1d"] = ("FOX One", "aiv_fox_one"),
            // Channel UUIDs (when benefit_id returns the channel instead of the short benefit id).
            ["amzn1.dv.channel.7a36cb2b-40e6-40c7-809f-a6cf9b9f0859"] = ("NBA League Pass", "aiv_nba_league_pass"),
        };

    public static readonly IReadOnlyList<(Regex Pattern, string DisplayName, string LogicalServiceId)> TextInfer = new List<(Regex, string, string)>
    {
        (new Regex(@"\bNBA League Pass\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "NBA League Pass", "aiv_nba_league_pass"),
        (new Regex(@"\bWNBA League Pass\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "WNBA League Pass", "aiv_wnba_league_pass"),
        (new Regex(@"\bFOX One\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "FOX One", "aiv_fox_one"),
        (new Regex(@"\bPeacock\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Peacock", "aiv_peacock"),
        (new Regex(@"\bMax\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Max", "aiv_max"),
        (new Regex(@"\bDAZN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "DAZN", "aiv_dazn"),
        (new Regex(@"\bFanDuel Sports Network\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "FanDuel Sports Network", "aiv_fanduel"),
        (new Regex(@"\bViX Premium\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ViX Premium", "aiv_vix_premium"),
        (new Regex(@"\bViX\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ViX", "aiv_vix"),
        (new Regex(@"\bParamount\+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Paramount+", "aiv_paramount_plus"),
        (new Regex(@"\bWillow\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Willow TV", "aiv_willow"),
        (new Regex(@"\bSquashTV\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "SquashTV", "aiv_squash"),
        (new Regex(@"\bTennis Channel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Tennis Channel", "aiv_tennis_channel"),
        (new Regex(@"\bPrime\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Prime Exclusive", "aiv_prime"),
    };

    private static readonly Regex BenefitIdQueryParam = new(@"benefitId=([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);
    private static readonly Regex BenefitIdJsonField = new("\"benefitId\"\\s*:\\s*\"([A-Za-z0-9_.\\-]+)\"", RegexOptions.Compiled);

    /// <summary>Scans all matches of both benefitId patterns, in order, skipping the "amzn1" false positive.</summary>
    public static string? ParseBenefitId(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (var regex in new[] { BenefitIdQueryParam, BenefitIdJsonField })
        {
            foreach (Match m in regex.Matches(text))
            {
                var value = m.Groups[1].Value;
                if (string.IsNullOrEmpty(value) || string.Equals(value, "amzn1", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return value;
            }
        }

        return null;
    }

    public static string? FindKnownBenefitIdInHtml(string html)
    {
        foreach (var benefitId in Known.Keys)
        {
            if (html.Contains(benefitId, StringComparison.Ordinal))
            {
                return benefitId;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns (DisplayName, LogicalServiceId, UnknownReason). UnknownReason is empty when
    /// resolved via the known BenefitMap; otherwise non-empty, even in the final fallback case
    /// (entitlement text used as a best-effort display name) - matching _normalize()'s "always
    /// returns something" behavior.
    /// </summary>
    public static (string DisplayName, string LogicalServiceId, string UnknownReason) Classify(
        string? benefitId, string? entitlementText, string? pageText)
    {
        if (!string.IsNullOrEmpty(benefitId) && Known.TryGetValue(benefitId, out var known))
        {
            return (known.DisplayName, known.LogicalServiceId, "");
        }

        if (!string.IsNullOrEmpty(entitlementText))
        {
            foreach (var (pattern, name, sid) in TextInfer)
            {
                if (pattern.IsMatch(entitlementText))
                {
                    return (name, sid, $"UNKNOWN_BENEFIT_ID benefit_id={benefitId} inferred_from=entitlement");
                }
            }
        }

        if (!string.IsNullOrEmpty(pageText))
        {
            foreach (var (pattern, name, sid) in TextInfer)
            {
                if (pattern.IsMatch(pageText))
                {
                    return (name, sid, $"UNKNOWN_BENEFIT_ID benefit_id={benefitId} inferred_from=page_text");
                }
            }
        }

        var safeName = string.IsNullOrWhiteSpace(entitlementText) ? "Amazon Error" : entitlementText.Trim();
        var slug = Regex.Replace(safeName.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        var fallbackServiceId = string.IsNullOrEmpty(slug) ? "aiv_aggregator" : $"aiv_{slug}";
        return (safeName, fallbackServiceId, $"UNKNOWN_BENEFIT_ID benefit_id={benefitId} fallback_to_entitlement");
    }
}

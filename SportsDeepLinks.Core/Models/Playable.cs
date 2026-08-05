namespace SportsDeepLinks.Core.Models;

/// <summary>
/// One streaming option for a <see cref="SportingEvent"/> - the "ADB" (deep link) plus enough
/// metadata to identify which service it launches. Port of the playables row shape produced by
/// fruit_import_appletv.py::extract_playables(), minus the lane-priority fields.
/// </summary>
public class Playable
{
    public required string EventId { get; init; }
    public required string PlayableId { get; init; }

    /// <summary>URL scheme of the deep link, e.g. "sportscenter", "pplus", "aiv".</summary>
    public string? Provider { get; init; }

    public string? ServiceName { get; init; }
    public string? DeeplinkPlay { get; init; }
    public string? DeeplinkOpen { get; init; }
    public string? PlayableUrl { get; init; }

    /// <summary>The "ADB" value - an HTTPS equivalent of the app-scheme deep link, when derivable.</summary>
    public string? HttpDeeplinkUrl { get; init; }

    /// <summary>Populated only for Amazon ("aiv") playables via the Phase 7 channel-identification scrape.</summary>
    public string? AmazonChannelId { get; set; }
    public string? AmazonChannelName { get; set; }
}

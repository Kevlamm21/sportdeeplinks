namespace SportsDeepLinks.Core.Models;

/// <summary>
/// A discovered sporting event and its streaming options. Narrow port of the fields
/// fruit_import_appletv.py::normalize_event_structure()/map_apple_to_fruit() need for
/// ADB extraction - title/synopsis-building, images, and genre tagging are intentionally
/// left out since they aren't part of the deep-link extraction path.
/// </summary>
public class SportingEvent
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public string? SportName { get; init; }
    public string? LeagueName { get; init; }

    /// <summary>Epoch milliseconds; first non-null of tuneInTime, liveBadgeTime, gameKickOffStartTime.</summary>
    public long? StartTimeMs { get; init; }
    public long? EndTimeMs { get; init; }

    public List<Playable> Playables { get; init; } = new();
}

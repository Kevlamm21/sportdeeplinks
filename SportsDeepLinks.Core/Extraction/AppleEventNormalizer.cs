using System.Text.Json;

namespace SportsDeepLinks.Core.Extraction;

/// <summary>
/// A raw Apple TV event, flattened down to the fields ADB extraction needs. Narrow port of
/// fruit_import_appletv.py::normalize_event_structure() - deliberately skips title/synopsis
/// building, image selection, and genre/classification tagging.
/// </summary>
public class NormalizedAppleEvent
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public string? SportName { get; init; }
    public string? LeagueName { get; init; }
    public long? StartTimeMs { get; init; }
    public long? EndTimeMs { get; init; }

    /// <summary>Merged playables dict (data.playables + data.content.playables, data-level wins), keyed by playable id.</summary>
    public required IReadOnlyDictionary<string, JsonElement> Playables { get; init; }
}

public static class AppleEventNormalizer
{
    /// <summary>
    /// Normalizes the raw JSON returned by AppleTvApiClient.FetchEventAsync (shape:
    /// {"data": {"canvas": ..., "content": ..., "playables": ...}, "channels": ...}) into a flat
    /// NormalizedAppleEvent, mirroring normalize_event_structure()'s field extraction and its
    /// "data-level playables take precedence over content-level playables" merge rule.
    /// </summary>
    public static NormalizedAppleEvent Normalize(string eventId, JsonElement rawApiResponse)
    {
        var data = JsonHelpers.GetObjectOrDefault(rawApiResponse, "data");
        var content = JsonHelpers.GetObjectOrDefault(data, "content");

        var merged = new Dictionary<string, JsonElement>();
        MergePlayablesInto(merged, GetPlayablesContainer(content));
        MergePlayablesInto(merged, GetPlayablesContainer(data)); // data-level overwrites content-level

        var eventTime = JsonHelpers.GetObjectOrDefault(content, "eventTime");
        var tuneIn = JsonHelpers.GetObjectOrDefault(eventTime, "tuneInTime");
        var liveBadge = JsonHelpers.GetObjectOrDefault(eventTime, "liveBadgeTime");

        var startMs = JsonHelpers.GetInt64(tuneIn, "startTime")
            ?? JsonHelpers.GetInt64(liveBadge, "startTime")
            ?? JsonHelpers.GetInt64(eventTime, "gameKickOffStartTime");

        var endMs = JsonHelpers.GetInt64(tuneIn, "endTime")
            ?? JsonHelpers.GetInt64(liveBadge, "endTime");

        return new NormalizedAppleEvent
        {
            Id = eventId,
            Title = JsonHelpers.GetFirstString(content, "title", "shortTitle"),
            SportName = JsonHelpers.GetString(content, "sportName"),
            LeagueName = JsonHelpers.GetString(content, "leagueName"),
            StartTimeMs = startMs,
            EndTimeMs = endMs,
            Playables = merged,
        };
    }

    /// <summary>
    /// "playables" can be either a dict-of-dicts (object, keyed by playable id) or a list of
    /// playable objects (each carrying its own "id") - Apple's API returns both shapes depending
    /// on endpoint/fetch level, so both are handled, matching the Python isinstance(..., dict/list) checks.
    /// </summary>
    private static JsonElement GetPlayablesContainer(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty("playables", out var playables))
        {
            return default;
        }

        return playables;
    }

    private static void MergePlayablesInto(Dictionary<string, JsonElement> target, JsonElement container)
    {
        switch (container.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in container.EnumerateObject())
                {
                    target[property.Name] = property.Value;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in container.EnumerateArray())
                {
                    var id = JsonHelpers.GetString(item, "id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        target[id] = item;
                    }
                }
                break;
        }
    }
}

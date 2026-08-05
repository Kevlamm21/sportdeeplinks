using System.Text.Json;
using SportsDeepLinks.Core.Extraction;
using Xunit;

namespace SportsDeepLinks.Tests;

public class PlayableExtractorTests
{
    private static NormalizedAppleEvent BuildEvent(string playablesJson, string eventId = "evt-1")
    {
        using var doc = JsonDocument.Parse(playablesJson);
        var playables = new Dictionary<string, JsonElement>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            playables[property.Name] = property.Value.Clone();
        }

        return new NormalizedAppleEvent
        {
            Id = eventId,
            Playables = playables,
        };
    }

    [Fact]
    public void ExtractPlayables_NormalPunchoutPlay_ProducesHttpDeeplink()
    {
        var evt = BuildEvent("""
            {
              "p1": {
                "id": "p1",
                "punchoutUrls": { "play": "pplus://www.paramountplus.com/live-tv/stream/serie-a/uuid/" },
                "serviceName": "Paramount+"
              }
            }
            """);

        var playables = PlayableExtractor.ExtractPlayables(evt);

        var p = Assert.Single(playables);
        Assert.Equal("evt-1", p.EventId);
        Assert.Equal("p1", p.PlayableId);
        Assert.Equal("pplus", p.Provider);
        Assert.Equal("Paramount+", p.ServiceName);
        Assert.Equal("https://www.paramountplus.com/live-tv/stream/serie-a/uuid/", p.HttpDeeplinkUrl);
    }

    [Fact]
    public void ExtractPlayables_EspnPlayChannel_RewritesUsingExternalIdSuffix_ThenResolvesViaPlayableIdTier()
    {
        // externalId's LAST colon-delimited segment ("4050e1f9") becomes the rewritten playID -
        // matching extract_playables()'s `external_id.split(":")[-1]`, which is the URL suffix,
        // not the embedded UUID. Since that suffix isn't a valid UUID, DeeplinkConverter falls
        // through ESPN tier 2 to tier 3, which regex-extracts the UUID from the playable's own
        // "id" field instead (the tvs.sbd.<n>:<uuid>:<suffix> pattern real Apple data uses).
        var evt = BuildEvent("""
            {
              "p1": {
                "id": "tvs.sbd.30061:21a4067c-1db2-4cfa-8b6c-e8c339b32047:4050e1f9",
                "punchoutUrls": { "play": "sportscenter://x-callback-url/showWatchStream?playChannel=espn1&x-source=AppleUMC" },
                "externalId": "tvs.sbd.30061:21a4067c-1db2-4cfa-8b6c-e8c339b32047:4050e1f9"
              }
            }
            """);

        var p = Assert.Single(PlayableExtractor.ExtractPlayables(evt));

        Assert.Equal("sportscenter://x-callback-url/showWatchStream?playID=4050e1f9", p.DeeplinkPlay);
        Assert.Equal("https://www.espn.com/watch/player/_/id/21a4067c-1db2-4cfa-8b6c-e8c339b32047", p.HttpDeeplinkUrl);
    }

    [Fact]
    public void ExtractPlayables_EspnXSource_IsStrippedFromDeeplink()
    {
        var evt = BuildEvent("""
            {
              "p1": {
                "id": "p1",
                "punchoutUrls": { "play": "sportscenter://x-callback-url/showWatchStream?playID=3be751ec-31ee-466d-9d5a-59645ee401aa&x-source=AppleUMC" }
              }
            }
            """);

        var p = Assert.Single(PlayableExtractor.ExtractPlayables(evt));

        Assert.Equal("sportscenter://x-callback-url/showWatchStream?playID=3be751ec-31ee-466d-9d5a-59645ee401aa", p.DeeplinkPlay);
        Assert.Equal("https://www.espn.com/watch/player/_/id/3be751ec-31ee-466d-9d5a-59645ee401aa", p.HttpDeeplinkUrl);
    }

    [Fact]
    public void ExtractPlayables_MissingPlayableId_IsSkipped()
    {
        var evt = BuildEvent("""
            {
              "p1": {
                "punchoutUrls": { "play": "pplus://www.paramountplus.com/live-tv/stream/x/y/" }
              }
            }
            """);

        Assert.Empty(PlayableExtractor.ExtractPlayables(evt));
    }

    [Theory]
    [InlineData("playable_url")]
    [InlineData("url")]
    [InlineData("playableUrl")]
    public void ExtractPlayables_PlayableUrlFieldNameFallbacks_AllRecognized(string fieldName)
    {
        var evt = BuildEvent($$"""
            {
              "p1": {
                "id": "p1",
                "{{fieldName}}": "https://example.com/watch"
              }
            }
            """);

        var p = Assert.Single(PlayableExtractor.ExtractPlayables(evt));

        Assert.Equal("https://example.com/watch", p.PlayableUrl);
        Assert.Equal("https", p.Provider);
        // HttpDeeplinkUrl is only derived from deeplink_play/deeplink_open, never playable_url,
        // matching extract_playables()'s compute_http_deeplink_url(deeplink_play or deeplink_open, ...).
        Assert.Null(p.HttpDeeplinkUrl);
    }

    [Fact]
    public void ExtractPlayables_EspnGraphIdLookup_TakesPriorityOverPlayChannel()
    {
        var evt = BuildEvent("""
            {
              "p1": {
                "id": "p1",
                "punchoutUrls": { "play": "sportscenter://x-callback-url/showWatchStream?playChannel=espn1" },
                "externalId": "program-123"
              }
            }
            """);

        var lookup = new Dictionary<string, string>
        {
            ["program-123"] = "espn-watch:9eb9b68b-11c6-4da0-9492-df997dbbf897:bb816546ee4e3a967b98e9d775c9c6f3",
        };

        var p = Assert.Single(PlayableExtractor.ExtractPlayables(evt, lookup));

        Assert.Equal("https://www.espn.com/watch/player/_/id/9eb9b68b-11c6-4da0-9492-df997dbbf897", p.HttpDeeplinkUrl);
    }
}

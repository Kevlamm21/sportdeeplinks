using SportsDeepLinks.Core.Deeplinks;
using Xunit;

namespace SportsDeepLinks.Tests;

/// <summary>
/// Golden fixtures ported 1:1 from deeplink_converter.py's `if __name__ == "__main__":`
/// self-test block, plus a couple of extra edge cases the Python source implies but
/// didn't originally test (invalid-UUID playID fallthrough, foxone:// scheme).
/// </summary>
public class DeeplinkConverterTests
{
    public static IEnumerable<object?[]> GoldenFixtures()
    {
        yield return new object?[]
        {
            "aiv://aiv/detail?gti=amzn1.dv.gti.10fd272d-309e-427a-87b6-6289003e2ccb&action=watch&type=live",
            "aiv", null, null, null, null,
            "https://app.primevideo.com/detail?gti=amzn1.dv.gti.10fd272d-309e-427a-87b6-6289003e2ccb",
        };
        yield return new object?[]
        {
            "sportscenter://x-callback-url/showWatchStream?playID=3be751ec-31ee-466d-9d5a-59645ee401aa&x-source=AppleUMC",
            "sportscenter", null, null, null, null,
            "https://www.espn.com/watch/player/_/id/3be751ec-31ee-466d-9d5a-59645ee401aa",
        };
        yield return new object?[]
        {
            "sportscenter://x-callback-url/showWatchStream?playChannel=espn1&x-source=AppleUMC",
            "sportscenter", "tvs.sbd.30061:21a4067c-1db2-4cfa-8b6c-e8c339b32047:4050e1f9", null, null, null,
            "https://www.espn.com/watch/player/_/id/21a4067c-1db2-4cfa-8b6c-e8c339b32047",
        };
        yield return new object?[]
        {
            "pplus://www.paramountplus.com/live-tv/stream/serie-a/49f986ec-3ab2-44d7-ade6-6dfd2df5b492/",
            "pplus", null, null, null, null,
            "https://www.paramountplus.com/live-tv/stream/serie-a/49f986ec-3ab2-44d7-ade6-6dfd2df5b492/",
        };
        yield return new object?[]
        {
            "cbstve://www.cbs.com/live-tv/stream/sports/046fb39f-9eda-4968-adde-c0162f566980/",
            "cbstve", null, null, null, null,
            "https://www.cbs.com/live-tv/stream/sports/046fb39f-9eda-4968-adde-c0162f566980/",
        };
        yield return new object?[]
        {
            "open.dazn.com://media/open/74d3bc02-dc0b-4060-8d79-c9eb3b103461",
            "open.dazn.com", null, null, null, null,
            "https://open.dazn.com/media/open/74d3bc02-dc0b-4060-8d79-c9eb3b103461",
        };
        yield return new object?[]
        {
            "vixapp://live/transmission-matchid-LGUA25065?play",
            "vixapp", null, null, null, null,
            "https://vix.com/es-es/live/transmission-matchid-LGUA25065?play",
        };
        yield return new object?[]
        {
            "fsapp://live/FS1?eventId=undefined&headerTitle=FOX+Sports+Live&sport=undefined",
            "fsapp", null, null, null, null,
            "https://www.foxsports.com/live/fs1?eventId=undefined&headerTitle=FOX+Sports+Live&sport=undefined",
        };
        yield return new object?[]
        {
            "watchtnt://play?stream=east&appId=27125",
            "watchtnt", null, null, null, null,
            "https://www.tntdrama.com/watchtnt?stream=east&appId=27125",
        };
        yield return new object?[]
        {
            "nbcsportstve://watch/12013522",
            "nbcsportstve", null, null, null, null,
            "https://www.nbcsports.com/watch/schedule",
        };
        yield return new object?[]
        {
            "gametime://game/0022500373?source=atv-search",
            "gametime", null, null, null, null,
            "https://www.nba.com/game/0022500373",
        };
        yield return new object?[]
        {
            "cbssportsapp://home/watch/LET-211531296?source=tvapp",
            "cbssportsapp", null, "Serie A", null, null,
            "https://www.cbssports.com/watch/serie-a/LET-211531296",
        };
        yield return new object?[]
        {
            "nflctv://livestream/f8d8eae6-311e-11f0-b670-ae1250fadad1",
            "nflctv", null, null, null, null,
            "https://www.nfl.com/plus/",
        };
        yield return new object?[]
        {
            "https://play.hbomax.com/sport/10440061-0516-538b-a098-9f71e1edfc33?utm_source=generic_apple",
            "max", null, null, null, null,
            "https://play.hbomax.com/video/watch-sport/10440061-0516-538b-a098-9f71e1edfc33",
        };
        yield return new object?[]
        {
            "sportscenter://x-callback-url/showWatchStream?playChannel=espn1&x-source=AppleUMC",
            "sportscenter", null, null, null,
            "espn-watch:9eb9b68b-11c6-4da0-9492-df997dbbf897:bb816546ee4e3a967b98e9d775c9c6f3",
            "https://www.espn.com/watch/player/_/id/9eb9b68b-11c6-4da0-9492-df997dbbf897",
        };
    }

    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public void GenerateHttpDeeplink_MatchesPythonGoldenFixtures(
        string punchoutUrl, string? provider, string? playableId, string? league, string? vixLocale,
        string? espnGraphId, string expected)
    {
        var actual = DeeplinkConverter.GenerateHttpDeeplink(
            punchoutUrl, provider, playableId, league, vixLocale, espnGraphId);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GenerateEspnSchemeDeeplink_FromGraphId_BuildsPlayIdScheme()
    {
        const string espnGraphId = "espn-watch:9eb9b68b-11c6-4da0-9492-df997dbbf897:bb816546ee4e3a967b98e9d775c9c6f3";
        const string expected = "sportscenter://x-callback-url/showWatchStream?playID=9eb9b68b-11c6-4da0-9492-df997dbbf897";

        Assert.Equal(expected, DeeplinkConverter.GenerateEspnSchemeDeeplink(espnGraphId, null));
    }

    [Fact]
    public void GenerateEspnSchemeDeeplink_NoGraphId_ReturnsFallback()
    {
        const string fallbackUrl = "sportscenter://x-callback-url/showWatchStream?playChannel=espn1";

        Assert.Equal(fallbackUrl, DeeplinkConverter.GenerateEspnSchemeDeeplink(null, fallbackUrl));
    }

    [Fact]
    public void ConvertEspn_InvalidUuidPlayId_FallsThroughToPlayableIdTier()
    {
        // playID present but not a valid UUID -> must not short-circuit at tier 2;
        // should fall through to tier 3 (playableId extraction).
        const string url = "sportscenter://x-callback-url/showWatchStream?playID=not-a-uuid";
        const string playableId = "tvs.sbd.30061:21a4067c-1db2-4cfa-8b6c-e8c339b32047:4050e1f9";

        var result = DeeplinkConverter.ConvertEspn(url, playableId);

        Assert.Equal("https://www.espn.com/watch/player/_/id/21a4067c-1db2-4cfa-8b6c-e8c339b32047", result);
    }

    [Fact]
    public void ConvertEspn_NoSignals_FallsBackToLandingPage()
    {
        const string url = "sportscenter://x-callback-url/showWatchStream?playChannel=espn1";

        Assert.Equal("https://www.espn.com/watch/", DeeplinkConverter.ConvertEspn(url));
    }

    [Fact]
    public void ConvertFox_FoxoneChannelScheme_RewritesToFoxSportsLive()
    {
        const string url = "foxone://channel/fs1";

        Assert.Equal("https://www.foxsports.com/live/fs1", DeeplinkConverter.ConvertFox(url));
    }

    [Fact]
    public void ExtractGtiFromDeeplink_PrefersBroadcastParamOverGtiParam()
    {
        const string deeplink =
            "aiv://aiv/detail?gti=amzn1.dv.gti.00000000-0000-0000-0000-000000000000" +
            "&broadcast=amzn1.dv.gti.11111111-1111-1111-1111-111111111111";

        Assert.Equal(
            "amzn1.dv.gti.11111111-1111-1111-1111-111111111111",
            DeeplinkConverter.ExtractGtiFromDeeplink(deeplink));
    }

    [Fact]
    public void ExtractGtiFromDeeplink_FallsBackToGtiParam()
    {
        const string deeplink = "aiv://aiv/detail?gti=amzn1.dv.gti.00000000-0000-0000-0000-000000000000";

        Assert.Equal(
            "amzn1.dv.gti.00000000-0000-0000-0000-000000000000",
            DeeplinkConverter.ExtractGtiFromDeeplink(deeplink));
    }
}

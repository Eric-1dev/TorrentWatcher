using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Parsing;

namespace TorrentWatcher.UnitTests.Domain.Parsing;

public sealed class RutrackerUrlParserTests
{
    [Theory]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=123456", 123456)]
    [InlineData("http://rutracker.org/forum/viewtopic.php?t=123456", 123456)]
    [InlineData("https://www.rutracker.org/forum/viewtopic.php?t=42", 42)]
    [InlineData("https://RUTRACKER.ORG/forum/viewtopic.php?t=7", 7)]
    [InlineData("rutracker.org/forum/viewtopic.php?t=123", 123)]
    [InlineData("viewtopic.php?t=321", 321)]
    [InlineData("https://rutracker.org/forum/viewtopic.php?start=50&t=999", 999)]
    [InlineData("https://rutracker.org/forum/viewtopic.php?T=123", 123)]
    [InlineData("https://rutracker.org/forum/dl.php?t=555", 555)]
    [InlineData("dl.php?t=555", 555)]
    [InlineData("https://rutracker.org/forum/viewtopic.php#t=777", 777)]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=123#t=456", 123)]
    [InlineData("  123456  ", 123456)]
    [InlineData("007", 7)]
    public void TryParseTopicId_ValidInput_ReturnsId(string input, int expected)
    {
        bool parsed = RutrackerUrlParser.TryParseTopicId(input, out int topicId);

        Assert.True(parsed);
        Assert.Equal(expected, topicId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12.5")]
    [InlineData("12a")]
    [InlineData("2147483648")]
    [InlineData("t=123")]
    [InlineData("https://rutracker.org/forum/index.php")]
    [InlineData("https://rutracker.org/forum/viewtopic.php")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=abc")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=12a")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=-1")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?start=50")]
    [InlineData("https://example.com/forum/viewtopic.php?t=123")]
    [InlineData("https://rutracker.net/forum/viewtopic.php?t=123")]
    [InlineData("ftp://rutracker.org/forum/viewtopic.php?t=123")]
    public void TryParseTopicId_InvalidInput_ReturnsFalse(string? input)
    {
        bool parsed = RutrackerUrlParser.TryParseTopicId(input, out int topicId);

        Assert.False(parsed);
        Assert.Equal(0, topicId);
    }

    [Fact]
    public void Download_PointsToDlPhpWithTopicId()
    {
        Assert.Equal("https://rutracker.org/forum/dl.php?t=123456", RutrackerUrls.Download(123456));
    }

    [Fact]
    public void Topic_PointsToViewtopicPhpWithTopicId()
    {
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=123456", RutrackerUrls.Topic(123456));
    }

    [Fact]
    public void CanonicalTopicUrl_ParsesBackToSameTopicId()
    {
        string url = RutrackerUrls.Topic(123456);

        Assert.True(RutrackerUrlParser.TryParseTopicId(url, out int topicId));
        Assert.Equal(123456, topicId);
    }
}

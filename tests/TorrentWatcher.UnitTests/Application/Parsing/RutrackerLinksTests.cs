using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Parsing;

namespace TorrentWatcher.UnitTests.Application.Parsing;

public sealed class RutrackerLinksTests
{
    [Fact]
    public void ParseTopicId_ValidUrl_ReturnsId()
    {
        Assert.Equal(123456, RutrackerLinks.ParseTopicId("https://rutracker.org/forum/viewtopic.php?t=123456"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("мусор")]
    [InlineData("https://example.com/forum/viewtopic.php?t=1")]
    public void ParseTopicId_InvalidInput_ThrowsInvalidUrl(string input)
    {
        AppException exception = Assert.Throws<AppException>(() => RutrackerLinks.ParseTopicId(input));

        Assert.Equal(ErrorCodes.InvalidUrl, exception.Code);
        Assert.Equal("INVALID_URL", exception.Code);
    }
}

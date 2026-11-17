using TorrentWatcher.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Domain.Parsing;

public sealed class TorrentMetadataParserTests
{
    [Fact]
    public void SingleFile_ReturnsMetadata()
    {
        (byte[] torrent, byte[] info) = TorrentFixtures.SingleFile("ubuntu-24.04.iso", 1_048_576);

        TorrentMetadata metadata = TorrentMetadataParser.Parse(torrent);

        Assert.Equal("ubuntu-24.04.iso", metadata.Name);
        Assert.Equal(1_048_576, metadata.SizeBytes);
        Assert.Equal(1, metadata.FileCount);
        Assert.False(metadata.IsPrivate);
        Assert.Matches("^[0-9a-f]{40}$", metadata.InfoHashV1);
        Assert.Equal(TorrentFixtures.ExpectedInfoHash(info), metadata.InfoHashV1);
    }

    [Fact]
    public void MultiFile_SumsLengthsAndCountsFiles()
    {
        (byte[] torrent, byte[] info) = TorrentFixtures.MultiFile(
            "Кизиловый щербет / Kızılcık Şerbeti",
            [
                ("1 серия.avi", 1_500_000_000),
                ("2 серия.avi", 1_400_000_000),
                ("3 серия.avi", 1_600_000_000),
            ],
            isPrivate: true);

        TorrentMetadata metadata = TorrentMetadataParser.Parse(torrent);

        Assert.Equal("Кизиловый щербет / Kızılcık Şerbeti", metadata.Name);
        Assert.Equal(4_500_000_000, metadata.SizeBytes);
        Assert.Equal(3, metadata.FileCount);
        Assert.True(metadata.IsPrivate);
        Assert.Equal(TorrentFixtures.ExpectedInfoHash(info), metadata.InfoHashV1);
    }

    [Fact]
    public void Utf8Name_TakesPriorityOverLegacyName()
    {
        (byte[] torrent, _) = TorrentFixtures.SingleFileWithUtf8Name(
            "Кино в кино",
            [0xEA, 0xE8, 0xED, 0xEE]); // «кино» в cp1251

        TorrentMetadata metadata = TorrentMetadataParser.Parse(torrent);

        Assert.Equal("Кино в кино", metadata.Name);
    }

    [Fact]
    public void PrivateZero_IsNotPrivate()
    {
        byte[] info = TestBencode.Dictionary(
            ("length", TestBencode.Integer(10)),
            ("name", TestBencode.String("x")),
            ("private", TestBencode.Integer(0)));

        TorrentMetadata metadata = TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info));

        Assert.False(metadata.IsPrivate);
    }

    [Fact]
    public void InfoHash_DependsOnlyOnInfoDictionary()
    {
        (byte[] torrent, byte[] info) = TorrentFixtures.SingleFile();
        byte[] anotherTorrent = TestBencode.Dictionary(
            ("announce", TestBencode.String("https://another.example/announce")),
            ("comment", TestBencode.String("другой комментарий")),
            ("info", info));

        string originalHash = TorrentMetadataParser.Parse(torrent).InfoHashV1;
        string anotherHash = TorrentMetadataParser.Parse(anotherTorrent).InfoHashV1;

        Assert.Equal(originalHash, anotherHash);
    }

    [Fact]
    public void RealRutrackerTorrent_ParsesWithExpectedInfoHash()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rutracker-multifile.torrent");
        byte[] data = File.ReadAllBytes(path);

        TorrentMetadata metadata = TorrentMetadataParser.Parse(data);

        Assert.Equal("Dalekiy gorod 3 sezon", metadata.Name);
        Assert.Equal("97af3f3b7cfb36f86696a8c22065222fab67a116", metadata.InfoHashV1);
        Assert.Equal(13_642_873_018, metadata.SizeBytes);
        Assert.Equal(9, metadata.FileCount);
        Assert.False(metadata.IsPrivate);
    }

    [Fact]
    public void EmptyTorrent_ThrowsBencodeError()
    {
        Assert.Throws<BencodeFormatException>(() => TorrentMetadataParser.Parse(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void TruncatedTorrent_ThrowsBencodeError()
    {
        (byte[] torrent, _) = TorrentFixtures.SingleFile();

        Assert.Throws<BencodeFormatException>(() => TorrentMetadataParser.Parse(torrent[..^1]));
    }

    [Fact]
    public void NonDictionaryRoot_ThrowsTorrentError()
    {
        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TestBencode.String("hello")));
    }

    [Fact]
    public void MissingInfo_ThrowsTorrentError()
    {
        byte[] torrent = TestBencode.Dictionary(("announce", TestBencode.String("https://rutracker.org/ann")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(torrent));
    }

    [Fact]
    public void InfoIsNotDictionary_ThrowsTorrentError()
    {
        byte[] torrent = TestBencode.Dictionary(("info", TestBencode.List()));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(torrent));
    }

    [Fact]
    public void MissingName_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(("length", TestBencode.Integer(1)));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void EmptyName_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("length", TestBencode.Integer(1)),
            ("name", TestBencode.String("")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void MissingLengthAndFiles_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void NegativeLength_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("length", TestBencode.Integer(-1)),
            ("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void EmptyFilesList_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("files", TestBencode.List()),
            ("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void FileWithoutLength_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("files", TestBencode.List(TestBencode.Dictionary(("path", TestBencode.List(TestBencode.String("a.txt")))))),
            ("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void FilesEntryIsNotDictionary_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("files", TestBencode.List(TestBencode.String("a.txt"))),
            ("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }

    [Fact]
    public void SizeOverflow_ThrowsTorrentError()
    {
        byte[] info = TestBencode.Dictionary(
            ("files", TestBencode.List(
                TestBencode.Dictionary(("length", TestBencode.Integer(long.MaxValue))),
                TestBencode.Dictionary(("length", TestBencode.Integer(long.MaxValue))))),
            ("name", TestBencode.String("x")));

        Assert.Throws<TorrentFormatException>(() => TorrentMetadataParser.Parse(TorrentFixtures.BuildTorrent(info)));
    }
}

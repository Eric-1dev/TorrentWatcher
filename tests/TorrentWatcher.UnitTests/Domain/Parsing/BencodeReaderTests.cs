using System.Text;
using TorrentWatcher.Domain.Parsing;

namespace TorrentWatcher.UnitTests.Domain.Parsing;

public sealed class BencodeReaderTests
{
    [Theory]
    [InlineData("i42e", 42)]
    [InlineData("i-7e", -7)]
    [InlineData("i0e", 0)]
    [InlineData("i9223372036854775807e", long.MaxValue)]
    [InlineData("i-9223372036854775808e", long.MinValue)]
    public void Integer_ReturnsValue(string input, long expected)
    {
        BencodeInteger value = Assert.IsType<BencodeInteger>(Parse(input));

        Assert.Equal(expected, value.Value);
        Assert.Equal(0, value.Start);
        Assert.Equal(input.Length, value.End);
    }

    [Fact]
    public void String_ReturnsRawBytes()
    {
        BencodeString value = Assert.IsType<BencodeString>(Parse("5:hello"));

        Assert.Equal("hello", value.AsUtf8());
        Assert.Equal(5, value.Bytes.Length);
    }

    [Fact]
    public void String_Utf8Bytes_AreDecoded()
    {
        byte[] data = Encoding.UTF8.GetBytes("4:жж");

        BencodeString value = Assert.IsType<BencodeString>(BencodeReader.Parse(data));

        Assert.Equal("жж", value.AsUtf8());
    }

    [Fact]
    public void EmptyString_IsValid()
    {
        BencodeString value = Assert.IsType<BencodeString>(Parse("0:"));

        Assert.True(value.Bytes.IsEmpty);
    }

    [Fact]
    public void List_ReturnsItems()
    {
        BencodeList value = Assert.IsType<BencodeList>(Parse("l3:fooi42ee"));

        Assert.Equal(2, value.Items.Count);
        Assert.Equal("foo", Assert.IsType<BencodeString>(value.Items[0]).AsUtf8());
        Assert.Equal(42, Assert.IsType<BencodeInteger>(value.Items[1]).Value);
    }

    [Fact]
    public void Dictionary_ReturnsEntries()
    {
        BencodeDictionary value = Assert.IsType<BencodeDictionary>(Parse("d3:fooi1e3:bar3:quxe"));

        Assert.Equal(2, value.Entries.Count);
        Assert.Equal(1, Assert.IsType<BencodeInteger>(value.Entries["foo"]).Value);
        Assert.Equal("qux", Assert.IsType<BencodeString>(value.Entries["bar"]).AsUtf8());
        Assert.Equal(20, value.End);
    }

    [Fact]
    public void NestedValue_OffsetsSliceSourceBytes()
    {
        byte[] data = Encoding.ASCII.GetBytes("d4:infod1:ai1eee");

        BencodeDictionary root = Assert.IsType<BencodeDictionary>(BencodeReader.Parse(data));
        BencodeDictionary info = Assert.IsType<BencodeDictionary>(root.Entries["info"]);

        Assert.Equal("d1:ai1ee", Encoding.ASCII.GetString(data[info.Start..info.End]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("i")]
    [InlineData("i12")]
    [InlineData("i-e")]
    [InlineData("i+1e")]
    [InlineData("i01e")]
    [InlineData("i-0e")]
    [InlineData("i9223372036854775808e")]
    [InlineData("01:a")]
    [InlineData("5:abc")]
    [InlineData("99999999999999999999:x")]
    [InlineData("l")]
    [InlineData("li1e")]
    [InlineData("d")]
    [InlineData("d1:a")]
    [InlineData("d1:ae")]
    [InlineData("d1:ai1e1:ai2ee")]
    [InlineData("di1ei2ee")]
    [InlineData("i1eX")]
    public void InvalidDocument_Throws(string input)
    {
        Assert.Throws<BencodeFormatException>(() => Parse(input));
    }

    [Fact]
    public void TooDeepNesting_Throws()
    {
        string input = new string('l', BencodeReader.MaxDepth + 2) + new string('e', BencodeReader.MaxDepth + 2);

        Assert.Throws<BencodeFormatException>(() => Parse(input));
    }

    [Fact]
    public void MaxAllowedNesting_Parses()
    {
        string input = new string('l', BencodeReader.MaxDepth) + new string('e', BencodeReader.MaxDepth);

        Assert.IsType<BencodeList>(Parse(input));
    }

    private static BencodeValue Parse(string ascii) => BencodeReader.Parse(Encoding.ASCII.GetBytes(ascii));
}

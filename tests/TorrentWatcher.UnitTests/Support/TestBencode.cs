using System.Globalization;
using System.Text;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Минимальный bencode-кодировщик для сборки фикстур в тестах.</summary>
internal static class TestBencode
{
    public static byte[] String(string value) => String(Encoding.UTF8.GetBytes(value));

    public static byte[] String(byte[] bytes)
    {
        byte[] prefix = Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":");
        return Concat(prefix, bytes);
    }

    public static byte[] Integer(long value) =>
        Encoding.ASCII.GetBytes("i" + value.ToString(CultureInfo.InvariantCulture) + "e");

    public static byte[] List(params byte[][] items)
    {
        List<byte> result = [(byte)'l'];
        foreach (byte[] item in items)
        {
            result.AddRange(item);
        }

        result.Add((byte)'e');
        return [.. result];
    }

    public static byte[] Dictionary(params (string Key, byte[] Value)[] entries)
    {
        List<byte> result = [(byte)'d'];
        foreach ((string key, byte[] value) in entries)
        {
            result.AddRange(String(key));
            result.AddRange(value);
        }

        result.Add((byte)'e');
        return [.. result];
    }

    public static byte[] Concat(params byte[][] parts)
    {
        List<byte> result = [];
        foreach (byte[] part in parts)
        {
            result.AddRange(part);
        }

        return [.. result];
    }
}

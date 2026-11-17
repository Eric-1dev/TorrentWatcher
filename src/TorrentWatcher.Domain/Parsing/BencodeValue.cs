namespace TorrentWatcher.Domain.Parsing;

/// <summary>
/// Разобранное bencode-значение. <see cref="Start"/> и <see cref="End"/> — позиции в исходном
/// документе, по ним считается infohash без повторной сериализации.
/// </summary>
public abstract class BencodeValue
{
    private protected BencodeValue(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Позиция первого байта значения в документе.</summary>
    public int Start { get; }

    /// <summary>Позиция сразу после последнего байта значения в документе.</summary>
    public int End { get; }
}

/// <summary>Целое число bencode (<c>i42e</c>).</summary>
public sealed class BencodeInteger : BencodeValue
{
    internal BencodeInteger(int start, int end, long value)
        : base(start, end)
    {
        Value = value;
    }

    public long Value { get; }
}

/// <summary>Строка bencode (<c>5:hello</c>), хранится как срез исходных байтов.</summary>
public sealed class BencodeString : BencodeValue
{
    internal BencodeString(int start, int end, ReadOnlyMemory<byte> bytes)
        : base(start, end)
    {
        Bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Декодирует строку как UTF-8 (невалидные байты заменяются).</summary>
    public string AsUtf8() => System.Text.Encoding.UTF8.GetString(Bytes.Span);
}

/// <summary>Список bencode (<c>l...e</c>).</summary>
public sealed class BencodeList : BencodeValue
{
    internal BencodeList(int start, int end, IReadOnlyList<BencodeValue> items)
        : base(start, end)
    {
        Items = items;
    }

    public IReadOnlyList<BencodeValue> Items { get; }
}

/// <summary>Словарь bencode (<c>d...e</c>). Ключи сравниваются с учётом регистра.</summary>
public sealed class BencodeDictionary : BencodeValue
{
    internal BencodeDictionary(int start, int end, IReadOnlyDictionary<string, BencodeValue> entries)
        : base(start, end)
    {
        Entries = entries;
    }

    public IReadOnlyDictionary<string, BencodeValue> Entries { get; }
}

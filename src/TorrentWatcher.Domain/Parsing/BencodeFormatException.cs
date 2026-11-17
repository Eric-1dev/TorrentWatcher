namespace TorrentWatcher.Domain.Parsing;

/// <summary>Ошибка разбора bencode-документа.</summary>
public sealed class BencodeFormatException : Exception
{
    public BencodeFormatException(string message)
        : base(message)
    {
    }

    public BencodeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace TorrentWatcher.Domain.Parsing;

/// <summary>Ошибка структуры .torrent: документ корректен как bencode, но это не v1-торрент.</summary>
public sealed class TorrentFormatException : Exception
{
    public TorrentFormatException(string message)
        : base(message)
    {
    }

    public TorrentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

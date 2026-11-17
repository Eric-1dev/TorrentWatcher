namespace TorrentWatcher.Domain;

/// <summary>
/// Канонические адреса рутрекера. Хост фиксирован: все запросы идут только на rutracker.org.
/// </summary>
public static class RutrackerUrls
{
    public const string BaseUrl = "https://rutracker.org";

    public const string ForumUrl = BaseUrl + "/forum";

    /// <summary>Ссылка на топик для показа пользователю.</summary>
    public static string Topic(int topicId) => $"{ForumUrl}/viewtopic.php?t={topicId}";

    /// <summary>Ссылка неизменного скачивания <c>.torrent</c> по идентификатору топика.</summary>
    public static string Download(int topicId) => $"{ForumUrl}/dl.php?t={topicId}";
}

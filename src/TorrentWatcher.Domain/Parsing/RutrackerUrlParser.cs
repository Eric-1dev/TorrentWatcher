using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TorrentWatcher.Domain.Parsing;

/// <summary>
/// Разбирает пользовательский ввод в идентификатор топика рутрекера.
/// Поддерживаются ссылки на топик (<c>viewtopic.php?t=</c>), ссылки на скачивание
/// (<c>dl.php?t=</c>), якорь <c>#t=</c> и голый числовой id.
/// </summary>
public static class RutrackerUrlParser
{
    private const string TopicPage = "viewtopic.php";
    private const string DownloadPage = "dl.php";
    private const string TopicIdParameter = "t";

    private static readonly Uri s_relativeBase = new(RutrackerUrls.ForumUrl + "/", UriKind.Absolute);

    /// <summary>
    /// Пробует распознать идентификатор топика. Возвращает <c>false</c> для любого мусора,
    /// неположительных и переполняющих <see cref="int"/> значений.
    /// </summary>
    public static bool TryParseTopicId(string? input, out int topicId)
    {
        topicId = 0;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string value = input.Trim();

        if (TryReadId(value, out int bareId))
        {
            topicId = bareId;
            return true;
        }

        if (!TryCreateUri(value, out Uri? uri))
        {
            return false;
        }

        if (!IsRutrackerHost(uri.Host))
        {
            return false;
        }

        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        bool isTopicLink = EndsWithPage(path, TopicPage);
        bool isDownloadLink = EndsWithPage(path, DownloadPage);

        if (!isTopicLink && !isDownloadLink)
        {
            return false;
        }

        int? fromQuery = FindTopicId(uri.Query.TrimStart('?'));
        if (fromQuery is int queryId)
        {
            topicId = queryId;
            return true;
        }

        int? fromFragment = FindTopicId(uri.Fragment.TrimStart('#'));
        if (fromFragment is int fragmentId)
        {
            topicId = fragmentId;
            return true;
        }

        return false;
    }

    private static bool TryCreateUri(string value, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        // Схема, отличная от http(s), — не наша ссылка; относительный путь достраиваем от форума.
        if (value.Contains("://", StringComparison.Ordinal))
        {
            uri = null;
            return false;
        }

        return Uri.TryCreate(s_relativeBase, value, out uri);
    }

    private static bool IsRutrackerHost(string host) =>
        host.Equals("rutracker.org", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.rutracker.org", StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithPage(string path, string page) =>
        path.EndsWith("/" + page, StringComparison.OrdinalIgnoreCase);

    private static int? FindTopicId(string queryOrFragment)
    {
        foreach (string pair in queryOrFragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!pair.AsSpan(0, separator).Equals(TopicIdParameter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryReadId(pair[(separator + 1)..], out int id) ? id : null;
        }

        return null;
    }

    private static bool TryReadId(string value, out int topicId) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out topicId) && topicId > 0;
}

using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Domain.Parsing;

namespace TorrentWatcher.Application.Parsing;

/// <summary>
/// Разбор пользовательской ссылки на топик в терминах прикладного слоя: либо идентификатор,
/// либо ошибка с кодом <see cref="ErrorCodes.InvalidUrl"/>.
/// </summary>
public static class RutrackerLinks
{
    /// <summary>Возвращает идентификатор топика или бросает <see cref="AppException"/> с кодом INVALID_URL.</summary>
    public static int ParseTopicId(string? input)
    {
        if (RutrackerUrlParser.TryParseTopicId(input, out int topicId))
        {
            return topicId;
        }

        throw new AppException(
            ErrorCodes.InvalidUrl,
            "Не удалось распознать ссылку на топик рутрекера. "
            + "Подойдёт viewtopic.php?t=…, dl.php?t=…, ссылка с #t=… или числовой id.");
    }
}

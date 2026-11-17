using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Parsing;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.Storage;
using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Domain.Parsing;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Use-cases списка торрентов: добавление по ссылке на топик, список, удаление,
/// включение/пауза. Первое скачивание сразу сохраняет .torrent и метаданные.
/// </summary>
public sealed class TorrentService
{
    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly IRutrackerClient _rutrackerClient;
    private readonly ITorrentFileStorage _fileStorage;
    private readonly ISettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;

    public TorrentService(
        ITrackedTorrentRepository torrentRepository,
        IRutrackerClient rutrackerClient,
        ITorrentFileStorage fileStorage,
        ISettingsProvider settingsProvider,
        TimeProvider timeProvider)
    {
        _torrentRepository = torrentRepository;
        _rutrackerClient = rutrackerClient;
        _fileStorage = fileStorage;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
    }

    public async Task<TorrentListResult> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TrackedTorrent> torrents = await _torrentRepository.GetAllAsync(cancellationToken);
        TorrentView[] items = torrents.Select(TorrentView.FromEntity).ToArray();

        return new TorrentListResult(
            items,
            TotalCount: items.Length,
            UpdateAvailableCount: torrents.Count(torrent => torrent.LastCheckOutcome == TorrentCheckOutcome.Changed));
    }

    /// <summary>
    /// Добавляет торрент: разбирает ссылку, скачивает .torrent текущей кукой, сохраняет файл
    /// и метаданные. Дубликат по topicId отклоняется до скачивания.
    /// </summary>
    /// <exception cref="AppException">
    /// INVALID_URL, DUPLICATE_TOPIC, RUTRACKER_COOKIE_INVALID, RUTRACKER_* или TORRENT_STORAGE_FAILED.
    /// </exception>
    public async Task<TorrentView> AddAsync(string? url, CancellationToken cancellationToken)
    {
        int topicId = RutrackerLinks.ParseTopicId(url);

        if (await _torrentRepository.GetByTopicIdAsync(topicId, cancellationToken) is not null)
        {
            throw new AppException(
                ErrorCodes.DuplicateTopic,
                $"Топик {topicId} уже отслеживается.");
        }

        AppSettingsSnapshot settings = _settingsProvider.Current;
        if (string.IsNullOrWhiteSpace(settings.RutrackerCookie))
        {
            throw new AppException(
                ErrorCodes.RutrackerCookieInvalid,
                "Кука рутрекера не задана. Сохраните её в настройках, иначе скачать .torrent нельзя.");
        }

        byte[] payload = await _rutrackerClient.DownloadTorrentAsync(
            new RutrackerDownloadRequest(topicId, settings.RutrackerCookie, settings.RequestTimeout),
            cancellationToken);

        TorrentMetadata metadata = ParseMetadata(payload, topicId);

        StoredTorrentFile stored = await _fileStorage.SaveAsync(
            topicId,
            payload,
            cancellationToken);

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        var torrent = new TrackedTorrent
        {
            TopicId = topicId,
            TopicUrl = RutrackerUrls.Topic(topicId),
            Title = metadata.Name,
            InfoHashV1 = metadata.InfoHashV1,
            TorrentSizeBytes = metadata.SizeBytes,
            FileCount = metadata.FileCount,
            TorrentFilePath = stored.FilePath,
            TorrentFileSha256 = stored.Sha256,
            TorrentFileDownloadedAtUtc = stored.StoredAtUtc,
            LastCheckedAtUtc = stored.StoredAtUtc,
            LastCheckOutcome = TorrentCheckOutcome.Unchanged,
            LastError = null,
            LastChangedAtUtc = null,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await _torrentRepository.AddAsync(torrent, cancellationToken);

        return TorrentView.FromEntity(torrent);
    }

    /// <summary>Убирает торрент из отслеживания и удаляет его .torrent.</summary>
    /// <exception cref="AppException">TORRENT_NOT_FOUND или TORRENT_STORAGE_FAILED.</exception>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        TrackedTorrent torrent = await GetRequiredAsync(id, cancellationToken);

        // Файл удаляем первым: если диск недоступен, запись остаётся и операцию можно повторить.
        await _fileStorage.DeleteAsync(torrent.TopicId, cancellationToken);
        await _torrentRepository.DeleteAsync(torrent, cancellationToken);
    }

    /// <summary>Ставит торрент на паузу или возвращает в автоскан.</summary>
    /// <exception cref="AppException">TORRENT_NOT_FOUND.</exception>
    public async Task<TorrentView> SetEnabledAsync(
        int id,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        TrackedTorrent torrent = await GetRequiredAsync(id, cancellationToken);

        torrent.IsEnabled = isEnabled;
        torrent.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await _torrentRepository.UpdateAsync(torrent, cancellationToken);

        return TorrentView.FromEntity(torrent);
    }

    private async Task<TrackedTorrent> GetRequiredAsync(int id, CancellationToken cancellationToken)
    {
        TrackedTorrent? torrent = await _torrentRepository.GetByIdAsync(id, cancellationToken);

        if (torrent is null)
        {
            throw new AppException(ErrorCodes.TorrentNotFound, $"Торрент {id} не найден.");
        }

        return torrent;
    }

    private static TorrentMetadata ParseMetadata(byte[] payload, int topicId)
    {
        try
        {
            return TorrentMetadataParser.Parse(payload);
        }
        catch (Exception exception) when (exception is BencodeFormatException or TorrentFormatException)
        {
            throw new AppException(
                ErrorCodes.RutrackerInvalidPayload,
                $"Рутрекер вернул повреждённый .torrent для топика {topicId}.",
                exception);
        }
    }
}

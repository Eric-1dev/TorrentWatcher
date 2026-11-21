using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.Storage;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Domain.Parsing;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Проверка одного торрента: скачивает актуальный .torrent, сравнивает infohash v1,
/// сохраняет файл и фиксирует <see cref="TorrentCheckOutcome"/>, дату обновления и ошибку.
/// </summary>
public sealed class TorrentScanService
{
    private const int MaxErrorMessageLength = 4000;

    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly IRutrackerClient _rutrackerClient;
    private readonly IRutrackerSessionProvider _session;
    private readonly ITorrentFileStorage _fileStorage;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ITorrentUpdateApplier _updateApplier;
    private readonly TimeProvider _timeProvider;

    public TorrentScanService(
        ITrackedTorrentRepository torrentRepository,
        IRutrackerClient rutrackerClient,
        IRutrackerSessionProvider session,
        ITorrentFileStorage fileStorage,
        ISettingsProvider settingsProvider,
        ITorrentUpdateApplier updateApplier,
        TimeProvider timeProvider)
    {
        _torrentRepository = torrentRepository;
        _rutrackerClient = rutrackerClient;
        _session = session;
        _fileStorage = fileStorage;
        _settingsProvider = settingsProvider;
        _updateApplier = updateApplier;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Проверяет торрент и сохраняет результат. Ожидаемые сбои рутрекера и диска
    /// превращаются в <see cref="TorrentCheckResult"/> с outcome Error, а не в исключение.
    /// </summary>
    /// <exception cref="AppException">TORRENT_NOT_FOUND, если такой записи нет.</exception>
    public async Task<TorrentCheckResult> CheckAsync(int id, CancellationToken cancellationToken)
    {
        TrackedTorrent torrent = await GetRequiredAsync(id, cancellationToken);
        AppSettingsSnapshot settings = _settingsProvider.Current;
        DateTime now = UtcNow();
        string previousInfoHash = torrent.InfoHashV1;

        try
        {
            // Если кэша куки нет, сеанс сам войдёт по сохранённым логину и паролю.
            string cookie = await _session.GetCookieAsync(cancellationToken);

            byte[] payload = await _rutrackerClient.DownloadTorrentAsync(
                new RutrackerDownloadRequest(torrent.TopicId, cookie, settings.RequestTimeout),
                cancellationToken);

            TorrentMetadata metadata = ParseMetadata(payload, torrent.TopicId);
            StoredTorrentFile stored = await _fileStorage.SaveAsync(
                torrent.TopicId,
                payload,
                cancellationToken);

            bool changed = !string.Equals(
                metadata.InfoHashV1,
                torrent.InfoHashV1,
                StringComparison.OrdinalIgnoreCase);

            torrent.Title = metadata.Name;
            torrent.InfoHashV1 = metadata.InfoHashV1;
            torrent.TorrentSizeBytes = metadata.SizeBytes;
            torrent.FileCount = metadata.FileCount;
            torrent.TorrentFilePath = stored.FilePath;
            torrent.TorrentFileSha256 = stored.Sha256;
            torrent.TorrentFileDownloadedAtUtc = stored.StoredAtUtc;
            torrent.LastCheckedAtUtc = now;
            torrent.LastCheckOutcome = changed ? TorrentCheckOutcome.Changed : TorrentCheckOutcome.Unchanged;
            torrent.LastError = null;
            torrent.UpdatedAtUtc = now;

            if (changed)
            {
                torrent.LastChangedAtUtc = now;

                // Старый infohash нужен, чтобы убрать из qBittorrent прежнюю раздачу.
                if (previousInfoHash.Length > 0
                    && !string.Equals(previousInfoHash, torrent.InfoHashV1, StringComparison.OrdinalIgnoreCase))
                {
                    torrent.SupersededInfoHashV1 = previousInfoHash;
                }
            }

            await _torrentRepository.UpdateAsync(torrent, cancellationToken);

            // Сбой qBittorrent не должен делать проверку ошибочной: он попадёт в ApplyError.
            if (_settingsProvider.Current.QbittorrentAutoApply)
            {
                await _updateApplier.ApplyAsync(torrent, force: false, cancellationToken);
            }

            return new TorrentCheckResult(TorrentView.FromEntity(torrent), changed, null, ShouldStopScan: false);
        }
        catch (AppException exception)
        {
            return await RecordFailureAsync(
                torrent,
                exception.Code,
                exception.Message,
                ShouldStopScanFor(exception.Code),
                now,
                cancellationToken);
        }
    }

    private async Task<TorrentCheckResult> RecordFailureAsync(
        TrackedTorrent torrent,
        string errorCode,
        string message,
        bool shouldStopScan,
        DateTime now,
        CancellationToken cancellationToken)
    {
        torrent.LastCheckedAtUtc = now;
        torrent.LastCheckOutcome = TorrentCheckOutcome.Error;
        torrent.LastError = Truncate(message);
        torrent.UpdatedAtUtc = now;

        await _torrentRepository.UpdateAsync(torrent, cancellationToken);

        return TorrentCheckResult.Failure(
            TorrentView.FromEntity(torrent),
            errorCode,
            shouldStopScan);
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

    /// <summary>403/капча/битый ответ повторятся на следующих торрентах — скан прерывается.</summary>
    private static bool ShouldStopScanFor(string errorCode) =>
        errorCode is ErrorCodes.RutrackerCookieInvalid
            or ErrorCodes.RutrackerLoginFailed
            or ErrorCodes.RutrackerLoginCaptchaRequired
            or ErrorCodes.RutrackerCredentialsUnreadable
            or ErrorCodes.RutrackerInvalidPayload
            or ErrorCodes.RutrackerChallengeRequired;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}

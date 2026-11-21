using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.TorrentClients;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Применяет обнаруженное обновление в qBittorrent: старую раздачу удаляет без удаления
/// файлов, новую добавляет в ту же папку с той же категорией и тегами, запускает пересчёт
/// хешей, чтобы уже скачанное переиспользовалось. Старая раздача ищется по infohash,
/// а если он неизвестен — по названию из info-словаря. Сбой клиента не делает проверку
/// ошибочной: он сохраняется в <see cref="TrackedTorrent.ApplyError"/> и повторяется
/// на следующем скане.
/// </summary>
public sealed class TorrentApplyService : ITorrentUpdateApplier
{
    private const int MaxErrorMessageLength = 4000;

    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly ITorrentClient _torrentClient;
    private readonly ISettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;

    public TorrentApplyService(
        ITrackedTorrentRepository torrentRepository,
        ITorrentClient torrentClient,
        ISettingsProvider settingsProvider,
        TimeProvider timeProvider)
    {
        _torrentRepository = torrentRepository;
        _torrentClient = torrentClient;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
    }

    public async Task<TorrentApplyResult> ApplyByIdAsync(int id, CancellationToken cancellationToken)
    {
        TrackedTorrent? torrent = await _torrentRepository.GetByIdAsync(id, cancellationToken);

        if (torrent is null)
        {
            throw new AppException(ErrorCodes.TorrentNotFound, $"Торрент {id} не найден.");
        }

        return await ApplyAsync(torrent, force: true, cancellationToken);
    }

    public async Task<TorrentApplyResult> ApplyAsync(
        TrackedTorrent torrent,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        AppSettingsSnapshot settings = _settingsProvider.Current;

        if (!settings.QbittorrentConfigured)
        {
            if (force)
            {
                throw new AppException(
                    ErrorCodes.QbittorrentNotConfigured,
                    "qBittorrent не настроен: укажите адрес Web UI, логин и пароль в настройках.");
            }

            return new TorrentApplyResult(
                TorrentView.FromEntity(torrent),
                Applied: false,
                ErrorCode: null,
                "qBittorrent не настроен.");
        }

        if (!force && !settings.QbittorrentAutoApply)
        {
            return new TorrentApplyResult(
                TorrentView.FromEntity(torrent),
                Applied: false,
                ErrorCode: null,
                "Авто-применение обновлений выключено.");
        }

        // Авто-режим не импортирует раздачи, которых раньше не было в клиенте:
        // он реагирует только на обновление или на незавершённую прошлую попытку.
        if (!force
            && string.IsNullOrEmpty(torrent.AppliedInfoHashV1)
            && string.IsNullOrEmpty(torrent.SupersededInfoHashV1)
            && string.IsNullOrEmpty(torrent.ApplyError))
        {
            return new TorrentApplyResult(
                TorrentView.FromEntity(torrent),
                Applied: false,
                ErrorCode: null,
                "В qBittorrent ещё нет этой раздачи: обновлять нечего.");
        }

        if (!force && IsUpToDate(torrent))
        {
            return new TorrentApplyResult(
                TorrentView.FromEntity(torrent),
                Applied: false,
                ErrorCode: null,
                "В qBittorrent уже актуальная версия.");
        }

        DateTime now = UtcNow();

        try
        {
            await ReplaceAsync(torrent, cancellationToken);

            torrent.AppliedInfoHashV1 = torrent.InfoHashV1;
            torrent.AppliedAtUtc = now;
            torrent.SupersededInfoHashV1 = null;
            torrent.ApplyError = null;
            torrent.UpdatedAtUtc = now;

            await _torrentRepository.UpdateAsync(torrent, cancellationToken);

            return new TorrentApplyResult(
                TorrentView.FromEntity(torrent),
                Applied: true,
                ErrorCode: null,
                "Обновление применено в qBittorrent.");
        }
        catch (AppException exception)
        {
            return await RecordFailureAsync(torrent, exception.Code, exception.Message, now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await RecordFailureAsync(
                torrent,
                ErrorCodes.QbittorrentUnavailable,
                exception.Message,
                now,
                cancellationToken);
        }
    }

    private static bool IsUpToDate(TrackedTorrent torrent) =>
        string.Equals(torrent.AppliedInfoHashV1, torrent.InfoHashV1, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(torrent.SupersededInfoHashV1)
        && string.IsNullOrEmpty(torrent.ApplyError);

    private async Task ReplaceAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        TorrentClientTorrent? old = await RemoveOldVersionAsync(torrent, cancellationToken);

        TorrentClientTorrent? current = await _torrentClient.FindAsync(
            torrent.InfoHashV1,
            cancellationToken);

        if (current is null)
        {
            await _torrentClient.AddTorrentAsync(
                new TorrentClientAddRequest(
                    torrent.TopicId,
                    torrent.Title,
                    torrent.InfoHashV1,
                    torrent.TorrentFilePath,
                    SavePath: old?.SavePath,
                    Paused: false,
                    Category: old?.Category,
                    Tags: old?.Tags),
                cancellationToken);

            // Пересчёт подхватывает файлы, оставшиеся от старой раздачи.
            await _torrentClient.RecheckAsync(torrent.InfoHashV1, cancellationToken);
            await _torrentClient.ResumeAsync(torrent.InfoHashV1, cancellationToken);
        }
    }

    /// <summary>
    /// Убирает из клиента прежние версии раздачи и возвращает одну из них, чтобы взять
    /// её папку, категорию и теги. Сначала поиск идёт по сохранённому infohash, затем
    /// по названию из info-словаря: infohash старой версии может быть неизвестен, если
    /// раздачу добавили в клиент вручную или рутрекер перезалил торрент под новым хешем.
    /// </summary>
    private async Task<TorrentClientTorrent?> RemoveOldVersionAsync(
        TrackedTorrent torrent,
        CancellationToken cancellationToken)
    {
        TorrentClientTorrent? old = null;
        string? superseded = torrent.SupersededInfoHashV1;

        if (!string.IsNullOrWhiteSpace(superseded)
            && !SameHash(superseded, torrent.InfoHashV1))
        {
            TorrentClientTorrent? byHash = await _torrentClient.FindAsync(
                superseded,
                cancellationToken);

            if (byHash is not null && !SameHash(byHash.InfoHash, torrent.InfoHashV1))
            {
                // Файлы остаются на диске: новая версия заберёт их себе той же папкой.
                await _torrentClient.DeleteTorrentAsync(byHash.InfoHash, cancellationToken);
                old = byHash;
            }
        }

        if (old is not null || string.IsNullOrWhiteSpace(torrent.Title))
        {
            return old;
        }

        IReadOnlyList<TorrentClientTorrent> sameName = await _torrentClient.FindByNameAsync(
            torrent.Title,
            cancellationToken);

        foreach (TorrentClientTorrent candidate in sameName)
        {
            if (string.IsNullOrEmpty(candidate.InfoHash)
                || SameHash(candidate.InfoHash, torrent.InfoHashV1))
            {
                continue;
            }

            await _torrentClient.DeleteTorrentAsync(candidate.InfoHash, cancellationToken);
            old ??= candidate;
        }

        return old;
    }

    private static bool SameHash(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private async Task<TorrentApplyResult> RecordFailureAsync(
        TrackedTorrent torrent,
        string errorCode,
        string message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        torrent.ApplyError = Truncate(message);
        torrent.UpdatedAtUtc = now;

        await _torrentRepository.UpdateAsync(torrent, cancellationToken);

        return new TorrentApplyResult(
            TorrentView.FromEntity(torrent),
            Applied: false,
            errorCode,
            message);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}

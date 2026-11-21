using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Порт «решателя» проверки Cloudflare: поднимает браузер, проходит managed challenge и
/// возвращает <c>cf_clearance</c> вместе с User-Agent. Реализация — Playwright в Infrastructure.
/// </summary>
public interface ICloudflareChallengeSolver
{
    /// <summary>
    /// Открывает защищённую страницу <c>dl.php?t={topicId}</c> в браузере и ждёт выдачи пропуска.
    /// </summary>
    /// <exception cref="Exceptions.RutrackerException">
    /// Проверку пройти не удалось (код <c>RUTRACKER_CHALLENGE_REQUIRED</c>).
    /// </exception>
    Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken);
}

namespace TorrentWatcher.Application.Rutracker;

/// <summary>
/// Пропуск Cloudflare, добытый браузером: кука <c>cf_clearance</c> и User-Agent, под которым
/// она выдана. Cloudflare привязывает пропуск к IP и UA, поэтому оба значения неразделимы.
/// </summary>
/// <param name="Cookie">Готовый фрагмент заголовка Cookie: <c>cf_clearance=…</c>.</param>
/// <param name="UserAgent">User-Agent браузера, получившего пропуск.</param>
/// <param name="ObtainedAtUtc">Когда пропуск получен.</param>
/// <param name="ExpiresAtUtc">Срок действия куки, если известен.</param>
public sealed record CloudflareClearance(
    string Cookie,
    string UserAgent,
    DateTime ObtainedAtUtc,
    DateTime? ExpiresAtUtc)
{
    /// <summary>Пропуск просрочен (с небольшим запасом, чтобы не ловить отказ на границе).</summary>
    public bool IsExpired(DateTime nowUtc) =>
        ExpiresAtUtc is { } expires && expires <= nowUtc.AddMinutes(2);
}

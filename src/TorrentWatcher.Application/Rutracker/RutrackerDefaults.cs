namespace TorrentWatcher.Application.Rutracker;

/// <summary>
/// Значения по умолчанию для обращений к рутрекеру. UA должен совпадать с тем, под которым
/// получен <c>cf_clearance</c>, иначе Cloudflare снова покажет челлендж.
/// </summary>
public static class RutrackerDefaults
{
    /// <summary>
    /// Резервный User-Agent для случая, когда clearance ещё нет: браузерный Chrome.
    /// После автоматического решения челленджа используется UA реального Chromium.
    /// </summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
}

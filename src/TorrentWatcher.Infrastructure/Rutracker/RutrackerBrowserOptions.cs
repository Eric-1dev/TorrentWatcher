namespace TorrentWatcher.Infrastructure.Rutracker;

/// <summary>
/// Настройки браузера, которым проходится проверка Cloudflare. Профиль хранится на диске:
/// так кука <c>cf_clearance</c> и остальные данные Cloudflare переживают перезапуск.
/// </summary>
public sealed class RutrackerBrowserOptions
{
    public const string SectionName = "Rutracker:Browser";

    /// <summary>Каталог persistent-профиля Chromium.</summary>
    public string ProfileDirectory { get; set; } = Path.Combine("data", "browser");

    /// <summary>
    /// Запускать браузер без окна. Cloudflare уверенно детектит headless, поэтому
    /// по умолчанию <c>false</c>; в контейнере браузер работает под Xvfb.
    /// </summary>
    public bool Headless { get; set; }

    /// <summary>Сколько ждать прохождения челленджа.</summary>
    public int SolveTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Отключить sandbox Chromium. Нужно в контейнерах, где user namespaces запрещены;
    /// локально можно оставить включённым.
    /// </summary>
    public bool DisableSandbox { get; set; } = true;

    /// <summary>Явный путь к Chromium, если нужен установленный в системе браузер.</summary>
    public string? ExecutablePath { get; set; }

    public TimeSpan SolveTimeout => TimeSpan.FromSeconds(Math.Max(10, SolveTimeoutSeconds));
}

using Microsoft.AspNetCore.Mvc;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Web.Contracts;

namespace TorrentWatcher.Web.Controllers;

/// <summary>Глобальные настройки. PUT применяется сразу, без перезапуска.</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly CloudflareClearanceService _cloudflareService;
    private readonly RutrackerSessionService _sessionService;

    public SettingsController(
        SettingsService settingsService,
        CloudflareClearanceService cloudflareService,
        RutrackerSessionService sessionService)
    {
        _settingsService = settingsService;
        _cloudflareService = cloudflareService;
        _sessionService = sessionService;
    }

    [HttpGet]
    public ActionResult<SettingsView> Get() => Ok(_settingsService.Get());

    [HttpPut]
    public async Task<ActionResult<SettingsView>> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var update = new SettingsUpdate(
            request.RutrackerCookie,
            request.ScanIntervalMinutes,
            request.AutoScanEnabled,
            request.RequestDelayMs,
            request.RequestTimeoutSeconds,
            request.CloudflareClearanceCookie,
            request.CloudflareUserAgent,
            request.RutrackerLogin,
            request.RutrackerPassword,
            request.QbittorrentUrl,
            request.QbittorrentUsername,
            request.QbittorrentPassword,
            request.QbittorrentAutoApply);

        return Ok(await _settingsService.UpdateAsync(update, cancellationToken));
    }

    /// <summary>Проверка соединения с qBittorrent по сохранённым настройкам.</summary>
    [HttpPost("qbittorrent/test")]
    public async Task<ActionResult<TorrentClientTestResult>> TestQbittorrent(
        CancellationToken cancellationToken) =>
        Ok(await _settingsService.TestQbittorrentAsync(cancellationToken));

    /// <summary>
    /// Вход в рутрекер сохранёнными логином и паролем. Браузер может быть занят
    /// скачиванием, поэтому запрос ждёт очередь и проходит Cloudflare.
    /// </summary>
    [HttpPost("rutracker/login")]
    public async Task<ActionResult<RutrackerLoginResult>> Login(CancellationToken cancellationToken) =>
        Ok(await _sessionService.LoginForUiAsync(cancellationToken));

    /// <summary>Пробное скачивание по первому отслеживаемому топику (проверка сеанса).</summary>
    [HttpPost("test-cookie")]
    public async Task<ActionResult<CookieTestResult>> TestCookie(CancellationToken cancellationToken) =>
        Ok(await _settingsService.TestCookieAsync(cancellationToken));

    /// <summary>
    /// Пройти проверку Cloudflare браузером и сохранить пропуск. Может занять до минуты:
    /// пока идёт челлендж, запрос висит.
    /// </summary>
    [HttpPost("cloudflare/solve")]
    public async Task<ActionResult<CloudflareSolveResult>> SolveCloudflare(
        CancellationToken cancellationToken) =>
        Ok(await _cloudflareService.SolveForUiAsync(cancellationToken));
}

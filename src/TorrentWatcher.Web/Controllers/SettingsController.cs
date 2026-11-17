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

    public SettingsController(SettingsService settingsService)
    {
        _settingsService = settingsService;
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
            request.RequestTimeoutSeconds);

        return Ok(await _settingsService.UpdateAsync(update, cancellationToken));
    }

    /// <summary>Пробное скачивание по первому отслеживаемому топику.</summary>
    [HttpPost("test-cookie")]
    public async Task<ActionResult<CookieTestResult>> TestCookie(CancellationToken cancellationToken) =>
        Ok(await _settingsService.TestCookieAsync(cancellationToken));
}

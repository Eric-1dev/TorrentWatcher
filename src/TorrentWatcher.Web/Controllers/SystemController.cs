using Microsoft.AspNetCore.Mvc;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Services;

namespace TorrentWatcher.Web.Controllers;

/// <summary>Состояние приложения для шапки UI и healthcheck контейнера.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly SystemStatusService _statusService;

    public SystemController(SystemStatusService statusService)
    {
        _statusService = statusService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusView>> Status(CancellationToken cancellationToken) =>
        Ok(await _statusService.GetAsync(cancellationToken));
}

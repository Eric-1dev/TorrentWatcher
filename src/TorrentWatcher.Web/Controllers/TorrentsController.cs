using Microsoft.AspNetCore.Mvc;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.State;
using TorrentWatcher.Web.Contracts;

namespace TorrentWatcher.Web.Controllers;

/// <summary>Список отслеживаемых торрентов и операции над ними. Бизнес-логика — в сервисах.</summary>
[ApiController]
[Route("api/torrents")]
public sealed class TorrentsController : ControllerBase
{
    private readonly TorrentService _torrentService;
    private readonly TorrentScanService _scanService;
    private readonly ScanCoordinator _scanCoordinator;
    private readonly ITorrentUpdateApplier _updateApplier;

    public TorrentsController(
        TorrentService torrentService,
        TorrentScanService scanService,
        ScanCoordinator scanCoordinator,
        ITorrentUpdateApplier updateApplier)
    {
        _torrentService = torrentService;
        _scanService = scanService;
        _scanCoordinator = scanCoordinator;
        _updateApplier = updateApplier;
    }

    [HttpGet]
    public async Task<ActionResult<TorrentListResult>> GetAll(CancellationToken cancellationToken) =>
        Ok(await _torrentService.GetAllAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<TorrentView>> Add(
        [FromBody] AddTorrentRequest request,
        CancellationToken cancellationToken)
    {
        TorrentView torrent = await _torrentService.AddAsync(request.Url, cancellationToken);

        return Created($"/api/torrents/{torrent.Id}", torrent);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _torrentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:int}/enabled")]
    public async Task<ActionResult<TorrentView>> SetEnabled(
        int id,
        [FromBody] SetEnabledRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _torrentService.SetEnabledAsync(id, request.IsEnabled, cancellationToken));

    /// <summary>Синхронная форс-проверка одного торрента; результат — сразу в ответе.</summary>
    [HttpPost("{id:int}/check")]
    public async Task<ActionResult<TorrentCheckResult>> Check(int id, CancellationToken cancellationToken) =>
        Ok(await _scanService.CheckAsync(id, cancellationToken));

    /// <summary>
    /// Ручное применение обновления в qBittorrent: старая раздача удаляется без файлов,
    /// новая добавляется в ту же папку. Работает и при выключенном авто-применении.
    /// </summary>
    [HttpPost("{id:int}/apply")]
    public async Task<ActionResult<TorrentApplyResult>> Apply(int id, CancellationToken cancellationToken) =>
        Ok(await _updateApplier.ApplyByIdAsync(id, cancellationToken));

    /// <summary>Запускает проверку всех включённых торрентов в фоне. 202 + runId.</summary>
    [HttpPost("check-all")]
    public ActionResult<ScanStartResult> CheckAll()
    {
        ScanStartResult result = _scanCoordinator.StartBackground(ScanTrigger.Manual);
        return Accepted(result);
    }
}

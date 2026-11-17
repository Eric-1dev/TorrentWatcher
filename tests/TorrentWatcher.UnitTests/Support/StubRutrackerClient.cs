using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Клиент рутрекера с подменяемым обработчиком и записью входящих запросов.</summary>
public sealed class StubRutrackerClient(Func<RutrackerDownloadRequest, Task<byte[]>> handler) : IRutrackerClient
{
    public List<RutrackerDownloadRequest> Requests { get; } = [];

    public Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return handler(request);
    }
}

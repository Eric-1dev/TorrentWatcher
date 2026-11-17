using System.Text;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.IntegrationTests.Support;

/// <summary>Минимальные валидные .torrent для API-тестов; сеть не используется.</summary>
public static class TorrentPayloads
{
    public static byte[] SingleFile(string name)
    {
        int nameBytes = Encoding.UTF8.GetByteCount(name);

        return Encoding.UTF8.GetBytes(
            $"d4:infod6:lengthi1024e4:name{nameBytes}:{name}ee");
    }
}

/// <summary>Клиент рутрекера, отдающий заранее заданные байты без HTTP.</summary>
public sealed class FakeRutrackerClient : IRutrackerClient
{
    public byte[] Payload { get; set; } = TorrentPayloads.SingleFile("test");

    public List<RutrackerDownloadRequest> Requests { get; } = [];

    public Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Payload);
    }
}

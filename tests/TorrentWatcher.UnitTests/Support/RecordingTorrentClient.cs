using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.TorrentClients;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>
/// Фейковый торрент-клиент: записывает вызовы и позволяет задать, какие раздачи «есть»
/// в клиенте. По умолчанию клиент пуст, версия — тестовая.
/// </summary>
public sealed class RecordingTorrentClient : ITorrentClient
{
    public string Version { get; set; } = "v5.0.0";

    public Exception? VersionException { get; set; }

    public Func<string, TorrentClientTorrent?>? FindHandler { get; set; }

    public bool FailOnAdd { get; set; }

    public List<string> Calls { get; } = [];

    public List<string> Deleted { get; } = [];

    public List<string> Rechecked { get; } = [];

    public List<string> Resumed { get; } = [];

    public TorrentClientAddRequest? LastAdd { get; private set; }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        Calls.Add("version");

        if (VersionException is not null)
        {
            throw VersionException;
        }

        return Task.FromResult(Version);
    }

    public Task<TorrentClientTorrent?> FindAsync(string infoHash, CancellationToken cancellationToken)
    {
        Calls.Add($"find:{infoHash}");
        return Task.FromResult(FindHandler?.Invoke(infoHash));
    }

    public Task AddTorrentAsync(TorrentClientAddRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"add:{request.InfoHashV1}");
        LastAdd = request;

        if (FailOnAdd)
        {
            throw new AppException(
                ErrorCodes.QbittorrentUnavailable,
                "qBittorrent не принял торрент.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteTorrentAsync(string infoHash, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{infoHash}");
        Deleted.Add(infoHash);
        return Task.CompletedTask;
    }

    public Task RecheckAsync(string infoHash, CancellationToken cancellationToken)
    {
        Calls.Add($"recheck:{infoHash}");
        Rechecked.Add(infoHash);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string infoHash, CancellationToken cancellationToken)
    {
        Calls.Add($"resume:{infoHash}");
        Resumed.Add(infoHash);
        return Task.CompletedTask;
    }
}

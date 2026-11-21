using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Доступ к действующему пропуску Cloudflare: чтение из снапшота, решение челленджа,
/// фиксация отказа. Позволяет обёртке клиента не знать про БД и браузер.
/// </summary>
public interface ICloudflareClearanceProvider
{
    /// <summary>Действующий пропуск или <c>null</c>, если его нет либо он просрочен.</summary>
    CloudflareClearance? Current { get; }

    /// <summary>Решает челлендж браузером, сохраняет пропуск и обновляет снапшот настроек.</summary>
    Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken);

    /// <summary>Сохраняет пропуск, полученный браузером при скачивании, без повторного челленджа.</summary>
    Task SaveAsync(CloudflareClearance clearance, CancellationToken cancellationToken);

    /// <summary>Сбрасывает пропуск после повторного отказа и сохраняет причину для UI.</summary>
    Task ReportRejectedAsync(string message, DateTime failedAtUtc, CancellationToken cancellationToken);
}

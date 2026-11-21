namespace TorrentWatcher.Application.Dtos;

/// <summary>Итог ручного запуска решения проверки Cloudflare из UI.</summary>
public sealed record CloudflareSolveResult(
    bool Success,
    string Message,
    DateTime? ObtainedAtUtc,
    DateTime? ExpiresAtUtc);

namespace TorrentWatcher.Application.Rutracker;

/// <summary>Сеанс рутрекера, полученный браузером после входа.</summary>
/// <param name="Cookie">Готовый фрагмент заголовка Cookie: <c>bb_session=…; bb_data=…</c>.</param>
public sealed record RutrackerBrowserSession(string Cookie);

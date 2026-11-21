namespace TorrentWatcher.Application.Settings;

/// <summary>
/// Сериализует изменение единственной строки настроек. Писателей несколько (экран настроек,
/// планировщик, сеанс рутрекера, пропуск Cloudflare), и каждый делает read-modify-write.
/// Без общей блокировки один писатель сохраняет устаревший снимок и затирает чужие поля —
/// например, скан, завершившийся во время входа, стирал только что сохранённую куку.
/// </summary>
public sealed class SettingsWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// Занимает блокировку до освобождения возвращённого объекта. Держите её только вокруг
    /// чтения и записи настроек, без сетевых вызовов: иначе ожидание входа заблокирует скан.
    /// </summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            semaphore.Release();
        }
    }
}

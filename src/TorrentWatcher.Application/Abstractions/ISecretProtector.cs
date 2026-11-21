namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Симметричное шифрование секретов настроек. Пароль рутрекера хранится в БД только
/// в зашифрованном виде; ключи живут вне БД (каталог на диске).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Шифрует значение. Каждый вызов даёт новую строку даже для того же пароля.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Расшифровывает значение. <c>null</c> — значение повреждено или ключ шифрования
    /// недоступен (например, потерян каталог ключей).
    /// </summary>
    string? Unprotect(string protectedValue);
}

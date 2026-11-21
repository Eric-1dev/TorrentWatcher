using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using TorrentWatcher.Application.Abstractions;

namespace TorrentWatcher.Infrastructure.Security;

/// <summary>
/// Шифрование секретов через ASP.NET Core Data Protection. Ключи лежат в отдельном
/// каталоге на диске (по умолчанию рядом с БД, в контейнере — <c>/data/keys</c>),
/// поэтому копия одной только БД не раскрывает пароль рутрекера.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Purpose = "TorrentWatcher.RutrackerPassword.v1";

    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            // Ключ потерян или строка повреждена: пароль нужно ввести заново.
            return null;
        }
    }
}

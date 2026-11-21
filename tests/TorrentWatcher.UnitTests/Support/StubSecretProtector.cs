using TorrentWatcher.Application.Abstractions;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Обратимое «шифрование» для тестов: помечает значение префиксом.</summary>
public sealed class StubSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:";

    public string Protect(string plaintext) => Prefix + plaintext;

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return string.Empty;
        }

        return protectedValue.StartsWith(Prefix, StringComparison.Ordinal)
            ? protectedValue[Prefix.Length..]
            : null;
    }
}

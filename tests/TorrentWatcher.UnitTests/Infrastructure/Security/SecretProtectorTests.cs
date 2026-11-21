using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Infrastructure.Security;

namespace TorrentWatcher.UnitTests.Infrastructure.Security;

public sealed class SecretProtectorTests
{
    [Fact]
    public void Protect_ProducesDifferentCiphertextsAndRoundTrips()
    {
        string keysDirectory = Path.Combine(
            Path.GetTempPath(),
            "torrentwatcher-keys-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var services = new ServiceCollection();
            services
                .AddDataProtection()
                .SetApplicationName("TorrentWatcher.Tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
            services.AddSingleton<ISecretProtector, SecretProtector>();

            using ServiceProvider provider = services.BuildServiceProvider();
            var protector = provider.GetRequiredService<ISecretProtector>();

            string first = protector.Protect("secret-password");
            string second = protector.Protect("secret-password");

            // Случайный nonce: одинаковый пароль даёт разные шифротексты.
            Assert.NotEqual(first, second);
            Assert.DoesNotContain("secret-password", first, StringComparison.Ordinal);
            Assert.Equal("secret-password", protector.Unprotect(first));
            Assert.Equal("secret-password", protector.Unprotect(second));
        }
        finally
        {
            if (Directory.Exists(keysDirectory))
            {
                Directory.Delete(keysDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Unprotect_GarbageOrEmpty_ReturnsNullOrEmpty()
    {
        string keysDirectory = Path.Combine(
            Path.GetTempPath(),
            "torrentwatcher-keys-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var services = new ServiceCollection();
            services
                .AddDataProtection()
                .SetApplicationName("TorrentWatcher.Tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
            services.AddSingleton<ISecretProtector, SecretProtector>();

            using ServiceProvider provider = services.BuildServiceProvider();
            var protector = provider.GetRequiredService<ISecretProtector>();

            Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
            Assert.Null(protector.Unprotect("not-a-protected-value"));
        }
        finally
        {
            if (Directory.Exists(keysDirectory))
            {
                Directory.Delete(keysDirectory, recursive: true);
            }
        }
    }
}

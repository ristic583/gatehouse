using Gatehouse.Domain.Credentials;
using Gatehouse.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.UnitTests.Credentials;

public sealed class LocalPinVaultTests
{
    private static readonly LocalPinVault Vault = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmSecretProtector.MasterKeySetting] = "unit-test-master-key-0123456789abcdef" })
        .Build());

    [Fact]
    public async Task Vault_round_trips_pin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (ciphertext, keyId) = await Vault.EncryptAsync(Pin.Create("4711").Value, ct);

        var reference = Vault.ProtectForTransport(ciphertext, keyId);
        var unwrapped = Vault.UnprotectTransport(reference);
        var pin = await Vault.DecryptAsync(unwrapped!.Value.Ciphertext, unwrapped.Value.KeyId, ct);

        Assert.Equal(LocalPinVault.LocalKeyId, keyId);
        Assert.StartsWith("vault:", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("4711", reference, StringComparison.Ordinal);
        Assert.Equal("4711", pin.Reveal());
    }

    [Fact]
    public async Task Vault_ciphertext_differs_per_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var pin = Pin.Create("123456").Value;

        var first = await Vault.EncryptAsync(pin, ct);
        var second = await Vault.EncryptAsync(pin, ct);

        Assert.NotEqual(Convert.ToBase64String(first.Ciphertext), Convert.ToBase64String(second.Ciphertext));
        Assert.Equal("123456", (await Vault.DecryptAsync(second.Ciphertext, second.KeyId, ct)).Reveal());
        var tampered = (byte[])first.Ciphertext.Clone();
        tampered[^1] ^= 0xFF;
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => Vault.DecryptAsync(tampered, first.KeyId, ct));
    }
}

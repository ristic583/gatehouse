using System.Security.Cryptography;
using Gatehouse.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.UnitTests.Security;

public sealed class AesGcmSecretProtectorTests
{
    private const string Plaintext = "Host=db;Database=gh_tenant_nordlys;Username=u;Password=p";

    [Fact]
    public void Protect_then_unprotect_round_trips()
    {
        var protector = Create("a-master-key-that-is-long-enough-for-tests-0001");

        var protectedValue = protector.Protect(Plaintext);

        Assert.StartsWith("v1:", protectedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("gh_tenant_nordlys", protectedValue, StringComparison.Ordinal);
        Assert.Equal(Plaintext, protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Tampered_value_throws()
    {
        var protector = Create("a-master-key-that-is-long-enough-for-tests-0001");
        var bytes = Convert.FromBase64String(protector.Protect(Plaintext)["v1:".Length..]);
        bytes[^1] ^= 0x01;
        var tampered = "v1:" + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("v2:" + tampered[3..]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public void Missing_master_key_throws(string? masterKey)
    {
        Assert.Throws<InvalidOperationException>(() => Create(masterKey));
    }

    [Fact]
    public void Same_plaintext_produces_different_ciphertexts()
    {
        var protector = Create("a-master-key-that-is-long-enough-for-tests-0001");

        Assert.NotEqual(protector.Protect(Plaintext), protector.Protect(Plaintext));
    }

    private static AesGcmSecretProtector Create(string? masterKey) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmSecretProtector.MasterKeySetting] = masterKey })
            .Build());
}

using System.Security.Cryptography;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;
using Gatehouse.Infrastructure.Identity;
using Gatehouse.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.UnitTests.Identity;

public sealed class MagicLinkTokenCodecTests
{
    private static MagicLinkTokenCodec Codec(string key = "unit-test-master-key-0123456789abcdef") => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmSecretProtector.MasterKeySetting] = key })
        .Build());

    [Fact]
    public void Round_trip_keeps_tenant_and_link()
    {
        var codec = Codec();
        var linkId = MagicLinkId.New();
        var secret = RandomNumberGenerator.GetBytes(32);

        var decoded = codec.TryDecode(codec.Encode(new TenantId("nordlys"), linkId, secret));

        Assert.NotNull(decoded);
        Assert.Equal("nordlys", decoded.TenantId.Value);
        Assert.Equal(linkId, decoded.LinkId);
        Assert.Equal(secret, decoded.Secret);
    }

    [Fact]
    public void Tampered_token_fails_decode()
    {
        var codec = Codec();
        var token = codec.Encode(new TenantId("nordlys"), MagicLinkId.New(), RandomNumberGenerator.GetBytes(32));
        var forged = Codec().Encode(new TenantId("bricks"), MagicLinkId.New(), RandomNumberGenerator.GetBytes(32));
        var otherKey = Codec("another-master-key-0123456789abcdefgh").Encode(new TenantId("nordlys"), MagicLinkId.New(), RandomNumberGenerator.GetBytes(32));
        var dot = token.IndexOf('.', StringComparison.Ordinal);
        var swappedPayload = forged[..forged.IndexOf('.', StringComparison.Ordinal)] + token[dot..];
        var flipped = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        Assert.NotNull(codec.TryDecode(forged));
        Assert.Null(codec.TryDecode(swappedPayload));
        Assert.Null(codec.TryDecode(flipped));
        Assert.Null(codec.TryDecode(otherKey));
        Assert.Null(codec.TryDecode(token.Replace(".", string.Empty, StringComparison.Ordinal)));
        Assert.Null(codec.TryDecode("not-a-token"));
        Assert.Null(codec.TryDecode(null));
    }
}

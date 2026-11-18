using System.Net;
using System.Net.Sockets;
using Gatehouse.Application.Identity.IdentityProviders;

namespace Gatehouse.Infrastructure.Identity;

/// <summary>
/// Destination rules for identity provider URLs (ADR-009 §4): public addresses only, unless the host is listed in
/// <c>Identity:AllowedInsecureHosts</c> (Development stubs).
/// </summary>
public static class IdpAddressRules
{
    /// <summary>Production primary handler: no redirects, and every connection re-validates the resolved address (DNS rebinding).</summary>
    public static SocketsHttpHandler CreateHandler(IdentityProviderOptions options) => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = IdpMetadataFetcher.Timeout,
        UseCookies = false,
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var allowed = IsAllowedInsecure(options, host, context.DnsEndPoint.Port);
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
            var target = addresses.FirstOrDefault(a => allowed || IsPublic(a))
                ?? throw new HttpRequestException("idp.url_not_allowed");
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = address.GetAddressBytes();
            if (EmbeddedIpv4(b6) is { } embedded)
            {
                // SEC-07: NAT64, 6to4, IPv4-compatible and IPv4-translated forms reach the embedded IPv4 address where routed.
                return IsPublic(embedded);
            }

            // Local-use NAT64 (64:ff9b:1::/48, RFC 8215) only exists inside an operator's network.
            if (b6[0] == 0x00 && b6[1] == 0x64 && b6[2] == 0xFF && b6[3] == 0x9B && b6[4] == 0x00 && b6[5] == 0x01)
            {
                return false;
            }

            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b6[0] & 0xFE) == 0xFC);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 10
            || b[0] == 0
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
            || b[0] >= 224);
    }

    private static IPAddress? EmbeddedIpv4(byte[] b)
    {
        var zero64 = b.AsSpan(0, 8).IndexOfAnyExcept((byte)0) < 0;
        var isNat64 = b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;
        var isCompatible = zero64 && b.AsSpan(8, 4).IndexOfAnyExcept((byte)0) < 0;
        var isTranslated = zero64 && b[8] == 0xFF && b[9] == 0xFF && b[10] == 0 && b[11] == 0;
        if (isNat64 || isCompatible || isTranslated)
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        return b[0] == 0x20 && b[1] == 0x02 ? new IPAddress(b.AsSpan(2, 4)) : null;
    }

    internal static bool IsAllowedInsecure(IdentityProviderOptions options, string host, int port) =>
        options.AllowedInsecureHosts.Any(h =>
            string.Equals(h, $"{host}:{port}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
}

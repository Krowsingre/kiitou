using System.Net;
using System.Net.Sockets;

namespace Kiitou.Verification;

/// <summary>Decides whether an address is publicly routable on the internet.</summary>
public static class IpClassifier
{
    private static readonly (string Network, int PrefixLength, string Description)[] NonPublicIpv4 =
    [
        ("0.0.0.0", 8, "an unspecified \"this network\" address"),
        ("10.0.0.0", 8, "a private network address"),
        ("100.64.0.0", 10, "a carrier-grade NAT address"),
        ("127.0.0.0", 8, "a loopback address"),
        ("169.254.0.0", 16, "a link-local address"),
        ("172.16.0.0", 12, "a private network address"),
        ("192.0.0.0", 24, "reserved for protocol assignments"),
        ("192.0.2.0", 24, "reserved for documentation"),
        ("192.88.99.0", 24, "a deprecated relay address"),
        ("192.168.0.0", 16, "a private network address"),
        ("198.18.0.0", 15, "reserved for benchmarking"),
        ("198.51.100.0", 24, "reserved for documentation"),
        ("203.0.113.0", 24, "reserved for documentation"),
        ("224.0.0.0", 4, "a multicast address"),
        ("240.0.0.0", 4, "a reserved address")
    ];

    private static readonly (string Network, int PrefixLength, string Description)[] NonPublicIpv6 =
    [
        ("::", 128, "the unspecified address"),
        ("::1", 128, "the loopback address"),
        ("100::", 64, "a discard-only address"),
        ("2001:db8::", 32, "reserved for documentation"),
        ("fc00::", 7, "a private (unique local) address"),
        ("fe80::", 10, "a link-local address"),
        ("fec0::", 10, "a deprecated site-local address"),
        ("ff00::", 8, "a multicast address")
    ];

    /// <param name="reason">When not public, a phrase that completes "It is ...".</param>
    public static bool IsPublic(string? ipAddress, out string reason)
    {
        reason = "";
        if (!IPAddress.TryParse(ipAddress, out IPAddress? address))
        {
            reason = "not a valid IP address";
            return false;
        }
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        var table = address.AddressFamily == AddressFamily.InterNetwork ? NonPublicIpv4 : NonPublicIpv6;
        byte[] bytes = address.GetAddressBytes();
        foreach ((string network, int prefixLength, string description) in table)
        {
            if (HasPrefix(bytes, IPAddress.Parse(network).GetAddressBytes(), prefixLength))
            {
                reason = description;
                return false;
            }
        }

        // Every public IPv6 address is currently allocated from 2000::/3.
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xE0) != 0x20)
        {
            reason = "outside the range used for public IPv6 addresses";
            return false;
        }
        return true;
    }

    private static bool HasPrefix(byte[] address, byte[] network, int prefixLength)
    {
        int fullBytes = prefixLength / 8;
        for (int i = 0; i < fullBytes; i++)
            if (address[i] != network[i]) return false;
        int remainingBits = prefixLength % 8;
        if (remainingBits == 0) return true;
        int mask = 0xFF << (8 - remainingBits) & 0xFF;
        return (address[fullBytes] & mask) == (network[fullBytes] & mask);
    }
}

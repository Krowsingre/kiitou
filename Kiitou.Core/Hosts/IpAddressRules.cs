using System.Net;
using System.Net.Sockets;

namespace Kiitou.Hosts;

public enum IpFamily
{
    IPv4,
    IPv6
}

/// <summary>Strict validation and normalization of candidate IP addresses.</summary>
public static class IpAddressRules
{
    public static bool TryNormalize(string? input, out string normalized, out IpFamily family, out string error)
    {
        normalized = "";
        family = IpFamily.IPv4;
        error = "";
        string value = (input ?? "").Trim();

        if (value.Length == 0)
        {
            error = "Enter an IP address.";
            return false;
        }

        if (value.Contains(':'))
        {
            if (value.IndexOfAny(['%', '[', ']', '/']) >= 0 ||
                !IPAddress.TryParse(value, out IPAddress? v6) ||
                v6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                error = "This is not a valid IPv6 address.";
                return false;
            }
            normalized = v6.ToString();
            family = IpFamily.IPv6;
            return true;
        }

        string[] parts = value.Split('.');
        if (parts.Length != 4 || parts.Any(p =>
                p.Length is 0 or > 3 || !p.All(char.IsAsciiDigit) ||
                (p.Length > 1 && p[0] == '0') || int.Parse(p) > 255))
        {
            error = "This is not a valid IPv4 address. Use four numbers from 0 to 255, such as 93.184.216.34.";
            return false;
        }
        normalized = string.Join('.', parts);
        family = IpFamily.IPv4;
        return true;
    }

    public static bool IsNormalized(string? value) =>
        TryNormalize(value, out string normalized, out _, out _) && normalized == value;

    public static IpFamily? FamilyOf(string? value) =>
        TryNormalize(value, out _, out IpFamily family, out _) ? family : null;
}

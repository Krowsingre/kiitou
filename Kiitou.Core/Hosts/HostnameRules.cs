using System.Globalization;
using System.Net;

namespace Kiitou.Hosts;

/// <summary>Validation and normalization of website hostnames.</summary>
public static class HostnameRules
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Produces the canonical lower-case ASCII form of a hostname, or explains why it is not acceptable.
    /// </summary>
    public static bool TryNormalize(string? input, out string hostname, out string error)
    {
        hostname = "";
        error = "";
        string value = (input ?? "").Trim();
        if (value.EndsWith('.')) value = value[..^1];

        if (value.Length == 0)
        {
            error = "Enter a hostname.";
            return false;
        }
        if (value.Any(char.IsWhiteSpace) || value.IndexOfAny(['/', '\\', ':', '@', '?', '#', '*']) >= 0)
        {
            error = "Enter only the hostname, such as example.com, without a scheme, port or path.";
            return false;
        }
        if (IPAddress.TryParse(value, out _))
        {
            error = "A hostname is required here, not an IP address.";
            return false;
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            error = "The hostname contains characters that cannot be used in a domain name.";
            return false;
        }

        if (ascii.Length > 253)
        {
            error = "The hostname is longer than 253 characters.";
            return false;
        }

        string[] labels = ascii.Split('.');
        foreach (string label in labels)
        {
            if (label.Length is 0 or > 63)
            {
                error = "Each part of the hostname must be between 1 and 63 characters.";
                return false;
            }
            if (label.StartsWith('-') || label.EndsWith('-') ||
                !label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                error = "Hostnames may contain only letters, digits and hyphens, and parts may not start or end with a hyphen.";
                return false;
            }
        }
        if (labels[^1].All(char.IsAsciiDigit))
        {
            error = "The last part of a hostname cannot be entirely numeric.";
            return false;
        }
        if (ascii == "localhost")
        {
            error = "localhost is reserved and cannot be remapped.";
            return false;
        }

        hostname = ascii;
        return true;
    }

    public static bool IsNormalized(string? value) =>
        TryNormalize(value, out string normalized, out _) && normalized == value;
}

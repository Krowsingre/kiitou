using Kiitou.Hosts;

namespace Kiitou.Tests;

public class AddressAndHostnameRulesTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("  WWW.Example.COM. ", "www.example.com")]
    [InlineData("a-b.example.co.uk", "a-b.example.co.uk")]
    [InlineData("intranet", "intranet")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void ValidHostnamesAreNormalized(string input, string expected)
    {
        Assert.True(HostnameRules.TryNormalize(input, out string hostname, out string error), error);
        Assert.Equal(expected, hostname);
        Assert.True(HostnameRules.IsNormalized(hostname));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:443")]
    [InlineData("exa mple.com")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("under_score.example.com")]
    [InlineData("double..dot.com")]
    [InlineData("93.184.216.34")]
    [InlineData("example.123")]
    [InlineData("localhost")]
    [InlineData("*.example.com")]
    public void InvalidHostnamesAreRejected(string input)
    {
        Assert.False(HostnameRules.TryNormalize(input, out _, out string error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void OverlongHostnamesAreRejected()
    {
        string label = new('a', 63);
        Assert.False(HostnameRules.TryNormalize(new string('a', 64) + ".com", out _, out _));
        Assert.False(HostnameRules.TryNormalize(string.Join('.', label, label, label, label, "com"), out _, out _));
    }

    [Theory]
    [InlineData("93.184.216.34", "93.184.216.34", IpFamily.IPv4)]
    [InlineData(" 10.0.0.1 ", "10.0.0.1", IpFamily.IPv4)]
    [InlineData("0.0.0.0", "0.0.0.0", IpFamily.IPv4)]
    [InlineData("2606:2800:0220:0001:0248:1893:25C8:1946", "2606:2800:220:1:248:1893:25c8:1946", IpFamily.IPv6)]
    [InlineData("::1", "::1", IpFamily.IPv6)]
    [InlineData("2001:db8::", "2001:db8::", IpFamily.IPv6)]
    public void ValidAddressesAreNormalized(string input, string expected, IpFamily expectedFamily)
    {
        Assert.True(IpAddressRules.TryNormalize(input, out string normalized, out IpFamily family, out string error), error);
        Assert.Equal(expected, normalized);
        Assert.Equal(expectedFamily, family);
        Assert.True(IpAddressRules.IsNormalized(normalized));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.1.1.1")]
    [InlineData("01.2.3.4")]
    [InlineData("1.2.3.-4")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("example.com")]
    [InlineData("93.184.216.34:443")]
    [InlineData("[::1]")]
    [InlineData("fe80::1%eth0")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8:::1")]
    [InlineData("12345::1")]
    public void InvalidAddressesAreRejected(string input)
    {
        Assert.False(IpAddressRules.TryNormalize(input, out _, out _, out string error));
        Assert.NotEqual("", error);
        Assert.Null(IpAddressRules.FamilyOf(input));
    }
}

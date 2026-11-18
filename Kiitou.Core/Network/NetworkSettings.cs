using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Verification;

namespace Kiitou.Network;

/// <summary>
/// Reads and changes the test proxy. Like a reputation row, the proxy is locked while it is
/// switched on; it has to be switched off to be changed.
/// </summary>
public sealed class NetworkSettings
{
    private readonly AppConfiguration _config;
    private readonly ISecretProtector _secrets;
    private readonly Action _save;

    public NetworkSettings(AppConfiguration config, ISecretProtector secrets, Action save)
    {
        _config = config;
        _secrets = secrets;
        _save = save;
    }

    public ProxySettings Proxy => _config.Settings.Proxy;

    /// <summary>The proxy tests should use right now, or null for a direct connection.</summary>
    public Socks5Proxy? ActiveProxy => Proxy.Enabled ? Build(Proxy.Host, Proxy.Port, Proxy.Username, Password) : null;

    public string Password => Proxy.ProtectedPassword.Length == 0 ? "" : _secrets.Unprotect(Proxy.ProtectedPassword) ?? "";

    /// <summary>Checks and stores the proxy details. An empty password leaves no password stored.</summary>
    public void SetProxy(string host, string port, string username, string password)
    {
        if (Proxy.Enabled) throw new CatalogException("Switch the proxy off before changing it.");
        (string cleanHost, int cleanPort) = Validate(host, port);
        string user = (username ?? "").Trim();
        if (user.Length > 255 || user.Any(char.IsControl)) throw new CatalogException("The proxy username is not valid.");
        if ((password ?? "").Length > 255) throw new CatalogException("The proxy password is too long.");

        Proxy.Host = cleanHost;
        Proxy.Port = cleanPort;
        Proxy.Username = user;
        Proxy.ProtectedPassword = string.IsNullOrEmpty(password) ? "" : _secrets.Protect(password);
        _save();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && (Proxy.Host.Length == 0 || Proxy.Port == 0))
            throw new CatalogException("Enter the proxy's server and port first.");
        Proxy.Enabled = enabled;
        _save();
    }

    /// <summary>A proxy built from what is typed, for the Test button, without storing it.</summary>
    public static Socks5Proxy BuildFromInput(string host, string port, string username, string password)
    {
        (string cleanHost, int cleanPort) = Validate(host, port);
        return Build(cleanHost, cleanPort, (username ?? "").Trim(), password ?? "")!;
    }

    private static Socks5Proxy? Build(string host, int port, string username, string password) =>
        host.Length == 0 || port == 0
            ? null
            : new Socks5Proxy(host, port, username.Length == 0 ? null : username, username.Length == 0 ? null : password);

    private static (string Host, int Port) Validate(string host, string port)
    {
        string cleanHost = (host ?? "").Trim();
        if (cleanHost.Length == 0) return ("", ParsePort(port, allowEmpty: true));
        bool isAddress = IpAddressRules.TryNormalize(cleanHost, out string normalized, out _, out _);
        if (isAddress) cleanHost = normalized;
        else if (cleanHost != "localhost" && !HostnameRules.TryNormalize(cleanHost, out cleanHost, out _))
            throw new CatalogException("Enter the proxy's server as an IP address or hostname, such as 127.0.0.1.");
        return (cleanHost, ParsePort(port, allowEmpty: false));
    }

    private static int ParsePort(string port, bool allowEmpty)
    {
        string text = (port ?? "").Trim();
        if (text.Length == 0 && allowEmpty) return 0;
        if (!int.TryParse(text, out int value) || value is < 1 or > 65535)
            throw new CatalogException("Enter a port from 1 to 65535, such as 9150.");
        return value;
    }
}

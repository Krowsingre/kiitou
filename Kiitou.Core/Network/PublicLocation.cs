using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Kiitou.Network;

/// <summary>Where the internet sees Kiitou's connections coming from.</summary>
public sealed record PublicLocation(string IpAddress, string CountryCode, string CountryName, bool ThroughProxy, DateTime CheckedUtc)
{
    public string Describe() => $"{IpAddress} · {CountryName}";
}

/// <summary>The public address could not be found out.</summary>
public sealed class LocationException(string message) : Exception(message);

public interface ILocationService
{
    /// <summary>Finds the public address and country, through <paramref name="proxy"/> when one is given.</summary>
    Task<PublicLocation> GetAsync(Socks5Proxy? proxy, CancellationToken cancellationToken);
}

/// <summary>
/// Asks Cloudflare's public trace page, which reports the address a request arrived from and its
/// country. It needs no account. Through a proxy, it reports the proxy's address.
/// </summary>
public sealed class CloudflareLocationService : ILocationService
{
    public const string TraceUrl = "https://one.one.one.one/cdn-cgi/trace";
    private readonly Func<DateTime> _utcNow;
    private readonly HttpMessageHandler? _handler;

    public CloudflareLocationService(Func<DateTime>? utcNow = null, HttpMessageHandler? handler = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _handler = handler;
    }

    public async Task<PublicLocation> GetAsync(Socks5Proxy? proxy, CancellationToken cancellationToken)
    {
        HttpMessageHandler handler = _handler ?? CreateHandler(proxy);
        try
        {
            using var client = new HttpClient(handler, disposeHandler: _handler is null) { Timeout = TimeSpan.FromSeconds(15) };
            string body = await client.GetStringAsync(TraceUrl, cancellationToken).ConfigureAwait(false);
            return Parse(body, proxy is not null, _utcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProxyException ex)
        {
            throw new LocationException(ex.Message);
        }
        catch (HttpRequestException ex) when (ex.InnerException is ProxyException proxyError)
        {
            throw new LocationException(proxyError.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            throw new LocationException(proxy is null
                ? "Kiitou could not reach Cloudflare to find your public address."
                : $"Kiitou could not reach Cloudflare through the proxy at {proxy}.");
        }
    }

    /// <summary>Reads the "ip=" and "loc=" lines of the trace page.</summary>
    public static PublicLocation Parse(string body, bool throughProxy, DateTime utcNow)
    {
        string? ip = null, code = null;
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("ip=", StringComparison.Ordinal)) ip = trimmed[3..];
            else if (trimmed.StartsWith("loc=", StringComparison.Ordinal)) code = trimmed[4..];
        }
        if (ip is null || !IPAddress.TryParse(ip, out IPAddress? parsed))
            throw new LocationException("Cloudflare's answer did not include an address.");
        code = (code ?? "").ToUpperInvariant();
        return new PublicLocation(parsed.ToString(), code, CountryName(code), throughProxy, utcNow);
    }

    public static string CountryName(string code)
    {
        if (code.Length != 2 || code == "XX" || code == "T1") return code == "T1" ? "Tor network" : "Unknown country";
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    private static SocketsHttpHandler CreateHandler(Socks5Proxy? proxy) => new()
    {
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = async (context, token) =>
        {
            if (proxy is not null)
                return await Socks5Client.ConnectAsync(proxy, context.DnsEndPoint.Host, context.DnsEndPoint.Port, token).ConfigureAwait(false);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}

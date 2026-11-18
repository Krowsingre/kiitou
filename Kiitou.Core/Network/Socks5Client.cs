using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kiitou.Network;

/// <summary>A SOCKS5 proxy that Kiitou's tests can be sent through, such as Tor.</summary>
public sealed record Socks5Proxy(string Host, int Port, string? Username = null, string? Password = null)
{
    public bool HasCredentials => !string.IsNullOrEmpty(Username);
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>The proxy refused or could not complete a connection.</summary>
public sealed class ProxyException(string message) : IOException(message);

/// <summary>
/// A minimal SOCKS5 client (RFC 1928, with RFC 1929 username/password). It asks the proxy to
/// connect to an exact IP address, so a test still reaches the chosen candidate address, or to a
/// hostname, which the proxy then resolves (as Tor does).
/// </summary>
public static class Socks5Client
{
    private const byte Version = 0x05;
    private const byte NoAuthentication = 0x00;
    private const byte UsernamePassword = 0x02;
    private const byte NoAcceptableMethod = 0xFF;

    public static Task<Stream> ConnectAsync(Socks5Proxy proxy, IPAddress target, int port, CancellationToken cancellationToken)
    {
        byte[] address = target.GetAddressBytes();
        byte type = target.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        return ConnectAsync(proxy, [type, .. address], port, cancellationToken);
    }

    public static Task<Stream> ConnectAsync(Socks5Proxy proxy, string hostname, int port, CancellationToken cancellationToken)
    {
        byte[] name = Encoding.ASCII.GetBytes(hostname);
        if (name.Length is 0 or > 255) throw new ArgumentException("Invalid hostname.", nameof(hostname));
        return ConnectAsync(proxy, [0x03, (byte)name.Length, .. name], port, cancellationToken);
    }

    private static async Task<Stream> ConnectAsync(Socks5Proxy proxy, byte[] destination, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            try
            {
                await socket.ConnectAsync(proxy.Host, proxy.Port, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new ProxyException($"The proxy at {proxy} could not be reached ({ex.SocketErrorCode}).");
            }
            var stream = new NetworkStream(socket, ownsSocket: true);

            // Greeting: offer no authentication, and username/password when credentials are set.
            byte[] methods = proxy.HasCredentials ? [NoAuthentication, UsernamePassword] : [NoAuthentication];
            byte[] greeting = [Version, (byte)methods.Length, .. methods];
            await stream.WriteAsync(greeting.AsMemory(), cancellationToken).ConfigureAwait(false);
            byte[] choice = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
            if (choice[0] != Version) throw new ProxyException($"{proxy} is not a SOCKS5 proxy.");
            if (choice[1] == NoAcceptableMethod)
                throw new ProxyException(proxy.HasCredentials
                    ? "The proxy accepted none of the offered sign-in methods."
                    : "The proxy requires a username and password.");

            if (choice[1] == UsernamePassword)
            {
                if (!proxy.HasCredentials) throw new ProxyException("The proxy requires a username and password.");
                byte[] user = Encoding.UTF8.GetBytes(proxy.Username!);
                byte[] pass = Encoding.UTF8.GetBytes(proxy.Password ?? "");
                if (user.Length > 255 || pass.Length > 255) throw new ProxyException("The proxy username or password is too long.");
                byte[] signIn = [0x01, (byte)user.Length, .. user, (byte)pass.Length, .. pass];
                await stream.WriteAsync(signIn.AsMemory(), cancellationToken).ConfigureAwait(false);
                byte[] status = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
                if (status[1] != 0x00) throw new ProxyException("The proxy rejected the username or password.");
            }
            else if (choice[1] != NoAuthentication)
            {
                throw new ProxyException("The proxy asked for a sign-in method Kiitou does not support.");
            }

            // CONNECT request.
            byte[] request = [Version, 0x01, 0x00, .. destination, (byte)(port >> 8), (byte)port];
            await stream.WriteAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            byte[] reply = await ReadAsync(stream, 4, cancellationToken).ConfigureAwait(false);
            if (reply[0] != Version) throw new ProxyException("The proxy sent a reply Kiitou could not read.");
            if (reply[1] != 0x00) throw new ProxyException(Describe(reply[1]));

            // Skip the bound address the proxy reports.
            int addressLength = reply[3] switch
            {
                0x01 => 4,
                0x04 => 16,
                0x03 => (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0],
                _ => throw new ProxyException("The proxy sent a reply Kiitou could not read.")
            };
            await ReadAsync(stream, addressLength + 2, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string Describe(byte code) => code switch
    {
        0x01 => "The proxy reported a general failure.",
        0x02 => "The proxy does not allow this connection.",
        0x03 => "The proxy reports that the network is unreachable.",
        0x04 => "The proxy reports that the address is unreachable.",
        0x05 => "The address refused the proxy's connection.",
        0x06 => "The connection through the proxy timed out.",
        0x07 => "The proxy does not support this kind of connection.",
        0x08 => "The proxy does not support this kind of address.",
        _ => $"The proxy refused the connection (code {code})."
    };

    private static async Task<byte[]> ReadAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[count];
        try
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            throw new ProxyException("The proxy closed the connection.");
        }
        return buffer;
    }
}

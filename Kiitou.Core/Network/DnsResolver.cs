using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Kiitou.Network;

/// <summary>The A and AAAA records currently published for a hostname.</summary>
public sealed record DnsLookupResult(
    string Hostname,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6,
    string? Server,
    string? Error,
    bool NameNotFound = false)
{
    public bool Succeeded => Error is null;
    public IEnumerable<string> All => Ipv4.Concat(Ipv6);
}

public interface IDnsResolver
{
    Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the DNS servers configured on this computer directly. Because it does not use the
/// Windows resolver, answers are never taken from the hosts file or the local DNS cache.
/// </summary>
public sealed class DirectDnsResolver : IDnsResolver
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);
    private readonly Func<IReadOnlyList<IPAddress>> _servers;
    private readonly int _port;

    public DirectDnsResolver(Func<IReadOnlyList<IPAddress>>? servers = null, int port = 53)
    {
        _servers = servers ?? GetSystemDnsServers;
        _port = port;
    }

    public async Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> servers = _servers();
        if (servers.Count == 0)
            return Failure(hostname, null, "No DNS server is configured on this computer.");

        string lastError = "No DNS server answered.";
        foreach (IPAddress server in servers)
        {
            try
            {
                DnsResponse a = await QueryAsync(server, hostname, DnsRecordType.A, cancellationToken).ConfigureAwait(false);
                if (a.ResponseCode == DnsMessage.NameError)
                    return Failure(hostname, server.ToString(), $"{hostname} does not exist in DNS.") with { NameNotFound = true };
                DnsResponse aaaa = await QueryAsync(server, hostname, DnsRecordType.AAAA, cancellationToken).ConfigureAwait(false);

                if (a.ResponseCode != 0 && aaaa.ResponseCode != 0)
                {
                    lastError = $"DNS server {server} returned error code {a.ResponseCode}.";
                    continue;
                }
                return new DnsLookupResult(hostname, a.Addresses, aaaa.Addresses, server.ToString(), null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or OperationCanceledException)
            {
                lastError = ex is OperationCanceledException
                    ? $"DNS server {server} did not answer in time."
                    : $"DNS server {server}: {ex.Message}";
            }
        }
        return Failure(hostname, null, lastError);
    }

    private static DnsLookupResult Failure(string hostname, string? server, string error) =>
        new(hostname, [], [], server, error);

    private async Task<DnsResponse> QueryAsync(
        IPAddress server, string hostname, DnsRecordType type, CancellationToken cancellationToken)
    {
        ushort id = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
        byte[] query = DnsMessage.BuildQuery(id, hostname, type);
        var endpoint = new IPEndPoint(server, _port);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        DnsResponse response = await QueryUdpAsync(endpoint, query, id, type, timeout.Token).ConfigureAwait(false);
        if (response.Truncated)
            response = await QueryTcpAsync(endpoint, query, id, type, timeout.Token).ConfigureAwait(false);
        return response;
    }

    private static async Task<DnsResponse> QueryUdpAsync(
        IPEndPoint endpoint, byte[] query, ushort id, DnsRecordType type, CancellationToken cancellationToken)
    {
        using var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await socket.SendAsync(query.AsMemory(), SocketFlags.None, cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[4096];
        while (true)
        {
            int received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            // Ignore stray datagrams that are not the reply to this query.
            if (received >= 2 && BinaryPrimitives.ReadUInt16BigEndian(buffer) == id)
                return DnsMessage.ParseResponse(buffer.AsSpan(0, received), id, type);
        }
    }

    private static async Task<DnsResponse> QueryTcpAsync(
        IPEndPoint endpoint, byte[] query, ushort id, DnsRecordType type, CancellationToken cancellationToken)
    {
        using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        byte[] framed = new byte[query.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed.AsMemory(), cancellationToken).ConfigureAwait(false);

        byte[] lengthPrefix = new byte[2];
        await stream.ReadExactlyAsync(lengthPrefix.AsMemory(), cancellationToken).ConfigureAwait(false);
        byte[] reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await stream.ReadExactlyAsync(reply.AsMemory(), cancellationToken).ConfigureAwait(false);
        return DnsMessage.ParseResponse(reply, id, type);
    }

    /// <summary>DNS servers of the network adapters that are currently up, in adapter order.</summary>
    public static IReadOnlyList<IPAddress> GetSystemDnsServers()
    {
        var servers = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (IPAddress address in adapter.GetIPProperties().DnsAddresses)
                {
                    // Windows lists fec0:0:0:ffff::1-3 as placeholder servers on adapters without IPv6 DNS.
                    if (address.IsIPv6SiteLocal) continue;
                    if (!servers.Contains(address)) servers.Add(address);
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Report as "no DNS server configured".
        }
        // Prefer IPv4 servers: they are reachable on every network that has DNS at all.
        return servers.OrderBy(s => s.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
    }
}

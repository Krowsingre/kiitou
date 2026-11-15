using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kiitou.Catalog;
using Kiitou.Models;
using Kiitou.Network;

namespace Kiitou.Tests;

public class NetworkTests
{
    // ---- DNS wire format ---------------------------------------------------------------

    private static byte[] Reply(ushort id, ushort flags, params byte[][] answers)
    {
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)id, (byte)(flags >> 8), (byte)flags,
            0, 1, 0, (byte)answers.Length, 0, 0, 0, 0,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m', 0,
            0, 1, 0, 1
        };
        foreach (byte[] answer in answers) bytes.AddRange(answer);
        return bytes.ToArray();
    }

    /// <summary>An answer record whose owner name is a compression pointer to the question name.</summary>
    private static byte[] Answer(ushort type, params byte[] data) =>
    [
        0xC0, 0x0C, (byte)(type >> 8), (byte)type, 0, 1, 0, 0, 0, 60, (byte)(data.Length >> 8), (byte)data.Length, .. data
    ];

    [Fact]
    public void QueryIsEncodedInWireFormat()
    {
        byte[] query = DnsMessage.BuildQuery(0x1234, "example.com", DnsRecordType.AAAA);
        byte[] expected =
        [
            0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m', 0,
            0, 28, 0, 1
        ];
        Assert.Equal(expected, query);
    }

    [Fact]
    public void ReplyAddressesAreReadPastAliasesAndDuplicates()
    {
        byte[] alias = Answer(5, 3, (byte)'w', (byte)'w', (byte)'w', 0xC0, 0x0C);
        byte[] reply = Reply(7, 0x8180,
            alias,
            Answer(1, 93, 184, 216, 34),
            Answer(1, 93, 184, 216, 34),
            Answer(1, 198, 51, 100, 20),
            Answer(28, 0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1));

        DnsResponse a = DnsMessage.ParseResponse(reply, 7, DnsRecordType.A);
        DnsResponse aaaa = DnsMessage.ParseResponse(reply, 7, DnsRecordType.AAAA);

        Assert.Equal(0, a.ResponseCode);
        Assert.False(a.Truncated);
        Assert.Equal(new[] { "93.184.216.34", "198.51.100.20" }, a.Addresses);
        Assert.Equal(new[] { "2001:db8::1" }, aaaa.Addresses);
    }

    [Fact]
    public void ReplyFlagsAreReported()
    {
        Assert.Equal(DnsMessage.NameError, DnsMessage.ParseResponse(Reply(7, 0x8183), 7, DnsRecordType.A).ResponseCode);
        Assert.True(DnsMessage.ParseResponse(Reply(7, 0x8380), 7, DnsRecordType.A).Truncated);
    }

    [Fact]
    public void MalformedRepliesAreRejected()
    {
        byte[] good = Reply(7, 0x8180, Answer(1, 93, 184, 216, 34));

        Assert.Throws<InvalidDataException>(() => DnsMessage.ParseResponse(new byte[5], 7, DnsRecordType.A));
        Assert.Throws<InvalidDataException>(() => DnsMessage.ParseResponse(good, 8, DnsRecordType.A));
        Assert.Throws<InvalidDataException>(() => DnsMessage.ParseResponse(Reply(7, 0x0100), 7, DnsRecordType.A));
        Assert.Throws<InvalidDataException>(() => DnsMessage.ParseResponse(good[..^2], 7, DnsRecordType.A));
        Assert.Throws<InvalidDataException>(() => DnsMessage.ParseResponse(good[..20], 7, DnsRecordType.A));
    }

    // ---- Resolver against a local DNS server -------------------------------------------

    /// <summary>A tiny DNS server on the loopback address that answers with canned records.</summary>
    private sealed class FakeDnsServer : IDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly TcpListener _tcp;
        private readonly CancellationTokenSource _stop = new();
        public int Port { get; }
        public bool TruncateUdp { get; init; }
        public bool Silent { get; init; }
        public ushort Flags { get; init; } = 0x8180;
        public int UdpQueries;
        public int TcpQueries;

        public FakeDnsServer()
        {
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _tcp = new TcpListener(IPAddress.Loopback, Port);
            _tcp.Start();
            _ = Task.Run(ServeUdpAsync);
            _ = Task.Run(ServeTcpAsync);
        }

        private byte[] Build(byte[] query, bool truncated)
        {
            ushort id = (ushort)((query[0] << 8) | query[1]);
            ushort type = (ushort)((query[^4] << 8) | query[^3]);
            ushort flags = truncated ? (ushort)(Flags | 0x0200) : Flags;
            if (truncated || (Flags & 0x000F) != 0) return Reply(id, flags);
            return type == 1
                ? Reply(id, flags, Answer(1, 93, 184, 216, 34), Answer(1, 198, 51, 100, 20))
                : Reply(id, flags, Answer(28, 0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1));
        }

        private async Task ServeUdpAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    UdpReceiveResult received = await _udp.ReceiveAsync(_stop.Token);
                    Interlocked.Increment(ref UdpQueries);
                    if (Silent) continue;
                    byte[] reply = Build(received.Buffer, TruncateUdp);
                    await _udp.SendAsync(reply, reply.Length, received.RemoteEndPoint);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }

        private async Task ServeTcpAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using TcpClient client = await _tcp.AcceptTcpClientAsync(_stop.Token);
                    NetworkStream stream = client.GetStream();
                    byte[] prefix = new byte[2];
                    await stream.ReadExactlyAsync(prefix.AsMemory(), _stop.Token);
                    byte[] query = new byte[(prefix[0] << 8) | prefix[1]];
                    await stream.ReadExactlyAsync(query.AsMemory(), _stop.Token);
                    Interlocked.Increment(ref TcpQueries);
                    byte[] reply = Build(query, false);
                    await stream.WriteAsync(new[] { (byte)(reply.Length >> 8), (byte)reply.Length }.AsMemory(), _stop.Token);
                    await stream.WriteAsync(reply.AsMemory(), _stop.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException) { }
        }

        public DirectDnsResolver Resolver() => new(() => [IPAddress.Loopback], Port);

        public void Dispose()
        {
            _stop.Cancel();
            _udp.Dispose();
            _tcp.Stop();
        }
    }

    [Fact]
    public async Task ResolverReturnsPublishedIpv4AndIpv6Records()
    {
        using var server = new FakeDnsServer();

        DnsLookupResult result = await server.Resolver().ResolveAsync("example.com", CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(new[] { "93.184.216.34", "198.51.100.20" }, result.Ipv4);
        Assert.Equal(new[] { "2001:db8::1" }, result.Ipv6);
        Assert.Equal("127.0.0.1", result.Server);
        Assert.Equal(2, server.UdpQueries);
        Assert.Equal(0, server.TcpQueries);
    }

    [Fact]
    public async Task ResolverRetriesOverTcpWhenTheReplyIsTruncated()
    {
        using var server = new FakeDnsServer { TruncateUdp = true };

        DnsLookupResult result = await server.Resolver().ResolveAsync("example.com", CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Ipv4.Count);
        Assert.Single(result.Ipv6);
        Assert.Equal(2, server.TcpQueries);
    }

    [Fact]
    public async Task ResolverReportsNamesThatDoNotExist()
    {
        using var server = new FakeDnsServer { Flags = 0x8183 };

        DnsLookupResult result = await server.Resolver().ResolveAsync("missing.example", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Error);
        Assert.Empty(result.All);
    }

    [Fact]
    public async Task ResolverReportsSilentAndMissingServers()
    {
        using var server = new FakeDnsServer { Silent = true };
        DnsLookupResult silent = await server.Resolver().ResolveAsync("example.com", CancellationToken.None);
        Assert.False(silent.Succeeded);
        Assert.Contains("did not answer", silent.Error);

        DnsLookupResult none = await new DirectDnsResolver(() => []).ResolveAsync("example.com", CancellationToken.None);
        Assert.False(none.Succeeded);
        Assert.Contains("No DNS server", none.Error);
    }

    // ---- Discovery ---------------------------------------------------------------------

    [Fact]
    public void DiscoveryOffersNewAddressesAndNeverRemovesSavedOnes()
    {
        AppConfiguration config = AppConfiguration.CreateDefault();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite("example.com");
        catalog.AddAddress(site.Id, "93.184.216.34");
        catalog.AddAddress(site.Id, "192.0.2.50");
        var lookup = new DnsLookupResult("example.com", ["93.184.216.34", "198.51.100.20"], ["2001:db8::1"], "127.0.0.1", null);

        DiscoveryResult result = DnsDiscovery.Compare(site, lookup);

        Assert.Equal(new[] { "198.51.100.20", "2001:db8::1" }, result.NewAddresses);
        Assert.Equal(new[] { "93.184.216.34" }, result.AlreadySaved);
        Assert.Equal(new[] { "192.0.2.50" }, result.SavedButNotPublished);
        Assert.Equal(2, site.Addresses.Count);

        Assert.Equal(1, DnsDiscovery.AddDiscovered(catalog, site.Id, ["2001:db8::1", "93.184.216.34", "2001:db8::1"]));
        Assert.Equal(new[] { "93.184.216.34", "192.0.2.50", "2001:db8::1" }, site.Addresses.Select(a => a.IpAddress).ToArray());
    }

    [Fact]
    public void FailedLookupDoesNotMarkSavedAddressesAsUnpublished()
    {
        AppConfiguration config = AppConfiguration.CreateDefault();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite("example.com");
        catalog.AddAddress(site.Id, "93.184.216.34");

        DiscoveryResult result = DnsDiscovery.Compare(site, new DnsLookupResult("example.com", [], [], null, "No DNS server answered."));

        Assert.Empty(result.NewAddresses);
        Assert.Empty(result.SavedButNotPublished);
    }

    // ---- Test sequencing ---------------------------------------------------------------

    private sealed class ScriptedTester(params string[] passing) : IAddressTester
    {
        public List<string> Tested { get; } = [];

        public Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
        {
            Tested.Add(ipAddress);
            bool pass = passing.Contains(ipAddress);
            return Task.FromResult(new AddressTestResult
            {
                Hostname = hostname, IpAddress = ipAddress, Connected = pass, TlsValid = pass, HttpStatus = pass ? 200 : null
            });
        }
    }

    private static readonly string[] Candidates = ["192.0.2.1", "192.0.2.2", "192.0.2.3", "192.0.2.4"];

    [Fact]
    public async Task TestAllExhaustsTheList()
    {
        var tester = new ScriptedTester("192.0.2.2");
        var reported = new List<string>();

        var results = await AddressTestRunner.TestAllAsync(
            tester, "example.com", Candidates, new SynchronousProgress(r => reported.Add(r.IpAddress)), CancellationToken.None);

        Assert.Equal(Candidates, tester.Tested);
        Assert.Equal(Candidates, reported);
        Assert.Equal(new[] { false, true, false, false }, results.Select(r => r.Passed).ToArray());
    }

    [Fact]
    public async Task FindFirstWorkingStopsAtTheFirstPassingAddress()
    {
        var tester = new ScriptedTester("192.0.2.2", "192.0.2.4");

        var (first, tested) = await AddressTestRunner.FindFirstWorkingAsync(
            tester, "example.com", Candidates, null, CancellationToken.None);

        Assert.Equal("192.0.2.2", first!.IpAddress);
        Assert.Equal(new[] { "192.0.2.1", "192.0.2.2" }, tester.Tested);
        Assert.Equal(2, tested.Count);
    }

    [Fact]
    public async Task FindFirstWorkingReportsWhenNothingPasses()
    {
        var tester = new ScriptedTester();

        var (first, tested) = await AddressTestRunner.FindFirstWorkingAsync(
            tester, "example.com", Candidates, null, CancellationToken.None);

        Assert.Null(first);
        Assert.Equal(4, tested.Count);
    }

    [Fact]
    public async Task CancellationStopsARun()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AddressTestRunner.TestAllAsync(
            new ScriptedTester(), "example.com", Candidates, null, cancel.Token));
    }

    private sealed class SynchronousProgress(Action<AddressTestResult> report) : IProgress<AddressTestResult>
    {
        public void Report(AddressTestResult value) => report(value);
    }

    // ---- HTTPS tester ------------------------------------------------------------------

    [Fact]
    public async Task UntrustedCertificateFailsTheTestButIsDescribed()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=kiitou-test.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("kiitou-test.example");
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pfx), null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(serverCertificate);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or SocketException) { }
        });

        try
        {
            AddressTestResult result = await new HttpsAddressTester(TimeSpan.FromSeconds(10), port)
                .TestAsync("kiitou-test.example", "127.0.0.1", CancellationToken.None);

            Assert.True(result.Connected);
            Assert.False(result.TlsValid);
            Assert.False(result.Passed);
            Assert.Null(result.HttpStatus);
            Assert.Contains("not trusted", result.TlsError);
            Assert.Equal("CN=kiitou-test.example", result.CertificateSubject);
            Assert.Equal(selfSigned.Thumbprint, result.CertificateThumbprint);
            Assert.NotNull(result.Error);
            Assert.NotNull(result.ConnectMilliseconds);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task ClosedPortIsReportedAsNotConnected()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        AddressTestResult result = await new HttpsAddressTester(TimeSpan.FromSeconds(5), port)
            .TestAsync("kiitou-test.example", "127.0.0.1", CancellationToken.None);

        Assert.False(result.Connected);
        Assert.False(result.Passed);
        Assert.NotNull(result.Error);
        Assert.Null(result.CertificateSubject);
    }

    [Fact]
    public async Task InvalidAddressTextIsReportedWithoutConnecting()
    {
        AddressTestResult result = await new HttpsAddressTester().TestAsync("example.com", "not-an-ip", CancellationToken.None);
        Assert.False(result.Passed);
        Assert.Contains("not a valid IP", result.Error);
    }

    [Theory]
    [InlineData("<html><head><title>Example Domain</title></head>", "Example Domain")]
    [InlineData("<TITLE lang=\"en\">\n  Caf&eacute;   &amp; Bar \n</TITLE>", "Café & Bar")]
    [InlineData("<html><body>no title</body></html>", null)]
    [InlineData("<title>   </title>", null)]
    public void PageTitleIsExtracted(string body, string? expected) =>
        Assert.Equal(expected, HttpsAddressTester.ExtractTitle(body));

    // ---- Live network checks (build runner only) ---------------------------------------

    [Fact]
    public async Task LiveLookupAndConnectionOnBuildRunner()
    {
        if (Environment.GetEnvironmentVariable("KIITOU_CI") != "1") return;

        var resolver = new DirectDnsResolver();
        DnsLookupResult example = await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.True(example.Succeeded, example.Error);
        Assert.NotEmpty(example.Ipv4);

        var tester = new HttpsAddressTester(TimeSpan.FromSeconds(20));
        AddressTestResult direct = await tester.TestAsync("example.com", example.Ipv4[0], CancellationToken.None);
        Assert.True(direct.Passed, $"{direct.Error} {direct.TlsError}");
        Assert.NotNull(direct.CertificateSubject);
        Assert.NotNull(direct.HttpStatus);

        // The same hostname sent to another site's server must fail certificate validation.
        DnsLookupResult other = await resolver.ResolveAsync("github.com", CancellationToken.None);
        Assert.True(other.Succeeded, other.Error);
        AddressTestResult wrong = await tester.TestAsync("example.com", other.Ipv4[0], CancellationToken.None);
        Assert.False(wrong.Passed);
        Assert.False(wrong.TlsValid);
    }
}

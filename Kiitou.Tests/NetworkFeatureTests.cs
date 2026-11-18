using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Kiitou.Catalog;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Verification;

namespace Kiitou.Tests;

public class NetworkFeatureTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A small SOCKS5 server on the loopback address that forwards to the requested target.</summary>
    private sealed class FakeSocksServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public int Port { get; }
        public string? RequiredUser { get; init; }
        public string? RequiredPassword { get; init; }
        /// <summary>When set, the CONNECT request is answered with this failure code.</summary>
        public byte? RefuseWith { get; init; }
        public List<string> Targets { get; } = [];

        public FakeSocksServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptAsync);
        }

        public Socks5Proxy Proxy(string? user = null, string? password = null) => new("127.0.0.1", Port, user, password);

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Task.Run(() => HandleAsync(client));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    byte[] head = await Read(stream, 2);
                    byte[] methods = await Read(stream, head[1]);
                    bool needAuth = RequiredUser is not null;
                    byte chosen = needAuth ? (methods.Contains((byte)2) ? (byte)2 : (byte)0xFF) : (byte)0;
                    await stream.WriteAsync(new byte[] { 5, chosen }.AsMemory());
                    if (chosen == 0xFF) return;
                    if (chosen == 2)
                    {
                        byte[] version = await Read(stream, 2);
                        string user = Encoding.UTF8.GetString(await Read(stream, version[1]));
                        byte[] passLength = await Read(stream, 1);
                        string pass = Encoding.UTF8.GetString(await Read(stream, passLength[0]));
                        bool ok = user == RequiredUser && pass == RequiredPassword;
                        await stream.WriteAsync(new byte[] { 1, ok ? (byte)0 : (byte)1 }.AsMemory());
                        if (!ok) return;
                    }

                    byte[] request = await Read(stream, 4);
                    string host;
                    switch (request[3])
                    {
                        case 1: host = new IPAddress(await Read(stream, 4)).ToString(); break;
                        case 4: host = new IPAddress(await Read(stream, 16)).ToString(); break;
                        default:
                            byte[] length = await Read(stream, 1);
                            host = Encoding.ASCII.GetString(await Read(stream, length[0]));
                            break;
                    }
                    byte[] portBytes = await Read(stream, 2);
                    int port = (portBytes[0] << 8) | portBytes[1];
                    lock (Targets) Targets.Add($"{host}:{port}");

                    if (RefuseWith is byte code)
                    {
                        await stream.WriteAsync(new byte[] { 5, code, 0, 1, 0, 0, 0, 0, 0, 0 }.AsMemory());
                        return;
                    }
                    using var upstream = new TcpClient();
                    await upstream.ConnectAsync(IPAddress.Parse(host == "localhost" ? "127.0.0.1" : host), port);
                    await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }.AsMemory());
                    NetworkStream up = upstream.GetStream();
                    await Task.WhenAny(stream.CopyToAsync(up), up.CopyToAsync(stream));
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or FormatException) { }
            }
        }

        private static async Task<byte[]> Read(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            await stream.ReadExactlyAsync(buffer.AsMemory());
            return buffer;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    /// <summary>A loopback TCP server that answers "hello" to anything.</summary>
    private sealed class EchoTarget : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port { get; }

        public EchoTarget()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using TcpClient client = await _listener.AcceptTcpClientAsync();
                        await client.GetStream().WriteAsync("hello"u8.ToArray().AsMemory());
                    }
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException) { }
            });
        }

        public void Dispose() => _listener.Stop();
    }

    private static async Task<string> ReadHello(Stream stream)
    {
        byte[] buffer = new byte[5];
        await stream.ReadExactlyAsync(buffer.AsMemory());
        return Encoding.ASCII.GetString(buffer);
    }

    // ---- SOCKS5 client -----------------------------------------------------------------

    [Fact]
    public async Task Socks5ConnectsToAnExactAddressWithoutSignIn()
    {
        using var target = new EchoTarget();
        using var proxy = new FakeSocksServer();

        await using Stream stream = await Socks5Client.ConnectAsync(proxy.Proxy(), IPAddress.Loopback, target.Port, CancellationToken.None);

        Assert.Equal("hello", await ReadHello(stream));
        Assert.Equal($"127.0.0.1:{target.Port}", Assert.Single(proxy.Targets));
    }

    [Fact]
    public async Task Socks5SignsInAndSendsHostnamesForTheProxyToResolve()
    {
        using var target = new EchoTarget();
        using var proxy = new FakeSocksServer { RequiredUser = "alice", RequiredPassword = "s3cret" };

        await using Stream stream = await Socks5Client.ConnectAsync(proxy.Proxy("alice", "s3cret"), "localhost", target.Port, CancellationToken.None);

        Assert.Equal("hello", await ReadHello(stream));
        Assert.Equal($"localhost:{target.Port}", Assert.Single(proxy.Targets));
    }

    [Fact]
    public async Task Socks5ExplainsSignInProblemsRefusalsAndUnreachableProxies()
    {
        using var proxy = new FakeSocksServer { RequiredUser = "alice", RequiredPassword = "s3cret" };
        using var refusing = new FakeSocksServer { RefuseWith = 0x05 };

        var missing = await Assert.ThrowsAsync<ProxyException>(() => Socks5Client.ConnectAsync(proxy.Proxy(), IPAddress.Loopback, 9, CancellationToken.None));
        Assert.Contains("requires a username and password", missing.Message);
        var wrong = await Assert.ThrowsAsync<ProxyException>(() => Socks5Client.ConnectAsync(proxy.Proxy("alice", "nope"), IPAddress.Loopback, 9, CancellationToken.None));
        Assert.Contains("rejected the username or password", wrong.Message);
        var refused = await Assert.ThrowsAsync<ProxyException>(() => Socks5Client.ConnectAsync(refusing.Proxy(), IPAddress.Loopback, 9, CancellationToken.None));
        Assert.Contains("refused the proxy's connection", refused.Message);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var unreachable = await Assert.ThrowsAsync<ProxyException>(() =>
            Socks5Client.ConnectAsync(new Socks5Proxy("127.0.0.1", closedPort), IPAddress.Loopback, 9, CancellationToken.None));
        Assert.Contains("could not be reached", unreachable.Message);
    }

    [Fact]
    public async Task AddressTestsGoThroughTheProxyToTheCandidateAddress()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=kiitou-test.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or SocketException) { }
        });
        using var proxy = new FakeSocksServer();

        try
        {
            AddressTestResult result = await new HttpsAddressTester(TimeSpan.FromSeconds(10), port, () => proxy.Proxy())
                .TestAsync("kiitou-test.example", "127.0.0.1", CancellationToken.None);

            Assert.True(result.Connected);
            Assert.False(result.TlsValid);
            Assert.Equal("CN=kiitou-test.example", result.CertificateSubject);
            Assert.Equal($"127.0.0.1:{port}", Assert.Single(proxy.Targets));
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task AProxyFailureIsReportedAsTheTestError()
    {
        using var refusing = new FakeSocksServer { RefuseWith = 0x04 };

        AddressTestResult result = await new HttpsAddressTester(TimeSpan.FromSeconds(5), 443, () => refusing.Proxy())
            .TestAsync("example.com", "93.184.216.34", CancellationToken.None);

        Assert.False(result.Connected);
        Assert.False(result.Passed);
        Assert.Contains("address is unreachable", result.Error);
    }

    // ---- Location ----------------------------------------------------------------------

    [Fact]
    public void CloudflareTraceIsRead()
    {
        const string trace = "fl=123\nh=one.one.one.one\nip=203.0.113.5\nts=1.2\nloc=DE\ntls=TLSv1.3\n";
        PublicLocation location = CloudflareLocationService.Parse(trace, throughProxy: true, Now);

        Assert.Equal("203.0.113.5", location.IpAddress);
        Assert.Equal("DE", location.CountryCode);
        Assert.Equal("Germany", location.CountryName);
        Assert.True(location.ThroughProxy);
        Assert.Equal(Now, location.CheckedUtc);
        Assert.Equal("Tor network", CloudflareLocationService.CountryName("T1"));
        Assert.Equal("Unknown country", CloudflareLocationService.CountryName("XX"));
        Assert.Throws<LocationException>(() => CloudflareLocationService.Parse("loc=DE\n", false, Now));
    }

    [Fact]
    public async Task LiveLocationCheckOnBuildRunner()
    {
        if (Environment.GetEnvironmentVariable("KIITOU_CI") != "1") return;
        PublicLocation location = await new CloudflareLocationService().GetAsync(null, CancellationToken.None);
        Assert.True(IPAddress.TryParse(location.IpAddress, out _));
        Assert.Equal(2, location.CountryCode.Length);
        Assert.False(location.ThroughProxy);
    }

    // ---- Proxy settings ----------------------------------------------------------------

    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string secret) => "enc:" + new string(secret.Reverse().ToArray());
        public string? Unprotect(string protectedSecret) =>
            protectedSecret.StartsWith("enc:") ? new string(protectedSecret[4..].Reverse().ToArray()) : null;
    }

    [Fact]
    public void ProxySettingsAreValidatedEncryptedAndLockedWhileOn()
    {
        AppConfiguration config = AppConfiguration.CreateDefault();
        int saves = 0;
        var network = new NetworkSettings(config, new ReversingProtector(), () => saves++);

        Assert.Null(network.ActiveProxy);
        Assert.Throws<CatalogException>(() => network.SetEnabled(true));
        Assert.Throws<CatalogException>(() => network.SetProxy("not a host!", "9150", "", ""));
        Assert.Throws<CatalogException>(() => network.SetProxy("127.0.0.1", "70000", "", ""));
        Assert.Throws<CatalogException>(() => network.SetProxy("127.0.0.1", "", "", ""));

        network.SetProxy(" 127.0.0.1 ", "9150", "alice", "s3cret");
        Assert.DoesNotContain("s3cret", config.Settings.Proxy.ProtectedPassword);
        Assert.Equal("s3cret", network.Password);
        network.SetEnabled(true);

        Assert.Equal(new Socks5Proxy("127.0.0.1", 9150, "alice", "s3cret"), network.ActiveProxy);
        Assert.Throws<CatalogException>(() => network.SetProxy("10.0.0.1", "1080", "", ""));
        network.SetEnabled(false);
        Assert.Null(network.ActiveProxy);
        network.SetProxy("localhost", "1080", "", "");
        Assert.Equal("", config.Settings.Proxy.ProtectedPassword);
        Assert.Equal(4, saves);
    }

    // ---- Menu order --------------------------------------------------------------------

    [Fact]
    public void MenuOrderDefaultsAndRepairs()
    {
        Assert.Equal(new[] { "home", "websites", "discovery", "profiles", "network", "hosts", "settings" }, Navigation.DefaultOrder);
        Assert.Equal(Navigation.DefaultOrder, Navigation.Normalize(null));
        Assert.Equal(new[] { "settings", "home", "websites", "discovery", "profiles", "network", "hosts" },
            Navigation.Normalize(new[] { "settings", "home", "websites", "discovery", "profiles", "network", "hosts" }));
        // Unknown and repeated pages are dropped; missing ones return near their default place.
        Assert.Equal(new[] { "hosts", "home", "websites", "discovery", "profiles", "network", "settings" },
            Navigation.Normalize(new string?[] { "hosts", "nonsense", "home", "home", null, "websites", "discovery", "profiles", "settings" }));
    }

    // ---- Test log ----------------------------------------------------------------------

    [Fact]
    public void TestLogKeepsNewestEntriesAndSurvivesDamage()
    {
        using var dir = new TempDirectory();
        var log = new TestLogStore(dir.Path);
        for (int i = 0; i < TestLogStore.MaximumEntries + 5; i++)
            log.Add(new TestLogEntry { TestedUtc = Now.AddSeconds(i), Hostname = "example.com", IpAddress = "93.184.216.34", Status = VerificationStatus.ConnectionVerified });

        var reloaded = new TestLogStore(dir.Path).Entries;
        Assert.Equal(TestLogStore.MaximumEntries, reloaded.Count);
        Assert.Equal(Now.AddSeconds(TestLogStore.MaximumEntries + 4), reloaded[0].TestedUtc);

        File.WriteAllText(dir.File("test-log.json"), "{ damaged");
        Assert.Empty(new TestLogStore(dir.Path).Entries);
        Assert.True(File.Exists(dir.File("test-log.json.damaged")));

        var fresh = new TestLogStore(dir.Path);
        fresh.Add(new TestLogEntry { TestedUtc = Now, Hostname = "a.example", IpAddress = "192.0.2.1" });
        fresh.Clear();
        Assert.Empty(new TestLogStore(dir.Path).Entries);
    }
}

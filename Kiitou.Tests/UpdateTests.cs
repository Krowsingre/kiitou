using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kiitou.Updates;

namespace Kiitou.Tests;

public class UpdateTests
{
    private const string Feed = "https://github.com/Krowsingre/kiitou/releases/latest/download/latest.json";
    private const string Setup = "https://github.com/Krowsingre/kiitou/releases/download/v0.9.1/Kiitou-Setup-0.9.1.exe";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _pages = [];
        public List<string> Requests { get; } = [];

        public FakeGitHub Page(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            Page(url, Encoding.UTF8.GetBytes(body), status);

        public FakeGitHub Page(string url, byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _pages[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(_pages.TryGetValue(url, out var page)
                ? new HttpResponseMessage(page.Status) { Content = new ByteArrayContent(page.Body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Dir { get; } = new();
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public FakeGitHub Web { get; } = new();
        public byte[] SetupBytes { get; } = Encoding.UTF8.GetBytes("pretend this is the Kiitou setup program " + new string('x', 5000));

        public string Sign(byte[] data) => Convert.ToBase64String(Key.SignData(data, HashAlgorithmName.SHA256));

        public string FeedJson(string version, string url, string signature) =>
            "{\"version\":\"" + version + "\",\"notes\":\"Kiitou " + version + "\",\"url\":\"" + url + "\",\"signature\":\"" + signature + "\"}";

        public UpdateService Service(string current = "0.9.0") =>
            new(Version.Parse(current), Web, Key.ExportSubjectPublicKeyInfoPem(), Feed, Dir.Path);

        public void Dispose()
        {
            Key.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task ANewerSignedReleaseIsFoundDownloadedAndVerified()
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", Setup, fx.Sign(fx.SetupBytes))).Page(Setup, fx.SetupBytes);
        var progress = new List<double>();

        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);
        Assert.True(info.IsAvailable);
        Assert.Equal(new Version(0, 9, 1), info.Available);
        Assert.Equal("Kiitou 0.9.1", info.Notes);

        string path = await fx.Service().DownloadAsync(info, new SynchronousProgress(progress.Add), CancellationToken.None);

        Assert.Equal(fx.SetupBytes, File.ReadAllBytes(path));
        Assert.StartsWith(fx.Dir.Path, path);
        Assert.EndsWith("Kiitou-Setup-0.9.1.exe", path);
        Assert.Equal(1.0, progress[^1], 3);
    }

    [Theory]
    [InlineData("0.9.0", "0.9.0")]
    [InlineData("0.9.1", "0.9.0")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.9.0.0", "0.9.0")]
    public async Task TheSameOrAnOlderReleaseIsNotAnUpdate(string current, string published)
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, fx.FeedJson(published, Setup, fx.Sign(fx.SetupBytes)));

        UpdateInfo info = await fx.Service(current).CheckAsync(CancellationToken.None);

        Assert.False(info.IsAvailable);
        await Assert.ThrowsAsync<UpdateException>(() => fx.Service(current).DownloadAsync(info, null, CancellationToken.None));
    }

    [Fact]
    public async Task NoPublishedReleaseMeansNoUpdateAndNoError()
    {
        using var fx = new Fixture();
        Assert.False((await fx.Service().CheckAsync(CancellationToken.None)).IsAvailable);
    }

    [Theory]
    [InlineData("{ not json", HttpStatusCode.OK)]
    [InlineData("{\"version\":\"soon\"}", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.OK)]
    [InlineData("oops", HttpStatusCode.InternalServerError)]
    public async Task UnreadableUpdateInformationIsReported(string body, HttpStatusCode status)
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, body, status);
        await Assert.ThrowsAsync<UpdateException>(() => fx.Service().CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ATamperedDownloadIsRejectedAndDeleted()
    {
        using var fx = new Fixture();
        byte[] tampered = (byte[])fx.SetupBytes.Clone();
        tampered[100] ^= 0xFF;
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", Setup, fx.Sign(fx.SetupBytes))).Page(Setup, tampered);
        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => fx.Service().DownloadAsync(info, null, CancellationToken.None));

        Assert.Contains("not signed with Kiitou's update key", ex.Message);
        Assert.Empty(Directory.GetFiles(fx.Dir.Path));
    }

    [Fact]
    public async Task ASignatureFromAnotherKeyIsRejected()
    {
        using var fx = new Fixture();
        using ECDsa stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string forged = Convert.ToBase64String(stranger.SignData(fx.SetupBytes, HashAlgorithmName.SHA256));
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", Setup, forged)).Page(Setup, fx.SetupBytes);
        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);

        await Assert.ThrowsAsync<UpdateException>(() => fx.Service().DownloadAsync(info, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(fx.Dir.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public async Task AnUnsignedUpdateIsNeverDownloaded(string signature)
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", Setup, signature)).Page(Setup, fx.SetupBytes);
        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => fx.Service().DownloadAsync(info, null, CancellationToken.None));

        Assert.Contains("not signed", ex.Message);
        Assert.DoesNotContain(Setup, fx.Web.Requests);
    }

    [Theory]
    [InlineData("https://example.com/Kiitou-Setup-0.9.1.exe")]
    [InlineData("https://github.com/someone-else/kiitou/releases/download/v0.9.1/Kiitou-Setup-0.9.1.exe")]
    [InlineData("http://github.com/Krowsingre/kiitou/releases/download/v0.9.1/Kiitou-Setup-0.9.1.exe")]
    [InlineData("https://github.com/Krowsingre/kiitou/releases/download/../../../evil/x.exe")]
    [InlineData("https://github.com/Krowsingre/kiitou/releases/download/v0.9.1/readme.txt")]
    public async Task DownloadsFromAnywhereElseAreRefused(string url)
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", url, fx.Sign(fx.SetupBytes))).Page(url, fx.SetupBytes);
        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);

        await Assert.ThrowsAsync<UpdateException>(() => fx.Service().DownloadAsync(info, null, CancellationToken.None));
        Assert.DoesNotContain(url, fx.Web.Requests);
    }

    [Fact]
    public async Task AFailedDownloadIsReportedAndLeavesNothingBehind()
    {
        using var fx = new Fixture();
        fx.Web.Page(Feed, fx.FeedJson("0.9.1", Setup, fx.Sign(fx.SetupBytes)));
        UpdateInfo info = await fx.Service().CheckAsync(CancellationToken.None);

        await Assert.ThrowsAsync<UpdateException>(() => fx.Service().DownloadAsync(info, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(fx.Dir.Path));
    }

    [Fact]
    public void TheBuiltInUpdateKeyIsAValidPublicKey()
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(UpdateService.PublicKeyPem);
        Assert.Equal(256, key.KeySize);
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

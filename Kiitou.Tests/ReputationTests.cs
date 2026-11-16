using System.Net;
using System.Text;
using Kiitou.Catalog;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Verification;

namespace Kiitou.Tests;

public class ReputationTests
{
    private const string Host = "example.com";
    private const string Address = "93.184.216.34";

    /// <summary>Stands in for a web service: answers by the first rule whose text is in the request URL.</summary>
    private sealed class FakeWeb : HttpMessageHandler
    {
        private readonly List<(string UrlPart, HttpStatusCode Status, string Body)> _rules = [];
        public List<string> Requests { get; } = [];
        public List<string> Keys { get; } = [];

        public FakeWeb On(string urlPart, HttpStatusCode status, string body = "{}")
        {
            _rules.Add((urlPart, status, body));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            foreach (string header in new[] { "Key", "x-apikey" })
                if (request.Headers.TryGetValues(header, out var values)) Keys.AddRange(values);
            foreach (var rule in _rules)
            {
                if (url.Contains(rule.UrlPart))
                    return Task.FromResult(new HttpResponseMessage(rule.Status) { Content = new StringContent(rule.Body, Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string secret) => "enc:" + new string(secret.Reverse().ToArray());

        public string? Unprotect(string protectedSecret) =>
            protectedSecret.StartsWith("enc:") ? new string(protectedSecret[4..].Reverse().ToArray()) : null;
    }

    private sealed class CannedResolver(Func<string, DnsLookupResult> answer) : IDnsResolver
    {
        public Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken) =>
            Task.FromResult(answer(hostname));
    }

    private static DnsLookupResult Found(string host) => new(host, ["93.184.216.34"], [], "test", null);
    private static DnsLookupResult Missing(string host) => new(host, [], [], "test", "does not exist", true);
    private static DnsLookupResult Sinkholed(string host) => new(host, ["0.0.0.0"], [], "test", null);

    private static string Abuse(int score, int reports) =>
        "{\"data\":{\"abuseConfidenceScore\":" + score + ",\"totalReports\":" + reports + "}}";

    private static string Vt(int malicious) =>
        "{\"data\":{\"attributes\":{\"last_analysis_stats\":{\"malicious\":" + malicious + ",\"harmless\":60}}}}";

    private static (AppConfiguration Config, ReputationSettings Settings, int[] Saves) NewSettings()
    {
        AppConfiguration config = AppConfiguration.CreateDefault();
        int[] saves = [0];
        return (config, new ReputationSettings(config, new ReversingProtector(), () => saves[0]++), saves);
    }

    // ---- AbuseIPDB ---------------------------------------------------------------------

    [Theory]
    [InlineData(0, ReputationVerdict.NoKnownProblems)]
    [InlineData(49, ReputationVerdict.NoKnownProblems)]
    [InlineData(50, ReputationVerdict.Flagged)]
    [InlineData(100, ReputationVerdict.Flagged)]
    public async Task AbuseIpDbFlagsFromItsWarningLevel(int score, ReputationVerdict expected)
    {
        var web = new FakeWeb().On("abuseipdb.com", HttpStatusCode.OK, Abuse(score, 7));
        ReputationResult result = await new AbuseIpDbProvider("my-key", web).CheckAsync(Host, Address, CancellationToken.None);

        Assert.Equal(expected, result.Verdict);
        Assert.Contains(score + "%", result.Detail);
        Assert.Contains("ipAddress=" + Address, Assert.Single(web.Requests));
        Assert.Equal("my-key", Assert.Single(web.Keys));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key", KeyTestResult.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, "rejected the API key", KeyTestResult.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, "limit", KeyTestResult.LimitReached)]
    [InlineData(HttpStatusCode.InternalServerError, "could not be reached", KeyTestResult.Unreachable)]
    public async Task FailedRequestsAreNeverReportedAsClean(HttpStatusCode status, string expectedDetail, KeyTestResult expectedTest)
    {
        var web = new FakeWeb().On("abuseipdb.com", status).On("virustotal.com", status);

        foreach (IKeyedReputationProvider provider in new IKeyedReputationProvider[] { new AbuseIpDbProvider("k", web), new VirusTotalProvider("k", web) })
        {
            ReputationResult result = await provider.CheckAsync(Host, Address, CancellationToken.None);
            Assert.Equal(ReputationVerdict.NotChecked, result.Verdict);
            Assert.Contains(expectedDetail, result.Detail);
            Assert.Equal(expectedTest, await provider.TestKeyAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task UnreadableRepliesAreNotChecked()
    {
        var web = new FakeWeb().On("abuseipdb.com", HttpStatusCode.OK, "<html>maintenance</html>").On("virustotal.com", HttpStatusCode.OK, "{\"data\":{}}");

        Assert.Equal(ReputationVerdict.NotChecked, (await new AbuseIpDbProvider("k", web).CheckAsync(Host, Address, CancellationToken.None)).Verdict);
        Assert.Equal(ReputationVerdict.NotChecked, (await new VirusTotalProvider("k", web).CheckAsync(Host, Address, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task WorkingKeysPassTheKeyTest()
    {
        var web = new FakeWeb().On("abuseipdb.com", HttpStatusCode.OK, Abuse(0, 0)).On("virustotal.com", HttpStatusCode.OK, Vt(0));

        Assert.Equal(KeyTestResult.Works, await new AbuseIpDbProvider("k", web).TestKeyAsync(CancellationToken.None));
        Assert.Equal(KeyTestResult.Works, await new VirusTotalProvider("k", web).TestKeyAsync(CancellationToken.None));
    }

    // ---- VirusTotal --------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, ReputationVerdict.NoKnownProblems, "do not flag")]
    [InlineData(1, 1, ReputationVerdict.NoKnownProblems, "do not flag")]
    [InlineData(2, 0, ReputationVerdict.Flagged, "flag this address")]
    [InlineData(0, 5, ReputationVerdict.Flagged, "flag this website")]
    public async Task VirusTotalFlagsAddressOrWebsite(int addressFlags, int websiteFlags, ReputationVerdict expected, string expectedDetail)
    {
        var web = new FakeWeb()
            .On("ip_addresses/", HttpStatusCode.OK, Vt(addressFlags))
            .On("domains/", HttpStatusCode.OK, Vt(websiteFlags));

        ReputationResult result = await new VirusTotalProvider("vt-key", web).CheckAsync(Host, Address, CancellationToken.None);

        Assert.Equal(expected, result.Verdict);
        Assert.Contains(expectedDetail, result.Detail);
        Assert.All(web.Keys, key => Assert.Equal("vt-key", key));
    }

    [Fact]
    public async Task VirusTotalAsksAboutTheWebsiteOnlyOncePerHostname()
    {
        var web = new FakeWeb().On("ip_addresses/", HttpStatusCode.OK, Vt(0)).On("domains/", HttpStatusCode.OK, Vt(0));
        var provider = new VirusTotalProvider("k", web);

        await provider.CheckAsync(Host, Address, CancellationToken.None);
        await provider.CheckAsync(Host, "104.18.26.120", CancellationToken.None);

        Assert.Equal(1, web.Requests.Count(r => r.Contains("domains/")));
        Assert.Equal(2, web.Requests.Count(r => r.Contains("ip_addresses/")));
    }

    // ---- Filtering DNS -----------------------------------------------------------------

    [Fact]
    public async Task CustomFilteringDnsFlagsRefusedOrSinkholedNames()
    {
        var refused = new FilteringDnsProvider("My Filter", new CannedResolver(Missing), new CannedResolver(Found));
        var sinkholed = new FilteringDnsProvider("My Filter", new CannedResolver(Sinkholed), new CannedResolver(Found));
        var clean = new FilteringDnsProvider("My Filter", new CannedResolver(Found), new CannedResolver(Found));

        ReputationResult a = await refused.CheckAsync(Host, Address, CancellationToken.None);
        Assert.Equal(ReputationVerdict.Flagged, a.Verdict);
        Assert.Equal("My Filter", a.Source);
        Assert.Contains("My Filter blocks this hostname", a.Detail);
        Assert.Equal(ReputationVerdict.Flagged, (await sinkholed.CheckAsync(Host, Address, CancellationToken.None)).Verdict);
        Assert.Equal(ReputationVerdict.NoKnownProblems, (await clean.CheckAsync(Host, Address, CancellationToken.None)).Verdict);

        Assert.True(await FilteringDnsProvider.AnswersAsync(new CannedResolver(Found), CancellationToken.None));
        Assert.False(await FilteringDnsProvider.AnswersAsync(
            new CannedResolver(h => new DnsLookupResult(h, [], [], null, "No DNS server answered.")), CancellationToken.None));
    }

    // ---- Settings rules ----------------------------------------------------------------

    [Fact]
    public void DefaultsAreQuad9OnAndNothingElse()
    {
        var (config, settings, _) = NewSettings();

        Assert.True(settings.UseQuad9);
        Assert.Equal(2, settings.KeySlots.Count);
        Assert.All(settings.KeySlots, slot => Assert.True(slot.ServiceId == "" && !slot.Enabled));
        Assert.False(settings.CustomDns.Enabled);
        Assert.Empty(config.Settings.ProtectedKeys);
    }

    [Fact]
    public void AKeyRowNeedsAServiceAndAKeyBeforeItCanBeSwitchedOn()
    {
        var (config, settings, saves) = NewSettings();

        Assert.Throws<CatalogException>(() => settings.SetSlotEnabled(0, true));
        Assert.Throws<CatalogException>(() => settings.SetKey(0, "abc"));
        Assert.Throws<CatalogException>(() => settings.SetSlotService(0, "unknown-service"));

        settings.SetSlotService(0, ReputationCatalog.AbuseIpDb);
        Assert.Throws<CatalogException>(() => settings.SetSlotEnabled(0, true));
        Assert.Throws<CatalogException>(() => settings.SetKey(0, "has a space"));

        settings.SetKey(0, "  secret-key-123  ");
        settings.SetSlotEnabled(0, true);

        Assert.True(settings.KeySlots[0].Enabled);
        Assert.Equal("secret-key-123", settings.GetKey(ReputationCatalog.AbuseIpDb));
        Assert.DoesNotContain("secret-key-123", config.Settings.ProtectedKeys[ReputationCatalog.AbuseIpDb]);
        Assert.Equal(3, saves[0]);
    }

    [Fact]
    public void ARowIsLockedWhileItIsSwitchedOn()
    {
        var (_, settings, _) = NewSettings();
        settings.SetSlotService(0, ReputationCatalog.AbuseIpDb);
        settings.SetKey(0, "key-a");
        settings.SetSlotEnabled(0, true);

        Assert.Throws<CatalogException>(() => settings.SetSlotService(0, ReputationCatalog.VirusTotal));
        Assert.Throws<CatalogException>(() => settings.SetKey(0, "other"));
        Assert.Throws<CatalogException>(() => settings.SetSlotService(1, ReputationCatalog.AbuseIpDb));

        settings.SetSlotEnabled(0, false);
        settings.SetSlotService(0, ReputationCatalog.VirusTotal);
        Assert.Null(settings.GetKey(ReputationCatalog.VirusTotal));

        // Switching back finds the key that was stored for the first service.
        settings.SetSlotService(0, ReputationCatalog.AbuseIpDb);
        Assert.Equal("key-a", settings.GetKey(ReputationCatalog.AbuseIpDb));

        settings.SetKey(0, "");
        Assert.Null(settings.GetKey(ReputationCatalog.AbuseIpDb));
    }

    [Fact]
    public void CustomDnsNeedsAValidServerAndIsLockedWhileOn()
    {
        var (_, settings, _) = NewSettings();

        Assert.Throws<CatalogException>(() => settings.SetCustomDnsEnabled(true));
        Assert.Throws<CatalogException>(() => settings.SetCustomDns("Mine", "dns.example.com"));
        Assert.Throws<CatalogException>(() => settings.SetCustomDns(new string('x', 41), "1.1.1.2"));

        settings.SetCustomDns("  My DNS Filter ", " 1.1.1.2 ");
        settings.SetCustomDnsEnabled(true);

        Assert.Equal("My DNS Filter", settings.CustomDns.Name);
        Assert.Equal("1.1.1.2", settings.CustomDns.Server);
        Assert.Throws<CatalogException>(() => settings.SetCustomDns("Other", "9.9.9.9"));
    }

    [Fact]
    public void WindowsProtectionRoundTripsAndHidesTheKey()
    {
        var protector = new WindowsSecretProtector();
        string stored = protector.Protect("vt-secret-000111");

        Assert.DoesNotContain("vt-secret", stored);
        Assert.Equal("vt-secret-000111", protector.Unprotect(stored));
        Assert.Null(protector.Unprotect("not protected data"));
    }

    [Fact]
    public void KeysAreSavedEncryptedReloadedAndNeverExported()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        var settings = new ReputationSettings(config, new WindowsSecretProtector(), () => store.Save(config));
        settings.SetSlotService(1, ReputationCatalog.VirusTotal);
        settings.SetKey(1, "vt-secret-000111");
        settings.SetSlotEnabled(1, true);
        new WebsiteCatalog(config).AddWebsite(Host);
        store.Save(config);

        Assert.DoesNotContain("vt-secret-000111", File.ReadAllText(dir.File("config.json")));
        AppConfiguration reloaded = store.Load();
        var reloadedSettings = new ReputationSettings(reloaded, new WindowsSecretProtector(), () => { });
        Assert.True(reloadedSettings.KeySlots[1].Enabled);
        Assert.Equal("vt-secret-000111", reloadedSettings.GetKey(ReputationCatalog.VirusTotal));

        string export = ProfileTransfer.ExportProfile(reloaded, reloaded.ActiveProfileId, DateTime.UtcNow);
        Assert.DoesNotContain("virustotal", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProtectedKeys", export);
    }

    [Fact]
    public void DamagedReputationSettingsAreRepairedToSafeValues()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        config.Settings.KeyServices =
        [
            new KeyServiceSlot { ServiceId = "abuseipdb", Enabled = true },
            new KeyServiceSlot { ServiceId = "abuseipdb", Enabled = true },
            new KeyServiceSlot { ServiceId = "made-up", Enabled = true }
        ];
        config.Settings.CustomDns = new CustomDnsSettings { Server = "not an address", Enabled = true };
        store.Save(config);

        AppSettings repaired = store.Load().Settings;

        Assert.Equal(2, repaired.KeyServices.Count);
        Assert.Equal("abuseipdb", repaired.KeyServices[0].ServiceId);
        Assert.False(repaired.KeyServices[0].Enabled);
        Assert.Equal("", repaired.KeyServices[1].ServiceId);
        Assert.False(repaired.KeyServices[1].Enabled);
        Assert.Equal("", repaired.CustomDns.Server);
        Assert.False(repaired.CustomDns.Enabled);
    }

    // ---- Combining ---------------------------------------------------------------------

    [Fact]
    public void AnyFlagWinsOtherwiseOneCleanAnswerIsEnough()
    {
        var clean = new ReputationResult(ReputationVerdict.NoKnownProblems, "A", "A is fine.");
        var flagged = new ReputationResult(ReputationVerdict.Flagged, "B", "B flags it.");
        var unknown = new ReputationResult(ReputationVerdict.NotChecked, "C", "C could not be reached.");

        ReputationResult bad = ReputationHub.Combine([clean, flagged, unknown]);
        Assert.Equal(ReputationVerdict.Flagged, bad.Verdict);
        Assert.Equal("B", bad.Source);

        ReputationResult good = ReputationHub.Combine([unknown, clean]);
        Assert.Equal(ReputationVerdict.NoKnownProblems, good.Verdict);
        Assert.Contains("A is fine.", good.Detail);
        Assert.Contains("C could not be reached.", good.Detail);

        Assert.Equal(ReputationVerdict.NotChecked, ReputationHub.Combine([unknown]).Verdict);
        ReputationResult none = ReputationHub.Combine([]);
        Assert.Equal(ReputationVerdict.NotChecked, none.Verdict);
        Assert.Contains("No reputation check is switched on", none.Detail);
    }

    [Fact]
    public async Task HubAsksOnlyTheServicesThatAreSwitchedOn()
    {
        var (_, settings, _) = NewSettings();
        var web = new FakeWeb().On("abuseipdb.com", HttpStatusCode.OK, Abuse(80, 12));
        var quad9 = new Quad9ReputationProvider(new CannedResolver(Found), new CannedResolver(Found));
        var hub = new ReputationHub(settings, quad9, web, _ => new CannedResolver(Missing), new CannedResolver(Found));

        ReputationResult onlyQuad9 = await hub.CheckAsync(Host, Address, CancellationToken.None);
        Assert.Equal(ReputationVerdict.NoKnownProblems, onlyQuad9.Verdict);
        Assert.Empty(web.Requests);

        settings.SetSlotService(0, ReputationCatalog.AbuseIpDb);
        settings.SetKey(0, "abuse-key");
        settings.SetSlotEnabled(0, true);
        ReputationResult withAbuse = await hub.CheckAsync(Host, Address, CancellationToken.None);
        Assert.Equal(ReputationVerdict.Flagged, withAbuse.Verdict);
        Assert.Equal("AbuseIPDB", withAbuse.Source);
        Assert.Equal("abuse-key", Assert.Single(web.Keys));

        settings.SetSlotEnabled(0, false);
        settings.SetQuad9(false);
        Assert.Equal(ReputationVerdict.NotChecked, (await hub.CheckAsync(Host, Address, CancellationToken.None)).Verdict);

        settings.SetCustomDns("Family Filter", "1.1.1.3");
        settings.SetCustomDnsEnabled(true);
        ReputationResult custom = await hub.CheckAsync(Host, Address, CancellationToken.None);
        Assert.Equal(ReputationVerdict.Flagged, custom.Verdict);
        Assert.Equal("Family Filter", custom.Source);
    }

    [Fact]
    public async Task HubTestsKeysAndDnsServers()
    {
        var (_, settings, _) = NewSettings();
        var web = new FakeWeb().On("abuseipdb.com", HttpStatusCode.Unauthorized).On("virustotal.com", HttpStatusCode.OK, Vt(0));
        var hub = new ReputationHub(settings, null, web, _ => new CannedResolver(Found), new CannedResolver(Found));

        Assert.Equal(KeyTestResult.Rejected, await hub.TestKeyAsync(ReputationCatalog.AbuseIpDb, "bad", CancellationToken.None));
        Assert.Equal(KeyTestResult.Works, await hub.TestKeyAsync(ReputationCatalog.VirusTotal, " good ", CancellationToken.None));
        Assert.Equal(KeyTestResult.Rejected, await hub.TestKeyAsync("made-up", "x", CancellationToken.None));
        Assert.True(await hub.TestCustomDnsAsync("1.1.1.2", CancellationToken.None));
        Assert.False(await hub.TestCustomDnsAsync("not an address", CancellationToken.None));
    }
}

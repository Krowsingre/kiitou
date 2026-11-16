using Kiitou.Catalog;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Verification;

namespace Kiitou.Tests;

public class VerificationTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private const string Host = "example.com";
    private const string Candidate = "93.184.216.34";
    private const string Published = "104.18.26.120";

    private static AddressTestResult Passing(string ip, string thumbprint = "AAAA", int status = 200) => new()
    {
        Hostname = Host, IpAddress = ip, TestedUtc = Now, Connected = true, TlsValid = true, HttpStatus = status,
        CertificateSubject = "CN=example.com", CertificateIssuer = "CN=Test CA", CertificateThumbprint = thumbprint,
        CertificateNotAfterUtc = Now.AddDays(90), ConnectMilliseconds = 12, TotalMilliseconds = 80
    };

    private static DnsLookupResult Dns(params string[] ipv4) => new(Host, ipv4, [], "192.0.2.53", null);

    private static AssessmentInput Input(
        AddressTestResult? test, DnsLookupResult? dns,
        ReferenceComparison reference = ReferenceComparison.NotAvailable,
        ReputationResult? reputation = null, string ip = Candidate) =>
        new(ip, test, dns, reference, "reference detail", reputation ?? NoReputationProvider.NotConfigured);

    // ---- Public routability ------------------------------------------------------------

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("2a00:1450:4001:81b::200e")]
    public void PublicAddressesAreAccepted(string ip) => Assert.True(IpClassifier.IsPublic(ip, out _));

    [Theory]
    [InlineData("0.1.2.3", "this network")]
    [InlineData("10.1.2.3", "private")]
    [InlineData("100.64.0.1", "carrier-grade")]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("169.254.10.10", "link-local")]
    [InlineData("172.16.0.1", "private")]
    [InlineData("172.31.255.255", "private")]
    [InlineData("192.168.1.1", "private")]
    [InlineData("192.0.2.9", "documentation")]
    [InlineData("198.18.0.1", "benchmarking")]
    [InlineData("198.19.255.255", "benchmarking")]
    [InlineData("203.0.113.10", "documentation")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("255.255.255.255", "reserved")]
    [InlineData("::", "unspecified")]
    [InlineData("::1", "loopback")]
    [InlineData("::ffff:10.0.0.1", "private")]
    [InlineData("2001:db8::1", "documentation")]
    [InlineData("fc00::1", "unique local")]
    [InlineData("fd12:3456::1", "unique local")]
    [InlineData("fe80::1", "link-local")]
    [InlineData("ff02::1", "multicast")]
    [InlineData("4000::1", "outside the range")]
    [InlineData("not-an-ip", "not a valid")]
    public void NonPublicAddressesAreExplained(string ip, string expectedReason)
    {
        Assert.False(IpClassifier.IsPublic(ip, out string reason));
        Assert.Contains(expectedReason, reason);
    }

    // ---- The four statuses -------------------------------------------------------------

    [Fact]
    public void UntestedAddressIsNotTested()
    {
        AddressAssessment result = LegitimacyAssessor.Assess(Input(null, null), Now);
        Assert.Equal(VerificationStatus.NotTested, result.Status);
        Assert.Equal("Not Tested", LegitimacyAssessor.StatusText(result.Status));
    }

    [Fact]
    public void WorkingConnectionToADnsPublishedAddressIsVerified()
    {
        AddressAssessment result = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate, Published)), Now);

        Assert.Equal(VerificationStatus.ConnectionVerified, result.Status);
        Assert.Equal("Connection Verified", LegitimacyAssessor.StatusText(result.Status));
        Assert.Contains(result.Reasons, r => r.Contains("published in DNS"));
        Assert.Contains(result.Warnings, w => w.Contains("Reputation was not checked"));
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal(Now, result.AssessedUtc);
    }

    [Fact]
    public void WorkingConnectionMatchingTheReferenceIsVerifiedEvenIfNotPublished()
    {
        AddressAssessment result = LegitimacyAssessor.Assess(
            Input(Passing(Candidate), Dns(Published), ReferenceComparison.Match), Now);

        Assert.Equal(VerificationStatus.ConnectionVerified, result.Status);
        Assert.Contains("reference detail", result.Reasons);
        Assert.Contains(result.Warnings, w => w.Contains("not among those currently published"));
    }

    [Theory]
    [InlineData(ReferenceComparison.NotAvailable)]
    [InlineData(ReferenceComparison.Mismatch)]
    public void WorkingConnectionWithoutAssociationEvidenceIsIncomplete(ReferenceComparison reference)
    {
        AddressAssessment result = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Published), reference), Now);

        Assert.Equal(VerificationStatus.ConnectionWorksVerificationIncomplete, result.Status);
        Assert.Equal("Connection Works, Verification Incomplete", LegitimacyAssessor.StatusText(result.Status));
        Assert.Contains(result.Warnings, w => w.Contains("not evidence of a problem"));
        Assert.Contains("reference detail", result.Warnings);
    }

    [Fact]
    public void FailedDnsLookupLeavesAWorkingConnectionIncomplete()
    {
        var failedDns = new DnsLookupResult(Host, [], [], null, "No DNS server answered.");
        AddressAssessment result = LegitimacyAssessor.Assess(Input(Passing(Candidate), failedDns), Now);

        Assert.Equal(VerificationStatus.ConnectionWorksVerificationIncomplete, result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("DNS could not be checked"));
    }

    [Fact]
    public void ConnectionFailuresAreFailedOrUnsafeWithTheCause()
    {
        var refused = new AddressTestResult { Hostname = Host, IpAddress = Candidate, Error = "The address refused the connection on port 443." };
        var badCertificate = new AddressTestResult
        {
            Hostname = Host, IpAddress = Candidate, Connected = true,
            TlsError = "Certificate problem: the certificate is not issued for this hostname.", Error = "handshake failed"
        };
        var noReply = new AddressTestResult { Hostname = Host, IpAddress = Candidate, Connected = true, TlsValid = true, Error = "The server did not reply in time." };

        AddressAssessment a = LegitimacyAssessor.Assess(Input(refused, Dns(Candidate)), Now);
        AddressAssessment b = LegitimacyAssessor.Assess(Input(badCertificate, Dns(Candidate)), Now);
        AddressAssessment c = LegitimacyAssessor.Assess(Input(noReply, Dns(Candidate)), Now);

        Assert.All(new[] { a, b, c }, x => Assert.Equal(VerificationStatus.FailedOrUnsafe, x.Status));
        Assert.Equal("Failed or Unsafe", LegitimacyAssessor.StatusText(a.Status));
        Assert.Contains(a.Reasons, r => r.Contains("refused the connection"));
        Assert.Contains(b.Reasons, r => r.Contains("not issued for this hostname"));
        Assert.Contains(c.Reasons, r => r.Contains("did not reply in time"));
    }

    [Fact]
    public void PrivateAddressIsJudgedOnItsConnectionWithAWarning()
    {
        AddressAssessment published = LegitimacyAssessor.Assess(
            Input(Passing("192.168.1.10"), Dns("192.168.1.10"), ip: "192.168.1.10"), Now);
        AddressAssessment unpublished = LegitimacyAssessor.Assess(
            Input(Passing("10.0.0.5"), Dns(Published), ip: "10.0.0.5"), Now);
        var down = new AddressTestResult { Hostname = Host, IpAddress = "10.0.0.5", Error = "The connection attempt timed out." };
        AddressAssessment unreachable = LegitimacyAssessor.Assess(Input(down, Dns(Published), ip: "10.0.0.5"), Now);

        Assert.Equal(VerificationStatus.ConnectionVerified, published.Status);
        Assert.Contains(published.Warnings, w => w.Contains("private network address") && w.Contains("only works on your own network"));
        Assert.Equal(VerificationStatus.ConnectionWorksVerificationIncomplete, unpublished.Status);
        Assert.Equal(VerificationStatus.FailedOrUnsafe, unreachable.Status);
    }

    [Theory]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("192.0.2.9", "documentation")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("::1", "loopback")]
    public void UnusableAddressIsFailedOrUnsafeEvenWhenItConnects(string ip, string expected)
    {
        AddressAssessment result = LegitimacyAssessor.Assess(Input(Passing(ip), Dns(ip), ip: ip), Now);

        Assert.Equal(VerificationStatus.FailedOrUnsafe, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("Not a usable website address") && r.Contains(expected));
    }

    [Theory]
    [InlineData("93.184.216.34", AddressScope.Public)]
    [InlineData("10.1.2.3", AddressScope.Private)]
    [InlineData("172.20.0.1", AddressScope.Private)]
    [InlineData("192.168.0.1", AddressScope.Private)]
    [InlineData("100.64.0.1", AddressScope.Private)]
    [InlineData("169.254.1.1", AddressScope.Private)]
    [InlineData("fd00::1", AddressScope.Private)]
    [InlineData("fe80::1", AddressScope.Private)]
    [InlineData("::ffff:192.168.0.1", AddressScope.Private)]
    [InlineData("127.0.0.1", AddressScope.Unusable)]
    [InlineData("0.0.0.0", AddressScope.Unusable)]
    [InlineData("203.0.113.1", AddressScope.Unusable)]
    [InlineData("2001:db8::1", AddressScope.Unusable)]
    [InlineData("ff02::1", AddressScope.Unusable)]
    [InlineData("nonsense", AddressScope.Unusable)]
    public void AddressScopeIsClassified(string ip, AddressScope expected) =>
        Assert.Equal(expected, IpClassifier.Classify(ip, out _));

    [Fact]
    public void FlaggedReputationIsFailedOrUnsafeEvenWhenEverythingElsePasses()
    {
        var flagged = new ReputationResult(ReputationVerdict.Flagged, "Example Feed", "listed as phishing");
        AddressAssessment result = LegitimacyAssessor.Assess(
            Input(Passing(Candidate), Dns(Candidate), reputation: flagged), Now);

        Assert.Equal(VerificationStatus.FailedOrUnsafe, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("Example Feed") && r.Contains("phishing"));
    }

    [Fact]
    public void CleanReputationIsReportedButNeverUpgradesTheStatus()
    {
        var clean = new ReputationResult(ReputationVerdict.NoKnownProblems, "Example Feed", "");
        var detailed = new ReputationResult(ReputationVerdict.NoKnownProblems, "Quad9", "Quad9 does not list this hostname as malicious.");
        Assert.Contains("Quad9 does not list this hostname as malicious.",
            LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate), reputation: detailed), Now).Reasons);
        AddressAssessment result = LegitimacyAssessor.Assess(
            Input(Passing(Candidate), Dns(Published), reputation: clean), Now);

        Assert.Equal(VerificationStatus.ConnectionWorksVerificationIncomplete, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("no known problems"));
    }

    [Fact]
    public void WarningsCoverChallengesServerErrorsRedirectsAndExpiry()
    {
        AddressTestResult test = Passing(Candidate, status: 503) with
        {
            ChallengeSuspected = true,
            RedirectLocation = "https://www.example.com/",
            CertificateNotAfterUtc = Now.AddDays(3)
        };
        AddressAssessment result = LegitimacyAssessor.Assess(Input(test, Dns(Candidate)), Now);

        Assert.Equal(VerificationStatus.ConnectionVerified, result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("browser challenge"));
        Assert.Contains(result.Warnings, w => w.Contains("HTTP 503"));
        Assert.Contains(result.Warnings, w => w.Contains("expires within seven days"));
        Assert.Contains(result.Reasons, r => r.Contains("redirects to https://www.example.com/"));
    }

    [Fact]
    public void NoStatusOrCaveatClaimsSafetyOrAuthenticity()
    {
        foreach (VerificationStatus status in Enum.GetValues<VerificationStatus>())
        {
            string text = LegitimacyAssessor.StatusText(status);
            Assert.DoesNotContain("safe", text.Replace("Unsafe", ""), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authentic", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("legitimate", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("cannot prove", LegitimacyAssessor.Caveat);

        AddressAssessment verified = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate)), Now);
        Assert.DoesNotContain(verified.Reasons.Concat(verified.Warnings),
            line => line.Contains("is safe", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("is authentic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssessmentsGoStaleAfterADay()
    {
        AddressAssessment result = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate)), Now);

        Assert.False(LegitimacyAssessor.IsStale(result, Now.AddHours(23)));
        Assert.True(LegitimacyAssessor.IsStale(result, Now.AddHours(25)));
        Assert.False(LegitimacyAssessor.IsStale(null, Now));
    }

    // ---- Reference comparison ----------------------------------------------------------

    [Fact]
    public void ReferenceComparisonUsesCertificateAndStatus()
    {
        AddressTestResult candidate = Passing(Candidate);

        Assert.Equal(ReferenceComparison.Match, ReferenceComparer.Compare(candidate, Passing(Published), out string match));
        Assert.Contains(Published, match);
        Assert.Equal(ReferenceComparison.Match,
            ReferenceComparer.Compare(candidate, Passing(Published, thumbprint: "BBBB"), out _));
        Assert.Equal(ReferenceComparison.Mismatch,
            ReferenceComparer.Compare(candidate, Passing(Published, thumbprint: "BBBB") with { CertificateSubject = "CN=other.example" }, out string different));
        Assert.Contains("different certificate", different);
        Assert.Equal(ReferenceComparison.Mismatch, ReferenceComparer.Compare(candidate, Passing(Published, status: 404), out _));
        Assert.Equal(ReferenceComparison.NotAvailable, ReferenceComparer.Compare(candidate, null, out _));
        Assert.Equal(ReferenceComparison.NotAvailable,
            ReferenceComparer.Compare(candidate, new AddressTestResult { Hostname = Host, IpAddress = Published }, out _));
    }

    // ---- Quad9 ------------------------------------------------------------------------

    private sealed class CannedResolver(Func<string, DnsLookupResult> answer) : IDnsResolver
    {
        public int Calls { get; private set; }

        public Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer(hostname));
        }
    }

    private static DnsLookupResult Found(string host) => new(host, ["93.184.216.34"], [], "9.9.9.9", null);
    private static DnsLookupResult Missing(string host) => new(host, [], [], "9.9.9.9", host + " does not exist in DNS.", true);
    private static DnsLookupResult Unreachable(string host) => new(host, [], [], null, "No DNS server answered.");

    [Fact]
    public async Task Quad9ResolvingTheHostnameMeansNoKnownProblemsAndIsCached()
    {
        var filtered = new CannedResolver(Found);
        var unfiltered = new CannedResolver(Found);
        var quad9 = new Quad9ReputationProvider(filtered, unfiltered, () => Now);

        ReputationResult first = await quad9.CheckAsync(Host, Candidate, CancellationToken.None);
        ReputationResult second = await quad9.CheckAsync(Host, "104.18.26.120", CancellationToken.None);

        Assert.Equal(ReputationVerdict.NoKnownProblems, first.Verdict);
        Assert.Equal("Quad9", first.Source);
        Assert.Same(first, second);
        Assert.Equal(1, filtered.Calls);
        Assert.Equal(0, unfiltered.Calls);
    }

    [Fact]
    public async Task Quad9BlockingAHostnameThatOtherwiseExistsIsFlagged()
    {
        var quad9 = new Quad9ReputationProvider(new CannedResolver(Missing), new CannedResolver(Found), () => Now);

        ReputationResult result = await quad9.CheckAsync(Host, Candidate, CancellationToken.None);

        Assert.Equal(ReputationVerdict.Flagged, result.Verdict);
        Assert.Contains("blocks this hostname", result.Detail);
        AddressAssessment assessment = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate), reputation: result), Now);
        Assert.Equal(VerificationStatus.FailedOrUnsafe, assessment.Status);
    }

    [Fact]
    public async Task Quad9NeverReportsCleanWhenItCouldNotAnswer()
    {
        var down = new Quad9ReputationProvider(new CannedResolver(Unreachable), new CannedResolver(Found), () => Now);
        var halfDown = new Quad9ReputationProvider(new CannedResolver(Missing), new CannedResolver(Unreachable), () => Now);
        var nowhere = new Quad9ReputationProvider(new CannedResolver(Missing), new CannedResolver(Missing), () => Now);

        Assert.Equal(ReputationVerdict.NotChecked, (await down.CheckAsync(Host, Candidate, CancellationToken.None)).Verdict);
        Assert.Equal(ReputationVerdict.NotChecked, (await halfDown.CheckAsync(Host, Candidate, CancellationToken.None)).Verdict);
        ReputationResult missing = await nowhere.CheckAsync(Host, Candidate, CancellationToken.None);
        Assert.Equal(ReputationVerdict.NotChecked, missing.Verdict);
        Assert.Contains("does not exist", missing.Detail);
    }

    [Fact]
    public async Task LiveQuad9CheckOnBuildRunner()
    {
        if (Environment.GetEnvironmentVariable("KIITOU_CI") != "1") return;
        ReputationResult result = await new Quad9ReputationProvider().CheckAsync("example.com", Candidate, CancellationToken.None);
        // The runner may not be able to reach Quad9; it must never be reported as flagged.
        Assert.NotEqual(ReputationVerdict.Flagged, result.Verdict);
    }

    // ---- Service -----------------------------------------------------------------------

    private sealed class FakeResolver(DnsLookupResult result) : IDnsResolver
    {
        public int Calls { get; private set; }

        public Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeTester(Func<string, AddressTestResult> respond) : IAddressTester
    {
        public List<string> Tested { get; } = [];

        public Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
        {
            Tested.Add(ipAddress);
            return Task.FromResult(respond(ipAddress));
        }
    }

    private static (AppConfiguration Config, WebsiteEntry Site, AddressCandidate Address) SiteWithCandidate()
    {
        AppConfiguration config = AppConfiguration.CreateDefault();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite(Host);
        return (config, site, catalog.AddAddress(site.Id, Candidate));
    }

    [Fact]
    public async Task PublishedCandidateIsVerifiedWithoutAReferenceTest()
    {
        var (config, site, address) = SiteWithCandidate();
        var resolver = new FakeResolver(Dns(Candidate, Published));
        var tester = new FakeTester(ip => Passing(ip));
        var service = new VerificationService(resolver, tester, utcNow: () => Now);

        AddressAssessment result = await service.VerifyAsync(site, address, null, CancellationToken.None);

        Assert.Equal(VerificationStatus.ConnectionVerified, result.Status);
        Assert.Equal(new[] { Candidate }, tester.Tested);
        Assert.Equal(1, resolver.Calls);
        Assert.Same(result, address.LastAssessment);
        Assert.Empty(config.Profiles[0].Selections);
    }

    [Fact]
    public async Task UnpublishedCandidateIsComparedWithAPublishedAddress()
    {
        var (_, site, address) = SiteWithCandidate();
        var tester = new FakeTester(ip => Passing(ip));
        var service = new VerificationService(new FakeResolver(Dns(Published)), tester, utcNow: () => Now);

        AddressAssessment result = await service.VerifyAsync(site, address, null, CancellationToken.None);

        Assert.Equal(VerificationStatus.ConnectionVerified, result.Status);
        Assert.Equal(new[] { Candidate, Published }, tester.Tested);
        Assert.Contains(result.Reasons, r => r.Contains("same certificate"));
    }

    [Fact]
    public async Task UnpublishedCandidateWithADifferentCertificateIsIncomplete()
    {
        var (_, site, address) = SiteWithCandidate();
        var tester = new FakeTester(ip => ip == Candidate
            ? Passing(ip, "AAAA") with { CertificateSubject = "CN=cdn.example" }
            : Passing(ip, "BBBB"));
        var service = new VerificationService(new FakeResolver(Dns(Published)), tester, utcNow: () => Now);

        AddressAssessment result = await service.VerifyAsync(site, address, null, CancellationToken.None);

        Assert.Equal(VerificationStatus.ConnectionWorksVerificationIncomplete, result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("different certificate"));
    }

    [Fact]
    public async Task SuppliedDnsResultIsReusedAndFailuresNeedNoReference()
    {
        var (_, site, address) = SiteWithCandidate();
        var resolver = new FakeResolver(Dns(Published));
        var tester = new FakeTester(ip => new AddressTestResult { Hostname = Host, IpAddress = ip, Error = "The connection attempt timed out." });
        var service = new VerificationService(resolver, tester, utcNow: () => Now);

        AddressAssessment result = await service.VerifyAsync(site, address, Dns(Published), CancellationToken.None);

        Assert.Equal(VerificationStatus.FailedOrUnsafe, result.Status);
        Assert.Equal(0, resolver.Calls);
        Assert.Single(tester.Tested);
    }

    // ---- Persistence -------------------------------------------------------------------

    [Fact]
    public void AssessmentIsStoredWithTheAddressAndClearedWhenTheAddressChanges()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite(Host);
        AddressCandidate address = catalog.AddAddress(site.Id, Candidate);
        address.LastAssessment = LegitimacyAssessor.Assess(Input(Passing(Candidate), Dns(Candidate)), Now);
        store.Save(config);

        Assert.Contains("\"ConnectionVerified\"", File.ReadAllText(dir.File("config.json")));
        AppConfiguration reloaded = store.Load();
        AddressAssessment stored = reloaded.Websites[0].Addresses[0].LastAssessment!;
        Assert.Equal(VerificationStatus.ConnectionVerified, stored.Status);
        Assert.Equal(Now, stored.AssessedUtc);
        Assert.NotEmpty(stored.Reasons);

        var reloadedCatalog = new WebsiteCatalog(reloaded);
        reloadedCatalog.EditAddress(reloaded.Websites[0].Id, reloaded.Websites[0].Addresses[0].Id, Candidate);
        Assert.NotNull(reloaded.Websites[0].Addresses[0].LastAssessment);
        reloadedCatalog.EditAddress(reloaded.Websites[0].Id, reloaded.Websites[0].Addresses[0].Id, "93.184.216.35");
        Assert.Null(reloaded.Websites[0].Addresses[0].LastAssessment);
    }
}

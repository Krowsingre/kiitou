using System.Text;
using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Session;

namespace Kiitou.Tests;

public class SessionTests
{
    private const string OriginalHosts = "# sample hosts\r\n127.0.0.1 localhost\r\n";
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeResolver : IDnsResolver
    {
        public string[] Published { get; set; } = ["93.184.216.34"];

        public Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken) =>
            Task.FromResult(new DnsLookupResult(hostname, Published, [], "192.0.2.53", null));
    }

    private sealed class FakeTester : IAddressTester
    {
        public HashSet<string> Working { get; } = [];
        public List<string> Tested { get; } = [];

        public Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
        {
            Tested.Add(ipAddress);
            bool ok = Working.Contains(ipAddress);
            return Task.FromResult(new AddressTestResult
            {
                Hostname = hostname, IpAddress = ipAddress, TestedUtc = Now, Connected = ok, TlsValid = ok,
                HttpStatus = ok ? 200 : null, CertificateSubject = ok ? "CN=" + hostname : null,
                CertificateThumbprint = ok ? "AAAA" : null, Error = ok ? null : "The connection attempt timed out."
            });
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Dir { get; } = new();
        public FakeResolver Resolver { get; } = new();
        public FakeTester Tester { get; } = new();
        public KiitouSession Session { get; }
        public string HostsPath => Dir.File("hosts");
        public string HostsText => Encoding.Latin1.GetString(File.ReadAllBytes(HostsPath));

        public Fixture()
        {
            File.WriteAllText(HostsPath, OriginalHosts);
            Session = Open();
        }

        /// <summary>Opens a session over the same data folder, as a fresh launch would.</summary>
        public KiitouSession Open()
        {
            var store = new ConfigurationStore(new StoragePaths(Dir.Path, Dir.Path, true));
            var hostsFile = new HostsFileService(HostsPath);
            var hosts = new HostsApplyService(
                hostsFile, new HostsBackupStore(Path.Combine(Dir.Path, "backups")),
                new DirectHostsWriter(hostsFile, () => true), () => Now);
            return new KiitouSession(store, store.Load(), hosts, Resolver, Tester, null, () => Now);
        }

        public void Dispose() => Dir.Dispose();
    }

    private static (WebsiteEntry Site, AddressCandidate First, AddressCandidate Second) AddExample(KiitouSession session)
    {
        WebsiteEntry site = session.AddWebsite("example.com");
        return (site, session.AddAddress(site.Id, "93.184.216.34"), session.AddAddress(site.Id, "151.101.1.69"));
    }

    [Fact]
    public void WorkingChoicesAreUnsavedUntilSavedAndSurviveRelaunchOnlyWhenSaved()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);

        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        Assert.True(fx.Session.HasUnsavedChanges);
        Assert.Empty(fx.Session.Profiles.Active.Selections);

        // Websites are saved immediately; the unsaved choices are not.
        KiitouSession relaunched = fx.Open();
        Assert.Single(relaunched.Catalog.Websites);
        Assert.False(relaunched.Working.View(site.Id).Enabled);

        fx.Session.SaveToCurrentProfile();
        Assert.False(fx.Session.HasUnsavedChanges);
        Assert.True(fx.Open().Working.View(site.Id).Enabled);
    }

    [Fact]
    public void ApplyWritesTheSavedProfileNotTheUnsavedChoices()
    {
        using var fx = new Fixture();
        var (site, first, second) = AddExample(fx.Session);
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, second.Id);

        HostsOperationResult result = fx.Session.Apply(fx.Session.PreviewApply());

        Assert.True(result.Succeeded);
        Assert.Contains("93.184.216.34 example.com # Kiitou", fx.HostsText);
        Assert.DoesNotContain("151.101.1.69", fx.HostsText);
        Assert.StartsWith(OriginalHosts, fx.HostsText);

        HostsStatus status = fx.Session.GetHostsStatus();
        Assert.Equal(1, status.ManagedMappingCount);
        Assert.True(status.MatchesSelectedProfile);
        Assert.False(status.ExternalEditDetected);
        Assert.Equal(fx.Session.Config.ActiveProfileId, fx.Session.Config.Hosts.LastAppliedProfileId);
        Assert.Equal(Now, fx.Session.Config.Hosts.LastAppliedUtc);

        // The applied state is remembered across a relaunch.
        Assert.True(fx.Open().GetHostsStatus().MatchesSelectedProfile);
    }

    [Fact]
    public void TheHeaderStateFollowsApplyEditAndDisable()
    {
        using var fx = new Fixture();
        var (site, first, second) = AddExample(fx.Session);
        Assert.Equal(ApplyState.NothingApplied, fx.Session.GetHostsStatus().State);
        Assert.False(fx.Session.GetHostsStatus().ProfileLocked);

        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Apply(fx.Session.PreviewApply());
        Assert.Equal(ApplyState.Active, fx.Session.GetHostsStatus().State);
        Assert.True(fx.Session.GetHostsStatus().ProfileLocked);

        // Editing the applied profile: the change is pending until it is applied in one write.
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, second.Id);
        Assert.Equal(ApplyState.ChangesPending, fx.Session.GetHostsStatus().State);
        Assert.True(fx.Session.GetHostsStatus().ProfileLocked);
        fx.Session.SaveToCurrentProfile();
        Assert.Equal(ApplyState.ChangesPending, fx.Session.GetHostsStatus().State);
        Assert.True(fx.Session.Apply(fx.Session.PreviewApply()).Succeeded);
        Assert.Contains("151.101.1.69 example.com # Kiitou", fx.HostsText);
        Assert.DoesNotContain("93.184.216.34", fx.HostsText);
        Assert.Equal(ApplyState.Active, fx.Session.GetHostsStatus().State);

        // Disabling removes the mappings and unlocks the selection.
        Assert.True(fx.Session.RemoveAllMappings().Succeeded);
        Assert.Equal(OriginalHosts, fx.HostsText);
        Assert.Equal(ApplyState.NothingApplied, fx.Session.GetHostsStatus().State);
        Assert.True(fx.Session.Working.View(site.Id).Enabled);
    }

    [Fact]
    public void TheSelectionIsLockedToTheAppliedProfile()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);
        MappingProfile other = fx.Session.CreateProfile("Other");
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Apply(fx.Session.PreviewApply());
        Guid applied = fx.Session.Config.ActiveProfileId;

        Assert.Throws<CatalogException>(() => fx.Session.SwitchProfile(other.Id));
        Assert.Throws<CatalogException>(() => fx.Session.DeleteProfile(applied));
        Assert.Throws<CatalogException>(() => fx.Session.SaveAsNewProfile("Copy"));
        Assert.Equal(applied, fx.Session.Config.ActiveProfileId);
        fx.Session.DeleteProfile(other.Id);

        fx.Session.RemoveAllMappings();
        MappingProfile second = fx.Session.CreateProfile("Second");
        fx.Session.SwitchProfile(second.Id);
        Assert.Equal(second.Id, fx.Session.Config.ActiveProfileId);
    }

    [Fact]
    public void ARelaunchSelectsTheAppliedProfileAndUnknownMappingsAreRecognised()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Apply(fx.Session.PreviewApply());
        Guid applied = fx.Session.Config.ActiveProfileId;

        // A settings file that points at another profile while one is applied is corrected on launch.
        MappingProfile stray = fx.Session.Profiles.Create("Stray");
        fx.Session.Config.ActiveProfileId = stray.Id;
        fx.Session.Save();
        KeiSessionCheck(fx.Open(), applied, ApplyState.Active);

        // Mappings whose profile is no longer recorded can only be cleared.
        fx.Session.Config.ActiveProfileId = applied;
        fx.Session.Config.Hosts.LastAppliedProfileId = null;
        fx.Session.Save();
        KiitouSession unknown = fx.Open();
        Assert.Equal(ApplyState.UnknownMappings, unknown.GetHostsStatus().State);
        Assert.False(unknown.GetHostsStatus().ProfileLocked);
        Assert.True(unknown.RemoveAllMappings().Succeeded);
        Assert.Equal(ApplyState.NothingApplied, unknown.GetHostsStatus().State);
    }

    private static void KeiSessionCheck(KiitouSession session, Guid expectedProfile, ApplyState expectedState)
    {
        Assert.Equal(expectedProfile, session.Config.ActiveProfileId);
        Assert.Equal(expectedState, session.GetHostsStatus().State);
    }

    [Fact]
    public void SwitchingDiscardsUnsavedChoices()
    {
        using var fx = new Fixture();
        var (site, _, _) = AddExample(fx.Session);
        MappingProfile other = fx.Session.CreateProfile("Other");
        fx.Session.Working.SetEnabled(site.Id, true);

        fx.Session.SwitchProfile(other.Id);
        fx.Session.SwitchProfile(fx.Session.Profiles.Profiles[0].Id);

        Assert.False(fx.Session.Working.View(site.Id).Enabled);
        Assert.False(fx.Session.HasUnsavedChanges);
    }

    [Fact]
    public void RemoveAllMappingsAndRestoreLeaveSavedDataAlone()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Apply(fx.Session.PreviewApply());
        string applied = fx.HostsText;

        Assert.True(fx.Session.RemoveAllMappings().Succeeded);
        Assert.Equal(OriginalHosts, fx.HostsText);
        Assert.Null(fx.Session.Config.Hosts.LastAppliedProfileId);
        Assert.True(fx.Session.Working.View(site.Id).Enabled);
        Assert.False(fx.Session.GetHostsStatus().ExternalEditDetected);

        HostsBackup beforeRemoval = fx.Session.Backups.List().First(b => !b.IsOriginal);
        Assert.True(fx.Session.Restore(beforeRemoval.Path).Succeeded);
        Assert.Equal(applied, fx.HostsText);
        Assert.True(fx.Session.GetHostsStatus().MatchesSelectedProfile);
        Assert.Equal(ApplyState.Active, fx.Session.GetHostsStatus().State);
    }

    [Fact]
    public void OutsideEditIsReportedAndConflictsAreListed()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        fx.Session.SaveToCurrentProfile();
        fx.Session.Apply(fx.Session.PreviewApply());

        File.AppendAllText(fx.HostsPath, "203.0.113.5 example.com\r\n");
        HostsStatus status = fx.Session.GetHostsStatus();

        Assert.True(status.ExternalEditDetected);
        Assert.Single(status.Conflicts);
        Assert.True(status.MatchesSelectedProfile);
    }

    [Fact]
    public void RemovingAWebsiteCleansUnsavedChoicesToo()
    {
        using var fx = new Fixture();
        var (site, first, _) = AddExample(fx.Session);
        fx.Session.Working.SetEnabled(site.Id, true);
        fx.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);

        fx.Session.RemoveAddress(site.Id, first.Id);
        Assert.Null(fx.Session.Working.View(site.Id).SelectedIpv4Id);

        fx.Session.RemoveWebsite(site.Id);
        Assert.Empty(fx.Session.Working.Selections);
        Assert.Empty(fx.Open().Catalog.Websites);
    }

    [Fact]
    public async Task DiscoveryAndTestingNeverEnableSelectOrApply()
    {
        using var fx = new Fixture();
        var (site, first, second) = AddExample(fx.Session);
        fx.Resolver.Published = ["93.184.216.34", "104.18.26.120"];
        fx.Tester.Working.Add("151.101.1.69");
        fx.Tester.Working.Add("104.18.26.120");

        DiscoveryResult discovery = await fx.Session.DiscoverAsync(site.Id, CancellationToken.None);
        Assert.Equal(new[] { "104.18.26.120" }, discovery.NewAddresses);
        Assert.Equal(1, fx.Session.AddDiscovered(site.Id, discovery.NewAddresses));
        Assert.Equal(3, site.Addresses.Count);

        AddressCandidate? found = await fx.Session.FindFirstWorkingAsync(site.Id, null, CancellationToken.None);

        Assert.Equal(second.Id, found!.Id);
        Assert.Equal(VerificationStatus.FailedOrUnsafe, first.LastAssessment!.Status);
        Assert.NotNull(second.LastAssessment);
        Assert.Null(site.Addresses[2].LastAssessment);
        Assert.Empty(fx.Session.Working.Selections);
        Assert.False(fx.Session.HasUnsavedChanges);
        Assert.Equal(OriginalHosts, fx.HostsText);

        await fx.Session.VerifyAllAsync(site.Id, null, CancellationToken.None);
        Assert.All(site.Addresses, a => Assert.NotNull(a.LastAssessment));
        Assert.Equal(VerificationStatus.ConnectionVerified, site.Addresses[2].LastAssessment!.Status);

        // Results are stored and reloaded.
        Assert.NotNull(fx.Open().Catalog.Websites[0].Addresses[0].LastAssessment);
        Assert.Equal(OriginalHosts, fx.HostsText);
    }

    [Fact]
    public void ThemeAndTrayChoicesPersistAndInvalidThemesAreRefused()
    {
        using var fx = new Fixture();
        fx.Session.SetTheme("Teal", "LIGHT");
        fx.Session.SetMinimizeToTray(true);

        KiitouSession relaunched = fx.Open();
        Assert.Equal("teal", relaunched.Config.Theme.Color);
        Assert.Equal("light", relaunched.Config.Theme.Mode);
        Assert.True(relaunched.Config.Settings.MinimizeToTray);
        Assert.True(relaunched.Config.Settings.UseQuad9);
        fx.Session.SetUseQuad9(false);
        Assert.False(fx.Open().Config.Settings.UseQuad9);

        Assert.True(relaunched.Config.Settings.CheckUpdates);
        Assert.False(relaunched.Config.Settings.AutoInstallUpdates);
        fx.Session.SetUpdateOptions(true, true);
        Assert.True(fx.Open().Config.Settings.AutoInstallUpdates);
        fx.Session.SetUpdateOptions(false, true);
        KiitouSession off = fx.Open();
        Assert.False(off.Config.Settings.CheckUpdates);
        Assert.False(off.Config.Settings.AutoInstallUpdates);
        Assert.Throws<CatalogException>(() => fx.Session.SetTheme("pink", "dark"));
    }

    [Fact]
    public void EveryColorFamilyAndModeIsAccepted()
    {
        using var fx = new Fixture();
        int combinations = 0;
        foreach (string color in ThemeCatalog.Colors)
        {
            foreach (string mode in ThemeCatalog.Modes)
            {
                fx.Session.SetTheme(color, mode);
                Assert.Equal($"kiitou_{color}.ico", ThemeCatalog.IconFileName(fx.Session.Config.Theme.Color));
                combinations++;
            }
        }
        Assert.Equal(21, combinations);
    }

    [Fact]
    public void ImportThroughTheSessionIsSavedAndLeavesHostsAlone()
    {
        using var source = new Fixture();
        var (site, first, _) = AddExample(source.Session);
        source.Session.Working.SetEnabled(site.Id, true);
        source.Session.Working.SelectAddress(site.Id, IpFamily.IPv4, first.Id);
        MappingProfile gaming = source.Session.SaveAsNewProfile("Gaming");
        string profileJson = source.Session.ExportProfile(gaming.Id);

        using var target = new Fixture();
        ImportSummary summary = target.Session.ImportProfile(profileJson);

        Assert.Equal(1, summary.WebsitesAdded);
        Assert.Equal(2, summary.AddressesAdded);
        KiitouSession relaunched = target.Open();
        Assert.Equal(new[] { "Default", "Gaming" }, relaunched.Profiles.Profiles.Select(p => p.Name).ToArray());
        Assert.Single(relaunched.Profiles.BuildMappings(summary.Profile!.Id));
        Assert.Equal(OriginalHosts, target.HostsText);
    }
}

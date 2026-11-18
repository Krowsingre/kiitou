using System.Text;
using System.Text.Json.Nodes;
using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Updates;

namespace Kiitou.Tests;

/// <summary>User data surviving updates: older settings formats, safety copies, and test history.</summary>
public class DataSafetyTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A settings file exactly as Kiitou 0.9.1 (format 1) wrote it.</summary>
    private const string Version1Settings = """
        {
          "SchemaVersion": 1,
          "Theme": { "Color": "teal", "Mode": "light" },
          "Websites": [
            { "Id": "aaaaaaaa-0000-0000-0000-000000000001", "Hostname": "example.com",
              "Addresses": [ { "Id": "bbbbbbbb-0000-0000-0000-000000000001", "IpAddress": "93.184.216.34",
                "LastAssessment": { "Status": "ConnectionVerified", "Reasons": ["ok"], "Warnings": [], "AssessedUtc": "2026-11-17T10:00:00Z" } } ] }
          ],
          "Profiles": [
            { "Id": "cccccccc-0000-0000-0000-000000000001", "Name": "Gaming", "LastSavedUtc": "2026-11-17T09:00:00Z",
              "Selections": [ { "WebsiteId": "aaaaaaaa-0000-0000-0000-000000000001", "Enabled": true,
                                "SelectedIpv4Id": "bbbbbbbb-0000-0000-0000-000000000001" } ] }
          ],
          "ActiveProfileId": "cccccccc-0000-0000-0000-000000000001",
          "Hosts": { "LastWrittenSha256": "ABC", "LastAppliedUtc": "2026-11-17T09:30:00Z",
                     "LastAppliedProfileId": "cccccccc-0000-0000-0000-000000000001" },
          "Settings": { "MinimizeToTray": true, "UseQuad9": false, "CheckUpdates": true, "AutoInstallUpdates": true,
                        "KeyServices": [ { "ServiceId": "", "Enabled": false }, { "ServiceId": "", "Enabled": false } ],
                        "ProtectedKeys": {}, "CustomDns": { "Name": "Mine", "Server": "1.1.1.2", "Enabled": false } }
        }
        """;

    private static ConfigurationStore StoreIn(TempDirectory dir) => new(new StoragePaths(dir.Path, dir.Path, false));

    [Fact]
    public void Format1SettingsAreConvertedWithNothingLost()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("config.json"), Version1Settings);

        AppConfiguration config = StoreIn(dir).Load();

        Assert.Equal(AppConfiguration.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal("teal", config.Theme.Color);
        Assert.Equal("light", config.Theme.Mode);
        Assert.Equal("example.com", Assert.Single(config.Websites).Hostname);
        Assert.Equal(VerificationStatus.ConnectionVerified, config.Websites[0].Addresses[0].LastAssessment!.Status);
        Assert.Equal("Gaming", Assert.Single(config.Profiles).Name);
        Assert.True(config.Profiles[0].Selections[0].Enabled);
        Assert.Equal("ABC", config.Hosts.LastWrittenSha256);
        Assert.True(config.Settings.MinimizeToTray);
        Assert.False(config.Settings.UseQuad9);
        Assert.True(config.Settings.AutoInstallUpdates);
        Assert.Equal("1.1.1.2", config.Settings.CustomDns.Server);
        // New settings take their defaults.
        Assert.Equal(Navigation.DefaultOrder, config.Settings.NavOrder);
        Assert.False(config.Settings.Proxy.Enabled);

        // The converted file is saved, and the original is kept as a safety copy.
        Assert.Equal(AppConfiguration.CurrentSchemaVersion,
            JsonNode.Parse(File.ReadAllText(dir.File("config.json")))!["SchemaVersion"]!.GetValue<int>());
        string copy = Assert.Single(StoreIn(dir).SafetyCopies);
        Assert.Contains("schema1", Path.GetFileName(copy));
        Assert.Equal(Version1Settings, File.ReadAllText(copy));
    }

    [Fact]
    public void SettingsFromBeforeVersioningCountAsFormat1()
    {
        using var dir = new TempDirectory();
        JsonObject root = JsonNode.Parse(Version1Settings)!.AsObject();
        root.Remove("SchemaVersion");
        File.WriteAllText(dir.File("config.json"), root.ToJsonString());

        Assert.Equal(AppConfiguration.CurrentSchemaVersion, StoreIn(dir).Load().SchemaVersion);
    }

    [Fact]
    public void SettingsFromANewerVersionAreRefusedAndLeftAlone()
    {
        using var dir = new TempDirectory();
        string newer = Version1Settings.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 7");
        File.WriteAllText(dir.File("config.json"), newer);

        var ex = Assert.Throws<InvalidDataException>(() => StoreIn(dir).Load());

        Assert.Contains("newer version of Kiitou", ex.Message);
        Assert.Equal(newer, File.ReadAllText(dir.File("config.json")));
        Assert.Empty(StoreIn(dir).SafetyCopies);
    }

    [Fact]
    public void ABrokenFormat1FileIsNotOverwrittenWhenConversionFails()
    {
        using var dir = new TempDirectory();
        const string broken = "{\"SchemaVersion\":1,\"Profiles\":[]}";
        File.WriteAllText(dir.File("config.json"), broken);

        Assert.Throws<InvalidDataException>(() => StoreIn(dir).Load());
        Assert.Equal(broken, File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void SafetyCopiesKeepTheNewestTen()
    {
        using var dir = new TempDirectory();
        ConfigurationStore store = StoreIn(dir);
        Assert.Null(store.CreateSafetyCopy("before-0.9.2"));
        store.Load();

        for (int i = 0; i < 13; i++)
        {
            store.CreateSafetyCopy($"before-0.9.{i}");
            Thread.Sleep(3);
        }

        IReadOnlyList<string> copies = store.SafetyCopies;
        Assert.Equal(ConfigurationStore.SafetyCopiesKept, copies.Count);
        Assert.EndsWith("before-0.9.12.json", copies[0]);
        Assert.All(copies, c => Assert.Equal(File.ReadAllText(dir.File("config.json")), File.ReadAllText(c)));
    }

    [Fact]
    public void UpdateLeftoversAreCleanedUp()
    {
        using var dir = new TempDirectory();
        string folder = Path.Combine(dir.Path, "Kiitou-update");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Kiitou-Setup-0.9.2.exe"), "x");
        File.WriteAllText(Path.Combine(folder, "Kiitou-updater.exe"), "y");
        var updates = new UpdateService(new Version(0, 9, 2), downloadFolder: folder);

        Assert.Equal(2, updates.CleanUpDownloads());
        Assert.False(Directory.Exists(folder));
        Assert.Equal(0, updates.CleanUpDownloads());
    }

    [Fact]
    public void ASetupFileOnDiskIsCheckedAgainstTheUpdateKey()
    {
        using var dir = new TempDirectory();
        string setup = dir.File("setup.exe");
        File.WriteAllText(setup, "not signed");
        var updates = new UpdateService(new Version(0, 9, 2), downloadFolder: dir.Path);

        Assert.Throws<UpdateException>(() => updates.VerifyFile(setup, Convert.ToBase64String(new byte[64])));
        Assert.Throws<UpdateException>(() => updates.VerifyFile(setup, ""));
        Assert.Throws<UpdateException>(() => updates.VerifyFile(dir.File("missing.exe"), "AAAA"));
    }

    [Fact]
    public void InstallerLogsWithScriptErrorsAreRecognised()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("good.log"), "Setup completed.\r\nInstallation process succeeded.\r\n");
        File.WriteAllText(dir.File("bad.log"), "Exception message:\r\nInternal error: Cannot call \"UninstallSilent\" function during Setup.\r\n");

        Assert.False(UpdateService.LogHasErrors(dir.File("good.log")));
        Assert.True(UpdateService.LogHasErrors(dir.File("bad.log")));
        Assert.False(UpdateService.LogHasErrors(dir.File("missing.log")));
    }

    // ---- Where tests come from ---------------------------------------------------------

    private sealed class FakeLocation : ILocationService
    {
        public int Calls { get; private set; }
        public List<Socks5Proxy?> Proxies { get; } = [];
        public bool Fail { get; set; }

        public Task<PublicLocation> GetAsync(Socks5Proxy? proxy, CancellationToken cancellationToken)
        {
            Calls++;
            Proxies.Add(proxy);
            if (Fail) throw new LocationException("Cloudflare could not be reached.");
            return Task.FromResult(proxy is null
                ? new PublicLocation("198.51.100.7", "US", "United States", false, Now)
                : new PublicLocation("185.220.101.1", "DE", "Germany", true, Now));
        }
    }

    private sealed class PassingTester : IAddressTester
    {
        public Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken) =>
            Task.FromResult(new AddressTestResult
            {
                Hostname = hostname, IpAddress = ipAddress, TestedUtc = Now, Connected = true, TlsValid = true, HttpStatus = 200,
                CertificateSubject = "CN=" + hostname, CertificateThumbprint = "AA"
            });
    }

    private sealed class FixedDns : IDnsResolver
    {
        public Task<DnsLookupResult> ResolveAsync(string hostname, CancellationToken cancellationToken) =>
            Task.FromResult(new DnsLookupResult(hostname, ["93.184.216.34"], [], "test", null));
    }

    [Fact]
    public async Task EachTestRecordsAndLogsWhereItCameFrom()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("hosts"), "127.0.0.1 localhost\r\n");
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, false));
        var hostsFile = new HostsFileService(dir.File("hosts"));
        var hosts = new HostsApplyService(hostsFile, new HostsBackupStore(Path.Combine(dir.Path, "backups")), new DirectHostsWriter(hostsFile, () => true));
        var location = new FakeLocation();
        var log = new TestLogStore(dir.Path);
        Socks5Proxy? proxy = null;
        var session = new KiitouSession(store, store.Load(), hosts, new FixedDns(), new PassingTester(), null, () => Now,
            location, log, () => proxy);
        WebsiteEntry site = session.AddWebsite("example.com");
        AddressCandidate address = session.AddAddress(site.Id, "93.184.216.34");

        await session.VerifyAddressAsync(site.Id, address.Id, null, CancellationToken.None);
        await session.VerifyAddressAsync(site.Id, address.Id, null, CancellationToken.None);
        Assert.Equal(1, location.Calls);
        Assert.Equal("198.51.100.7", address.LastAssessment!.TestedFromIp);
        Assert.Equal("United States", address.LastAssessment.TestedFromCountry);
        Assert.False(address.LastAssessment.ThroughProxy);

        // Switching to a proxy looks the location up again, through the proxy.
        proxy = new Socks5Proxy("127.0.0.1", 9150);
        await session.VerifyAddressAsync(site.Id, address.Id, null, CancellationToken.None);
        Assert.Equal(2, location.Calls);
        Assert.Equal(proxy, location.Proxies[^1]);
        Assert.Equal("Germany", address.LastAssessment!.TestedFromCountry);
        Assert.True(address.LastAssessment.ThroughProxy);

        // When the location cannot be found, the test still runs and the log says so.
        location.Fail = true;
        proxy = new Socks5Proxy("127.0.0.1", 1080);
        await session.VerifyAddressAsync(site.Id, address.Id, null, CancellationToken.None);
        Assert.Null(address.LastAssessment!.TestedFromIp);
        await Assert.ThrowsAsync<LocationException>(() => session.GetOriginAsync(CancellationToken.None, refresh: true));

        IReadOnlyList<TestLogEntry> entries = new TestLogStore(dir.Path).Entries;
        Assert.Equal(4, entries.Count);
        Assert.Equal(2, entries.Count(e => e.FromCountry == "United States" && !e.ThroughProxy));
        Assert.Single(entries, e => e.FromCountry == "Germany" && e.ThroughProxy);
        Assert.Single(entries, e => e.FromIp == "" && e.ThroughProxy);
        Assert.All(entries, e => Assert.Equal(VerificationStatus.ConnectionVerified, e.Status));
    }

    [Fact]
    public void MenuOrderAndWindowPositionPersist()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("hosts"), "");
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, false));
        var hostsFile = new HostsFileService(dir.File("hosts"));
        var hosts = new HostsApplyService(hostsFile, new HostsBackupStore(Path.Combine(dir.Path, "backups")), new DirectHostsWriter(hostsFile, () => true));
        var session = new KiitouSession(store, store.Load(), hosts, new FixedDns(), new PassingTester());

        session.SetNavOrder(new[] { "settings", "home" });
        session.SetWindowPosition(120.5, 64);

        AppConfiguration reloaded = store.Load();
        Assert.Equal(new[] { "settings", "home", "websites", "discovery", "profiles", "network", "hosts" }, reloaded.Settings.NavOrder);
        Assert.Equal(120.5, reloaded.Settings.WindowLeft);
        Assert.Equal(64, reloaded.Settings.WindowTop);
    }
}

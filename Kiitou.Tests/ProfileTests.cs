using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Services;

namespace Kiitou.Tests;

public class ProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Sample
    {
        public AppConfiguration Config { get; } = AppConfiguration.CreateDefault();
        public WebsiteCatalog Catalog { get; }
        public ProfileManager Profiles { get; }
        public WebsiteEntry Example { get; }
        public AddressCandidate ExampleV4 { get; }
        public AddressCandidate ExampleV4Alt { get; }
        public AddressCandidate ExampleV6 { get; }
        public WebsiteEntry Media { get; }
        public AddressCandidate MediaV4 { get; }
        public MappingProfile Default => Config.Profiles[0];

        public Sample()
        {
            Catalog = new WebsiteCatalog(Config);
            Profiles = new ProfileManager(Config, () => Now);
            Example = Catalog.AddWebsite("example.com");
            ExampleV4 = Catalog.AddAddress(Example.Id, "93.184.216.34");
            ExampleV4Alt = Catalog.AddAddress(Example.Id, "198.51.100.20");
            ExampleV6 = Catalog.AddAddress(Example.Id, "2606:2800:220:1:248:1893:25c8:1946");
            Media = Catalog.AddWebsite("media.example.com");
            MediaV4 = Catalog.AddAddress(Media.Id, "203.0.113.10");
        }

        /// <summary>A working copy with example.com enabled on its alternate IPv4 and IPv6 addresses.</summary>
        public SelectionEditor WorkingGaming()
        {
            var editor = new SelectionEditor(Config, []);
            editor.SetEnabled(Example.Id, true);
            editor.SelectAddress(Example.Id, IpFamily.IPv4, ExampleV4Alt.Id);
            editor.SelectAddress(Example.Id, IpFamily.IPv6, ExampleV6.Id);
            editor.SelectAddress(Media.Id, IpFamily.IPv4, MediaV4.Id);
            return editor;
        }
    }

    [Fact]
    public void DefaultProfileHasNothingEnabled()
    {
        var s = new Sample();
        Assert.Equal("Default", s.Profiles.Active.Name);
        Assert.Empty(s.Profiles.BuildMappings(s.Default.Id));
    }

    [Fact]
    public void CreateRenameAndNameRules()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.Create("  Gaming ");
        Assert.Equal("Gaming", gaming.Name);
        Assert.Empty(gaming.Selections);
        Assert.Equal(Now, gaming.LastSavedUtc);

        Assert.Throws<CatalogException>(() => s.Profiles.Create("gaming"));
        Assert.Throws<CatalogException>(() => s.Profiles.Create("   "));
        Assert.Throws<CatalogException>(() => s.Profiles.Create(new string('x', 61)));
        Assert.Throws<CatalogException>(() => s.Profiles.Rename(gaming.Id, "DEFAULT"));

        s.Profiles.Rename(gaming.Id, "GAMING");
        Assert.Equal("GAMING", gaming.Name);
        s.Profiles.Rename(gaming.Id, "Work");
        Assert.Equal(new[] { "Default", "Work" }, s.Profiles.Profiles.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void WorkingChangesDoNotAlterASavedProfileUntilSaved()
    {
        var s = new Sample();
        SelectionEditor working = s.WorkingGaming();

        Assert.Empty(s.Default.Selections);
        Assert.Empty(s.Profiles.BuildMappings(s.Default.Id));

        s.Profiles.SaveSelections(s.Default.Id, working.Selections);
        Assert.Equal(2, s.Profiles.BuildMappings(s.Default.Id).Count);

        // Further edits to the working copy stay out of the saved profile.
        working.DisableAll();
        Assert.Equal(2, s.Profiles.BuildMappings(s.Default.Id).Count);
    }

    [Fact]
    public void SaveAsNewLeavesExistingProfilesUnchanged()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.SaveAsNew("Gaming", s.WorkingGaming().Selections);

        Assert.Empty(s.Default.Selections);
        Assert.Equal(2, s.Config.Profiles.Count);
        Assert.Equal(
            new[]
            {
                new HostsMapping("198.51.100.20", "example.com"),
                new HostsMapping("2606:2800:220:1:248:1893:25c8:1946", "example.com")
            },
            s.Profiles.BuildMappings(gaming.Id));
        Assert.Throws<CatalogException>(() => s.Profiles.SaveAsNew("Gaming", []));
    }

    [Fact]
    public void DuplicateIsAnIndependentCopyWithAUniqueName()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.SaveAsNew("Gaming", s.WorkingGaming().Selections);

        MappingProfile copy = s.Profiles.Duplicate(gaming.Id);
        MappingProfile second = s.Profiles.Duplicate(gaming.Id);
        Assert.Equal("Gaming (copy)", copy.Name);
        Assert.Equal("Gaming (copy) 2", second.Name);
        Assert.NotEqual(gaming.Id, copy.Id);
        Assert.Equal(s.Profiles.BuildMappings(gaming.Id), s.Profiles.BuildMappings(copy.Id));

        new SelectionEditor(s.Config, copy.Selections).DisableAll();
        Assert.Empty(s.Profiles.BuildMappings(copy.Id));
        Assert.Equal(2, s.Profiles.BuildMappings(gaming.Id).Count);
    }

    [Fact]
    public void SwitchingProfilesYieldsEachProfilesExactMappings()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.SaveAsNew("Gaming", s.WorkingGaming().Selections);
        var streaming = new SelectionEditor(s.Config, []);
        streaming.SetEnabled(s.Media.Id, true);
        streaming.SelectAddress(s.Media.Id, IpFamily.IPv4, s.MediaV4.Id);
        streaming.SetEnabled(s.Example.Id, true);
        streaming.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);
        MappingProfile stream = s.Profiles.SaveAsNew("Streaming", streaming.Selections);

        s.Profiles.SetActive(gaming.Id);
        Assert.Equal(gaming.Id, s.Profiles.Active.Id);
        Assert.Equal(new[] { "198.51.100.20", "2606:2800:220:1:248:1893:25c8:1946" },
            s.Profiles.BuildMappings(s.Profiles.Active.Id).Select(m => m.IpAddress).ToArray());

        s.Profiles.SetActive(stream.Id);
        Assert.Equal(new[] { "93.184.216.34 example.com", "203.0.113.10 media.example.com" },
            s.Profiles.BuildMappings(s.Profiles.Active.Id).Select(m => m.IpAddress + " " + m.Hostname).ToArray());

        s.Profiles.SetActive(s.Default.Id);
        Assert.Empty(s.Profiles.BuildMappings(s.Profiles.Active.Id));
        Assert.Throws<CatalogException>(() => s.Profiles.SetActive(Guid.NewGuid()));
    }

    [Fact]
    public void DeleteRulesAndActiveFallback()
    {
        var s = new Sample();
        Assert.Throws<CatalogException>(() => s.Profiles.Delete(s.Default.Id));

        MappingProfile gaming = s.Profiles.Create("Gaming");
        s.Profiles.SetActive(gaming.Id);
        s.Config.Hosts.LastAppliedProfileId = gaming.Id;

        s.Profiles.Delete(gaming.Id);

        Assert.Single(s.Config.Profiles);
        Assert.Equal(s.Default.Id, s.Config.ActiveProfileId);
        Assert.Null(s.Config.Hosts.LastAppliedProfileId);
        Assert.Equal(2, s.Config.Websites.Count);
    }

    [Fact]
    public void SavingDropsChoicesForDeletedWebsitesAndAddresses()
    {
        var s = new Sample();
        SelectionEditor working = s.WorkingGaming();
        s.Catalog.RemoveWebsite(s.Media.Id);
        s.Catalog.RemoveAddress(s.Example.Id, s.ExampleV6.Id);

        MappingProfile saved = s.Profiles.SaveAsNew("Gaming", working.Selections);

        ProfileWebsiteSelection only = Assert.Single(saved.Selections);
        Assert.Equal(s.ExampleV4Alt.Id, only.SelectedIpv4Id);
        Assert.Null(only.SelectedIpv6Id);
    }

    [Fact]
    public void ProfilesPersistAcrossLoads()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite("example.com");
        AddressCandidate address = catalog.AddAddress(site.Id, "192.0.2.1");
        var working = new SelectionEditor(config, []);
        working.SetEnabled(site.Id, true);
        working.SelectAddress(site.Id, IpFamily.IPv4, address.Id);
        var profiles = new ProfileManager(config, () => Now);
        MappingProfile gaming = profiles.SaveAsNew("Gaming", working.Selections);
        profiles.SetActive(gaming.Id);
        store.Save(config);

        AppConfiguration reloaded = store.Load();
        var reloadedProfiles = new ProfileManager(reloaded);
        Assert.Equal("Gaming", reloadedProfiles.Active.Name);
        Assert.Equal(Now, reloadedProfiles.Active.LastSavedUtc);
        Assert.Equal(new[] { new HostsMapping("192.0.2.1", "example.com") },
            reloadedProfiles.BuildMappings(reloaded.ActiveProfileId));
    }

    [Fact]
    public void ExportedProfileImportsIntoAnEmptyConfiguration()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.SaveAsNew("Gaming", s.WorkingGaming().Selections);
        string json = ProfileTransfer.ExportProfile(s.Config, gaming.Id, Now);

        AppConfiguration target = AppConfiguration.CreateDefault();
        ImportSummary summary = ProfileTransfer.ImportProfile(target, json, Now);

        Assert.Equal(2, summary.WebsitesAdded);
        Assert.Equal(4, summary.AddressesAdded);
        Assert.Equal("Gaming", summary.Profile!.Name);
        Assert.Equal(2, target.Profiles.Count);
        var imported = new ProfileManager(target);
        Assert.Equal(s.Profiles.BuildMappings(gaming.Id), imported.BuildMappings(summary.Profile.Id));

        // The disabled website keeps its chosen address in the imported profile.
        WebsiteEntry media = target.Websites.Single(w => w.Hostname == "media.example.com");
        ProfileWebsiteSelection mediaChoice = summary.Profile.Selections.Single(x => x.WebsiteId == media.Id);
        Assert.False(mediaChoice.Enabled);
        Assert.Equal(media.Addresses.Single().Id, mediaChoice.SelectedIpv4Id);
    }

    [Fact]
    public void ImportMergesWithoutRemovingOrOverwritingAnything()
    {
        var s = new Sample();
        MappingProfile gaming = s.Profiles.SaveAsNew("Gaming", s.WorkingGaming().Selections);
        string json = ProfileTransfer.ExportProfile(s.Config, gaming.Id, Now);

        AppConfiguration target = AppConfiguration.CreateDefault();
        var targetCatalog = new WebsiteCatalog(target);
        WebsiteEntry existing = targetCatalog.AddWebsite("example.com");
        AddressCandidate keep = targetCatalog.AddAddress(existing.Id, "192.0.2.99");
        targetCatalog.AddAddress(existing.Id, "93.184.216.34");
        new ProfileManager(target).Create("Gaming");

        ImportSummary summary = ProfileTransfer.ImportProfile(target, json, Now);

        Assert.Equal(1, summary.WebsitesAdded);
        Assert.Equal(3, summary.AddressesAdded);
        Assert.Equal("Gaming 2", summary.Profile!.Name);
        Assert.Contains(existing.Addresses, a => a.Id == keep.Id);
        Assert.Equal(4, existing.Addresses.Count);
        Assert.Empty(target.Profiles.Single(p => p.Name == "Gaming").Selections);
        Assert.Empty(target.Profiles[0].Selections);
    }

    [Fact]
    public void CollectionExportAndImportCarryWebsitesButNoProfile()
    {
        var s = new Sample();
        string json = ProfileTransfer.ExportCollection(s.Config, Now);

        AppConfiguration target = AppConfiguration.CreateDefault();
        ImportSummary first = ProfileTransfer.ImportCollection(target, json);
        ImportSummary second = ProfileTransfer.ImportCollection(target, json);

        Assert.Equal((2, 4), (first.WebsitesAdded, first.AddressesAdded));
        Assert.Equal((0, 0), (second.WebsitesAdded, second.AddressesAdded));
        Assert.Null(first.Profile);
        Assert.Single(target.Profiles);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("{ nope", "not valid JSON")]
    [InlineData("null", "not a Kiitou export")]
    [InlineData("{\"Format\":\"kiitou-collection\",\"Version\":1,\"Websites\":[]}", "not a Kiitou profile export")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":9,\"Websites\":[]}", "different version")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[null]}", "incomplete website")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[{\"Hostname\":\"bad host\",\"Addresses\":[]}]}", "bad host")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[{\"Hostname\":\"ok.example\",\"Addresses\":[\"1.2.3\"]}]}", "1.2.3")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[{\"Hostname\":\"ok.example\",\"Addresses\":[\"192.0.2.1\"]},{\"Hostname\":\"OK.example\",\"Addresses\":[]}]}", "more than once")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[{\"Hostname\":\"ok.example\",\"Addresses\":[\"192.0.2.1\"],\"SelectedIpv4\":\"192.0.2.2\"}]}", "not among its candidates")]
    [InlineData("{\"Format\":\"kiitou-profile\",\"Version\":1,\"Websites\":[{\"Hostname\":\"ok.example\",\"Addresses\":[\"192.0.2.1\"],\"SelectedIpv6\":\"192.0.2.1\"}]}", "not an IPv6 address")]
    public void InvalidImportFilesChangeNothing(string json, string expectedMessagePart)
    {
        AppConfiguration target = AppConfiguration.CreateDefault();

        var ex = Assert.Throws<CatalogException>(() => ProfileTransfer.ImportProfile(target, json, Now));

        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(target.Websites);
        Assert.Single(target.Profiles);
    }

    [Fact]
    public void ImportedProfileNameIsSanitized()
    {
        const string json = "{\"Format\":\"kiitou-profile\",\"Version\":1,\"Name\":\"  \\u0007 \",\"Websites\":[]}";
        AppConfiguration target = AppConfiguration.CreateDefault();

        ImportSummary summary = ProfileTransfer.ImportProfile(target, json, Now);

        Assert.Equal("Imported profile", summary.Profile!.Name);
    }
}

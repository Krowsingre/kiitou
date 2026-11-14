using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Services;

namespace Kiitou.Tests;

public class CatalogTests
{
    private sealed class Sample
    {
        public AppConfiguration Config { get; } = AppConfiguration.CreateDefault();
        public WebsiteCatalog Catalog { get; }
        public SelectionEditor Editor { get; }
        public WebsiteEntry Example { get; }
        public AddressCandidate ExampleV4 { get; }
        public AddressCandidate ExampleV4Alt { get; }
        public AddressCandidate ExampleV6 { get; }
        public WebsiteEntry Media { get; }
        public AddressCandidate MediaV4 { get; }

        public Sample()
        {
            Catalog = new WebsiteCatalog(Config);
            Example = Catalog.AddWebsite("example.com");
            ExampleV4 = Catalog.AddAddress(Example.Id, "93.184.216.34");
            ExampleV4Alt = Catalog.AddAddress(Example.Id, "198.51.100.20");
            ExampleV6 = Catalog.AddAddress(Example.Id, "2606:2800:220:1:248:1893:25c8:1946");
            Media = Catalog.AddWebsite("media.example.com");
            MediaV4 = Catalog.AddAddress(Media.Id, "203.0.113.10");
            Editor = new SelectionEditor(Config, Config.Profiles[0].Selections);
        }
    }

    [Fact]
    public void WebsitesAreNormalizedAndDuplicatesRefused()
    {
        var s = new Sample();
        WebsiteEntry added = s.Catalog.AddWebsite("  API.Example.com. ");

        Assert.Equal("api.example.com", added.Hostname);
        Assert.Equal(3, s.Catalog.Websites.Count);
        Assert.Throws<CatalogException>(() => s.Catalog.AddWebsite("EXAMPLE.COM"));
        Assert.Throws<CatalogException>(() => s.Catalog.AddWebsite("not a hostname"));
        Assert.Throws<CatalogException>(() => s.Catalog.AddWebsite(""));
    }

    [Fact]
    public void WebsiteCanBeRenamedButNotOntoAnother()
    {
        var s = new Sample();
        s.Catalog.RenameWebsite(s.Example.Id, "www.example.com");
        Assert.Equal("www.example.com", s.Example.Hostname);

        s.Catalog.RenameWebsite(s.Example.Id, "WWW.example.com");
        Assert.Throws<CatalogException>(() => s.Catalog.RenameWebsite(s.Example.Id, "media.example.com"));
        Assert.Equal(3, s.Example.Addresses.Count);
    }

    [Fact]
    public void AddressesAreValidatedNormalizedAndUniquePerWebsite()
    {
        var s = new Sample();

        Assert.Equal("2001:db8::1", s.Catalog.AddAddress(s.Media.Id, "2001:0DB8:0000:0000:0000:0000:0000:0001").IpAddress);
        Assert.Throws<CatalogException>(() => s.Catalog.AddAddress(s.Example.Id, "93.184.216.34"));
        Assert.Throws<CatalogException>(() => s.Catalog.AddAddress(s.Example.Id, "2606:2800:0220:1:248:1893:25C8:1946"));
        Assert.Throws<CatalogException>(() => s.Catalog.AddAddress(s.Example.Id, "999.1.1.1"));
        Assert.Throws<CatalogException>(() => s.Catalog.AddAddress(Guid.NewGuid(), "10.0.0.1"));

        // The same address may serve as a candidate for a different website.
        s.Catalog.AddAddress(s.Media.Id, "93.184.216.34");
        Assert.Equal(3, s.Media.Addresses.Count);
    }

    [Fact]
    public void EnablingOneManyAllAndNone()
    {
        var s = new Sample();
        Assert.Equal(0, s.Editor.EnabledCount);

        s.Editor.SetEnabled(s.Media.Id, true);
        Assert.Equal(1, s.Editor.EnabledCount);
        Assert.False(s.Editor.View(s.Example.Id).Enabled);

        s.Editor.EnableAll();
        Assert.Equal(2, s.Editor.EnabledCount);

        s.Editor.SetEnabled(s.Example.Id, false);
        Assert.Equal(1, s.Editor.EnabledCount);

        s.Editor.DisableAll();
        Assert.Equal(0, s.Editor.EnabledCount);
    }

    [Fact]
    public void SelectedAddressesMapPerFamilyAndOtherCandidatesAreKept()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4Alt.Id);
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv6, s.ExampleV6.Id);
        s.Editor.SelectAddress(s.Media.Id, IpFamily.IPv4, s.MediaV4.Id);
        s.Editor.SetEnabled(s.Example.Id, true);

        Assert.Equal(
            new[]
            {
                new HostsMapping("198.51.100.20", "example.com"),
                new HostsMapping("2606:2800:220:1:248:1893:25c8:1946", "example.com")
            },
            s.Editor.BuildMappings());
        Assert.Equal(3, s.Example.Addresses.Count);

        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv6, null);
        Assert.Equal(new[] { new HostsMapping("198.51.100.20", "example.com") }, s.Editor.BuildMappings());
    }

    [Fact]
    public void DisablingKeepsTheChosenAddresses()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);
        s.Editor.SetEnabled(s.Example.Id, true);
        s.Editor.SetEnabled(s.Example.Id, false);

        Assert.Empty(s.Editor.BuildMappings());
        Assert.Equal(s.ExampleV4.Id, s.Editor.View(s.Example.Id).SelectedIpv4Id);

        s.Editor.SetEnabled(s.Example.Id, true);
        Assert.Single(s.Editor.BuildMappings());
    }

    [Fact]
    public void SelectingAWrongFamilyOrForeignAddressIsRefused()
    {
        var s = new Sample();
        Assert.Throws<CatalogException>(() => s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV6.Id));
        Assert.Throws<CatalogException>(() => s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv6, s.ExampleV4.Id));
        Assert.Throws<CatalogException>(() => s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.MediaV4.Id));
        Assert.Throws<CatalogException>(() => s.Editor.SelectAddress(Guid.NewGuid(), IpFamily.IPv4, null));
        Assert.Empty(s.Editor.Selections);
    }

    [Fact]
    public void EnabledWebsiteWithoutAnAddressIsReportedAndNotMapped()
    {
        var s = new Sample();
        s.Editor.EnableAll();
        s.Editor.SelectAddress(s.Media.Id, IpFamily.IPv4, s.MediaV4.Id);

        SelectionIssue issue = Assert.Single(s.Editor.FindIssues());
        Assert.Equal(s.Example.Id, issue.WebsiteId);
        Assert.Equal(new[] { new HostsMapping("203.0.113.10", "media.example.com") }, s.Editor.BuildMappings());
    }

    [Fact]
    public void RemovingAnAddressClearsOnlyReferencesToIt()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv6, s.ExampleV6.Id);
        s.Editor.SetEnabled(s.Example.Id, true);

        s.Catalog.RemoveAddress(s.Example.Id, s.ExampleV4.Id);

        ProfileWebsiteSelection selection = s.Editor.View(s.Example.Id);
        Assert.Null(selection.SelectedIpv4Id);
        Assert.Equal(s.ExampleV6.Id, selection.SelectedIpv6Id);
        Assert.True(selection.Enabled);
        Assert.Equal(2, s.Example.Addresses.Count);
    }

    [Fact]
    public void EditingAnAddressKeepsItsSelectionUnlessTheFamilyChanges()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);

        s.Catalog.EditAddress(s.Example.Id, s.ExampleV4.Id, "192.0.2.44");
        Assert.Equal(s.ExampleV4.Id, s.Editor.View(s.Example.Id).SelectedIpv4Id);
        Assert.Equal("192.0.2.44", s.ExampleV4.IpAddress);

        Assert.Throws<CatalogException>(() => s.Catalog.EditAddress(s.Example.Id, s.ExampleV4.Id, "198.51.100.20"));

        s.Catalog.EditAddress(s.Example.Id, s.ExampleV4.Id, "2001:db8::44");
        Assert.Null(s.Editor.View(s.Example.Id).SelectedIpv4Id);
    }

    [Fact]
    public void RemovingAWebsiteRemovesItFromEveryProfile()
    {
        var s = new Sample();
        var second = new MappingProfile { Name = "Second" };
        s.Config.Profiles.Add(second);
        var secondEditor = new SelectionEditor(s.Config, second.Selections);
        s.Editor.SetEnabled(s.Example.Id, true);
        secondEditor.SetEnabled(s.Example.Id, true);
        secondEditor.SetEnabled(s.Media.Id, true);

        s.Catalog.RemoveWebsite(s.Example.Id);

        Assert.Empty(s.Config.Profiles[0].Selections);
        Assert.Equal(s.Media.Id, Assert.Single(second.Selections).WebsiteId);
        Assert.Single(s.Catalog.Websites);
    }

    [Fact]
    public void PruneDropsDanglingChoicesFromAnUnsavedCopy()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);
        s.Editor.SetEnabled(s.Media.Id, true);
        var working = new SelectionEditor(s.Config, SelectionEditor.Copy(s.Editor.Selections));

        s.Catalog.RemoveAddress(s.Example.Id, s.ExampleV4.Id);
        s.Catalog.RemoveWebsite(s.Media.Id);
        working.Prune();

        Assert.Null(Assert.Single(working.Selections).SelectedIpv4Id);
    }

    [Fact]
    public void CopyIsIndependentAndEquivalenceIgnoresEmptyEntries()
    {
        var s = new Sample();
        s.Editor.SelectAddress(s.Example.Id, IpFamily.IPv4, s.ExampleV4.Id);
        var copy = SelectionEditor.Copy(s.Editor.Selections);
        Assert.True(SelectionEditor.AreEquivalent(copy, s.Editor.Selections));

        var copyEditor = new SelectionEditor(s.Config, copy);
        copyEditor.SetEnabled(s.Media.Id, false);
        Assert.True(SelectionEditor.AreEquivalent(copy, s.Editor.Selections));

        copyEditor.SetEnabled(s.Media.Id, true);
        Assert.False(SelectionEditor.AreEquivalent(copy, s.Editor.Selections));
        Assert.False(s.Editor.View(s.Media.Id).Enabled);
    }

    [Fact]
    public void CatalogChangesPersistAcrossLoads()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        var catalog = new WebsiteCatalog(config);
        WebsiteEntry site = catalog.AddWebsite("example.com");
        AddressCandidate v6 = catalog.AddAddress(site.Id, "2001:db8::1");
        catalog.AddAddress(site.Id, "192.0.2.1");
        var editor = new SelectionEditor(config, config.Profiles[0].Selections);
        editor.SelectAddress(site.Id, IpFamily.IPv6, v6.Id);
        store.Save(config);

        AppConfiguration reloaded = store.Load();
        var reloadedEditor = new SelectionEditor(reloaded, reloaded.Profiles[0].Selections);
        Assert.Equal(2, Assert.Single(reloaded.Websites).Addresses.Count);
        Assert.False(reloadedEditor.View(site.Id).Enabled);
        Assert.Equal(v6.Id, reloadedEditor.View(site.Id).SelectedIpv6Id);
    }

    [Theory]
    [InlineData("Example.com", "93.184.216.34", "invalid hostname")]
    [InlineData("example.com", "93.184.216.034", "invalid IP address")]
    public void StoredWebsitesMustAlreadyBeValid(string hostname, string ip, string expected)
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        config.Websites.Add(new WebsiteEntry { Hostname = hostname, Addresses = [new AddressCandidate { IpAddress = ip }] });

        var ex = Assert.Throws<InvalidDataException>(() => store.Save(config));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void StoredSelectionMustMatchItsAddressFamily()
    {
        using var dir = new TempDirectory();
        var store = new ConfigurationStore(new StoragePaths(dir.Path, dir.Path, true));
        AppConfiguration config = store.Load();
        var v6 = new AddressCandidate { IpAddress = "2001:db8::1" };
        var site = new WebsiteEntry { Hostname = "example.com", Addresses = [v6] };
        config.Websites.Add(site);
        config.Profiles[0].Selections.Add(new ProfileWebsiteSelection { WebsiteId = site.Id, SelectedIpv4Id = v6.Id });

        Assert.Throws<InvalidDataException>(() => store.Save(config));
    }
}

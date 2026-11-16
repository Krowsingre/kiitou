using Kiitou.Models;
using Kiitou.Services;

namespace Kiitou.Tests;

public class ConfigurationStoreTests
{
    private static ConfigurationStore StoreIn(TempDirectory dir) =>
        new(new StoragePaths(dir.Path, dir.Path, true));

    [Fact]
    public void FirstLoadCreatesDefaultConfiguration()
    {
        using var dir = new TempDirectory();
        var config = StoreIn(dir).Load();

        Assert.True(File.Exists(dir.File("config.json")));
        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal("purple", config.Theme.Color);
        Assert.Equal("auto", config.Theme.Mode);
        Assert.Empty(config.Websites);
        var profile = Assert.Single(config.Profiles);
        Assert.Equal("Default", profile.Name);
        Assert.Empty(profile.Selections);
        Assert.Equal(profile.Id, config.ActiveProfileId);
    }

    [Fact]
    public void SavedConfigurationRoundTrips()
    {
        using var dir = new TempDirectory();
        var store = StoreIn(dir);
        var config = store.Load();
        var site = new WebsiteEntry { Hostname = "example.com" };
        var address = new AddressCandidate { IpAddress = "93.184.216.34" };
        site.Addresses.Add(address);
        config.Websites.Add(site);
        config.Profiles[0].Selections.Add(new ProfileWebsiteSelection
        {
            WebsiteId = site.Id, Enabled = true, SelectedIpv4Id = address.Id
        });
        config.Theme.Color = "teal";
        store.Save(config);

        var reloaded = StoreIn(dir).Load();
        Assert.Equal("teal", reloaded.Theme.Color);
        var reloadedSite = Assert.Single(reloaded.Websites);
        Assert.Equal("example.com", reloadedSite.Hostname);
        Assert.Equal(address.Id, Assert.Single(reloadedSite.Addresses).Id);
        var selection = Assert.Single(reloaded.Profiles[0].Selections);
        Assert.True(selection.Enabled);
        Assert.Equal(address.Id, selection.SelectedIpv4Id);
    }

    [Fact]
    public void SaveLeavesNoTemporaryFiles()
    {
        using var dir = new TempDirectory();
        var store = StoreIn(dir);
        store.Save(store.Load());
        string[] files = Directory.GetFiles(dir.Path).Select(p => Path.GetFileName(p)!).ToArray();
        Assert.Equal(new[] { "config.json" }, files);
    }

    [Theory]
    [InlineData("{ \"SchemaVersion\": 1, \"Theme\": {", "invalid")]
    [InlineData("", "invalid")]
    [InlineData("null", "no root object")]
    [InlineData("{\"SchemaVersion\":2}", "schema version")]
    [InlineData("{\"SchemaVersion\":1,\"Theme\":{\"Color\":\"pink\",\"Mode\":\"dark\"}}", "theme")]
    [InlineData("{\"SchemaVersion\":1,\"Websites\":[]}", "missing required collections or profiles")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[null]}", "profile entry")]
    [InlineData("{\"SchemaVersion\":1,\"Websites\":[null],\"Profiles\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Default\"}],\"ActiveProfileId\":\"11111111-1111-1111-1111-111111111111\"}", "website entry")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"A\"},{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"B\"}],\"ActiveProfileId\":\"11111111-1111-1111-1111-111111111111\"}", "duplicate identifiers")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"A\",\"Selections\":[{\"WebsiteId\":\"22222222-2222-2222-2222-222222222222\",\"Enabled\":true}]}],\"ActiveProfileId\":\"11111111-1111-1111-1111-111111111111\"}", "website that does not exist")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"A\"}],\"ActiveProfileId\":\"33333333-3333-3333-3333-333333333333\"}", "Active profile does not exist")]
    public void InvalidConfigurationIsRejectedAndNotOverwritten(string json, string expectedMessagePart)
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("config.json"), json);

        var ex = Assert.Throws<InvalidDataException>(() => StoreIn(dir).Load());
        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void SelectionOfMissingAddressIsRejected()
    {
        using var dir = new TempDirectory();
        var store = StoreIn(dir);
        var config = store.Load();
        var site = new WebsiteEntry { Hostname = "example.com" };
        config.Websites.Add(site);
        config.Profiles[0].Selections.Add(new ProfileWebsiteSelection
        {
            WebsiteId = site.Id, SelectedIpv4Id = Guid.NewGuid()
        });

        var ex = Assert.Throws<InvalidDataException>(() => store.Save(config));
        Assert.Contains("address that does not exist", ex.Message);
    }
}

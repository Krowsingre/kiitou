using System.Text.Json.Serialization;

namespace Kiitou.Models;

public sealed class AppConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public ThemeSelection Theme { get; set; } = new();
    public List<WebsiteEntry> Websites { get; set; } = [];
    public List<MappingProfile> Profiles { get; set; } = [];
    public Guid ActiveProfileId { get; set; }
    public HostsState Hosts { get; set; } = new();

    public static AppConfiguration CreateDefault()
    {
        var profile = new MappingProfile { Name = "Default" };
        return new AppConfiguration
        {
            Profiles = [profile],
            ActiveProfileId = profile.Id
        };
    }
}

/// <summary>What Kiitou last wrote to the system hosts file. Independent of the saved websites and profiles.</summary>
public sealed class HostsState
{
    /// <summary>SHA-256 of the hosts file immediately after Kiitou's last write; used to notice outside edits.</summary>
    public string? LastWrittenSha256 { get; set; }
    public DateTime? LastAppliedUtc { get; set; }
    public Guid? LastAppliedProfileId { get; set; }
}

public sealed class ThemeSelection
{
    public string Color { get; set; } = "purple";
    public string Mode { get; set; } = "dark";
}

public sealed class WebsiteEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Hostname { get; set; } = "";
    public List<AddressCandidate> Addresses { get; set; } = [];
}

public sealed class AddressCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IpAddress { get; set; } = "";
}

public sealed class MappingProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<ProfileWebsiteSelection> Selections { get; set; } = [];
    /// <summary>When this profile's choices were last explicitly saved.</summary>
    public DateTime? LastSavedUtc { get; set; }
}

public sealed class ProfileWebsiteSelection
{
    public Guid WebsiteId { get; set; }
    public bool Enabled { get; set; }
    public Guid? SelectedIpv4Id { get; set; }
    public Guid? SelectedIpv6Id { get; set; }
}

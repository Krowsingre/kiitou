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
    public AppSettings Settings { get; set; } = new();

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

public sealed class AppSettings
{
    /// <summary>Show a notification-area icon and hide the window there when minimized.</summary>
    public bool MinimizeToTray { get; set; }
    /// <summary>Ask Quad9 whether it lists a website's hostname as malicious. Sends the hostname to Quad9.</summary>
    public bool UseQuad9 { get; set; } = true;
    /// <summary>Ask GitHub for a newer version a few seconds after Kiitou opens.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Install a newer version found at startup without asking. Needs <see cref="CheckUpdates"/>.</summary>
    public bool AutoInstallUpdates { get; set; }
    /// <summary>The two rows for key-based reputation services.</summary>
    public List<KeyServiceSlot> KeyServices { get; set; } = [new(), new()];
    /// <summary>API keys by service, encrypted for the signed-in Windows user. Never exported.</summary>
    public Dictionary<string, string> ProtectedKeys { get; set; } = [];
    public CustomDnsSettings CustomDns { get; set; } = new();
}

public sealed class KeyServiceSlot
{
    /// <summary>Empty when no service is chosen in this row.</summary>
    public string ServiceId { get; set; } = "";
    public bool Enabled { get; set; }
}

/// <summary>A filtering DNS server the user supplies, checked the same way as Quad9.</summary>
public sealed class CustomDnsSettings
{
    public string Name { get; set; } = "";
    public string Server { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class ThemeSelection
{
    public string Color { get; set; } = "purple";
    /// <summary>"auto" follows the Windows light/dark setting.</summary>
    public string Mode { get; set; } = "auto";
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
    /// <summary>Result of the most recent verification of this address, if any.</summary>
    public AddressAssessment? LastAssessment { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<VerificationStatus>))]
public enum VerificationStatus
{
    NotTested,
    ConnectionVerified,
    ConnectionWorksVerificationIncomplete,
    FailedOrUnsafe
}

/// <summary>A stored verification outcome with the reasons behind it and when it was made.</summary>
public sealed class AddressAssessment
{
    public VerificationStatus Status { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public DateTime AssessedUtc { get; set; }
    public int? HttpStatus { get; set; }
    public string? RedirectLocation { get; set; }
    public string? CertificateSubject { get; set; }
    public string? CertificateIssuer { get; set; }
    public DateTime? CertificateNotAfterUtc { get; set; }
    public double? ConnectMilliseconds { get; set; }
    public double? TotalMilliseconds { get; set; }
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

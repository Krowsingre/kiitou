using System.Text.Json.Serialization;

namespace Kiitou.Models;

public sealed class AppConfiguration
{
    /// <summary>
    /// The settings format written by this version of Kiitou. Older formats are converted when
    /// loaded (see <c>ConfigurationMigrations</c>); a newer format is refused, never overwritten.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
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
    /// <summary>The order of the left-hand menu, as page identifiers. Changed by dragging.</summary>
    public List<string> NavOrder { get; set; } = [.. Navigation.DefaultOrder];
    /// <summary>An optional SOCKS5 proxy that only Kiitou's address tests use.</summary>
    public ProxySettings Proxy { get; set; } = new();
    /// <summary>Where the window was last, so Kiitou (and a new version after an update) opens there.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}

/// <summary>The pages of the left-hand menu.</summary>
public static class Navigation
{
    public const string Home = "home";
    public const string Websites = "websites";
    public const string Discovery = "discovery";
    public const string Profiles = "profiles";
    public const string Network = "network";
    public const string Hosts = "hosts";
    public const string Settings = "settings";

    public static readonly IReadOnlyList<string> DefaultOrder = [Home, Websites, Discovery, Profiles, Network, Hosts, Settings];

    /// <summary>
    /// A complete, valid order: unknown or repeated pages are dropped, and pages missing from a
    /// saved order (for example ones added by a later version) take their default position.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? saved)
    {
        var order = new List<string>();
        foreach (string? page in saved ?? [])
        {
            if (page is not null && DefaultOrder.Contains(page) && !order.Contains(page)) order.Add(page);
        }
        for (int i = 0; i < DefaultOrder.Count; i++)
        {
            string page = DefaultOrder[i];
            if (order.Contains(page)) continue;
            // Insert after the page that precedes it by default, if that page is present.
            int after = i == 0 ? -1 : order.IndexOf(DefaultOrder[i - 1]);
            order.Insert(after + 1, page);
        }
        return order;
    }
}

/// <summary>A SOCKS5 proxy for tests. The password is encrypted for the signed-in Windows user.</summary>
public sealed class ProxySettings
{
    public bool Enabled { get; set; }
    public string Type { get; set; } = "socks5";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Username { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
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
    /// <summary>The public address the test came from, when it could be found out.</summary>
    public string? TestedFromIp { get; set; }
    public string? TestedFromCountry { get; set; }
    public bool ThroughProxy { get; set; }
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

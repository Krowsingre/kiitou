
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou.Services;

public sealed class ConfigurationStore
{
    private const string ConfigFileName = "config.json";
    public const string SafetyCopyFolder = "config-backups";
    public const int SafetyCopiesKept = 10;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public StoragePaths Paths { get; }
    private string ConfigFilePath => Path.Combine(Paths.DataDirectory, ConfigFileName);

    public ConfigurationStore(StoragePaths paths) => Paths = paths;

    public AppConfiguration Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var initial = AppConfiguration.CreateDefault();
            Save(initial);
            return initial;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(ConfigFilePath)) as JsonObject
                ?? throw new InvalidDataException("Configuration JSON has no root object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Configuration JSON is invalid; the existing file was not overwritten.", ex);
        }

        int version = ConfigurationMigrations.ReadVersion(root);
        if (version > AppConfiguration.CurrentSchemaVersion)
            throw new InvalidDataException(
                "These settings were saved by a newer version of Kiitou. Install the newer version again; the settings were not changed.");
        bool converted = version < AppConfiguration.CurrentSchemaVersion;
        if (converted)
        {
            // Keep the original before converting it, so nothing is lost if conversion goes wrong.
            CreateSafetyCopy($"schema{version}");
            ConfigurationMigrations.Upgrade(root, version);
        }

        try
        {
            AppConfiguration config = root.Deserialize<AppConfiguration>(JsonOptions)
                ?? throw new InvalidDataException("Configuration JSON has no root object.");
            Validate(config);
            if (converted) Save(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Configuration JSON is invalid; the existing file was not overwritten.", ex);
        }
    }

    /// <summary>
    /// Copies the current settings file into the safety-copy folder, for example before an update,
    /// keeping the newest <see cref="SafetyCopiesKept"/> copies. Returns the copy's path, or null if
    /// there is no settings file yet.
    /// </summary>
    public string? CreateSafetyCopy(string reason)
    {
        if (!File.Exists(ConfigFilePath)) return null;
        string folder = Path.Combine(Paths.DataDirectory, SafetyCopyFolder);
        Directory.CreateDirectory(folder);
        string safeReason = new string(reason.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-').ToArray());
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string copy = Path.Combine(folder, $"config-{stamp}-{safeReason}.json");
        File.Copy(ConfigFilePath, copy, overwrite: true);

        foreach (string old in Directory.GetFiles(folder, "config-*.json")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(SafetyCopiesKept))
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return copy;
    }

    public IReadOnlyList<string> SafetyCopies =>
        Directory.Exists(Path.Combine(Paths.DataDirectory, SafetyCopyFolder))
            ? Directory.GetFiles(Path.Combine(Paths.DataDirectory, SafetyCopyFolder), "config-*.json")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public void Save(AppConfiguration config)
    {
        Validate(config);
        Directory.CreateDirectory(Paths.DataDirectory);
        var temp = Path.Combine(Paths.DataDirectory, $".kiitou-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, config, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temp, ConfigFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void Validate(AppConfiguration config)
    {
        if (config.SchemaVersion != AppConfiguration.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported configuration schema version.");
        if (config.Theme is null || !ThemeCatalog.IsValid(config.Theme.Color, config.Theme.Mode))
            throw new InvalidDataException("Unrecognized theme selection.");
        config.Hosts ??= new HostsState();
        config.Settings ??= new AppSettings();
        NormalizeReputationSettings(config.Settings);
        config.Settings.NavOrder = Navigation.Normalize(config.Settings.NavOrder);
        config.Settings.Proxy ??= new ProxySettings();
        NormalizeProxy(config.Settings.Proxy);
        if (config.Websites is null || config.Profiles is null || config.Profiles.Count == 0)
            throw new InvalidDataException("Configuration is missing required collections or profiles.");
        if (config.Websites.Any(w => w is null || w.Hostname is null || w.Addresses is null ||
                w.Addresses.Any(a => a is null || a.IpAddress is null)))
            throw new InvalidDataException("Configuration contains an empty or incomplete website entry.");
        if (config.Profiles.Any(p => p is null || p.Name is null || p.Selections is null ||
                p.Selections.Any(s => s is null)))
            throw new InvalidDataException("Configuration contains an empty or incomplete profile entry.");
        if (HasDuplicates(config.Websites.Select(w => w.Id)) ||
            HasDuplicates(config.Websites.SelectMany(w => w.Addresses).Select(a => a.Id)) ||
            HasDuplicates(config.Profiles.Select(p => p.Id)))
            throw new InvalidDataException("Configuration contains duplicate identifiers.");

        foreach (WebsiteEntry website in config.Websites)
        {
            foreach (AddressCandidate candidate in website.Addresses)
            {
                if (candidate.LastAssessment is not { } assessment) continue;
                assessment.Reasons ??= [];
                assessment.Warnings ??= [];
            }
            if (!HostnameRules.IsNormalized(website.Hostname))
                throw new InvalidDataException($"Configuration contains an invalid hostname: \"{website.Hostname}\".");
            if (website.Addresses.Any(a => !IpAddressRules.IsNormalized(a.IpAddress)))
                throw new InvalidDataException($"Configuration contains an invalid IP address for {website.Hostname}.");
            if (website.Addresses.Select(a => a.IpAddress).Distinct().Count() != website.Addresses.Count)
                throw new InvalidDataException($"Configuration lists the same IP address twice for {website.Hostname}.");
        }
        if (config.Websites.Select(w => w.Hostname).Distinct().Count() != config.Websites.Count)
            throw new InvalidDataException("Configuration lists the same website twice.");

        foreach (var selection in config.Profiles.SelectMany(p => p.Selections))
        {
            var website = config.Websites.FirstOrDefault(w => w.Id == selection.WebsiteId)
                ?? throw new InvalidDataException("A profile refers to a website that does not exist.");
            CheckSelectedAddress(website, selection.SelectedIpv4Id, IpFamily.IPv4);
            CheckSelectedAddress(website, selection.SelectedIpv6Id, IpFamily.IPv6);
        }

        if (!config.Profiles.Any(x => x.Id == config.ActiveProfileId))
            throw new InvalidDataException("Active profile does not exist.");
    }

    /// <summary>Repairs reputation settings in place so a damaged entry can never enable a check by accident.</summary>
    private static void NormalizeReputationSettings(AppSettings settings)
    {
        settings.ProtectedKeys ??= [];
        settings.CustomDns ??= new CustomDnsSettings();
        settings.CustomDns.Name ??= "";
        settings.CustomDns.Server ??= "";
        if (!IpAddressRules.IsNormalized(settings.CustomDns.Server))
        {
            settings.CustomDns.Server = "";
            settings.CustomDns.Enabled = false;
        }

        settings.KeyServices ??= [];
        settings.KeyServices.RemoveAll(slot => slot is null);
        while (settings.KeyServices.Count < 2) settings.KeyServices.Add(new KeyServiceSlot());
        if (settings.KeyServices.Count > 2) settings.KeyServices.RemoveRange(2, settings.KeyServices.Count - 2);
        var seen = new HashSet<string>();
        foreach (KeyServiceSlot slot in settings.KeyServices)
        {
            slot.ServiceId ??= "";
            bool known = Kiitou.Verification.ReputationCatalog.Find(slot.ServiceId) is not null;
            if (!known || !seen.Add(slot.ServiceId))
            {
                slot.ServiceId = "";
                slot.Enabled = false;
            }
            else if (!settings.ProtectedKeys.ContainsKey(slot.ServiceId))
            {
                slot.Enabled = false;
            }
        }
    }

    private static void NormalizeProxy(ProxySettings proxy)
    {
        proxy.Type = "socks5";
        proxy.Host ??= "";
        proxy.Username ??= "";
        proxy.ProtectedPassword ??= "";
        if (proxy.Port is < 0 or > 65535) proxy.Port = 0;
        if (proxy.Host.Length == 0 || proxy.Port == 0) proxy.Enabled = false;
    }

    private static void CheckSelectedAddress(WebsiteEntry website, Guid? addressId, IpFamily family)
    {
        if (addressId is not Guid id) return;
        AddressCandidate address = website.Addresses.FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidDataException("A profile selects an address that does not exist.");
        if (IpAddressRules.FamilyOf(address.IpAddress) != family)
            throw new InvalidDataException($"A profile selects {address.IpAddress} as an {family} address.");
    }

    private static bool HasDuplicates(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        return ids.Any(id => !seen.Add(id));
    }
}

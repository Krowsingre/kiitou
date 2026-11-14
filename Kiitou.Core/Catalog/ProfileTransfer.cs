using System.Text.Json;
using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou.Catalog;

/// <summary>The portable file format for one profile or for the whole website collection.</summary>
public sealed class TransferDocument
{
    public const string ProfileFormat = "kiitou-profile";
    public const string CollectionFormat = "kiitou-collection";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = "";
    public int Version { get; set; } = CurrentVersion;
    /// <summary>Profile name; unused for collections.</summary>
    public string? Name { get; set; }
    public DateTime ExportedUtc { get; set; }
    public List<TransferWebsite> Websites { get; set; } = [];
}

public sealed class TransferWebsite
{
    public string Hostname { get; set; } = "";
    public List<string> Addresses { get; set; } = [];
    /// <summary>Profile files only.</summary>
    public bool Enabled { get; set; }
    public string? SelectedIpv4 { get; set; }
    public string? SelectedIpv6 { get; set; }
}

public sealed record ImportSummary(int WebsitesAdded, int AddressesAdded, MappingProfile? Profile);

/// <summary>
/// Exports and imports profiles and website collections as self-contained JSON. Files refer to
/// websites and addresses by value, so they can be moved between computers. Importing only ever
/// adds websites and candidate addresses; nothing already saved is removed or overwritten, and
/// nothing is written to the hosts file.
/// </summary>
public static class ProfileTransfer
{
    private const int MaximumLength = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ExportProfile(AppConfiguration config, Guid profileId, DateTime utcNow)
    {
        MappingProfile profile = new ProfileManager(config).Get(profileId);
        var editor = new SelectionEditor(config, profile.Selections);
        var document = new TransferDocument
        {
            Format = TransferDocument.ProfileFormat,
            Name = profile.Name,
            ExportedUtc = utcNow
        };
        foreach (WebsiteEntry website in config.Websites)
        {
            ProfileWebsiteSelection selection = editor.View(website.Id);
            document.Websites.Add(new TransferWebsite
            {
                Hostname = website.Hostname,
                Addresses = website.Addresses.Select(a => a.IpAddress).ToList(),
                Enabled = selection.Enabled,
                SelectedIpv4 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv4Id)?.IpAddress,
                SelectedIpv6 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv6Id)?.IpAddress
            });
        }
        return JsonSerializer.Serialize(document, Options);
    }

    public static string ExportCollection(AppConfiguration config, DateTime utcNow)
    {
        var document = new TransferDocument
        {
            Format = TransferDocument.CollectionFormat,
            ExportedUtc = utcNow,
            Websites = config.Websites.Select(w => new TransferWebsite
            {
                Hostname = w.Hostname,
                Addresses = w.Addresses.Select(a => a.IpAddress).ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>
    /// Adds the file's websites and addresses where missing and creates a new profile from its
    /// choices. If the file is invalid in any way, nothing is changed.
    /// </summary>
    public static ImportSummary ImportProfile(AppConfiguration config, string json, DateTime utcNow)
    {
        List<TransferWebsite> websites = Parse(json, TransferDocument.ProfileFormat, out TransferDocument document);
        var manager = new ProfileManager(config, () => utcNow);
        string cleanName = new string((document.Name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        string profileName = manager.UniqueName(cleanName.Length == 0 ? "Imported profile" : cleanName);

        (int websitesAdded, int addressesAdded) = Merge(config, websites);
        var selections = new List<ProfileWebsiteSelection>();
        foreach (TransferWebsite item in websites)
        {
            WebsiteEntry website = config.Websites.First(w => w.Hostname == item.Hostname);
            selections.Add(new ProfileWebsiteSelection
            {
                WebsiteId = website.Id,
                Enabled = item.Enabled,
                SelectedIpv4Id = website.Addresses.FirstOrDefault(a => a.IpAddress == item.SelectedIpv4)?.Id,
                SelectedIpv6Id = website.Addresses.FirstOrDefault(a => a.IpAddress == item.SelectedIpv6)?.Id
            });
        }
        MappingProfile profile = manager.SaveAsNew(profileName, selections);
        return new ImportSummary(websitesAdded, addressesAdded, profile);
    }

    /// <summary>Adds the file's websites and addresses where missing. If the file is invalid, nothing is changed.</summary>
    public static ImportSummary ImportCollection(AppConfiguration config, string json)
    {
        List<TransferWebsite> websites = Parse(json, TransferDocument.CollectionFormat, out _);
        (int websitesAdded, int addressesAdded) = Merge(config, websites);
        return new ImportSummary(websitesAdded, addressesAdded, null);
    }

    /// <summary>Reads and fully validates a file, returning its websites in normalized, de-duplicated form.</summary>
    private static List<TransferWebsite> Parse(string json, string expectedFormat, out TransferDocument document)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new CatalogException("The file is empty.");
        if (json.Length > MaximumLength) throw new CatalogException("The file is too large to be a Kiitou export.");

        TransferDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TransferDocument>(json, Options);
        }
        catch (JsonException)
        {
            throw new CatalogException("The file is not valid JSON.");
        }
        if (parsed is null || parsed.Websites is null)
            throw new CatalogException("The file is not a Kiitou export.");
        if (parsed.Format != expectedFormat)
        {
            string wanted = expectedFormat == TransferDocument.ProfileFormat ? "profile" : "website collection";
            throw new CatalogException($"The file is not a Kiitou {wanted} export.");
        }
        if (parsed.Version != TransferDocument.CurrentVersion)
            throw new CatalogException("The file was made by a different version of Kiitou and cannot be read.");
        document = parsed;

        var result = new List<TransferWebsite>();
        foreach (TransferWebsite? item in parsed.Websites)
        {
            if (item is null || item.Addresses is null)
                throw new CatalogException("The file contains an incomplete website entry.");
            if (!HostnameRules.TryNormalize(item.Hostname, out string hostname, out string hostError))
                throw new CatalogException($"\"{item.Hostname}\": {hostError}");
            if (result.Any(r => r.Hostname == hostname))
                throw new CatalogException($"The file lists {hostname} more than once.");

            var addresses = new List<string>();
            foreach (string? raw in item.Addresses)
            {
                if (!IpAddressRules.TryNormalize(raw, out string address, out _, out string addressError))
                    throw new CatalogException($"{hostname}: \"{raw}\": {addressError}");
                if (!addresses.Contains(address)) addresses.Add(address);
            }

            result.Add(new TransferWebsite
            {
                Hostname = hostname,
                Addresses = addresses,
                Enabled = item.Enabled,
                SelectedIpv4 = NormalizeSelected(hostname, item.SelectedIpv4, IpFamily.IPv4, addresses),
                SelectedIpv6 = NormalizeSelected(hostname, item.SelectedIpv6, IpFamily.IPv6, addresses)
            });
        }
        return result;
    }

    private static string? NormalizeSelected(string hostname, string? selected, IpFamily family, List<string> addresses)
    {
        if (string.IsNullOrWhiteSpace(selected)) return null;
        if (!IpAddressRules.TryNormalize(selected, out string address, out IpFamily actual, out string error))
            throw new CatalogException($"{hostname}: \"{selected}\": {error}");
        if (actual != family)
            throw new CatalogException($"{hostname}: {address} is selected as {family} but is not an {family} address.");
        if (!addresses.Contains(address))
            throw new CatalogException($"{hostname}: the selected address {address} is not among its candidates.");
        return address;
    }

    private static (int WebsitesAdded, int AddressesAdded) Merge(AppConfiguration config, List<TransferWebsite> websites)
    {
        var catalog = new WebsiteCatalog(config);
        int websitesAdded = 0, addressesAdded = 0;
        foreach (TransferWebsite item in websites)
        {
            WebsiteEntry? website = catalog.FindByHostname(item.Hostname);
            if (website is null)
            {
                website = catalog.AddWebsite(item.Hostname);
                websitesAdded++;
            }
            foreach (string address in item.Addresses)
            {
                if (website.Addresses.Any(a => a.IpAddress == address)) continue;
                catalog.AddAddress(website.Id, address);
                addressesAdded++;
            }
        }
        return (websitesAdded, addressesAdded);
    }
}

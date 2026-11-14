using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou.Catalog;

/// <summary>A change was refused for a reason the user can act on.</summary>
public sealed class CatalogException(string message) : Exception(message);

/// <summary>
/// Adds, edits and removes websites and their candidate addresses. Candidates are never
/// removed implicitly; removing a website or address also clears references to it in every saved profile.
/// </summary>
public sealed class WebsiteCatalog
{
    private readonly AppConfiguration _config;

    public WebsiteCatalog(AppConfiguration config) => _config = config;

    public IReadOnlyList<WebsiteEntry> Websites => _config.Websites;

    public WebsiteEntry Get(Guid websiteId) =>
        _config.Websites.FirstOrDefault(w => w.Id == websiteId)
        ?? throw new CatalogException("That website no longer exists.");

    public WebsiteEntry? FindByHostname(string hostname) =>
        _config.Websites.FirstOrDefault(w => string.Equals(w.Hostname, hostname, StringComparison.OrdinalIgnoreCase));

    public WebsiteEntry AddWebsite(string hostname)
    {
        string normalized = NormalizeHostname(hostname);
        if (FindByHostname(normalized) is not null)
            throw new CatalogException($"{normalized} is already in the list.");

        var website = new WebsiteEntry { Hostname = normalized };
        _config.Websites.Add(website);
        return website;
    }

    public void RenameWebsite(Guid websiteId, string hostname)
    {
        WebsiteEntry website = Get(websiteId);
        string normalized = NormalizeHostname(hostname);
        WebsiteEntry? existing = FindByHostname(normalized);
        if (existing is not null && existing.Id != websiteId)
            throw new CatalogException($"{normalized} is already in the list.");
        website.Hostname = normalized;
    }

    public void RemoveWebsite(Guid websiteId)
    {
        WebsiteEntry website = Get(websiteId);
        _config.Websites.Remove(website);
        foreach (MappingProfile profile in _config.Profiles)
            profile.Selections.RemoveAll(s => s.WebsiteId == websiteId);
    }

    public AddressCandidate AddAddress(Guid websiteId, string ipAddress)
    {
        WebsiteEntry website = Get(websiteId);
        string normalized = NormalizeAddress(ipAddress, out _);
        if (website.Addresses.Any(a => a.IpAddress == normalized))
            throw new CatalogException($"{normalized} is already a candidate for {website.Hostname}.");

        var address = new AddressCandidate { IpAddress = normalized };
        website.Addresses.Add(address);
        return address;
    }

    public void EditAddress(Guid websiteId, Guid addressId, string ipAddress)
    {
        WebsiteEntry website = Get(websiteId);
        AddressCandidate address = GetAddress(website, addressId);
        string normalized = NormalizeAddress(ipAddress, out IpFamily family);
        if (website.Addresses.Any(a => a.Id != addressId && a.IpAddress == normalized))
            throw new CatalogException($"{normalized} is already a candidate for {website.Hostname}.");

        address.IpAddress = normalized;

        // A selection may only point at an address of its own family.
        foreach (ProfileWebsiteSelection selection in SelectionsFor(websiteId))
        {
            if (family != IpFamily.IPv4 && selection.SelectedIpv4Id == addressId) selection.SelectedIpv4Id = null;
            if (family != IpFamily.IPv6 && selection.SelectedIpv6Id == addressId) selection.SelectedIpv6Id = null;
        }
    }

    public void RemoveAddress(Guid websiteId, Guid addressId)
    {
        WebsiteEntry website = Get(websiteId);
        website.Addresses.Remove(GetAddress(website, addressId));
        foreach (ProfileWebsiteSelection selection in SelectionsFor(websiteId))
        {
            if (selection.SelectedIpv4Id == addressId) selection.SelectedIpv4Id = null;
            if (selection.SelectedIpv6Id == addressId) selection.SelectedIpv6Id = null;
        }
    }

    public static IpFamily FamilyOf(AddressCandidate address) =>
        IpAddressRules.FamilyOf(address.IpAddress)
        ?? throw new CatalogException($"{address.IpAddress} is not a valid IP address.");

    private IEnumerable<ProfileWebsiteSelection> SelectionsFor(Guid websiteId) =>
        _config.Profiles.SelectMany(p => p.Selections).Where(s => s.WebsiteId == websiteId);

    private static AddressCandidate GetAddress(WebsiteEntry website, Guid addressId) =>
        website.Addresses.FirstOrDefault(a => a.Id == addressId)
        ?? throw new CatalogException("That address no longer exists.");

    private static string NormalizeHostname(string hostname) =>
        HostnameRules.TryNormalize(hostname, out string normalized, out string error)
            ? normalized
            : throw new CatalogException(error);

    private static string NormalizeAddress(string ipAddress, out IpFamily family) =>
        IpAddressRules.TryNormalize(ipAddress, out string normalized, out family, out string error)
            ? normalized
            : throw new CatalogException(error);
}

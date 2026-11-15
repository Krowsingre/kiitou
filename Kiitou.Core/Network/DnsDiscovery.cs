using Kiitou.Catalog;
using Kiitou.Models;

namespace Kiitou.Network;

/// <summary>How the addresses published in DNS compare with a website's saved candidates.</summary>
public sealed record DiscoveryResult(
    DnsLookupResult Lookup,
    IReadOnlyList<string> NewAddresses,
    IReadOnlyList<string> AlreadySaved,
    IReadOnlyList<string> SavedButNotPublished);

/// <summary>
/// Compares current DNS records with saved candidates and adds the ones the user accepts.
/// Saved candidates are never removed here, whatever DNS says.
/// </summary>
public static class DnsDiscovery
{
    public static DiscoveryResult Compare(WebsiteEntry website, DnsLookupResult lookup)
    {
        var saved = website.Addresses.Select(a => a.IpAddress).ToList();
        var published = lookup.All.ToList();
        return new DiscoveryResult(
            lookup,
            published.Where(p => !saved.Contains(p)).ToList(),
            published.Where(saved.Contains).ToList(),
            lookup.Succeeded ? saved.Where(s => !published.Contains(s)).ToList() : []);
    }

    /// <summary>Adds the chosen discovered addresses as candidates; returns how many were new.</summary>
    public static int AddDiscovered(WebsiteCatalog catalog, Guid websiteId, IEnumerable<string> addresses)
    {
        WebsiteEntry website = catalog.Get(websiteId);
        int added = 0;
        foreach (string address in addresses.Distinct())
        {
            if (website.Addresses.Any(a => a.IpAddress == address)) continue;
            catalog.AddAddress(websiteId, address);
            added++;
        }
        return added;
    }
}

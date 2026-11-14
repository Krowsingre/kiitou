using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou.Catalog;

/// <summary>An enabled website that will not produce a mapping.</summary>
public sealed record SelectionIssue(Guid WebsiteId, string Hostname, string Message);

/// <summary>
/// Edits one set of per-website choices: which websites are enabled and which IPv4/IPv6
/// candidate is active for each. Disabling a website keeps its chosen addresses.
/// </summary>
public sealed class SelectionEditor
{
    private readonly AppConfiguration _config;

    public SelectionEditor(AppConfiguration config, List<ProfileWebsiteSelection> selections)
    {
        _config = config;
        Selections = selections;
    }

    public List<ProfileWebsiteSelection> Selections { get; }

    /// <summary>The website's selection, or a disabled, unassigned view of it if none is stored.</summary>
    public ProfileWebsiteSelection View(Guid websiteId) =>
        Selections.FirstOrDefault(s => s.WebsiteId == websiteId)
        ?? new ProfileWebsiteSelection { WebsiteId = websiteId };

    public void SetEnabled(Guid websiteId, bool enabled) => Stored(websiteId).Enabled = enabled;

    public void EnableAll()
    {
        foreach (WebsiteEntry website in _config.Websites) Stored(website.Id).Enabled = true;
    }

    public void DisableAll()
    {
        foreach (ProfileWebsiteSelection selection in Selections) selection.Enabled = false;
    }

    /// <summary>Chooses the active address of one family, or clears it with <c>null</c>.</summary>
    public void SelectAddress(Guid websiteId, IpFamily family, Guid? addressId)
    {
        WebsiteEntry website = Website(websiteId);
        if (addressId is Guid id)
        {
            AddressCandidate address = website.Addresses.FirstOrDefault(a => a.Id == id)
                ?? throw new CatalogException("That address is not a candidate for this website.");
            if (WebsiteCatalog.FamilyOf(address) != family)
                throw new CatalogException($"{address.IpAddress} is not an {family} address.");
        }

        ProfileWebsiteSelection selection = Stored(websiteId);
        if (family == IpFamily.IPv4) selection.SelectedIpv4Id = addressId;
        else selection.SelectedIpv6Id = addressId;
    }

    /// <summary>Drops choices that refer to websites or addresses that no longer exist.</summary>
    public void Prune()
    {
        Selections.RemoveAll(s => _config.Websites.All(w => w.Id != s.WebsiteId));
        foreach (ProfileWebsiteSelection selection in Selections)
        {
            WebsiteEntry website = Website(selection.WebsiteId);
            if (!IsCandidate(website, selection.SelectedIpv4Id, IpFamily.IPv4)) selection.SelectedIpv4Id = null;
            if (!IsCandidate(website, selection.SelectedIpv6Id, IpFamily.IPv6)) selection.SelectedIpv6Id = null;
        }
    }

    /// <summary>The hosts mappings these choices describe: enabled websites with an active address.</summary>
    public IReadOnlyList<HostsMapping> BuildMappings()
    {
        var mappings = new List<HostsMapping>();
        foreach (WebsiteEntry website in _config.Websites)
        {
            ProfileWebsiteSelection selection = View(website.Id);
            if (!selection.Enabled) continue;
            foreach (Guid? selected in new[] { selection.SelectedIpv4Id, selection.SelectedIpv6Id })
            {
                AddressCandidate? address = website.Addresses.FirstOrDefault(a => a.Id == selected);
                if (address is not null) mappings.Add(new HostsMapping(address.IpAddress, website.Hostname));
            }
        }
        return mappings;
    }

    public IReadOnlyList<SelectionIssue> FindIssues()
    {
        var issues = new List<SelectionIssue>();
        foreach (WebsiteEntry website in _config.Websites)
        {
            ProfileWebsiteSelection selection = View(website.Id);
            if (selection.Enabled && selection.SelectedIpv4Id is null && selection.SelectedIpv6Id is null)
                issues.Add(new SelectionIssue(website.Id, website.Hostname,
                    $"{website.Hostname} is enabled but has no address assigned, so it will not be mapped."));
        }
        return issues;
    }

    public int EnabledCount => _config.Websites.Count(w => View(w.Id).Enabled);

    /// <summary>An independent copy, so edits do not alter the source list.</summary>
    public static List<ProfileWebsiteSelection> Copy(IEnumerable<ProfileWebsiteSelection> selections) =>
        selections.Select(s => new ProfileWebsiteSelection
        {
            WebsiteId = s.WebsiteId,
            Enabled = s.Enabled,
            SelectedIpv4Id = s.SelectedIpv4Id,
            SelectedIpv6Id = s.SelectedIpv6Id
        }).ToList();

    /// <summary>True when two sets describe the same choices; a missing entry equals a disabled, unassigned one.</summary>
    public static bool AreEquivalent(
        IEnumerable<ProfileWebsiteSelection> first, IEnumerable<ProfileWebsiteSelection> second)
    {
        static Dictionary<Guid, (bool, Guid?, Guid?)> Meaningful(IEnumerable<ProfileWebsiteSelection> items) =>
            items.Where(s => s.Enabled || s.SelectedIpv4Id is not null || s.SelectedIpv6Id is not null)
                 .GroupBy(s => s.WebsiteId)
                 .ToDictionary(g => g.Key, g => (g.Last().Enabled, g.Last().SelectedIpv4Id, g.Last().SelectedIpv6Id));

        var a = Meaningful(first);
        var b = Meaningful(second);
        return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);
    }

    private ProfileWebsiteSelection Stored(Guid websiteId)
    {
        Website(websiteId);
        ProfileWebsiteSelection? selection = Selections.FirstOrDefault(s => s.WebsiteId == websiteId);
        if (selection is null)
        {
            selection = new ProfileWebsiteSelection { WebsiteId = websiteId };
            Selections.Add(selection);
        }
        return selection;
    }

    private WebsiteEntry Website(Guid websiteId) =>
        _config.Websites.FirstOrDefault(w => w.Id == websiteId)
        ?? throw new CatalogException("That website no longer exists.");

    private static bool IsCandidate(WebsiteEntry website, Guid? addressId, IpFamily family)
    {
        if (addressId is null) return true;
        AddressCandidate? address = website.Addresses.FirstOrDefault(a => a.Id == addressId);
        return address is not null && IpAddressRules.FamilyOf(address.IpAddress) == family;
    }
}

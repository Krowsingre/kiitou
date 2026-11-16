using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Verification;

namespace Kiitou.Session;

/// <summary>What the single header button would do, and whether the profile is live.</summary>
public enum ApplyState
{
    /// <summary>Kiitou manages nothing in the hosts file. The button applies the selected profile.</summary>
    NothingApplied,
    /// <summary>The hosts file holds exactly what is on screen. The button disables the profile.</summary>
    Active,
    /// <summary>The applied profile has been edited since. The button applies the changes.</summary>
    ChangesPending,
    /// <summary>Kiitou's mappings are in the hosts file but belong to no saved profile. The button clears them.</summary>
    UnknownMappings
}

/// <summary>How the hosts file currently relates to the selected profile.</summary>
public sealed record HostsStatus(
    int ManagedMappingCount,
    bool MatchesSelectedProfile,
    bool ExternalEditDetected,
    bool BlockDamaged,
    IReadOnlyList<HostsConflict> Conflicts,
    string? ReadError,
    ApplyState State = ApplyState.NothingApplied)
{
    /// <summary>While a profile is applied the profile drop-down stays on it.</summary>
    public bool ProfileLocked => State is ApplyState.Active or ApplyState.ChangesPending;
}

/// <summary>
/// The running application's state and every operation the user interface offers.
/// Website and profile changes are saved to the configuration immediately; enable/disable and
/// address choices are held in a working copy until the user saves them to a profile; the hosts
/// file changes only through <see cref="Apply"/>, <see cref="RemoveAllMappings"/> and <see cref="Restore"/>.
/// </summary>
public sealed class KiitouSession
{
    private readonly HostsApplyService _hosts;
    private readonly VerificationService _verification;
    private readonly IDnsResolver _dns;
    private readonly Func<DateTime> _utcNow;

    public KiitouSession(
        ConfigurationStore store, AppConfiguration config, HostsApplyService hosts,
        IDnsResolver dns, IAddressTester tester, IReputationProvider? reputation = null, Func<DateTime>? utcNow = null)
    {
        Store = store;
        Config = config;
        _hosts = hosts;
        _dns = dns;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _verification = new VerificationService(dns, tester, reputation, _utcNow);
        Catalog = new WebsiteCatalog(config);
        Profiles = new ProfileManager(config, _utcNow);
        Working = new SelectionEditor(config, SelectionEditor.Copy(Profiles.Active.Selections));
        FollowAppliedProfile();
    }

    /// <summary>
    /// If a profile is applied in the hosts file, makes it the selected one, because the
    /// selection is locked to the applied profile.
    /// </summary>
    private void FollowAppliedProfile()
    {
        try
        {
            Guid? applied = Config.Hosts.LastAppliedProfileId;
            if (applied is not Guid id || id == Config.ActiveProfileId) return;
            if (Config.Profiles.All(p => p.Id != id)) return;
            if (_hosts.Inspect().Analysis.ManagedMappings.Count == 0) return;
            Profiles.SetActive(id);
            ReloadWorking();
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The hosts file could not be read; the status shown to the user reports that.
        }
    }

    public ConfigurationStore Store { get; }
    public AppConfiguration Config { get; }
    public WebsiteCatalog Catalog { get; }
    public ProfileManager Profiles { get; }

    /// <summary>The unsaved enable/disable and address choices being edited.</summary>
    public SelectionEditor Working { get; private set; }

    public HostsBackupStore Backups => _hosts.Backups;

    public bool HasUnsavedChanges =>
        !SelectionEditor.AreEquivalent(Working.Selections, Profiles.Active.Selections);

    public void Save() => Store.Save(Config);

    // ---- Websites and addresses (saved immediately) ------------------------------------

    public WebsiteEntry AddWebsite(string hostname) => Saved(() => Catalog.AddWebsite(hostname));

    public void RenameWebsite(Guid websiteId, string hostname) => Saved(() => Catalog.RenameWebsite(websiteId, hostname));

    public void RemoveWebsite(Guid websiteId) => Saved(() =>
    {
        Catalog.RemoveWebsite(websiteId);
        Working.Prune();
    });

    public AddressCandidate AddAddress(Guid websiteId, string ipAddress) => Saved(() => Catalog.AddAddress(websiteId, ipAddress));

    public void EditAddress(Guid websiteId, Guid addressId, string ipAddress) => Saved(() =>
    {
        Catalog.EditAddress(websiteId, addressId, ipAddress);
        Working.Prune();
    });

    public void RemoveAddress(Guid websiteId, Guid addressId) => Saved(() =>
    {
        Catalog.RemoveAddress(websiteId, addressId);
        Working.Prune();
    });

    // ---- Profiles ----------------------------------------------------------------------

    /// <summary>Selects another profile and loads its saved choices, discarding unsaved ones.</summary>
    public void SwitchProfile(Guid profileId) => Saved(() =>
    {
        if (profileId != Config.ActiveProfileId && GetHostsStatus().ProfileLocked)
            throw new CatalogException($"\"{Profiles.Active.Name}\" is applied. Disable it before switching to another profile.");
        Profiles.SetActive(profileId);
        ReloadWorking();
    });

    public void SaveToCurrentProfile() => Saved(() => Profiles.SaveSelections(Config.ActiveProfileId, Working.Selections));

    /// <summary>Saves the working choices as a new profile and selects it. Existing profiles are unchanged.</summary>
    public MappingProfile SaveAsNewProfile(string name) => Saved(() =>
    {
        if (GetHostsStatus().ProfileLocked)
            throw new CatalogException($"\"{Profiles.Active.Name}\" is applied. Disable it before saving a new profile from it.");
        MappingProfile profile = Profiles.SaveAsNew(name, Working.Selections);
        Profiles.SetActive(profile.Id);
        ReloadWorking();
        return profile;
    });

    public MappingProfile CreateProfile(string name) => Saved(() => Profiles.Create(name));

    public void RenameProfile(Guid profileId, string name) => Saved(() => Profiles.Rename(profileId, name));

    public MappingProfile DuplicateProfile(Guid profileId) => Saved(() => Profiles.Duplicate(profileId));

    public void DeleteProfile(Guid profileId) => Saved(() =>
    {
        if (profileId == Config.ActiveProfileId && GetHostsStatus().ProfileLocked)
            throw new CatalogException($"\"{Profiles.Active.Name}\" is applied. Disable it before deleting it.");
        bool wasActive = Config.ActiveProfileId == profileId;
        Profiles.Delete(profileId);
        if (wasActive) ReloadWorking();
    });

    public string ExportProfile(Guid profileId) => ProfileTransfer.ExportProfile(Config, profileId, _utcNow());

    public ImportSummary ImportProfile(string json) => Saved(() => ProfileTransfer.ImportProfile(Config, json, _utcNow()));

    // ---- Hosts file --------------------------------------------------------------------

    /// <summary>What applying the selected profile, as saved, would do. Changes nothing.</summary>
    public HostsApplyPreview PreviewApply() =>
        _hosts.Preview(Profiles.BuildMappings(Config.ActiveProfileId), Config.Hosts.LastWrittenSha256);

    public HostsOperationResult Apply(HostsApplyPreview preview)
    {
        HostsOperationResult result = _hosts.Apply(preview);
        if (result.Succeeded) RecordWrite(result, Config.ActiveProfileId);
        return result;
    }

    /// <summary>
    /// Disables the applied profile: removes every Kiitou-managed mapping from the hosts file.
    /// Saved websites, addresses and profiles are unaffected.
    /// </summary>
    public HostsOperationResult RemoveAllMappings()
    {
        HostsOperationResult result = _hosts.Apply(_hosts.Preview([], Config.Hosts.LastWrittenSha256));
        if (result.Succeeded) RecordWrite(result, null);
        return result;
    }

    public HostsOperationResult Restore(string backupPath)
    {
        HostsOperationResult result = _hosts.Restore(backupPath);
        if (!result.Succeeded) return result;

        // If the restored file holds exactly a saved profile's mappings, that profile is applied again.
        IReadOnlyList<HostsMapping> restored = _hosts.Inspect().Analysis.ManagedMappings;
        MappingProfile? match = restored.Count == 0
            ? null
            : Config.Profiles.OrderByDescending(p => p.Id == Config.ActiveProfileId)
                .FirstOrDefault(p => Profiles.BuildMappings(p.Id).SequenceEqual(restored));
        if (match is not null && match.Id != Config.ActiveProfileId)
        {
            Profiles.SetActive(match.Id);
            ReloadWorking();
        }
        RecordWrite(result, match?.Id);
        return result;
    }

    public HostsStatus GetHostsStatus()
    {
        try
        {
            IReadOnlyList<HostsMapping> desired = Profiles.BuildMappings(Config.ActiveProfileId);
            HostsApplyPreview preview = _hosts.Preview(desired, Config.Hosts.LastWrittenSha256);
            IReadOnlyList<HostsMapping> managed = preview.Analysis.ManagedMappings;
            bool matches = managed.SequenceEqual(desired);

            ApplyState state;
            if (managed.Count == 0) state = ApplyState.NothingApplied;
            else if (Config.Hosts.LastAppliedProfileId != Config.ActiveProfileId) state = ApplyState.UnknownMappings;
            else state = managed.SequenceEqual(Working.BuildMappings()) ? ApplyState.Active : ApplyState.ChangesPending;

            return new HostsStatus(
                managed.Count, matches, preview.ExternalEditDetected,
                preview.Analysis.BlockDamaged, preview.Conflicts, null, state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostsStatus(0, false, false, false, [], ex.Message);
        }
    }

    /// <summary>The current hosts file text, for display only.</summary>
    public string ReadHostsText() => _hosts.Inspect().Snapshot.Text;

    // ---- Discovery and verification ----------------------------------------------------

    public async Task<DiscoveryResult> DiscoverAsync(Guid websiteId, CancellationToken cancellationToken)
    {
        WebsiteEntry website = Catalog.Get(websiteId);
        DnsLookupResult lookup = await _dns.ResolveAsync(website.Hostname, cancellationToken).ConfigureAwait(false);
        return DnsDiscovery.Compare(website, lookup);
    }

    /// <summary>Adds the discovered addresses the user accepted. Nothing is removed, enabled or selected.</summary>
    public int AddDiscovered(Guid websiteId, IEnumerable<string> addresses) =>
        Saved(() => DnsDiscovery.AddDiscovered(Catalog, websiteId, addresses));

    public async Task<AddressAssessment> VerifyAddressAsync(
        Guid websiteId, Guid addressId, DnsLookupResult? knownDns, CancellationToken cancellationToken)
    {
        WebsiteEntry website = Catalog.Get(websiteId);
        AddressCandidate address = website.Addresses.FirstOrDefault(a => a.Id == addressId)
            ?? throw new CatalogException("That address no longer exists.");
        AddressAssessment assessment = await _verification
            .VerifyAsync(website, address, knownDns, cancellationToken).ConfigureAwait(false);
        Save();
        return assessment;
    }

    /// <summary>Verifies every candidate of a website, in order.</summary>
    public async Task VerifyAllAsync(Guid websiteId, IProgress<AddressCandidate>? progress, CancellationToken cancellationToken)
    {
        WebsiteEntry website = Catalog.Get(websiteId);
        DnsLookupResult dns = await _dns.ResolveAsync(website.Hostname, cancellationToken).ConfigureAwait(false);
        foreach (AddressCandidate address in website.Addresses.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await VerifyAddressAsync(websiteId, address.Id, dns, cancellationToken).ConfigureAwait(false);
            progress?.Report(address);
        }
    }

    /// <summary>
    /// Verifies candidates in order and stops at the first whose connection works.
    /// The address is returned for the user to choose; it is not selected or applied.
    /// </summary>
    public async Task<AddressCandidate?> FindFirstWorkingAsync(
        Guid websiteId, IProgress<AddressCandidate>? progress, CancellationToken cancellationToken)
    {
        WebsiteEntry website = Catalog.Get(websiteId);
        DnsLookupResult dns = await _dns.ResolveAsync(website.Hostname, cancellationToken).ConfigureAwait(false);
        foreach (AddressCandidate address in website.Addresses.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddressAssessment assessment = await VerifyAddressAsync(websiteId, address.Id, dns, cancellationToken).ConfigureAwait(false);
            progress?.Report(address);
            if (assessment.Status is VerificationStatus.ConnectionVerified or VerificationStatus.ConnectionWorksVerificationIncomplete)
                return address;
        }
        return null;
    }

    // ---- Settings ----------------------------------------------------------------------

    public void SetTheme(string color, string mode) => Saved(() =>
    {
        if (!ThemeCatalog.IsValid(color, mode)) throw new CatalogException("That theme is not available.");
        Config.Theme.Color = color.ToLowerInvariant();
        Config.Theme.Mode = mode.ToLowerInvariant();
    });

    public void SetMinimizeToTray(bool enabled) => Saved(() => Config.Settings.MinimizeToTray = enabled);

    public void SetUseQuad9(bool enabled) => Saved(() => Config.Settings.UseQuad9 = enabled);

    // ---- Helpers -----------------------------------------------------------------------

    private void ReloadWorking() =>
        Working = new SelectionEditor(Config, SelectionEditor.Copy(Profiles.Active.Selections));

    private void RecordWrite(HostsOperationResult result, Guid? appliedProfileId)
    {
        Config.Hosts.LastWrittenSha256 = result.NewSha256;
        Config.Hosts.LastAppliedUtc = _utcNow();
        Config.Hosts.LastAppliedProfileId = appliedProfileId;
        Save();
    }

    private void Saved(Action change)
    {
        change();
        Save();
    }

    private T Saved<T>(Func<T> change)
    {
        T result = change();
        Save();
        return result;
    }
}

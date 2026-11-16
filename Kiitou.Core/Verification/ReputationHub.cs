using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Network;

namespace Kiitou.Verification;

/// <summary>Keeps API keys unreadable in the settings file.</summary>
public interface ISecretProtector
{
    string Protect(string secret);
    /// <summary>The original secret, or null when it cannot be recovered on this computer and account.</summary>
    string? Unprotect(string protectedSecret);
}

/// <summary>
/// Windows data protection tied to the signed-in user. A key protected here cannot be read by
/// another Windows account or on another computer, so it has to be entered again after a move.
/// </summary>
public sealed class WindowsSecretProtector : ISecretProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("Kiitou reputation service key");

    public string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Purpose, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedSecret)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Purpose, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads and changes the reputation-check settings. A row's service, key or server cannot be
/// changed while that row is switched on; it has to be switched off first.
/// </summary>
public sealed class ReputationSettings
{
    public const int KeySlotCount = 2;
    private readonly AppConfiguration _config;
    private readonly ISecretProtector _secrets;
    private readonly Action _save;

    public ReputationSettings(AppConfiguration config, ISecretProtector secrets, Action save)
    {
        _config = config;
        _secrets = secrets;
        _save = save;
    }

    private AppSettings Settings => _config.Settings;

    public bool UseQuad9 => Settings.UseQuad9;
    public IReadOnlyList<KeyServiceSlot> KeySlots => Settings.KeyServices;
    public CustomDnsSettings CustomDns => Settings.CustomDns;

    public void SetQuad9(bool enabled)
    {
        Settings.UseQuad9 = enabled;
        _save();
    }

    /// <summary>The key stored for a service, or null if there is none or it cannot be read here.</summary>
    public string? GetKey(string serviceId) =>
        Settings.ProtectedKeys.TryGetValue(serviceId, out string? stored) ? _secrets.Unprotect(stored) : null;

    public void SetSlotService(int slot, string serviceId)
    {
        KeyServiceSlot target = Slot(slot);
        if (target.Enabled) throw new CatalogException("Switch this service off before choosing a different one.");
        if (serviceId.Length > 0 && ReputationCatalog.Find(serviceId) is null)
            throw new CatalogException("That service is not available.");
        if (serviceId.Length > 0 && Settings.KeyServices.Any(s => !ReferenceEquals(s, target) && s.ServiceId == serviceId))
            throw new CatalogException($"{ReputationCatalog.Find(serviceId)!.Name} is already chosen in the other row.");
        target.ServiceId = serviceId;
        _save();
    }

    /// <summary>Stores, or with an empty value removes, the key for the service chosen in a row.</summary>
    public void SetKey(int slot, string key)
    {
        KeyServiceSlot target = Slot(slot);
        if (target.Enabled) throw new CatalogException("Switch this service off before changing its key.");
        if (target.ServiceId.Length == 0) throw new CatalogException("Choose a service first.");
        string trimmed = key.Trim();
        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl) || trimmed.Length > 512)
            throw new CatalogException("That does not look like an API key.");
        if (trimmed.Length == 0) Settings.ProtectedKeys.Remove(target.ServiceId);
        else Settings.ProtectedKeys[target.ServiceId] = _secrets.Protect(trimmed);
        _save();
    }

    public void SetSlotEnabled(int slot, bool enabled)
    {
        KeyServiceSlot target = Slot(slot);
        if (enabled)
        {
            if (target.ServiceId.Length == 0) throw new CatalogException("Choose a service first.");
            if (string.IsNullOrEmpty(GetKey(target.ServiceId)))
                throw new CatalogException($"Paste your {ReputationCatalog.Find(target.ServiceId)!.Name} API key first.");
        }
        target.Enabled = enabled;
        _save();
    }

    public void SetCustomDns(string name, string server)
    {
        if (Settings.CustomDns.Enabled) throw new CatalogException("Switch the custom DNS check off before changing it.");
        string trimmedName = name.Trim();
        string trimmedServer = server.Trim();
        if (trimmedName.Length > 40 || trimmedName.Any(char.IsControl))
            throw new CatalogException("The name can be at most 40 characters.");
        if (trimmedServer.Length > 0)
        {
            if (!IpAddressRules.TryNormalize(trimmedServer, out string normalized, out _, out _))
                throw new CatalogException("Enter the DNS server's IP address, such as 1.1.1.2.");
            trimmedServer = normalized;
        }
        Settings.CustomDns.Name = trimmedName;
        Settings.CustomDns.Server = trimmedServer;
        _save();
    }

    public void SetCustomDnsEnabled(bool enabled)
    {
        if (enabled && Settings.CustomDns.Server.Length == 0)
            throw new CatalogException("Enter the DNS server's IP address first.");
        Settings.CustomDns.Enabled = enabled;
        _save();
    }

    private KeyServiceSlot Slot(int slot) =>
        slot >= 0 && slot < Settings.KeyServices.Count
            ? Settings.KeyServices[slot]
            : throw new CatalogException("That service row does not exist.");
}

/// <summary>
/// Runs every reputation check that is switched on and combines the answers: any service
/// flagging the address means flagged; otherwise clean if at least one answered; otherwise
/// not checked. A service that could not be asked never counts as clean.
/// </summary>
public sealed class ReputationHub : IReputationProvider
{
    private readonly ReputationSettings _settings;
    private readonly IReputationProvider _quad9;
    private readonly HttpMessageHandler? _http;
    private readonly Func<IPAddress, IDnsResolver> _resolverFor;
    private readonly IDnsResolver _systemResolver;
    private readonly Dictionary<string, IReputationProvider> _providers = [];

    public ReputationHub(
        ReputationSettings settings, IReputationProvider? quad9 = null, HttpMessageHandler? http = null,
        Func<IPAddress, IDnsResolver>? resolverFor = null, IDnsResolver? systemResolver = null)
    {
        _settings = settings;
        _quad9 = quad9 ?? new Quad9ReputationProvider();
        _http = http;
        _resolverFor = resolverFor ?? (server => new DirectDnsResolver(() => [server]));
        _systemResolver = systemResolver ?? new DirectDnsResolver();
    }

    public async Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        var results = new List<ReputationResult>();
        foreach (IReputationProvider provider in ActiveProviders())
            results.Add(await provider.CheckAsync(hostname, ipAddress, cancellationToken).ConfigureAwait(false));
        return Combine(results);
    }

    public static ReputationResult Combine(IReadOnlyList<ReputationResult> results)
    {
        if (results.Count == 0)
            return new ReputationResult(ReputationVerdict.NotChecked, "none", "No reputation check is switched on.");

        var flagged = results.Where(r => r.Verdict == ReputationVerdict.Flagged).ToList();
        if (flagged.Count > 0)
            return new ReputationResult(ReputationVerdict.Flagged,
                string.Join(" and ", flagged.Select(r => r.Source)), string.Join(" ", flagged.Select(r => r.Detail)));

        var clean = results.Where(r => r.Verdict == ReputationVerdict.NoKnownProblems).ToList();
        var unknown = results.Where(r => r.Verdict == ReputationVerdict.NotChecked).ToList();
        string detail = string.Join(" ", clean.Concat(unknown).Select(r => r.Detail));
        return clean.Count > 0
            ? new ReputationResult(ReputationVerdict.NoKnownProblems, string.Join(" and ", clean.Select(r => r.Source)), detail)
            : new ReputationResult(ReputationVerdict.NotChecked, string.Join(" and ", unknown.Select(r => r.Source)), detail);
    }

    public Task<KeyTestResult> TestKeyAsync(string serviceId, string key, CancellationToken cancellationToken) =>
        CreateKeyed(serviceId, key.Trim()) is { } provider
            ? provider.TestKeyAsync(cancellationToken)
            : Task.FromResult(KeyTestResult.Rejected);

    /// <summary>Whether a DNS server at this address answers at all.</summary>
    public Task<bool> TestCustomDnsAsync(string server, CancellationToken cancellationToken) =>
        IPAddress.TryParse(server.Trim(), out IPAddress? address)
            ? FilteringDnsProvider.AnswersAsync(_resolverFor(address), cancellationToken)
            : Task.FromResult(false);

    private IEnumerable<IReputationProvider> ActiveProviders()
    {
        if (_settings.UseQuad9) yield return _quad9;

        foreach (KeyServiceSlot slot in _settings.KeySlots)
        {
            if (!slot.Enabled || _settings.GetKey(slot.ServiceId) is not { Length: > 0 } key) continue;
            if (Cached("key:" + slot.ServiceId + ":" + key.GetHashCode(), () => CreateKeyed(slot.ServiceId, key)) is { } provider)
                yield return provider;
        }

        CustomDnsSettings dns = _settings.CustomDns;
        if (dns.Enabled && IPAddress.TryParse(dns.Server, out IPAddress? server))
        {
            string name = dns.Name.Length > 0 ? dns.Name : "Custom DNS (" + dns.Server + ")";
            yield return Cached("dns:" + dns.Server + ":" + name,
                () => new FilteringDnsProvider(name, _resolverFor(server), _systemResolver))!;
        }
    }

    private IKeyedReputationProvider? CreateKeyed(string serviceId, string key) => serviceId switch
    {
        ReputationCatalog.AbuseIpDb => new AbuseIpDbProvider(key, _http),
        ReputationCatalog.VirusTotal => new VirusTotalProvider(key, _http),
        _ => null
    };

    /// <summary>Reuses a provider between checks so its own short-term cache keeps working.</summary>
    private IReputationProvider? Cached(string id, Func<IReputationProvider?> create)
    {
        lock (_providers)
        {
            if (_providers.TryGetValue(id, out IReputationProvider? existing)) return existing;
            IReputationProvider? created = create();
            if (created is not null) _providers[id] = created;
            return created;
        }
    }
}

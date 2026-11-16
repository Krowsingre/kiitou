using System.Collections.Concurrent;
using System.Net;
using Kiitou.Network;

namespace Kiitou.Verification;

/// <summary>
/// Asks a filtering DNS service whether it blocks a hostname. Such services answer "no such
/// name" (or a null address) for hostnames on their threat list. A hostname that the filtering
/// server refuses but an ordinary server resolves is therefore blocked.
/// Needs no account or key. Only the hostname is sent; the candidate IP address is not checked.
/// </summary>
public sealed class FilteringDnsProvider : IReputationProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly string _name;
    private readonly IDnsResolver _filtered;
    private readonly IDnsResolver _unfiltered;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<string, (ReputationResult Result, DateTime At)> _cache = new();

    public FilteringDnsProvider(string name, IDnsResolver filtered, IDnsResolver unfiltered, Func<DateTime>? utcNow = null)
    {
        _name = name;
        _filtered = filtered;
        _unfiltered = unfiltered;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(hostname, out var cached) && _utcNow() - cached.At < CacheLifetime)
            return cached.Result;

        DnsLookupResult filtered = await _filtered.ResolveAsync(hostname, cancellationToken).ConfigureAwait(false);
        bool refused = filtered.NameNotFound || (filtered.Succeeded && filtered.All.Any() && filtered.All.All(IsNullAddress));

        ReputationResult result;
        if (filtered.Succeeded && !refused)
        {
            result = new ReputationResult(ReputationVerdict.NoKnownProblems, _name,
                $"{_name} does not list this hostname as malicious. The address itself is not checked.");
        }
        else if (!refused)
        {
            // Never treat "could not ask" as "clean".
            return new ReputationResult(ReputationVerdict.NotChecked, _name, $"{_name} could not be reached.");
        }
        else
        {
            DnsLookupResult unfiltered = await _unfiltered.ResolveAsync(hostname, cancellationToken).ConfigureAwait(false);
            if (unfiltered.Succeeded && unfiltered.All.Any(a => !IsNullAddress(a)))
            {
                result = new ReputationResult(ReputationVerdict.Flagged, _name, $"{_name} blocks this hostname as malicious.");
            }
            else if (unfiltered.NameNotFound)
            {
                return new ReputationResult(ReputationVerdict.NotChecked, _name,
                    $"The hostname does not exist in public DNS, so {_name} has no opinion on it.");
            }
            else
            {
                return new ReputationResult(ReputationVerdict.NotChecked, _name, $"{_name} could not be reached.");
            }
        }

        _cache[hostname] = (result, _utcNow());
        return result;
    }

    /// <summary>True when the server answers at all, used by the Test button.</summary>
    public static async Task<bool> AnswersAsync(IDnsResolver server, CancellationToken cancellationToken)
    {
        DnsLookupResult result = await server.ResolveAsync("example.com", cancellationToken).ConfigureAwait(false);
        return result.Succeeded || result.NameNotFound;
    }

    private static bool IsNullAddress(string address) => address is "0.0.0.0" or "::";
}

/// <summary>
/// Quad9: its filtering resolver (9.9.9.9) is compared with its unfiltered one (9.9.9.10).
/// </summary>
public sealed class Quad9ReputationProvider : IReputationProvider
{
    public const string SourceName = "Quad9";
    private readonly FilteringDnsProvider _inner;

    public Quad9ReputationProvider()
        : this(new DirectDnsResolver(() => [IPAddress.Parse("9.9.9.9"), IPAddress.Parse("149.112.112.112")]),
               new DirectDnsResolver(() => [IPAddress.Parse("9.9.9.10"), IPAddress.Parse("149.112.112.10")]))
    {
    }

    public Quad9ReputationProvider(IDnsResolver filtered, IDnsResolver unfiltered, Func<DateTime>? utcNow = null) =>
        _inner = new FilteringDnsProvider(SourceName, filtered, unfiltered, utcNow);

    public Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken) =>
        _inner.CheckAsync(hostname, ipAddress, cancellationToken);
}

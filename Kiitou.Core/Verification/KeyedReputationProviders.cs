using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Kiitou.Verification;

/// <summary>A reputation service that needs the user's own account key.</summary>
public sealed record KeyServiceInfo(string Id, string Name, string Description, string SignUpUrl);

public static class ReputationCatalog
{
    public const string AbuseIpDb = "abuseipdb";
    public const string VirusTotal = "virustotal";

    /// <summary>The key-based services Kiitou knows how to talk to.</summary>
    public static readonly IReadOnlyList<KeyServiceInfo> KeyServices =
    [
        new(AbuseIpDb, "AbuseIPDB", "Checks the IP address.", "https://www.abuseipdb.com/register"),
        new(VirusTotal, "VirusTotal", "Checks the website and IP address.", "https://www.virustotal.com/gui/join-us")
    ];

    public static KeyServiceInfo? Find(string? id) => KeyServices.FirstOrDefault(s => s.Id == id);
}

public enum KeyTestResult
{
    Works,
    Rejected,
    LimitReached,
    Unreachable
}

public interface IKeyedReputationProvider : IReputationProvider
{
    /// <summary>Makes one harmless request to find out whether the service accepts the key.</summary>
    Task<KeyTestResult> TestKeyAsync(CancellationToken cancellationToken);
}

/// <summary>One authenticated JSON request, with every failure reduced to a status.</summary>
internal static class ReputationHttp
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    public static async Task<(HttpStatusCode? Status, JsonDocument? Json)> GetAsync(
        HttpMessageHandler? handler, string url, string keyHeader, string key, CancellationToken cancellationToken)
    {
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(keyHeader, key);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (response.StatusCode, null);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, JsonDocument.Parse(body));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return (null, null);
        }
    }

    public static KeyTestResult ToKeyTest(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.OK => KeyTestResult.Works,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => KeyTestResult.Rejected,
        HttpStatusCode.TooManyRequests => KeyTestResult.LimitReached,
        _ => KeyTestResult.Unreachable
    };

    /// <summary>The not-checked result for a request that did not return usable data.</summary>
    public static ReputationResult NotChecked(string name, HttpStatusCode? status) => new(
        ReputationVerdict.NotChecked, name, status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{name} rejected the API key.",
            HttpStatusCode.TooManyRequests => $"{name}'s request limit for this key has been reached.",
            HttpStatusCode.OK => $"{name} sent a reply Kiitou could not read.",
            _ => $"{name} could not be reached."
        });

    public static int Int(JsonElement element, params string[] path)
    {
        foreach (string name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return -1;
        }
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value) ? value : -1;
    }
}

/// <summary>AbuseIPDB: how confident its reporters are that an IP address is abusive.</summary>
public sealed class AbuseIpDbProvider : IKeyedReputationProvider
{
    public const string Name = "AbuseIPDB";
    /// <summary>Confidence, in percent, from which an address is treated as flagged.</summary>
    public const int FlagFromScore = 50;
    private const string Endpoint = "https://api.abuseipdb.com/api/v2/check?maxAgeInDays=90&ipAddress=";

    private readonly string _key;
    private readonly HttpMessageHandler? _handler;

    public AbuseIpDbProvider(string apiKey, HttpMessageHandler? handler = null)
    {
        _key = apiKey;
        _handler = handler;
    }

    public async Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        var (status, json) = await ReputationHttp
            .GetAsync(_handler, Endpoint + Uri.EscapeDataString(ipAddress), "Key", _key, cancellationToken).ConfigureAwait(false);
        using (json)
        {
            int score = json is null ? -1 : ReputationHttp.Int(json.RootElement, "data", "abuseConfidenceScore");
            if (score < 0) return ReputationHttp.NotChecked(Name, status);
            int reports = Math.Max(0, ReputationHttp.Int(json!.RootElement, "data", "totalReports"));
            return score >= FlagFromScore
                ? new ReputationResult(ReputationVerdict.Flagged, Name,
                    $"AbuseIPDB rates this address {score}% likely to be abusive, from {reports} report{(reports == 1 ? "" : "s")}.")
                : new ReputationResult(ReputationVerdict.NoKnownProblems, Name,
                    $"AbuseIPDB rates this address {score}% likely to be abusive, below its warning level.");
        }
    }

    public async Task<KeyTestResult> TestKeyAsync(CancellationToken cancellationToken)
    {
        var (status, json) = await ReputationHttp
            .GetAsync(_handler, Endpoint + "8.8.8.8", "Key", _key, cancellationToken).ConfigureAwait(false);
        json?.Dispose();
        return ReputationHttp.ToKeyTest(status);
    }
}

/// <summary>VirusTotal: how many of its scanners flag the IP address or the hostname.</summary>
public sealed class VirusTotalProvider : IKeyedReputationProvider
{
    public const string Name = "VirusTotal";
    /// <summary>Number of scanners calling something malicious from which it is treated as flagged.</summary>
    public const int FlagFromScanners = 2;
    private const string Api = "https://www.virustotal.com/api/v3/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly string _key;
    private readonly HttpMessageHandler? _handler;
    private readonly ConcurrentDictionary<string, (int Malicious, DateTime At)> _hostnames = new();

    public VirusTotalProvider(string apiKey, HttpMessageHandler? handler = null)
    {
        _key = apiKey;
        _handler = handler;
    }

    public async Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        var (addressStatus, addressFlags) = await FlagsAsync("ip_addresses/" + Uri.EscapeDataString(ipAddress), cancellationToken).ConfigureAwait(false);
        if (addressFlags < 0) return ReputationHttp.NotChecked(Name, addressStatus);

        int hostnameFlags;
        if (_hostnames.TryGetValue(hostname, out var cached) && DateTime.UtcNow - cached.At < CacheLifetime)
        {
            hostnameFlags = cached.Malicious;
        }
        else
        {
            (_, hostnameFlags) = await FlagsAsync("domains/" + Uri.EscapeDataString(hostname), cancellationToken).ConfigureAwait(false);
            if (hostnameFlags >= 0) _hostnames[hostname] = (hostnameFlags, DateTime.UtcNow);
        }

        if (addressFlags >= FlagFromScanners)
            return new ReputationResult(ReputationVerdict.Flagged, Name, $"{addressFlags} VirusTotal scanners flag this address as malicious.");
        if (hostnameFlags >= FlagFromScanners)
            return new ReputationResult(ReputationVerdict.Flagged, Name, $"{hostnameFlags} VirusTotal scanners flag this website as malicious.");
        return new ReputationResult(ReputationVerdict.NoKnownProblems, Name, hostnameFlags < 0
            ? "VirusTotal's scanners do not flag this address. The website name could not be checked."
            : "VirusTotal's scanners do not flag this address or website.");
    }

    public async Task<KeyTestResult> TestKeyAsync(CancellationToken cancellationToken)
    {
        var (status, json) = await ReputationHttp
            .GetAsync(_handler, Api + "ip_addresses/8.8.8.8", "x-apikey", _key, cancellationToken).ConfigureAwait(false);
        json?.Dispose();
        return ReputationHttp.ToKeyTest(status);
    }

    /// <summary>Number of scanners reporting "malicious", or -1 when the reply was unusable.</summary>
    private async Task<(HttpStatusCode? Status, int Malicious)> FlagsAsync(string path, CancellationToken cancellationToken)
    {
        var (status, json) = await ReputationHttp.GetAsync(_handler, Api + path, "x-apikey", _key, cancellationToken).ConfigureAwait(false);
        using (json)
        {
            int malicious = json is null
                ? -1
                : ReputationHttp.Int(json.RootElement, "data", "attributes", "last_analysis_stats", "malicious");
            return (status, malicious);
        }
    }
}

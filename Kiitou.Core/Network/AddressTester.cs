using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Kiitou.Network;

/// <summary>Everything observed while connecting to one candidate address for one hostname.</summary>
public sealed record AddressTestResult
{
    public required string Hostname { get; init; }
    public required string IpAddress { get; init; }
    public DateTime TestedUtc { get; init; }

    /// <summary>A TCP connection to port 443 of the address was established.</summary>
    public bool Connected { get; init; }
    /// <summary>The server presented a certificate that is trusted and valid for the hostname.</summary>
    public bool TlsValid { get; init; }
    public string? TlsError { get; init; }
    public string? CertificateSubject { get; init; }
    public string? CertificateIssuer { get; init; }
    public string? CertificateThumbprint { get; init; }
    public DateTime? CertificateNotAfterUtc { get; init; }

    public int? HttpStatus { get; init; }
    public string? RedirectLocation { get; init; }
    public string? PageTitle { get; init; }
    /// <summary>The reply looks like an interactive browser challenge that an automated test cannot complete.</summary>
    public bool ChallengeSuspected { get; init; }

    public double? ConnectMilliseconds { get; init; }
    public double? TotalMilliseconds { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// The defined connectivity criteria: reachable on 443, valid TLS for the hostname, and an HTTP reply.
    /// This says the connection works. It does not say the address is legitimate or the site safe.
    /// </summary>
    public bool Passed => Connected && TlsValid && HttpStatus is not null;
}

public interface IAddressTester
{
    Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Connects straight to a candidate IP address on port 443 while using the website's hostname
/// for TLS server name indication, certificate validation and the HTTP Host header.
/// It never reads or changes the hosts file and needs no administrator rights.
/// </summary>
public sealed partial class HttpsAddressTester : IAddressTester
{
    private const int MaximumBodyBytes = 64 * 1024;
    private readonly TimeSpan _timeout;
    private readonly int _port;

    public HttpsAddressTester(TimeSpan? timeout = null, int port = 443)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _port = port;
    }

    public async Task<AddressTestResult> TestAsync(string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        var result = new AddressTestResult { Hostname = hostname, IpAddress = ipAddress, TestedUtc = DateTime.UtcNow };
        if (!IPAddress.TryParse(ipAddress, out IPAddress? parsed))
            return result with { Error = "The address is not a valid IP address." };
        IPAddress address = parsed;

        bool connected = false;
        double? connectMs = null;
        SslPolicyErrors? policyErrors = null;
        X509Certificate2? certificate = null;
        var total = Stopwatch.StartNew();

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = _timeout,
            ConnectCallback = async (_, token) =>
            {
                // Always dial the candidate address, whatever the hostname resolves to.
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await socket.ConnectAsync(new IPEndPoint(address, _port), token).ConfigureAwait(false);
                    connectMs = stopwatch.Elapsed.TotalMilliseconds;
                    connected = true;
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, presented, _, errors) =>
                {
                    policyErrors = errors;
                    if (presented is not null) certificate = new X509Certificate2(presented);
                    return errors == SslPolicyErrors.None;
                }
            }
        };

        try
        {
            using var client = new HttpClient(handler) { Timeout = _timeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{hostname}:{_port}/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Kiitou/1.0");
            request.Headers.Accept.ParseAdd("text/html,*/*;q=0.8");

            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            string body = await ReadStartOfBodyAsync(response, cancellationToken).ConfigureAwait(false);

            result = result with
            {
                HttpStatus = (int)response.StatusCode,
                RedirectLocation = response.Headers.Location?.ToString(),
                PageTitle = ExtractTitle(body),
                ChallengeSuspected = LooksLikeChallenge(response, body)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            result = result with { Error = connected ? "The server did not reply in time." : "The connection attempt timed out." };
        }
        catch (HttpRequestException ex)
        {
            result = result with { Error = Describe(ex) };
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException)
        {
            result = result with { Error = ex.Message };
        }

        bool tlsValid = policyErrors == SslPolicyErrors.None;
        result = result with
        {
            Connected = connected,
            TlsValid = tlsValid,
            TlsError = policyErrors is null || tlsValid ? null : DescribeTls(policyErrors.Value),
            CertificateSubject = certificate?.Subject,
            CertificateIssuer = certificate?.Issuer,
            CertificateThumbprint = certificate?.Thumbprint,
            CertificateNotAfterUtc = certificate?.NotAfter.ToUniversalTime(),
            ConnectMilliseconds = connectMs,
            TotalMilliseconds = total.Elapsed.TotalMilliseconds
        };
        certificate?.Dispose();
        return result;
    }

    private static async Task<string> ReadStartOfBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[MaximumBodyBytes];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException)
        {
            return "";
        }
    }

    public static string? ExtractTitle(string body)
    {
        Match match = TitlePattern().Match(body);
        if (!match.Success) return null;
        string title = WebUtility.HtmlDecode(match.Groups[1].Value);
        title = WhitespacePattern().Replace(title, " ").Trim();
        if (title.Length == 0) return null;
        return title.Length > 200 ? title[..200] : title;
    }

    private static bool LooksLikeChallenge(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("cf-mitigated", out IEnumerable<string>? values) &&
            values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            return true;
        int status = (int)response.StatusCode;
        if (status is not (403 or 429 or 503)) return false;
        return body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(HttpRequestException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case AuthenticationException:
                    return "The secure connection could not be established: " + inner.Message;
                case SocketException socket:
                    return socket.SocketErrorCode switch
                    {
                        SocketError.ConnectionRefused => "The address refused the connection on port 443.",
                        SocketError.TimedOut => "The connection attempt timed out.",
                        SocketError.NetworkUnreachable or SocketError.HostUnreachable => "The address is unreachable from this network.",
                        _ => "Connection failed: " + socket.Message
                    };
            }
        }
        return exception.Message;
    }

    private static string DescribeTls(SslPolicyErrors errors)
    {
        var parts = new List<string>();
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) parts.Add("the server sent no certificate");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)) parts.Add("the certificate is not issued for this hostname");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)) parts.Add("the certificate is not trusted, has expired or is revoked");
        return "Certificate problem: " + string.Join("; ", parts) + ".";
    }

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

/// <summary>Runs tests over a website's candidates. Testing never selects or applies an address.</summary>
public static class AddressTestRunner
{
    /// <summary>Tests every address in order and returns all results.</summary>
    public static async Task<IReadOnlyList<AddressTestResult>> TestAllAsync(
        IAddressTester tester, string hostname, IEnumerable<string> ipAddresses,
        IProgress<AddressTestResult>? progress, CancellationToken cancellationToken)
    {
        var results = new List<AddressTestResult>();
        foreach (string ipAddress in ipAddresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddressTestResult result = await tester.TestAsync(hostname, ipAddress, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(result);
        }
        return results;
    }

    /// <summary>Tests addresses in order and stops at the first one that passes.</summary>
    public static async Task<(AddressTestResult? FirstWorking, IReadOnlyList<AddressTestResult> Tested)> FindFirstWorkingAsync(
        IAddressTester tester, string hostname, IEnumerable<string> ipAddresses,
        IProgress<AddressTestResult>? progress, CancellationToken cancellationToken)
    {
        var results = new List<AddressTestResult>();
        foreach (string ipAddress in ipAddresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddressTestResult result = await tester.TestAsync(hostname, ipAddress, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(result);
            if (result.Passed) return (result, results);
        }
        return (null, results);
    }
}

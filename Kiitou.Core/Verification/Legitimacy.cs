using Kiitou.Models;
using Kiitou.Network;

namespace Kiitou.Verification;

public enum ReputationVerdict
{
    NotChecked,
    NoKnownProblems,
    Flagged
}

public sealed record ReputationResult(ReputationVerdict Verdict, string Source, string Detail);

/// <summary>A threat-intelligence source that can be asked about an address.</summary>
public interface IReputationProvider
{
    Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken);
}

/// <summary>Used while no reputation service is configured. It never claims an address is clean.</summary>
public sealed class NoReputationProvider : IReputationProvider
{
    public static readonly ReputationResult NotConfigured =
        new(ReputationVerdict.NotChecked, "none", "No reputation service is configured.");

    public Task<ReputationResult> CheckAsync(string hostname, string ipAddress, CancellationToken cancellationToken) =>
        Task.FromResult(NotConfigured);
}

public enum ReferenceComparison
{
    /// <summary>No reference reply could be obtained to compare against.</summary>
    NotAvailable,
    /// <summary>The candidate serves the same certificate and status as an address published in DNS.</summary>
    Match,
    Mismatch
}

/// <summary>Compares a candidate's reply with the reply of an address currently published in DNS.</summary>
public static class ReferenceComparer
{
    public static ReferenceComparison Compare(AddressTestResult candidate, AddressTestResult? reference, out string detail)
    {
        if (reference is null || !reference.Passed)
        {
            detail = "No reply from a DNS-published address was available to compare with.";
            return ReferenceComparison.NotAvailable;
        }
        if (!candidate.Passed)
        {
            detail = "The candidate did not pass the connection test.";
            return ReferenceComparison.Mismatch;
        }

        bool sameCertificate =
            (candidate.CertificateThumbprint is not null && candidate.CertificateThumbprint == reference.CertificateThumbprint) ||
            (candidate.CertificateSubject is not null && candidate.CertificateSubject == reference.CertificateSubject &&
             candidate.CertificateIssuer == reference.CertificateIssuer);
        if (!sameCertificate)
        {
            detail = $"It presents a different certificate than the DNS-published address {reference.IpAddress}.";
            return ReferenceComparison.Mismatch;
        }
        if (candidate.HttpStatus != reference.HttpStatus)
        {
            detail = $"It replies with HTTP {candidate.HttpStatus}, but the DNS-published address {reference.IpAddress} replies with HTTP {reference.HttpStatus}.";
            return ReferenceComparison.Mismatch;
        }
        detail = $"It presents the same certificate and HTTP status as the DNS-published address {reference.IpAddress}.";
        return ReferenceComparison.Match;
    }
}

/// <summary>Everything known about one candidate address when it is assessed.</summary>
public sealed record AssessmentInput(
    string IpAddress,
    AddressTestResult? Test,
    DnsLookupResult? Dns,
    ReferenceComparison Reference,
    string ReferenceDetail,
    ReputationResult Reputation);

/// <summary>
/// Turns test evidence into one of the four statuses. It keeps three questions apart:
/// does the connection work, is there evidence the address belongs to the hostname, and is
/// anything known against it. No outcome claims that an address is authentic or a site safe.
/// </summary>
public static class LegitimacyAssessor
{
    public const string Caveat =
        "These checks confirm how the connection behaves. They cannot prove that an address is authentic or that a website is safe.";

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    public static string StatusText(VerificationStatus status) => status switch
    {
        VerificationStatus.ConnectionVerified => "Connection Verified",
        VerificationStatus.ConnectionWorksVerificationIncomplete => "Connection Works, Verification Incomplete",
        VerificationStatus.FailedOrUnsafe => "Failed or Unsafe",
        _ => "Not Tested"
    };

    public static bool IsStale(AddressAssessment? assessment, DateTime utcNow) =>
        assessment is not null && utcNow - assessment.AssessedUtc > StaleAfter;

    public static AddressAssessment Assess(AssessmentInput input, DateTime utcNow)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        AddressTestResult? test = input.Test;

        AddressAssessment Result(VerificationStatus status) => new()
        {
            Status = status,
            Reasons = reasons,
            Warnings = warnings,
            AssessedUtc = utcNow,
            HttpStatus = test?.HttpStatus,
            RedirectLocation = test?.RedirectLocation,
            CertificateSubject = test?.CertificateSubject,
            CertificateIssuer = test?.CertificateIssuer,
            CertificateNotAfterUtc = test?.CertificateNotAfterUtc,
            ConnectMilliseconds = test?.ConnectMilliseconds,
            TotalMilliseconds = test?.TotalMilliseconds
        };

        if (test is null)
        {
            reasons.Add("This address has not been tested.");
            return Result(VerificationStatus.NotTested);
        }

        // 1. Anything known against the address, or that makes it unusable.
        bool failed = false;
        if (!IpClassifier.IsPublic(input.IpAddress, out string notPublic))
        {
            reasons.Add($"Not a public internet address: it is {notPublic}.");
            failed = true;
        }
        if (input.Reputation.Verdict == ReputationVerdict.Flagged)
        {
            reasons.Add($"Flagged by {input.Reputation.Source}: {input.Reputation.Detail}");
            failed = true;
        }
        if (!test.Connected)
        {
            reasons.Add("Connection failed: " + (test.Error ?? "the address did not accept a connection on port 443."));
            failed = true;
        }
        else if (!test.TlsValid)
        {
            reasons.Add(test.TlsError ?? ("The secure connection failed: " + (test.Error ?? "no valid certificate was presented.")));
            failed = true;
        }
        else if (test.HttpStatus is null)
        {
            reasons.Add("No HTTP reply was received: " + (test.Error ?? "the server closed the connection."));
            failed = true;
        }
        if (failed) return Result(VerificationStatus.FailedOrUnsafe);

        // 2. The connection works.
        reasons.Add("Connected on port 443 with a certificate that is trusted and valid for the hostname.");
        reasons.Add($"The server replied with HTTP {test.HttpStatus}.");

        // 3. Evidence that the address belongs to the hostname.
        bool published = input.Dns is { Succeeded: true } && input.Dns.All.Contains(input.IpAddress);
        bool referenceMatch = input.Reference == ReferenceComparison.Match;

        if (published) reasons.Add("The address is currently published in DNS for this hostname.");
        else if (input.Dns is null || !input.Dns.Succeeded)
            warnings.Add("DNS could not be checked" + (input.Dns?.Error is null ? "." : ": " + input.Dns.Error));
        else
            warnings.Add("The address is not among those currently published in DNS for this hostname. On its own this is not evidence of a problem.");

        if (referenceMatch) reasons.Add(input.ReferenceDetail);
        else if (!published) warnings.Add(input.ReferenceDetail);

        // 4. Things worth knowing that do not change the status.
        if (input.Reputation.Verdict == ReputationVerdict.NotChecked)
            warnings.Add("Reputation was not checked: " + input.Reputation.Detail);
        else
            reasons.Add($"{input.Reputation.Source} reports no known problems.");
        if (test.ChallengeSuspected)
            warnings.Add("The site answered with a browser challenge. Confirm it loads correctly in your browser.");
        if (test.HttpStatus >= 500)
            warnings.Add($"The server reported an error (HTTP {test.HttpStatus}).");
        if (test.RedirectLocation is not null)
            reasons.Add("The site redirects to " + test.RedirectLocation + " (the redirect target was not tested).");
        if (test.CertificateNotAfterUtc is DateTime expiry && expiry - utcNow < TimeSpan.FromDays(7))
            warnings.Add("The certificate expires within seven days.");

        return Result(published || referenceMatch
            ? VerificationStatus.ConnectionVerified
            : VerificationStatus.ConnectionWorksVerificationIncomplete);
    }
}

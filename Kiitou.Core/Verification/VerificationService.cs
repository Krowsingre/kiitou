using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Network;

namespace Kiitou.Verification;

/// <summary>
/// Runs the full set of checks for a candidate address and stores the outcome with it.
/// It never enables a website, selects an address or touches the hosts file.
/// </summary>
public sealed class VerificationService
{
    private readonly IDnsResolver _dns;
    private readonly IAddressTester _tester;
    private readonly IReputationProvider _reputation;
    private readonly Func<DateTime> _utcNow;

    public VerificationService(
        IDnsResolver dns, IAddressTester tester, IReputationProvider? reputation = null, Func<DateTime>? utcNow = null)
    {
        _dns = dns;
        _tester = tester;
        _reputation = reputation ?? new NoReputationProvider();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <param name="knownDns">A lookup already made for this hostname, to avoid repeating it.</param>
    public async Task<AddressAssessment> VerifyAsync(
        WebsiteEntry website, AddressCandidate address, DnsLookupResult? knownDns, CancellationToken cancellationToken)
    {
        DnsLookupResult dns = knownDns ?? await _dns.ResolveAsync(website.Hostname, cancellationToken).ConfigureAwait(false);
        AddressTestResult test = await _tester.TestAsync(website.Hostname, address.IpAddress, cancellationToken).ConfigureAwait(false);

        ReferenceComparison reference = ReferenceComparison.NotAvailable;
        string referenceDetail = "No reply from a DNS-published address was available to compare with.";
        bool published = dns.Succeeded && dns.All.Contains(address.IpAddress);
        if (test.Passed && !published && dns.Succeeded)
        {
            string? referenceAddress = PickReference(dns, address.IpAddress);
            if (referenceAddress is not null)
            {
                AddressTestResult referenceTest = await _tester
                    .TestAsync(website.Hostname, referenceAddress, cancellationToken).ConfigureAwait(false);
                reference = ReferenceComparer.Compare(test, referenceTest, out referenceDetail);
            }
        }

        ReputationResult reputation;
        try
        {
            reputation = await _reputation.CheckAsync(website.Hostname, address.IpAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            reputation = new ReputationResult(ReputationVerdict.NotChecked, "reputation service", "The reputation service could not be reached.");
        }

        AddressAssessment assessment = LegitimacyAssessor.Assess(
            new AssessmentInput(address.IpAddress, test, dns, reference, referenceDetail, reputation), _utcNow());
        address.LastAssessment = assessment;
        return assessment;
    }

    /// <summary>A DNS-published address to compare against, preferring the candidate's own family.</summary>
    private static string? PickReference(DnsLookupResult dns, string candidate)
    {
        IpFamily? family = IpAddressRules.FamilyOf(candidate);
        IReadOnlyList<string> preferred = family == IpFamily.IPv6 ? dns.Ipv6 : dns.Ipv4;
        return preferred.FirstOrDefault() ?? dns.All.FirstOrDefault();
    }
}

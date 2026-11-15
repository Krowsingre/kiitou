# Kiitou Pass 5 Audit: DNS Discovery and Connectivity Testing

Pass 5 delivers DNS discovery and address testing as tested library code. The user interface
for it belongs to Pass 7.

## Implemented

| Requirement (task list 6, 7) | Where |
| --- | --- |
| Discover current A and AAAA records | `DirectDnsResolver` queries the computer's configured DNS servers directly (UDP, TCP when truncated), so answers never come from the hosts file or the local cache |
| Compare to saved candidates and offer new addresses | `DnsDiscovery.Compare` |
| Do not automatically delete saved candidates | `DnsDiscovery.AddDiscovered` only adds what the user accepts |
| Test an individual address | `HttpsAddressTester.TestAsync` |
| Test All (exhausts the list) | `AddressTestRunner.TestAllAsync` |
| Find First Working (stops at first pass) | `AddressTestRunner.FindFirstWorkingAsync` |
| Connect directly to the chosen IP on 443, keeping SNI, Host and hostname validation | The request is made for `https://hostname/` while the connection is dialled to the candidate IP; proxies are bypassed |
| Inspect TLS, response, redirects, errors, timings | `AddressTestResult` (certificate subject, issuer, thumbprint, expiry, policy errors, status, redirect target, page title, connect and total time, error text) |
| Never modify the hosts file or apply an address as a side effect | The test code has no reference to selections or to the hosts engine |
| Cases needing manual browser verification | `AddressTestResult.ChallengeSuspected` flags replies that look like interactive browser challenges |

The pass criterion is: TCP connection on 443, a certificate trusted and valid for the hostname,
and any HTTP reply. Redirects are recorded, not followed.

## Audit 1: Functional

Automated tests cover the DNS wire format (queries, aliases, duplicates, error codes, truncation,
malformed replies), the resolver against a local DNS server (normal, truncated then TCP,
non-existent name, silent server, no server), discovery comparison, Test All and Find First
Working sequencing, cancellation, an untrusted certificate, a closed port, and title extraction.

On the build runner a live check resolves a real hostname, confirms a direct connection to its
published address passes, and confirms the same hostname sent to a different site's address
fails certificate validation.

## Audit 2: Technical

- Certificate validation is never relaxed; a failed validation ends the connection.
- At most 64 KB of the reply body is read.
- No administrator rights are needed.

## Audit 3: Scope

Only discovery and connectivity testing were added. Legitimacy classification belongs to
Pass 6. The TLS protocol version is not recorded; certificate details and policy errors are.

## Regression

All earlier tests and smoke checks still run in the same build.

## Pass gate

**Passed on automated evidence** (see the build for this pass). No alteration to the approved
scope is requested.

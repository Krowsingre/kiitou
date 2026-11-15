# Kiitou Pass 6 Audit: Legitimacy and Security Assessment

Pass 6 delivers the assessment logic as tested library code. The user interface for it
belongs to Pass 7.

## Implemented

| Requirement (task list 8) | Where |
| --- | --- |
| Public-routability | `IpClassifier.IsPublic` (private, loopback, link-local, carrier-grade NAT, documentation, benchmarking, multicast and reserved ranges for IPv4 and IPv6) |
| DNS association | Whether the address is among the A/AAAA records currently published for the hostname |
| Certificate validation and HTTP response | Taken from the Pass 5 connection test |
| Reference content comparison | `ReferenceComparer`: when a working candidate is not published in DNS, a DNS-published address is tested too and certificate and HTTP status are compared |
| Supported reputation service | `IReputationProvider` extension point; see "Open item" |
| Separate connection, association and safety claims | `LegitimacyAssessor.Assess` evaluates them in separate steps and lists each as its own reason or warning |
| Four statuses, exactly | `Connection Verified`; `Connection Works, Verification Incomplete`; `Failed or Unsafe`; `Not Tested` |
| Explain failures, warnings, staleness | Every assessment stores its reasons, warnings and time; `IsStale` after 24 hours |
| Never claim complete authenticity or safety | No status or message says an address is authentic or safe; `LegitimacyAssessor.Caveat` states the limit |

Status rules:

- **Failed or Unsafe**: the connection, certificate or HTTP reply failed; or the address is not a
  public internet address; or a reputation source flags it.
- **Connection Verified**: the connection works **and** the address is either published in DNS
  for the hostname or serves the same certificate and status as a published address.
- **Connection Works, Verification Incomplete**: the connection works but neither kind of
  association evidence is available. A missing DNS match alone is described as not being
  evidence of a problem.
- **Not Tested**: no test has been run.

A clean reputation result never upgrades a status. Assessments are stored with the address in
`config.json` and cleared when the address is edited. Verifying never enables, selects or
applies anything.

## Open item for the user

The specification asks for a "supported reputation service" but does not name one. Every real
service needs an account or API key and means sending hostnames and IP addresses to a third
party. No service was chosen or contacted. Until one is approved, each assessment carries the
warning "Reputation was not checked: No reputation service is configured." Adding a provider
later is a single class implementing `IReputationProvider`.

Private addresses (for example an intranet server) are classed as Failed or Unsafe because the
task list requires a public-routability check. They can still be selected and applied manually.

## Audit 1: Functional

Automated tests cover 33 address classifications, every status, each failure cause, flagged and
clean reputation, warnings, reference comparison, staleness, the verification service flow, and
storage of assessments.

## Audit 2: Technical

- A reputation service that cannot be reached is reported as not checked, never as clean.
- A test asserts that no status text or reason claims safety or authenticity.

## Audit 3: Scope

Only assessment behavior was added. No UI was added.

## Regression

All earlier tests and smoke checks still run in the same build.

## Pass gate

**Passed on automated evidence** (see the build for this pass), with the reputation service
choice open. No alteration to the approved scope is requested.

# Kiitou Pass 3 Audit: Websites and Candidate Addresses

Pass 3 delivers website and address management as tested library code. The user interface
for it belongs to Pass 7.

## Implemented

| Requirement (task list 3, 4) | Where |
| --- | --- |
| Add, edit, remove websites | `WebsiteCatalog.AddWebsite`, `RenameWebsite`, `RemoveWebsite` |
| Multiple IPv4/IPv6 candidates per website; add, edit, remove | `WebsiteCatalog.AddAddress`, `EditAddress`, `RemoveAddress` |
| Enable/disable individually, all, none, any subset | `SelectionEditor.SetEnabled`, `EnableAll`, `DisableAll` |
| Manually select the active address per family; keep other candidates | `SelectionEditor.SelectAddress` |
| Preserve a disabled website's configuration | Disabling changes only the enabled flag |
| Validate IP addresses, duplicates and conflicts | `IpAddressRules`, `HostnameRules`; duplicate hostnames and duplicate addresses per website are refused; a selection must match its address family |
| Persist | Stored in `config.json`; invalid stored hostnames, addresses or selections are rejected on load without overwriting the file |

An enabled website with no address assigned produces no mapping and is reported by
`SelectionEditor.FindIssues`. Nothing is ever selected automatically.

## Audit 1: Functional

Automated tests cover adding, renaming and removing websites; adding, editing and removing
addresses; normalization; duplicates; enabling one, many, all and none; per-family selection;
retention of choices while disabled; cleanup of references when an address or website is
removed; persistence across save and load; and rejection of invalid stored data.

## Audit 2: Technical

- Removing a website or address clears references in every saved profile, so no saved
  selection can point at something that no longer exists.
- Editing an address to the other family clears selections that would then be mismatched.
- Unsaved working copies are cleaned with `SelectionEditor.Prune`.

## Audit 3: Scope

Only website and candidate-address behavior was added. Public-routability checks belong to
Pass 6 and conflicts with existing hosts-file lines were delivered in Pass 2. No profile
management, DNS, testing or UI behavior was added.

## Regression

All Pass 1 and Pass 2 tests and smoke checks still run in the same build.

## Pass gate

**Passed on automated evidence** (see the build for this pass). No alteration to the approved
scope is requested.

# KIITOU: Canonical Product Definition and Requirements
Last compiled: 2026-11-14. This is a handoff of approved scope, not permission to expand it.

## Purpose
Kiitou is a portable native Windows hosts-file manager with named profiles, multiple address candidates per hostname, DNS discovery, connectivity testing and limited legitimacy/security assessment. It modifies C:\Windows\System32\drivers\etc\hosts, NOT the Registry. Its motivation is helping users choose alternative domain IP mappings. No VPN, proxy, browser, background monitoring system or subscription service is required.

## Website and candidate model
- Each website has a hostname and any number of candidate IPv4/IPv6 addresses.
- Add, edit, delete and retain websites and candidate addresses.
- Enable or disable one, some, all, or no websites.
- Choose an active IPv4 address and, if desired, active IPv6 address for each hostname; all unused candidates remain saved.
- Validate syntax, public routability, duplicate addresses and conflicting mappings.
- Disabling a site does not erase its saved candidates or its configuration.
- Saved entries persist separately from the system hosts file.

## Profiles
- Create, rename, duplicate, edit, delete, save, switch, import and export profiles.
- Profiles store enabled websites plus their chosen IPv4/IPv6 address IDs.
- Default profile has no applied mappings.
- Changes to individual toggles or selections must not automatically overwrite a saved profile; user can save into current profile or new profile.
- Applying a profile updates only Kiitou-managed hosts-file entries. Profile switching must recreate that profile's specified mapping set.

## DNS discovery and network tests
- Discover DNS A and AAAA records and offer new IPs to add; never automatically delete old candidates.
- Test individual candidates, Test All, and Find First Working (sequential stop at first success).
- Testing must connect directly to candidate IP without changing Windows hosts file, while using the intended domain hostname for TLS SNI, certificate name validation and HTTP Host.
- Attempt HTTPS on port 443, inspect certificate, redirects, response and errors, and record response time and details.
- A green light signals meeting defined connection success criteria, NOT a guarantee of legitimacy or malware-free safety. Offer user manual selection, not automatic application.
- Some browser-challenge cases need final manual browser verification.
- The network test process should not require administrator access.

## Address legitimacy/security
Separate (1) a working network connection, (2) evidence that IP is legitimate for the hostname, and (3) website safety. Checks: valid public IP, currently published DNS relationship, TLS trust and hostname, HTTP response, optional available threat-intelligence reputation, and comparison with an established site reference. No test definitively proves authenticity. No DNS match alone is not conclusive evidence of fraud.
Four primary UI statuses exactly: Connection Verified; Connection Works, Verification Incomplete; Failed or Unsafe; Not Tested. Include reasons and staleness indicators.

## Hosts file and permissions
Read and inspect the Windows hosts file; label Kiitou-owned records; preserve non-Kiitou lines and third-party edits. Back up before modification; detect external changes before writing; use safe temporary replacement, maintain restoration capability, flush DNS after applicable edits, and verify written mappings. Ordinary configuration requires no administrator rights; protected writes request UAC. If elevation is refused/canceled, do not modify hosts file.

## Platform, storage, and distribution
C# / .NET 10 / WPF. Portable self-contained Windows .exe, no installer, separate .NET installation, Python runtime, server, user accounts, subscription or mandatory background service. Local versioned JSON configuration. Prefer writable executable folder for portable mode; fall back to Local AppData in protected locations. All software delivery to user after each pass must be ONLY the single Kiitou.exe (not ZIP/source); source/docs/assets live in GitHub.

## UI approval and sizes
Approved UI is the originally selected dark Windows utility screenshot titled "Kiitou – Host Mapping Manager": header profile selector and Apply Selected Profile control, left navigation Home/Websites/Profiles/Discovery & Test/Verification/Hosts File/Settings, compact website/selected-IP/status table, bottom profile management controls and status bar. The screen is NOT fullscreen. Ghostblood 720x450 px was cited as lower app size; Whisper Light upper app size was never verified. The approved picture is 1536x1024 pixels as an illustration, not mandated window dimensions. Later home dashboard illustrations are exploratory concepts, NOT approved replacements.

## Color and icon systems
Seven APPROVED icon-color families: blue, gold, green, purple, red, silver, teal. Each has dark and light modes (14 appearance combinations). Changing the color family changes app-window and tray icons to the matching approved ICO. Switching light/dark retains family icon. Windows compiled shortcut icon may remain fixed/cached. The original seven ICO + PNG masters are the correct art; do not swap in approximations. Theme behavior is implemented across existing passes, not an additional pass.

## Explicit exclusions
No cloud synchronization/accounts, network monitoring/continuous testing, subscription features, browser automation as main test engine, automatically applying successful candidates, automatically deleting previously saved addresses, fullscreen interface, or mandatory background service.

## Change governance
This is a LOCKED task scope. No adding, removing, modifying or expanding requirements without user's explicit approval. Corrective fixes within scope may be made and retested. Each pass requires functional, technical and scope audits plus regression checks. If deviation is necessary, present an alteration proposal detailing original requirement, observed issue, proposed alternative, impacts and request approval before action.

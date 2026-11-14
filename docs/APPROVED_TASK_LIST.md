# KIITOU: Approved 13-Section Task Checklist
Locked requirements, not a report of completed implementation. All unchecked until corresponding tested software is verified. Later explicitly approved amendments: seven light/dark paired themes, matching icons, user receives only standalone EXE per pass.

## 1. Application foundation
- [ ] C# / .NET 10 / native WPF project
- [ ] Portable self-contained Windows executable; no standalone framework installation
- [ ] No installer, server, cloud account, subscription, mandatory background service
- [ ] Local JSON configuration, writable executable directory or AppData fallback
- [ ] Persist entries and profiles across launches

## 2. Compact window/UI
- [ ] Approved original UI layout, not unapproved dashboard replacements
- [ ] Not fullscreen; 720x450 Ghostblood lower reference; Whisper Light upper reference requires verification
- [ ] Entries, Profiles, Settings and approved left navigation sections
- [ ] Search and filter, entry statuses, optional tray
- [ ] Seven icon colors with dark/light UI = 14 combinations; icon changes with color theme

## 3. Website management
- [ ] Add, edit, remove websites
- [ ] Individually enable/disable; enable all; disable all; arbitrary simultaneous subset
- [ ] Preserve disabled website configuration and persistence

## 4. Candidate address management
- [ ] Multiple IPv4/IPv6 IP candidates per website
- [ ] Add, edit, remove IP candidates
- [ ] Manually select per-family active address, preserve other candidates
- [ ] Validate IP addresses, duplicate entries and conflicts

## 5. Profiles
- [ ] Create, rename, duplicate, edit, delete, import, export
- [ ] Save enabled website combinations and selected IPs
- [ ] Default profile with none enabled
- [ ] Save changes to active profile or to new profile, no silent overwrite
- [ ] Switch and apply exact profile mappings

## 6. DNS discovery
- [ ] Discover current A and AAAA records
- [ ] Compare to saved candidates and offer new addresses
- [ ] Do not automatically delete saved candidates

## 7. Address testing
- [ ] Test individual address, Test All, Find First Working
- [ ] Validate candidate, connect to chosen IP directly using HTTPS 443, preserve SNI/Host and hostname validation
- [ ] Inspect TLS, response, redirects, errors, timings
- [ ] Find First Working stops at first passing candidate; Test All exhausts list
- [ ] Green indicator for defined connectivity checks, offer manual selection
- [ ] Never modify hosts file or automatically apply IP as side effect of testing
- [ ] Manual browser verification for cases where ordinary HTTP test is insufficient

## 8. Legitimacy and safety
- [ ] Public-routability, DNS association, certificate validation, HTTP response
- [ ] Supported reputation service and reference content comparison
- [ ] Separate connection, association, and safety claims
- [ ] Four statuses: Connection Verified; Connection Works, Verification Incomplete; Failed or Unsafe; Not Tested
- [ ] Explain failures, warnings, staleness; never claim complete authenticity/safety

## 9. System hosts file
- [ ] Read current Windows hosts file; identify Kiitou-managed lines
- [ ] Apply selected enabled mappings; remove only Kiitou-managed disabled mappings
- [ ] Preserve unrelated entries and modifications
- [ ] Back up before writing, detect external edits, replace safely
- [ ] Flush DNS after applicable changes; verify written state

## 10. Permissions
- [ ] Configuration editing without elevation
- [ ] UAC only for privileged hosts write
- [ ] Cancelled/denied elevation leaves hosts file untouched

## 11. Restore and recovery
- [ ] First-change backup, continued backups as needed, restore operation
- [ ] Safe recovery without unintended third-party modifications
- [ ] Collections import/export, configuration independent from applied hosts state

## 12. Packaging and distribution
- [ ] Self-contained standalone Kiitou.exe
- [ ] Direct EXE from GitHub Releases, no ZIP/source delivery to user
- [ ] Clean Windows testing, code-signing optional later improvement

## 13. Testing and verification
- [ ] CRUD and persistence of sites, candidate IPs and profiles
- [ ] Profile switching and selected IPv4/IPv6 mapping
- [ ] DNS/HTTPS/TLS/verification classifications
- [ ] Site activation one/many/all/none, conflicts
- [ ] Hosts backups, restore, unrelated lines, external edits, UAC cancel
- [ ] Unexpected termination, storage folder protections
- [ ] 14 theme schemes, icon matching and compact window sizing
- [ ] Real clean Windows EXE test and release verification

## Scope lock
No changes to requirements without approval. Every one of the nine passes requires functional, technical and scope audits plus regression checks; pass failure or missing Windows runtime evidence prevents advancement.

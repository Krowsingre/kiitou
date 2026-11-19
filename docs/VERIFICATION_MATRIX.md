# Kiitou Verification Matrix

Each item of `APPROVED_TASK_LIST.md`, with what was verified and how. Items changed by the user
afterwards are described in `AMENDMENTS.md`. The checkboxes in the task
list itself are left for the user to tick.

Legend: **A** = automated test on the Windows build runner. **E** = end-to-end run of the
published executable on the runner. **S** = screenshot reviewed. **Open** = not verified or not
decided.

## 1. Application foundation

| Item | Evidence |
| --- | --- |
| C# / .NET 10 / native WPF project | A (builds and runs on the runner) |
| Portable self-contained executable; no framework installation | E (single `Kiitou.exe`, no sidecar files). **Open:** a machine with no .NET at all |
| No installer, server, cloud account, subscription, background service | By construction; none exist in the code |
| Local JSON configuration; executable folder or AppData fallback | A, E (portable folder and write-denied folder) |
| Persist entries and profiles across launches | A, E |

## 2. Compact window / UI

| Item | Evidence |
| --- | --- |
| Approved original layout | S. **Open:** user's visual sign-off |
| Not fullscreen; between 720 x 450 and Echo Light's 1093 x 622 | E (window opens at 1040 x 622 and refuses to change size), per `AMENDMENTS.md` |
| Entries, Profiles, Settings and left navigation | E (all six views opened; Verification merged into Discovery & Test, since renamed Inspector, per `AMENDMENTS.md`) |
| Search and filter, entry statuses, optional tray | S for search, filter and statuses; E for starting with the tray icon on. **Open:** minimizing to and restoring from the tray by hand |
| Seven icon colors x dark/light = 14; icon changes with color | E (all 14 start and close cleanly), S |

## 3. Website management

| Item | Evidence |
| --- | --- |
| Add, edit, remove websites | A. **Open:** typing into the dialogs by hand |
| Enable/disable individually, all, none, any subset | A |
| Preserve disabled configuration; persistence | A |

## 4. Candidate address management

| Item | Evidence |
| --- | --- |
| Multiple IPv4/IPv6 candidates | A |
| Add, edit, remove candidates | A |
| Manually select per-family active address; keep others | A |
| Validate addresses, duplicates, conflicts | A |

## 5. Profiles

| Item | Evidence |
| --- | --- |
| Create, rename, duplicate, edit, delete, import, export | A |
| Save enabled combinations and selected IPs | A |
| Default profile with none enabled | A |
| Save to active or new profile; no silent overwrite | A |
| Switch and apply exact profile mappings | A, E (apply, disable, and restore through the window; profile list locked while applied) |

## 6. DNS discovery

| Item | Evidence |
| --- | --- |
| Discover current A and AAAA records | A (local DNS server and a live lookup) |
| Compare with saved candidates; offer new addresses | A |
| Never delete saved candidates automatically | A |

## 7. Address testing

| Item | Evidence |
| --- | --- |
| Test one, Test All, Find First Working | A |
| Direct connection on 443 keeping SNI, Host and hostname validation | A (live connection; a deliberate hostname/address mismatch fails) |
| Inspect TLS, response, redirects, errors, timings | A. Note: the TLS protocol version is not recorded |
| Find First Working stops at first pass; Test All exhausts | A |
| Green indicator; manual selection offered | A for logic, S for display |
| Never modify hosts or apply as a side effect | A |
| Manual browser verification where needed | Challenge detection: A. **Open:** the Open Website in Browser button by hand |

## 8. Legitimacy and safety

| Item | Evidence |
| --- | --- |
| Public routability, DNS association, certificate, HTTP response | A |
| Supported reputation service | A: Quad9 (logic, plus a live check), AbuseIPDB and VirusTotal against simulated replies, custom filtering DNS. **Open:** the two key-based services against the real services |
| Reference content comparison | A (certificate and HTTP status against a DNS-published address) |
| Separate connection, association and safety claims | A |
| The four statuses | A, S |
| Explain failures, warnings, staleness; never claim authenticity or safety | A |

## 9. System hosts file

| Item | Evidence |
| --- | --- |
| Read hosts; identify managed lines | A, E |
| Apply selected mappings; remove only managed ones | A, E |
| Preserve unrelated entries and modifications | A, E (byte comparison before and after) |
| Back up before writing; detect external edits; replace safely | A, E |
| Flush DNS; verify written state | A, E (Windows resolves the new mapping) |

## 10. Permissions

| Item | Evidence |
| --- | --- |
| Configuration editing without elevation | By construction; only the hosts write can elevate |
| UAC only for the privileged hosts write | Helper mode: E. **Open:** the prompt itself, as a non-elevated user |
| Cancelled or denied elevation leaves hosts untouched | A (simulated cancellation). **Open:** declining the real prompt |

## 11. Restore and recovery

| Item | Evidence |
| --- | --- |
| First-change backup, continued backups, restore | A, E |
| Safe recovery without third-party modifications | A, E |
| Collections import/export; configuration independent of hosts state | Collections import/export removed per `AMENDMENTS.md`; independence: A |

## 12. Packaging and distribution

| Item | Evidence |
| --- | --- |
| Self-contained standalone `Kiitou.exe` | E |
| Direct EXE from GitHub Releases | Published per pass by the build |
| Clean Windows testing | **Open** (Pass 9) |

## 13. Testing and verification

| Item | Evidence |
| --- | --- |
| CRUD and persistence of sites, addresses, profiles | A |
| Profile switching and IPv4/IPv6 mapping | A, E |
| DNS/HTTPS/TLS/verification classifications | A |
| Activation one/many/all/none; conflicts | A |
| Hosts backups, restore, unrelated lines, external edits, UAC cancel | A, E; real UAC cancel **Open** |
| Unexpected termination; storage folder protections | E |
| 14 themes, icon matching, compact sizing | E, S |
| Real clean Windows EXE test and release verification | **Open** (Pass 9) |

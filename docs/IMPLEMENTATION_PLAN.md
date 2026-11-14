# KIITOU: Approved Nine-Pass Execution Plan

The total is **nine** passes (eight development passes and one final release-verification pass); sequence and scope are locked. Each pass has three mandatory audits: functional, technical, scope, plus regression testing of earlier passes. A failing or unverified pass is not completed. No automatic progression; alterations outside scope require approval.

| Pass | Work and deliverable | Pass audit |
| --- | --- | --- |
| 1. Foundation and storage | .NET 10 WPF project, minimal bootstrap, local JSON data models, versioned configuration, portable/AppData paths, color/theme scaffolding | Build and startup on Windows, first-run write/read, data persistence, folder fallback, error safety |
| 2. Hosts engine | Read system hosts file; Kiitou-managed lines; backups; safe writes/restore; elevation boundaries; conflicts/external edits; DNS flush | Original unrelated lines unchanged, failed writes safely recovered, UAC cancel does not modify system |
| 3. Sites and addresses | Add/edit/delete sites and many IP candidates; enable one/many/all/none; IPv4/IPv6 selection; validation | Stored disabled data preserved, selected IP correctly mapped, duplicate/conflict checks |
| 4. Profiles | Create/rename/duplicate/edit/delete/save/switch/import/export profile configurations | Correct enabled sites and IPs per profile, saved profiles not overwritten by unsaved toggles |
| 5. DNS and connectivity | Discover A/AAAA records; test single/all/first-working; HTTPS/TLS/HTTP timing | Tests independent from system hosts file; first-working stops correctly |
| 6. Legitimacy and security | Public IP, DNS comparison, certificate, response, supported reputation and reference checks, four statuses | Cautious classification, warning rationale, stale data, no blanket safety guarantee |
| 7. Windows UI | Implement approved layout at compact sizes, all seven families x dark/light, matching window/tray icon, optional tray | Design fidelity, control usability, non-fullscreen sizing, theme switching/persistence |
| 8. Integration and hardening | Connect features and execute whole feature checklist; error recovery and regression tests | End-to-end functional and reliability evidence |
| 9. Release verification | Windows clean-machine verification, single-file standalone exe, direct GitHub Release, final comprehensive audit | Verify actual EXE, permissions, config persistence, dependency-free launch and downloadable asset |

## Theme allocation
Pass 1: configuration and icon name/theme scaffolding. Pass 7: complete UI and runtime icon switching. Passes 8 and 9: integration/release tests. No additional pass is authorized.

## Audit reporting
After every pass report implementation Completed/Partial/Blocked; functional audit; technical audit; scope audit; regression audit; outstanding defects; alteration required Y/N; next-pass readiness. Source inspection is not the same as running a Windows binary. A workflow compiling does not prove correct app behavior. Failed gates block progression. For an out-of-scope change, obtain user approval before implementing.

## Delivery
After **every** pass provide ONLY a standalone Kiitou.exe direct download. GitHub holds the source/audit files. GitHub Actions is the chosen Windows build environment and release publisher.

## Current position
Pass 1 source is in GitHub, but no successful Windows compile/startup was confirmed. User reports repeated workflow failures. Treat Pass 1 as **partial, gate not cleared**. Do not begin Pass 2 until fixed and verified.

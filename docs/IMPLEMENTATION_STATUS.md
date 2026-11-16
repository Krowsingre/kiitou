# KIITOU: Actual Implementation State

Observed on 2026-11-15. Do not confuse specifications and mockups with working implementation;
`VERIFICATION_MATRIX.md` says what was actually verified and how.

## GitHub
Public repository: https://github.com/Krowsingre/kiitou; `main` branch. Each pass was developed
on its own branch, built and tested by GitHub Actions, and merged through a pull request
(#1 to #8). Each merge publishes a `pass-N` prerelease containing only `Kiitou.exe`.

## How passes 2 to 8 were authorized
On 2026-11-14 the user authorized coding each phase, auditing it for scope and errors, fixing,
re-auditing, then pushing and merging, without stopping for approval between passes. The
per-pass audits are in `PASS_02_AUDIT.md` to `PASS_08_AUDIT.md`.

## What exists
- `Kiitou.Core`: configuration store; hostname and IP rules; hosts engine (managed section,
  byte-exact file service, backups, privileged helper, elevation launcher, apply/restore);
  website catalog and selection editor; profile manager and import/export; direct DNS resolver
  and discovery; HTTPS address tester; legitimacy assessor; session layer.
- `Kiitou`: WPF application with the approved layout, seven views, fourteen themes, matching
  icons, optional notification-area icon, and the elevated helper mode.
- `Kiitou.Tests`: 222 automated tests.
- `.github`: build workflow; smoke test; UI tour that drives every view, all themes, and an
  end-to-end apply, crash, remove and restore against the build runner's hosts file.

## Build history
- Runs #1 to #10 failed within seconds because GitHub refused to start jobs for account billing
  reasons while the repository was private. No code had run. Making the repository public
  resolved it with no source change.
- Every pass since then built and passed its checks before being merged.

## Pass state
| Pass | State |
| --- | --- |
| 1 Foundation and storage | Verified by automated tests, runner checks and a manual run on one PC |
| 2 Hosts engine | Verified on the runner, including the real hosts file. Elevation prompt not verified |
| 3 Sites and addresses | Verified by automated tests |
| 4 Profiles | Verified by automated tests |
| 5 DNS and connectivity | Verified by automated tests and a live network check |
| 6 Legitimacy and security | Verified by automated tests. Reputation service not chosen |
| 7 Windows UI | Verified by UI automation and screenshot review on the runner |
| 8 Integration and hardening | Verified end to end on the runner |
| 9 Release verification | **Not done. Needs a person on a clean Windows machine** |

No pass gate has been formally closed by the user.

## Required next work (Pass 9, by a person)
1. Run the latest release executable on a clean Windows machine as a normal, non-elevated user.
2. Apply a profile and accept the Windows elevation prompt; apply again and decline it; confirm
   declining changes nothing.
3. Add a website, addresses and a profile by hand; discover DNS; test addresses.
4. Check the look against the approved screenshot, including at 125% and 150% display scaling.
5. Try minimizing to the notification area with the option switched on.
6. Tick the task list and close the gates, or report what needs changing.

## Amendments
The user changed several requirements on 2026-11-15 and 2026-11-16: the apply/disable button,
a fixed window size, the Settings layout, reputation services, and the removal of redundant
functions. They are listed in `AMENDMENTS.md`, which overrides the earlier documents.

## Open decisions for the user
- **Portable storage.** Settings are written beside the executable whenever that folder is
  writable, so running from Downloads or the Desktop stores them there, and moving the
  executable starts a fresh configuration.
- **No backup of `config.json`** is kept when it is saved (hosts-file backups are kept).
- **No single-instance guard.** Two copies running at once can overwrite each other's settings.
- **Executable size.** About 174 MB uncompressed. Single-file compression would shrink the
  download at some cost in start-up time.
- **Code signing.** Unsigned; SmartScreen will warn.

## Known limits
- The TLS protocol version is not recorded by the address test.
- Button icons use the Segoe MDL2 Assets font included with Windows 10 and 11.
- Theme colors approximate the approved screenshot; they were not sampled from it.

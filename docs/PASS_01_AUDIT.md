# Kiitou Pass 1 Audit

## Implemented

- .NET 10 WPF Windows project and minimal bootstrap window
- Versioned JSON configuration and default profile structure
- Candidate website/address data types for later approved passes
- Portable-first configuration path and local AppData fallback
- Seven approved theme colors, light/dark configuration and icon naming
- Non-destructive handling of malformed/unsupported configuration

## Boundaries

- No hosts-file modifications
- No privilege elevation
- No profile switching or address tests
- No finished UI or release executable
- Icon artwork not embedded yet; Pass 7 will use the approved seven .ico assets

## Audit 1: Functional

Source-reviewed initial load/default creation, local JSON save/read path,
and active profile validation. Windows execution remains **not verified**.

## Audit 2: Technical

Source-reviewed null checks, path fallback, temporary-file write and cleanup.
Compilation and Windows execution remain **not verified** because no Windows
.NET 10 build environment was available in this session.

## Audit 3: Scope

Compared implemented files with Pass 1 foundation and theme infrastructure.
No later-pass operational behavior was implemented.

## Windows runtime verification (2026-11-14)

The audits above were source review only. The following was then observed by
running the `pass-1` release executable (built by GitHub Actions from commit
fab1497, SHA-256 fdb4ff36…bb1f) on one Windows 11 PC at 125% display scaling.

| Test | Result |
| --- | --- |
| First launch from a writable folder | Pass. Window opens; `config.json` created beside the executable with a Default profile and purple/dark theme. |
| Window size | Pass. 720 x 450 device-independent pixels. |
| Relaunch | Pass. Existing configuration loaded, file byte-identical, exit code 0, no temporary files left. |
| Truncated JSON | Pass. Error dialog shown; file not overwritten. |
| Executable folder not writable | Pass. Nothing written beside the executable; configuration created in and reloaded from `%LOCALAPPDATA%\Kiitou`. |
| Null profile entry | Poor message ("Object reference not set to an instance of an object."); file not damaged. |
| Missing `profiles` key | Misleading message ("Active profile does not exist."); file not damaged. |

First launch from each new folder took 9 to 17 seconds before the window
appeared; later launches took under one second. Cause not determined.

Not tested: a clean machine with no .NET installed, and running from a drive root.

## Corrective fixes after verification (not yet built or re-tested)

- Drive-root executable folder (`E:\`) no longer becomes a drive-relative path.
- Validation rejects null or incomplete website and profile entries, duplicate
  identifiers, and selections that point at missing websites or addresses, each
  with a specific message.
- A configuration without `profiles` now reports the missing collection.
- Unhandled errors after startup show a dialog instead of a silent crash.
- Workflow builds only when source, workflow or release tag changes, serializes
  release jobs, and recreates the release so its tag matches the built commit.

## Pass gate

**Verified except:** the corrective fixes above need a build and a repeat of the
runtime tests, and a clean-machine run is outstanding. The user closes the gate.
No alteration to the approved scope is requested.

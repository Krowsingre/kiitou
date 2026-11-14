# KIITOU: Actual Implementation State and Build Handoff

Observed on 2026-11-14. Intended for an LLM auditing the repository. Do not confuse specifications and mockups with working implementation.

## GitHub
Public repository (made public 2026-11-14): https://github.com/Krowsingre/kiitou; main branch.

## Files committed
- Kiitou/Kiitou.csproj: WinExe, .NET 10 Windows, WPF.
- Kiitou/App.xaml and App.xaml.cs: startup, create or load local configuration, error dialog.
- Kiitou/MainWindow.xaml and MainWindow.xaml.cs: minimal bootstrap screen; NOT approved final GUI. Width/height 720x450, minimum 720x450 after correction.
- Kiitou/Models/AppConfiguration.cs: JSON model classes for theme selection, website, candidate IP, profile, per-profile enabled and selected IPv4/IPv6 IDs.
- Kiitou/Services/StoragePaths.cs: writable app-folder probe and local AppData fallback.
- Kiitou/Services/ConfigurationStore.cs: load/create/save versioned JSON; basic validation; temporary file replacement.
- Kiitou/Services/ThemeCatalog.cs: seven theme family names, two modes, ICO naming convention.
- README.md, .gitignore, docs/PASS_01_AUDIT.md, release-tag.txt (pass-1).
- .github/workflows/windows-build.yml: Windows Actions runner, setup .NET 10, restore/build, publish single-file win-x64 self-contained executable, inspect publish folder, attempt GitHub prerelease attachment.

## Not implemented
The approved UI; fourteen rendered theme schemes; approved ICO binaries and runtime icon selection; site CRUD; actual profile manipulation; system hosts-file read/write/backups/UAC; DNS discovery; address testing; reputation/security checks. Data model definitions alone do not complete these features.

## Known commit and build history
- First Pass 1 WPF scaffolding pushed as separate file commits.
- MinWidth/MinHeight set to 720x450 after initial size audit: a68d77098270f57d6085268eb8d102ac95215aab.
- Build workflow added to main (initial single-exe publish/release logic).
- A subsequent release-existence check change and dotnet diagnostic output committed at 3fb4d9ff8ba09a6cb3a993b2f449461a1b06a950.
- Runs #1 to #10 all failed within seconds. Root cause, read from the run annotations: "The job was not started because recent account payments have failed or your spending limit needs to be increased." No runner started, so no step ever executed. The release-logic change in 3fb4d9f addressed code that had never run.
- After the repository was made public, run #10 was re-run on commit fab1497 and succeeded in 1m 14s with no source changes. It published the `pass-1` prerelease with a lone `Kiitou.exe` (133 MB, SHA-256 fdb4ff36b717931dbf3b63b8de4db32a6ec59c8658c6f07a6c07a6acd1e2bb1f).
- That executable was downloaded and tested on one Windows 11 PC; results are in PASS_01_AUDIT.md.
- Corrective fixes from that audit (validation, drive-root path, workflow triggers and release provenance) were then made in source. **They have not been compiled or run yet.**

## Required next work, no unauthorized feature expansion
1. Build the corrective fixes on the Windows runner and repeat the PASS_01_AUDIT.md runtime tests against the new executable, adding a drive-root run.
2. Run the executable on a clean Windows machine with no .NET installed.
3. Ask the user to close Pass 1. Do not advance to Pass 2 before that.
4. Upload the remaining reference art if wanted; see ASSET_TRANSFER.md.

## Open decisions for the user (not changed, would alter approved behavior)
- Portable mode is chosen whenever the executable folder is writable, so running from Downloads or Desktop writes `config.json` there, and moving the executable starts a fresh configuration.
- Saving replaces `config.json` without keeping a backup copy.
- There is no single-instance guard.
- No license file; the repository is now public.

## Audit caution
A timed hourly ChatGPT automation named Kiitou Build Watch was created to report workflow results. It is a status monitor, not evidence of success.

## Risks to examine during Pass 1 gate
- Partial/malformed JSON and null collection elements, incompatible schemas.
- Portable path behavior in protected folders.
- Replacement file robustness across real Windows filesystems.
- Windows/.NET build compatibility and the CI release permissions.
- Approved icon assets are in `assets/icons/` but not yet embedded in the compiled executable (Pass 7).
These are checks, not permission to add features.

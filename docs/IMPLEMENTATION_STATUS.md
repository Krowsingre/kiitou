# KIITOU: Actual Implementation State and Build Handoff

Observed on 2026-11-14. Intended for an LLM auditing the repository. Do not confuse specifications and mockups with working implementation.

## GitHub
Private repository: https://github.com/Krowsingre/kiitou; main branch; prior connector verified admin/push permission.

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
- User reported two or more failed builds. The available GitHub connector method for fetching workflow runs by commit filters to pull-request events; pushes were not visible through it. Combined commit status returned no statuses. Therefore the actual failing job, error message and root cause **were not obtained**. Do not say that release logic was proven to be the cause.
- The user requested actual failure-log diagnosis instead of speculative retries. Run/build/release success has NOT been established.

## Required next work, no unauthorized feature expansion
1. Obtain real GitHub Actions run and job IDs/logs via Actions UI or an API route that supports push runs, then determine whether YAML, restore, compile, publish, artifact verification or release publishing failed.
2. Fix the **proven** failure. Build on real Windows runner.
3. Verify application launches, creates config, reloads data, handles corrupted config, respects portable/AppData paths and intended default profile. Source review alone is insufficient.
4. Validate output is a directly downloadable lone Kiitou.exe in a GitHub development release.
5. Audit and close Pass 1 only after evidence. Do not advance to Pass 2 prematurely.
6. Upload original approved binary art separately once a supported binary-transfer path is available; see asset manifest.

## Audit caution
A timed hourly ChatGPT automation named Kiitou Build Watch was created to report new meaningful workflow successes or failures. It is a status monitor, not a Windows build agent and not evidence of success. No validated .exe download was delivered to the user.

## Risks to examine during Pass 1 gate
- Partial/malformed JSON and null collection elements, incompatible schemas.
- Portable path behavior in protected folders.
- Replacement file robustness across real Windows filesystems.
- Windows/.NET build compatibility and the CI release permissions.
- No approved icon assets currently embedded in compiled source.
These are checks, not permission to add features.

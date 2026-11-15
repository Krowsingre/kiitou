# Kiitou Pass 8 Audit: Integration and Hardening

Pass 8 connects and exercises everything together. Its main deliverable is evidence: the
published `Kiitou.exe` is driven through its real window against the real hosts file of the
Windows build runner (a disposable machine).

## End-to-end run through the user interface

Starting from a configuration with four websites and a "Gaming" profile that enables three of
them (four mappings, IPv4 and IPv6):

1. The header reads "Not Applied".
2. **Apply Selected Profile** is pressed. The confirmation names 4 mappings for "Gaming". After
   confirming, the result box says the profile was applied.
3. The hosts file contains exactly four `# Kiitou` lines, the original content is still at the
   start of the file, Windows resolves one of the hostnames to the applied address, and the
   header reads "Profile Active".
4. Pressing Apply again reports that nothing needs to change.
5. The process is **killed**. On relaunch the header still reads "Profile Active" and no
   temporary files are left in the data folder.
6. On the Hosts File view, **Remove Kiitou Mappings** removes all four lines; the header reads
   "Not Applied".
7. **Restore Selected Backup** (the newest backup is preselected) brings the four lines back;
   the header reads "Profile Active".
8. The mappings are removed again and the application is closed. The hosts file is identical to
   how it started, and the original plus four rotating backups are in the backup folder.

## Other checks added in this pass

- **Folder that cannot be written to:** the executable is started from a folder with write
  access denied. It starts normally, writes nothing there, and creates its configuration in
  `%LOCALAPPDATA%\Kiitou`.
- **Backup list:** when a new backup appears it becomes the selected one, so "restore" targets
  the most recent state by default. Found and fixed while writing the end-to-end run.
- **Backup ordering:** two backups taken in the same millisecond are now ordered correctly.
  Found by a session test in Pass 7 and fixed there.

## Audit 1: Functional

All of the above ran on the Windows build runner together with 222 automated tests, the Pass 1
startup checks, the Pass 2 helper checks, and the Pass 7 tour of every view and all fourteen
themes.

## Audit 2: Technical

- No compiler warnings.
- The end-to-end run uses the published single-file executable, not a debug build.
- Because the runner is already elevated, writes there go through the in-process writer. The
  separate elevated-helper path is exercised by the Pass 2 helper checks, but the Windows
  elevation prompt itself cannot be automated.

## Audit 3: Scope

No product feature was added in this pass. Changes to the application are limited to the backup
preselection fix, two control names used by the automated run, and the version number.

## Regression

Every earlier check still passes in the same build.

## Not verified (needs a person; this is Pass 9)

- Accepting and declining the real Windows elevation prompt as a standard, non-elevated user.
- A clean Windows machine with no .NET installed.
- Behavior alongside third-party security software.
- Display scaling other than 100%, and visual review against the approved screenshot.
- Adding and editing websites, addresses and profiles by typing into the dialogs. The logic
  behind them is covered by automated tests; the dialogs themselves were not driven.

## Pass gate

**Passed on automated evidence.** Pass 9 remains for the user. No alteration to the approved
scope is requested.

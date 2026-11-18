# Kiitou

Kiitou is a portable Windows hosts-file manager. You keep a list of websites, several candidate
IP addresses for each, and named profiles that say which websites are switched on and which
address each one uses. Applying a profile writes those mappings to the Windows hosts file.

It is a single `Kiitou.exe`. There is no installer, account, server or background service.

## Download

Download `Kiitou-Setup-<version>.exe` from the
[Releases page](https://github.com/Krowsingre/kiitou/releases) and run it. It installs Kiitou for
your Windows account only, so it does not need administrator rights. The setup program is not
code-signed, so Windows SmartScreen may warn about an unknown publisher.

Kiitou checks for new versions itself and installs them on request (Settings > Updates). It only
installs an update signed with Kiitou's own update key.

## What it does

- **Websites and addresses.** Add websites and any number of IPv4 and IPv6 candidates for each.
  Enable one, some, all or none. Disabling a website keeps its addresses and choices.
- **Profiles.** Create, rename, duplicate, delete, switch, import and export. **Save** stores
  your changes in the profile. **Apply Profile** stores them, writes the profile to the hosts
  file and returns to Home. While a profile is applied the same button reads **Disable
  Profile**, or **Apply Changes** if you have edited it.
- **DNS discovery.** Look up the addresses currently published for a website and add the ones
  you choose. Saved addresses are never removed for you.
- **Testing.** Test one address, all of them, or find the first that works. Tests connect
  straight to the candidate address on port 443 while using the real hostname for TLS and HTTP,
  without touching the hosts file.
- **Statuses.** Each address gets one of four, with the reasons behind it: Connection Verified;
  Connection Works, Verification Incomplete; Failed or Unsafe; Not Tested.
- **Reputation checks.** Quad9 is built in and needs no account. You can add your own AbuseIPDB
  or VirusTotal key, or any filtering DNS server. Each can be switched off.
- **Hosts file.** Kiitou only ever changes its own clearly marked section. Everything else in
  the file is preserved byte for byte. It backs up the file before every change, keeps the
  original permanently, notices changes made by other programs, and can restore any backup.
- **Appearance.** Seven color families, each in dark and light, with a matching icon, and an
  Auto mode that follows Windows.

Kiitou runs without administrator rights. Windows asks for permission only at the moment the
hosts file is written. If you decline, nothing is changed.

## Important limits

A green "Connection Verified" means the connection behaves correctly: the address answers, and
it presents a certificate that is trusted and valid for the hostname. **It does not prove that
an address is authentic or that a website is safe.**

Reputation checks send the website's hostname (Quad9, custom DNS) or its address (AbuseIPDB,
VirusTotal) to that service. A service is only contacted while it is switched on.

Security software sometimes blocks or reverts changes to the hosts file. Kiitou reports when
what it wrote is not what the file contains afterwards.

Kiitou is provided as is, without warranty of any kind. You use it at your own risk.

## Where settings are stored

Settings, profiles and hosts-file backups are in `%LOCALAPPDATA%\Kiitou`, separate from the
program. Uninstalling asks whether to delete them too.

## Build

Install the .NET 10 SDK on Windows, then:

```powershell
dotnet test .\Kiitou.Tests\Kiitou.Tests.csproj -c Release
dotnet run --project .\Kiitou\Kiitou.csproj
```

Every push is built and tested on GitHub Actions, including installing, reinstalling and
uninstalling the setup program. A new version is published when `version.txt` changes on `main`;
see `docs/RELEASING.md`.

| Folder | Contents |
| --- | --- |
| `Kiitou` | The WPF application |
| `Kiitou.Core` | Hosts engine, websites, profiles, DNS, testing, verification, updates, session |
| `installer` | The Inno Setup script for the setup program |
| `Kiitou.Tests` | Automated tests |
| `assets` | Approved icons and the approved UI screenshot |
| `docs` | Specification, plan, per-pass audits and the verification matrix |

## Status

Development passes 1 to 8 are complete on automated evidence. Pass 9 (release verification on a
clean Windows machine, by a person) is outstanding. See `docs/VERIFICATION_MATRIX.md` for what
has and has not been verified, and `docs/IMPLEMENTATION_STATUS.md` for open decisions.

## License

MIT License with the Commons Clause condition: you may use, copy, modify and share Kiitou
freely, but you may not sell it or sell a service whose value comes from it. See `LICENSE`.

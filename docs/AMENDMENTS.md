# Kiitou: Amendments Approved by the User

These changes were requested or approved by the user after the original specification and task
list were locked. Where they differ from `PRODUCT_SPECIFICATION.md`, `APPROVED_TASK_LIST.md` or
the pass audits, **this file wins**.

## 2026-11-15 and 2026-11-16

### Header and applying
- **One header button with three states.**
  - Nothing applied: **Apply Profile**. Saves any unsaved changes to the selected profile, writes
    it to the hosts file, and returns to Home.
  - Profile applied and unchanged: **Disable Profile**. Removes Kiitou's mappings from the hosts
    file.
  - Profile applied and edited since: **Apply Changes**. Saves the edits and replaces Kiitou's
    section of the hosts file in a single write; the profile is never switched off in between.
- **Save to Current Profile** only saves and stays on the current view.
- **The Profile drop-down is locked while a profile is applied.** It unlocks when the profile is
  disabled. The applied profile is shown as "Name (Active)".
- **One badge, "Profile Active".** Lit when the hosts file holds exactly what is on screen, grey
  otherwise. It is not clickable. The "Not Applied" and "Unsaved changes" badges are removed.
- **Apply asks a question only when there is a warning** (a conflicting line, an outside edit,
  altered markers, or an enabled website with no address). Success is reported in the status
  footer, not in a message box. The footer shows which profile is applied.
- Kiitou mappings that match no saved profile can only be cleared (the button reads Disable Profile).

### Window and layout
- **One fixed window size, 1040 x 622.** It cannot be resized or maximized. The size lies between
  the user's two references: 720 x 450 and Echo Light's 1093 x 622.
- **Every button row is a single row** laid out to fit; nothing wraps. On the Inspector page the
  website picker and its buttons share one row.
- **Spacing rules:** 12 between cards, around the page, and between the header button and the
  badge; 8 between "Profile:" and its drop-down; the drop-down's right edge aligns with the
  Appearance card's right edge; the badge's right edge aligns with the cards' right edge.
- **Design verification:** `Kiitou.exe --layout-check <file>` measures every view against these
  rules, plus "nothing cut off, nothing outside the window, no overlaps", and the build fails if
  any is broken.
- No vertical divider lines in the header. No icons on Settings card headings.

### Home
- **One address drop-down per address family a website has candidates for** (IPv4, IPv6, or
  both, stacked), replacing the single combined drop-down.

### Redundancy removed
- Verification is merged into Discovery & Test (renamed **Inspector** in 0.9.3) (six navigation items instead of seven). The
  reasons for a status are shown under the test table.
- Removed: the per-row "..." button on Home, "Test Selected" on Websites, "Apply Selected
  Profile" and "Remove Kiitou Mappings" on Hosts File, Import/Export on Profiles (they remain on
  Home), and **website-collection import/export** (a profile export already contains every
  website and address). This removes task-list item 11 "Collections import/export".

### Settings
- Laid out to the user's concept: Appearance (Theme Color and Mode drop-downs), System
  ("Minimize to system tray" switch), Reputation Checks, Storage, About.
- **Mode has Auto (Follow System), which is the default.** Dark and Light remain.
- **Reputation Checks:**
  - Quad9, on by default, with a switch. Only the website's hostname is sent.
  - Two rows for key-based services, each with a drop-down ("Key-based Services" when empty)
    offering AbuseIPDB and VirusTotal, a masked key box with a show/hide button, a "Get a free
    key" link, a Test Key button and a switch. At most two can be on at once.
  - A Custom DNS row (name, server address, Test button, switch) for any filtering DNS service.
  - **A row is locked to its service, key or server while it is switched on.**
  - Keys are encrypted for the signed-in Windows user and are never exported.
  - If any service flags an address the status is Failed or Unsafe. A service that cannot be
    reached is reported as not checked, never as clean.
  - No "Add Service" button; new services arrive with application updates.

### Verification
- **Private network addresses are judged on their connection**, with a warning that they only
  work on the user's own network. Only addresses that can never be a website (loopback,
  reserved, documentation, multicast) are Failed or Unsafe outright.

### License
- MIT License with the Commons Clause condition (`LICENSE`): free to use, copy, modify and
  share; may not be sold.

### Installed application and updates (approved 2026-11-16)
- **Kiitou is now an installed application**, replacing "portable executable, no installer" and
  "the user receives only a standalone exe". The download is `Kiitou-Setup-<version>.exe`.
- **Installed for the current user only**, in `%LOCALAPPDATA%\Programs\Kiitou`, so installing and
  updating never need administrator rights. A Start menu shortcut is created; a desktop shortcut
  is optional.
- **Nothing is written beside the program.** Settings, profiles and hosts-file backups live in
  `%LOCALAPPDATA%\Kiitou`.
- **Uninstalling asks each time** whether to also delete those settings. A quiet uninstall keeps
  them. If Kiitou's mappings are still in the hosts file, the uninstaller offers to remove them.
- **Updates work like The Bound Vigil's:** each version is a GitHub release with the setup program
  and `latest.json`; Kiitou checks it, and installs only a setup program signed with Kiitou's own
  update key. Settings: "Look for updates when Kiitou opens" (on by default) and "Install updates
  by themselves" (off by default), with a Check for Updates / Install and Restart button.
- **Real version numbers** (`version.txt`, starting at 0.9.0) replace the `pass-N` releases.
- On the Settings page the **Storage card is replaced by an Updates card**; the settings location
  is one line in About, with an Open Folder link.
- See `RELEASING.md` for how releases are made and how the signing key is set up.

### Network, menu, updating in place, data safety (approved 2026-11-18, Kiitou 0.9.2)
- **Network page** (new, between Profiles and Hosts File): where tests appear to come from (public
  address and country, found by asking Cloudflare, no account), an optional **SOCKS5 test proxy**
  used only by Kiitou's tests (for example Tor Browser on 127.0.0.1:9150; password encrypted;
  locked while switched on), and a **test log** recording every test with where it came from,
  filterable by website and location. This amends the exclusion "No VPN, proxy … is required":
  none is required, but tests may now use one.
- Kiitou does **not** act as a VPN or Tor. It cannot change which catalogue a streaming service
  shows; that depends on where the user appears to be, not on which server address is chosen.
- **Menu order:** Home, Websites, Inspector, Profiles, Network, Hosts File, Settings by
  default. Items can be dragged into any order, which is saved. It is not announced and there is
  no reset.
- **Updating in place:** a temporary copy of Kiitou opens an update window in the same size and
  place, showing a blurred picture of Kiitou with a progress card (downloading, installing,
  starting; Try Again / Open Kiitou on failure), installs the signed update and reopens Kiitou.
  The window position is remembered.
- **Uninstall Kiitou…** in Settings > About starts the normal uninstaller.
- **Settings format conversion:** settings carry a format number (now 2). Older formats are
  converted on load, after a safety copy of the original; a newer format is refused and left
  untouched.
- **Safety copy of the settings before every update**, in `%LOCALAPPDATA%\Kiitou\config-backups`,
  keeping the newest 10.
- **Update leftovers** are deleted on the next start and by the uninstaller.

### Renamed page (approved 2026-11-18, Kiitou 0.9.3)
- **"Discovery & Test" is now "Inspector".** Websites and Inspector stay separate pages.

### Not verified
- AbuseIPDB and VirusTotal were tested only against simulated replies; no account was available.
- Auto mode following a live change of the Windows setting was not exercised on the build runner.
- An update downloaded from a real release and installed end to end. The build tests the whole
  update window with a signed setup program on disk, and the download separately; together they
  cover it, but the first real update from 0.9.1 to 0.9.2 is the first full run.
- Through a real Tor or VPN proxy. The SOCKS5 client is tested against a stand-in proxy.
- The uninstaller's two questions (delete settings; remove hosts mappings) are interactive and
  were not driven; the quiet uninstall path was tested.

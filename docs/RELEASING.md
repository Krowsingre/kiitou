# Releasing Kiitou and Its Updates

Kiitou is installed with a setup program, `Kiitou-Setup-<version>.exe`, and updates itself from
this repository's GitHub releases, the same way The Bound Vigil does.

## How a release is made

1. Change the version in `version.txt` (for example `0.9.0` to `0.9.1`). It is the only place the
   version is written; the program and the setup program both read it.
2. Merge to `main`. The build tests everything, builds the setup program, tests installing,
   updating and uninstalling it, and then publishes release `v<version>` with:
   - `Kiitou-Setup-<version>.exe`, the setup program;
   - `latest.json`, which tells installed copies the newest version, where to download it, and
     the setup program's signature.
3. A version is published only once. Merges that do not change `version.txt` publish nothing.

## How updating works

- Kiitou reads `latest.json` from the newest release a few seconds after it opens (unless "Look
  for updates when Kiitou opens" is off) and when **Check for Updates** is pressed in Settings.
- If the version is newer, it offers **Install and Restart**, or installs straight away when
  "Install updates by themselves" is on.
- Installing downloads the setup program from this repository's releases only, and **checks its
  signature against the public key built into Kiitou**. A file that is not signed with the
  update-signing key is deleted and never run.
- Kiitou first saves a safety copy of the settings, then hands over to the **update window**: a
  temporary copy of Kiitou that opens in the same place, shows a picture of Kiitou, downloads and
  checks the update, runs the setup program quietly, and opens the new Kiitou. Settings, profiles,
  the test log and hosts-file backups are untouched; they live in `%LOCALAPPDATA%\Kiitou`.
- If the settings format changes in a new version, older settings are converted when it first
  opens (see `Kiitou.Core/Services/ConfigurationMigrations.cs`). Every released format must stay
  convertible, with a test that loads it.

## The update-signing key (one-time setup)

The key pair was generated on 2026-11-16. The **public** key is built into Kiitou
(`Kiitou.Core/Updates/UpdateService.cs`). The **private** key must be stored as a GitHub secret
so the build can sign releases. It must never be committed to the repository.

To add it:

1. Open `C:\Claude Work\Documents\kiitou-update-signing-key.pem` in Notepad and copy everything,
   including the `-----BEGIN PRIVATE KEY-----` and `-----END PRIVATE KEY-----` lines.
2. On GitHub, open the repository, then **Settings > Secrets and variables > Actions**.
3. Choose **New repository secret**. Name: `KIITOU_UPDATE_SIGNING_KEY`. Secret: paste the key.
   Choose **Add secret**.
4. Move the `.pem` file somewhere safe and private (for example a password manager or an
   encrypted drive), and delete it from the Documents folder. Losing it means a new key pair and
   a new version of Kiitou that every user installs by hand once.
5. In **Actions**, open the latest run of "Kiitou Windows build and executable release" on `main`
   and choose **Re-run all jobs**. The current version's release then gets its `latest.json`.

The build refuses to publish if the secret does not match the public key built into Kiitou.

Until the secret is added, releases are published without `latest.json`. They can be downloaded
and installed by hand, but installed copies are not offered them.

## Limits

- The setup program is not code-signed with a paid certificate, so Windows SmartScreen warns about
  an unknown publisher on first install. The update signature protects against tampered updates;
  it does not change what SmartScreen shows.
- Every update downloads the whole setup program.

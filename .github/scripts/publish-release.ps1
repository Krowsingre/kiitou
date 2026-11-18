# Publishes the setup program as a GitHub release, with latest.json for Kiitou's updater.
#
# A release is made once per version (version.txt). latest.json carries the version, the
# download address and a signature of the setup program made with the update-signing key from
# the KIITOU_UPDATE_SIGNING_KEY secret. Kiitou only installs an update whose signature matches the
# public key built into it. Without the secret, the release is published without latest.json, so
# installed copies do not see it; once the secret is added, re-running this adds it.
param(
    [Parameter(Mandatory = $true)][string]$Commit
)
$ErrorActionPreference = "Stop"

$version = (Get-Content version.txt -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "version.txt must hold a version such as 1.2.3, not '$version'" }
$tag = "v$version"
$setupName = "Kiitou-Setup-$version.exe"
$setup = Join-Path "out-installer" $setupName
if (-not (Test-Path $setup)) { throw "$setup was not built" }

$assets = @()
$json = gh release view $tag --json assets 2>$null
$exists = $LASTEXITCODE -eq 0
$global:LASTEXITCODE = 0
if ($exists) { $assets = @(($json | ConvertFrom-Json).assets | ForEach-Object name) }
if ($exists -and ($assets -contains "latest.json")) {
    Write-Host "$tag is already published with update information. Change version.txt to publish a new version."
    exit 0
}

$files = @($setup)
if ($env:SIGNING_KEY) {
    $key = [System.Security.Cryptography.ECDsa]::Create()
    $key.ImportFromPem($env:SIGNING_KEY)

    # The secret must match the public key built into Kiitou, or every installed copy would reject the update.
    $source = Get-Content "Kiitou.Core/Updates/UpdateService.cs" -Raw
    $builtIn = ([regex]::Matches($source, '"([A-Za-z0-9+/=]{20,})\\n"') | ForEach-Object { $_.Groups[1].Value }) -join ""
    $secretPublic = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    if ($builtIn -ne $secretPublic) {
        throw "The KIITOU_UPDATE_SIGNING_KEY secret does not match the public key built into Kiitou (UpdateService.PublicKeyPem)."
    }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $setup))
    $signature = $key.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not $key.VerifyData($bytes, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw "The signature just made does not verify"
    }
    [ordered]@{
        version = $version
        notes = "Kiitou $version"
        pub_date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        url = "https://github.com/Krowsingre/kiitou/releases/download/$tag/$setupName"
        signature = [Convert]::ToBase64String($signature)
    } | ConvertTo-Json | Set-Content latest.json -Encoding utf8NoBOM
    $files += "latest.json"
    Write-Host "Signed $setupName with the update key."
} else {
    Write-Warning "The KIITOU_UPDATE_SIGNING_KEY secret is not set. $tag is published without update information, so installed copies will not be offered it."
}

if ($exists) {
    # The release exists but has no update information yet: replace its setup program with this
    # build, so the published file is exactly the one that was signed.
    gh release upload $tag @files --clobber
} else {
    $notes = "Kiitou $version.`n`nDownload **$setupName** and run it. It installs Kiitou for your Windows account only, so it does not need administrator rights. " +
             "Windows SmartScreen may warn that the publisher is unknown, because the setup program is not code-signed.`n`n" +
             "Copies of Kiitou that are already installed are offered this version in Settings > Updates."
    gh release create $tag @files --target $Commit --title "Kiitou $version" --notes $notes --latest
}
if ($LASTEXITCODE -ne 0) { throw "Publishing the release failed" }
Write-Host "Published $tag."

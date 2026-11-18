# Installs the built setup program on the build runner, runs Kiitou from the installed copy,
# installs over it again (the update path), and uninstalls it quietly.
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = "Stop"
$env:KIITOU_NO_UPDATE_CHECK = "1"
Remove-Item Env:KIITOU_DATA_DIR -ErrorAction SilentlyContinue

$programDir = Join-Path $env:LOCALAPPDATA "Programs\Kiitou"
$dataDir = Join-Path $env:LOCALAPPDATA "Kiitou"
$exe = Join-Path $programDir "Kiitou.exe"
$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "Kiitou.lnk"
$config = Join-Path $dataDir "config.json"
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue

function Install-Kiitou([string]$what) {
    $p = Start-Process $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LAUNCH=0" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "$what failed with exit code $($p.ExitCode)" }
}

function Wait-Until([scriptblock]$condition, [string]$description, [int]$seconds = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for: $description"
}

function Start-And-Close-Kiitou {
    $p = Start-Process $exe -PassThru
    Wait-Until { $p.Refresh(); $p.HasExited -or $p.MainWindowHandle -ne 0 } "the Kiitou window" 90
    if ($p.HasExited) { throw "The installed Kiitou exited at startup (exit code $($p.ExitCode))" }
    if ($p.MainWindowTitle -notlike "Kiitou*Host Mapping Manager") { throw "Unexpected window title '$($p.MainWindowTitle)'" }
    Start-Sleep -Seconds 2
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(10000)) { $p.Kill(); throw "The installed Kiitou did not close when asked" }
}

Write-Host "== Install for the current user, without administrator rights"
Install-Kiitou "Setup"
if (-not (Test-Path $exe)) { throw "Kiitou.exe was not installed to $programDir" }
if (-not (Test-Path $shortcut)) { throw "No Start menu shortcut was created" }
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if (-not $fileVersion.StartsWith($Version)) { throw "The installed Kiitou is version $fileVersion, expected $Version" }
$uninstallKey = Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" |
    Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -eq "Kiitou" }
if (-not $uninstallKey) { throw "Kiitou is not listed in the current user's installed programs" }
Write-Host "  installed version $fileVersion to $programDir, with a Start menu shortcut"

Write-Host "== First run of the installed copy"
$before = @(Get-ChildItem $programDir -Force | ForEach-Object Name)
Start-And-Close-Kiitou
$after = @(Get-ChildItem $programDir -Force | ForEach-Object Name)
if (Compare-Object $before $after) { throw "Kiitou wrote into its program folder: $((Compare-Object $before $after).InputObject -join ', ')" }
if (-not (Test-Path $config)) { throw "Settings were not created in $dataDir" }
Write-Host "  settings created in $dataDir; nothing written to the program folder"

Write-Host "== Installing over an existing copy (the update path)"
$hash = (Get-FileHash $config).Hash
Install-Kiitou "Installing over the existing copy"
if (-not (Test-Path $exe)) { throw "Kiitou.exe is missing after installing over the existing copy" }
if ((Get-FileHash $config).Hash -ne $hash) { throw "Installing over the existing copy changed the user's settings" }
Start-And-Close-Kiitou
Write-Host "  reinstalled; settings untouched and Kiitou still starts"

Write-Host "== Quiet uninstall"
$uninstaller = Get-ChildItem $programDir -Filter "unins*.exe" | Select-Object -First 1
if (-not $uninstaller) { throw "No uninstaller was installed" }
Start-Process $uninstaller.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait | Out-Null
Wait-Until { -not (Test-Path $exe) } "Kiitou.exe to be removed"
if (Test-Path $shortcut) { throw "The Start menu shortcut was left behind" }
if (-not (Test-Path $config)) { throw "A quiet uninstall removed the user's settings; it should keep them" }
Write-Host "  program removed; settings kept (a quiet uninstall never asks, so it keeps them)"

Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Installer test passed."

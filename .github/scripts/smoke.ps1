# Launches the published Kiitou.exe on the build runner and checks startup behavior.
# Usage: smoke.ps1 -Exe <path to Kiitou.exe> -ShotDir <folder for screenshots>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$ShotDir = ""
)
$ErrorActionPreference = "Stop"

function Wait-Window([System.Diagnostics.Process]$Process, [int]$Seconds) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
        $Process.Refresh()
        if ($Process.HasExited -or $Process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    }
    $Process.Refresh()
    Write-Host ("  waited {0:n1}s; exited={1}; title='{2}'" -f $sw.Elapsed.TotalSeconds, $Process.HasExited, $Process.MainWindowTitle)
}

function Save-Screenshot([string]$Name) {
    if (-not $ShotDir) { return }
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        New-Item -ItemType Directory -Force $ShotDir | Out-Null
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $ShotDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch {
        Write-Host "  screenshot '$Name' not captured: $($_.Exception.Message)"
    }
}

function Stop-App([System.Diagnostics.Process]$Process) {
    if (-not $Process.HasExited) {
        [void]$Process.CloseMainWindow()
        if (-not $Process.WaitForExit(5000)) { $Process.Kill(); [void]$Process.WaitForExit(5000) }
    }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("kiitou-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$exeCopy = Join-Path $work "Kiitou.exe"
Copy-Item $Exe $exeCopy
$config = Join-Path $work "config.json"

Write-Host "== First launch"
$p = Start-Process $exeCopy -PassThru
Wait-Window $p 90
if ($p.HasExited) { throw "Kiitou exited during first launch (exit code $($p.ExitCode))" }
if ($p.MainWindowTitle -notlike "Kiitou*" -or $p.MainWindowTitle -like "*error*") { throw "Unexpected window title '$($p.MainWindowTitle)'" }
Start-Sleep -Seconds 2
Save-Screenshot "01-first-launch"
if (-not (Test-Path $config)) { throw "config.json was not created beside the executable" }
Stop-App $p
$hash = (Get-FileHash $config).Hash

Write-Host "== Relaunch"
$p = Start-Process $exeCopy -PassThru
Wait-Window $p 60
if ($p.HasExited) { throw "Kiitou exited during relaunch (exit code $($p.ExitCode))" }
if ($p.MainWindowTitle -like "*error*") { throw "Relaunch showed an error dialog" }
Stop-App $p
$leftovers = @(Get-ChildItem $work -Force -Filter ".kiitou-*")
if ($leftovers.Count -ne 0) { throw "Temporary files were left behind: $($leftovers.Name -join ', ')" }

Write-Host "== Corrupted configuration"
Set-Content $config '{ "SchemaVersion": 1, "Theme": {' -Encoding utf8
$corruptHash = (Get-FileHash $config).Hash
$p = Start-Process $exeCopy -PassThru
Wait-Window $p 60
if ($p.HasExited) { throw "Kiitou exited without showing the startup error dialog" }
if ($p.MainWindowTitle -ne "Kiitou startup error") { throw "Expected the startup error dialog, saw '$($p.MainWindowTitle)'" }
$p.Kill(); [void]$p.WaitForExit(5000)
if ((Get-FileHash $config).Hash -ne $corruptHash) { throw "The corrupted configuration file was modified" }

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Smoke test passed."

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

trap {
    Save-Screenshot "failure"
    break
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

Write-Host "== Folder that cannot be written to"
$protected = Join-Path $work "protected"
New-Item -ItemType Directory -Force $protected | Out-Null
Copy-Item $Exe (Join-Path $protected "Kiitou.exe")
$fallback = Join-Path $env:LOCALAPPDATA "Kiitou"
$fallbackExisted = Test-Path $fallback
& icacls $protected /deny "$($env:USERNAME):(WD,AD)" | Out-Null
try {
    $p = Start-Process (Join-Path $protected "Kiitou.exe") -PassThru
    Wait-Window $p 90
    if ($p.HasExited -or $p.MainWindowTitle -like "*error*") { throw "Kiitou did not start from a folder it cannot write to" }
    Stop-App $p
    $written = @(Get-ChildItem $protected -Force | Where-Object { $_.Name -ne "Kiitou.exe" })
    if ($written.Count -ne 0) { throw "Files were written into the protected folder: $($written.Name -join ', ')" }
    if (-not (Test-Path (Join-Path $fallback "config.json"))) { throw "config.json was not created in local application data" }
    Write-Host "  configuration was created in $fallback"
} finally {
    & icacls $protected /remove:d $env:USERNAME | Out-Null
    if (-not $fallbackExisted) { Remove-Item $fallback -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "== Privileged hosts helper (build runner only)"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($env:GITHUB_ACTIONS -eq "true" -and $elevated) {
    $hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
    function Get-HostsText { [System.Text.Encoding]::Latin1.GetString([System.IO.File]::ReadAllBytes($hostsPath)) }
    $beforeText = Get-HostsText

    function Invoke-Helper([string]$Sha, [object[]]$Mappings) {
        $request = [ordered]@{ Operation = "apply"; ExpectedSha256 = $Sha; Mappings = @($Mappings) } | ConvertTo-Json -Depth 4
        $file = Join-Path $work "request.json"
        [System.IO.File]::WriteAllText($file, $request)
        $helper = Start-Process $exeCopy -ArgumentList "--hosts-helper", "`"$file`"" -PassThru -Wait
        return $helper.ExitCode
    }
    function Get-HostsSha { (Get-FileHash $hostsPath -Algorithm SHA256).Hash }

    $mapping = @{ IpAddress = "203.0.113.78"; Hostname = "kiitou-smoke.example" }

    $code = Invoke-Helper ("0" * 64) @($mapping)
    if ($code -ne 3) { throw "Helper should refuse a stale hash with exit code 3, got $code" }
    $code = Invoke-Helper (Get-HostsSha) @(@{ IpAddress = "203.0.113.78"; Hostname = "bad host # x" })
    if ($code -ne 2) { throw "Helper should refuse an invalid mapping with exit code 2, got $code" }
    if (-not ((Get-HostsText) -ceq $beforeText)) { throw "Refused requests modified the hosts file" }

    $code = Invoke-Helper (Get-HostsSha) @($mapping)
    if ($code -ne 0) { throw "Helper apply failed with exit code $code" }
    if (-not (Select-String -Path $hostsPath -SimpleMatch "203.0.113.78 kiitou-smoke.example # Kiitou" -Quiet)) {
        throw "The mapping was not written to the hosts file"
    }
    $resolved = [System.Net.Dns]::GetHostAddresses("kiitou-smoke.example") | ForEach-Object { $_.ToString() }
    if ($resolved -notcontains "203.0.113.78") { throw "Windows did not resolve the new mapping (got: $resolved)" }

    $code = Invoke-Helper (Get-HostsSha) @()
    if ($code -ne 0) { throw "Helper removal failed with exit code $code" }
    $afterText = Get-HostsText
    if ($afterText -match "Kiitou") { throw "Managed entries were not fully removed" }
    # Kiitou may add one line break if the file did not end with one; nothing else may differ.
    if (-not (($afterText -ceq $beforeText) -or ($afterText -ceq ($beforeText + "`r`n")) -or ($afterText -ceq ($beforeText + "`n")))) {
        throw "Unrelated hosts content changed"
    }
    Write-Host "  helper apply, refusal and removal verified against the real hosts file"
} else {
    Write-Host "  skipped (not an elevated build runner)"
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Smoke test passed."

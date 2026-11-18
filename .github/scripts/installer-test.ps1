# Installs the built setup program on the build runner, runs Kiitou from the installed copy,
# installs over it again (the update path), and uninstalls it quietly. Every step has a time
# limit; on a timeout the setup log, running processes and a screenshot are kept for diagnosis.
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ShotDir = "shots"
)
$ErrorActionPreference = "Stop"
$env:KIITOU_NO_UPDATE_CHECK = "1"
Remove-Item Env:KIITOU_DATA_DIR -ErrorAction SilentlyContinue

$programDir = Join-Path $env:LOCALAPPDATA "Programs\Kiitou"
$dataDir = Join-Path $env:LOCALAPPDATA "Kiitou"
$exe = Join-Path $programDir "Kiitou.exe"
$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "Kiitou.lnk"
$config = Join-Path $dataDir "config.json"
$logs = Join-Path ([System.IO.Path]::GetTempPath()) ("kiitou-setup-logs-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $logs | Out-Null
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue

function Save-Screenshot([string]$Name) {
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
        Write-Host "  screenshot not captured: $($_.Exception.Message)"
    }
}

function Show-Diagnostics([string]$what) {
    Save-Screenshot "installer-failure"
    Write-Host "---- Processes ----"
    Get-Process | Where-Object { $_.ProcessName -match "Kiitou|unins|Setup|\.tmp" } |
        Select-Object Id, ProcessName, MainWindowTitle | Format-Table -AutoSize | Out-String | Write-Host
    foreach ($log in Get-ChildItem $logs -File -ErrorAction SilentlyContinue) {
        Write-Host "---- $($log.Name) ----"
        Get-Content $log.FullName -Tail 40 | Write-Host
    }
}

# Runs a program and waits at most $Seconds for it alone (not for programs it starts).
function Invoke-Bounded([string]$File, [string[]]$Arguments, [int]$Seconds, [string]$What) {
    $p = Start-Process $File -ArgumentList $Arguments -PassThru
    if (-not $p.WaitForExit($Seconds * 1000)) {
        Show-Diagnostics $What
        try { $p.Kill($true) } catch { }
        throw "$What did not finish within $Seconds seconds"
    }
    if ($p.ExitCode -ne 0) {
        Show-Diagnostics $What
        throw "$What failed with exit code $($p.ExitCode)"
    }
}

function Wait-Until([scriptblock]$condition, [string]$description, [int]$seconds = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 500
    }
    Show-Diagnostics $description
    throw "Timed out waiting for: $description"
}

function Stop-StrayKiitou {
    $stray = @(Get-Process Kiitou -ErrorAction SilentlyContinue)
    if ($stray.Count -gt 0) {
        $stray | ForEach-Object { $_.Kill() }
        throw "Kiitou was started by the setup program even though /LAUNCH=0 was given"
    }
}

# A quiet install hides error boxes and carries on, so the setup log is searched for them.
function Assert-CleanLog([string]$logName, [string]$what) {
    $log = Join-Path $logs $logName
    if (-not (Test-Path $log)) { throw "$what wrote no log" }
    $problems = @(Select-String -Path $log -Pattern "Internal error|Expression error|Exception message|Runtime error" -SimpleMatch:$false)
    if ($problems.Count -gt 0) {
        Show-Diagnostics $what
        throw "$what reported errors: $(($problems | ForEach-Object { $_.Line.Trim() }) -join ' | ')"
    }
}

function Install-Kiitou([string]$what, [string]$logName) {
    Invoke-Bounded $Setup @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LAUNCH=0", "/LOG=`"$(Join-Path $logs $logName)`"") 240 $what
    Start-Sleep -Seconds 2
    Stop-StrayKiitou
    Assert-CleanLog $logName $what
}

function Start-And-Close-Kiitou {
    $p = Start-Process $exe -PassThru
    Wait-Until { $p.Refresh(); $p.HasExited -or $p.MainWindowHandle -ne 0 } "the installed Kiitou window" 90
    if ($p.HasExited) { throw "The installed Kiitou exited at startup (exit code $($p.ExitCode))" }
    if ($p.MainWindowTitle -notlike "Kiitou*Host Mapping Manager") {
        Show-Diagnostics "window title"
        throw "Unexpected window title '$($p.MainWindowTitle)'"
    }
    Start-Sleep -Seconds 2
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(10000)) { $p.Kill(); throw "The installed Kiitou did not close when asked" }
}

Write-Host "== Install for the current user, without administrator rights"
Install-Kiitou "Setup" "install.log"
if (-not (Test-Path $exe)) { Show-Diagnostics "install"; throw "Kiitou.exe was not installed to $programDir" }
if (-not (Test-Path $shortcut)) { throw "No Start menu shortcut was created" }
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if (-not $fileVersion.StartsWith($Version)) { throw "The installed Kiitou is version $fileVersion, expected $Version" }
$listed = Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" |
    Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -eq "Kiitou" }
if (-not $listed) { throw "Kiitou is not listed in the current user's installed programs" }
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
Install-Kiitou "Installing over the existing copy" "reinstall.log"
if (-not (Test-Path $exe)) { throw "Kiitou.exe is missing after installing over the existing copy" }
if ((Get-FileHash $config).Hash -ne $hash) { throw "Installing over the existing copy changed the user's settings" }
Start-And-Close-Kiitou
Write-Host "  reinstalled; settings untouched and Kiitou still starts"

Write-Host "== Updating through the update window"
if ($env:SIGNING_KEY) {
    $key = [System.Security.Cryptography.ECDsa]::Create()
    $key.ImportFromPem($env:SIGNING_KEY)
    $setupFull = (Resolve-Path $Setup).Path
    $signature = [Convert]::ToBase64String($key.SignData([System.IO.File]::ReadAllBytes($setupFull), [System.Security.Cryptography.HashAlgorithmName]::SHA256))
    $updateFolder = Join-Path ([System.IO.Path]::GetTempPath()) "Kiitou-update"
    New-Item -ItemType Directory -Force $updateFolder | Out-Null
    $updater = Join-Path $updateFolder "Kiitou-updater.exe"
    Copy-Item $exe $updater -Force
    $request = Join-Path $updateFolder "update-request.json"
    [ordered]@{
        FromVersion = $Version; ToVersion = $Version; SetupPath = $setupFull; Signature = $signature
        TargetExe = $exe; DownloadFolder = $updateFolder; Left = 60; Top = 40; ThemeColor = "teal"; ThemeMode = "dark"
    } | ConvertTo-Json | Set-Content $request -Encoding utf8NoBOM
    $before = Get-Content $config -Raw | ConvertFrom-Json

    $window = Start-Process $updater -ArgumentList "--apply-update", "`"$request`"" -PassThru
    Wait-Until { $window.Refresh(); $window.HasExited -or $window.MainWindowTitle -like "Kiitou*Updating" } "the update window" 60
    Start-Sleep -Milliseconds 800
    Save-Screenshot "update-window-live"
    if (-not $window.WaitForExit(300000)) { Show-Diagnostics "update window"; $window.Kill(); throw "The update window did not finish within 5 minutes" }
    if ($window.ExitCode -ne 0) { Show-Diagnostics "update window"; throw "The update window ended with exit code $($window.ExitCode)" }

    $new = $null
    Wait-Until {
        $script:new = Get-Process Kiitou -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "Kiitou*Host Mapping Manager" } | Select-Object -First 1
        $null -ne $script:new
    } "the updated Kiitou to open" 60
    $installLog = Join-Path $updateFolder "install.log"
    if ((Test-Path $installLog) -and (Select-String -Path $installLog -Pattern "Internal error|Exception message|Runtime error" -Quiet)) {
        throw "The update's install log reports errors"
    }
    [void]$new.CloseMainWindow()
    if (-not $new.WaitForExit(10000)) { $new.Kill() }
    # Closing Kiitou saves its window position; the user's data must be unchanged.
    $after = Get-Content $config -Raw | ConvertFrom-Json
    if (($after.Profiles | ConvertTo-Json -Depth 8) -ne ($before.Profiles | ConvertTo-Json -Depth 8) -or
        ($after.Websites | ConvertTo-Json -Depth 8) -ne ($before.Websites | ConvertTo-Json -Depth 8)) {
        throw "The update changed the user's profiles or websites"
    }
    Write-Host "  update window installed the signed setup program and opened the new Kiitou; settings kept"

    # The next start removes what the update left behind.
    Start-And-Close-Kiitou
    $left = @(Get-ChildItem $updateFolder -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "Kiitou-updater.exe" })
    if ($left.Count -ne 0) { throw "Files from the update were not cleaned up: $($left.Name -join ', ')" }
    Write-Host "  files left by the update were cleaned up on the next start"
} else {
    Write-Host "  skipped: the KIITOU_UPDATE_SIGNING_KEY secret is not available to this build"
}

Write-Host "== Quiet uninstall"
$uninstaller = Get-ChildItem $programDir -Filter "unins*.exe" | Select-Object -First 1
if (-not $uninstaller) { throw "No uninstaller was installed" }
# The uninstaller hands over to a temporary copy of itself and returns at once, so wait for the result.
Invoke-Bounded $uninstaller.FullName @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$(Join-Path $logs 'uninstall.log')`"") 120 "Uninstall"
Wait-Until { -not (Test-Path $exe) } "Kiitou.exe to be removed" 120
Wait-Until { Test-Path (Join-Path $logs 'uninstall.log') } "the uninstall log" 30
Assert-CleanLog 'uninstall.log' "Uninstall"
if (Test-Path $shortcut) { throw "The Start menu shortcut was left behind" }
if (-not (Test-Path $config)) { throw "A quiet uninstall removed the user's settings; it should keep them" }
if (Test-Path (Join-Path ([System.IO.Path]::GetTempPath()) "Kiitou-update")) { throw "The uninstaller left the update folder behind" }
Write-Host "  program removed; settings kept (a quiet uninstall never asks, so it keeps them)"

Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $logs -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Installer test passed."

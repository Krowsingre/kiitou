# Drives the published Kiitou.exe through every view and all fourteen themes on the build
# runner, checks that each view really appears, and saves a screenshot of each.
# Runs under Windows PowerShell 5.1 (UI Automation assemblies are built in there).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$ShotDir
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class KiitouTourNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
}
"@

New-Item -ItemType Directory -Force $ShotDir | Out-Null
$ShotDir = (Resolve-Path $ShotDir).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("kiitou-tour-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$exeCopy = Join-Path $work "Kiitou.exe"
Copy-Item $Exe $exeCopy
$configPath = Join-Path $work "config.json"

function New-Assessment([string]$Status, [string[]]$Reasons, [string[]]$Warnings) {
    return [ordered]@{
        Status = $Status; Reasons = @($Reasons); Warnings = @($Warnings)
        AssessedUtc = (Get-Date).ToUniversalTime().AddHours(-1).ToString("o")
        HttpStatus = 200; CertificateSubject = "CN=example.com"; CertificateIssuer = "CN=Example CA"
        CertificateNotAfterUtc = (Get-Date).ToUniversalTime().AddDays(60).ToString("o")
        ConnectMilliseconds = 18.4; TotalMilliseconds = 96.2
    }
}

function Write-Config([string]$Color, [string]$Mode, [bool]$Tray) {
    $ids = @{}
    foreach ($name in "s1", "s2", "s3", "s4", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "p1", "p2") { $ids[$name] = [guid]::NewGuid().ToString() }
    $verified = New-Assessment "ConnectionVerified" @("Connected on port 443 with a certificate that is trusted and valid for the hostname.", "The server replied with HTTP 200.", "The address is currently published in DNS for this hostname.") @("Reputation was not checked: No reputation service is configured.")
    $incomplete = New-Assessment "ConnectionWorksVerificationIncomplete" @("Connected on port 443 with a certificate that is trusted and valid for the hostname.", "The server replied with HTTP 200.") @("The address is not among those currently published in DNS for this hostname. On its own this is not evidence of a problem.")
    $failed = New-Assessment "FailedOrUnsafe" @("Connection failed: The connection attempt timed out.") @()
    $config = [ordered]@{
        SchemaVersion = 1
        Theme = [ordered]@{ Color = $Color; Mode = $Mode }
        Websites = @(
            [ordered]@{ Id = $ids.s1; Hostname = "example.com"; Addresses = @(
                [ordered]@{ Id = $ids.a1; IpAddress = "93.184.216.34"; LastAssessment = $verified },
                [ordered]@{ Id = $ids.a2; IpAddress = "151.101.1.69"; LastAssessment = $incomplete },
                [ordered]@{ Id = $ids.a3; IpAddress = "2606:2800:220:1:248:1893:25c8:1946" }) },
            [ordered]@{ Id = $ids.s2; Hostname = "cdn.example.com"; Addresses = @(
                [ordered]@{ Id = $ids.a4; IpAddress = "2606:2800:220:1:248:1893:25c8:1947"; LastAssessment = $incomplete },
                [ordered]@{ Id = $ids.a5; IpAddress = "104.18.26.120" }) },
            [ordered]@{ Id = $ids.s3; Hostname = "update.example.com"; Addresses = @(
                [ordered]@{ Id = $ids.a6; IpAddress = "104.18.27.120" }) },
            [ordered]@{ Id = $ids.s4; Hostname = "media.example.com"; Addresses = @(
                [ordered]@{ Id = $ids.a7; IpAddress = "151.101.65.69"; LastAssessment = $failed }) })
        Profiles = @(
            [ordered]@{ Id = $ids.p1; Name = "Default"; Selections = @() },
            [ordered]@{ Id = $ids.p2; Name = "Gaming"; LastSavedUtc = (Get-Date).ToUniversalTime().AddDays(-2).ToString("o"); Selections = @(
                [ordered]@{ WebsiteId = $ids.s1; Enabled = $true; SelectedIpv4Id = $ids.a1; SelectedIpv6Id = $ids.a3 },
                [ordered]@{ WebsiteId = $ids.s2; Enabled = $true; SelectedIpv6Id = $ids.a4 },
                [ordered]@{ WebsiteId = $ids.s4; Enabled = $true; SelectedIpv4Id = $ids.a7 }) })
        ActiveProfileId = $ids.p2
        Settings = [ordered]@{ MinimizeToTray = $Tray }
    }
    [System.IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 10))
}

function Start-Kiitou {
    $process = Start-Process $exeCopy -PassThru
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 90) {
        $process.Refresh()
        if ($process.HasExited -or $process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    }
    $process.Refresh()
    if ($process.HasExited) { throw "Kiitou exited at startup with code $($process.ExitCode)" }
    if ($process.MainWindowTitle -notlike "Kiitou*Host Mapping Manager") {
        Save-Shot $process "failure-startup"
        throw "Unexpected window title '$($process.MainWindowTitle)'"
    }
    Start-Sleep -Milliseconds 900
    return $process
}

function Stop-Kiitou($process) {
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(8000)) { $process.Kill(); throw "Kiitou did not close when asked" }
    }
    if ($process.ExitCode -ne 0) { throw "Kiitou closed with exit code $($process.ExitCode)" }
}

function Save-Shot($process, [string]$name) {
    try {
        [void][KiitouTourNative]::SetForegroundWindow($process.MainWindowHandle)
        Start-Sleep -Milliseconds 250
        $rect = New-Object KiitouTourNative+Rect
        [void][KiitouTourNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect)
        $width = [Math]::Max(1, $rect.Right - $rect.Left); $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
        $bitmap = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
        $bitmap.Save((Join-Path $ShotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch {
        Write-Host "  screenshot '$name' not captured: $($_.Exception.Message)"
    }
}

function Find-ById($process, [string]$automationId) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Assert-Alive($process, [string]$after) {
    $process.Refresh()
    if ($process.HasExited) { throw "Kiitou exited after: $after (exit code $($process.ExitCode))" }
    if ($process.MainWindowTitle -like "*error*") { Save-Shot $process "failure"; throw "Kiitou showed an error after: $after" }
}

$views = @(
    @{ Nav = "NavHome"; Control = "HomeGrid"; Name = "1-home" },
    @{ Nav = "NavWebsites"; Control = "WebsiteList"; Name = "2-websites" },
    @{ Nav = "NavProfiles"; Control = "ProfileGrid"; Name = "3-profiles" },
    @{ Nav = "NavDiscovery"; Control = "TestGrid"; Name = "4-discovery-and-test" },
    @{ Nav = "NavVerification"; Control = "VerifyGrid"; Name = "5-verification" },
    @{ Nav = "NavHosts"; Control = "HostsText"; Name = "6-hosts-file" },
    @{ Nav = "NavSettings"; Control = "TrayOption"; Name = "7-settings" }
)

Write-Host "== Every view (purple, dark)"
Write-Config "purple" "dark" $false
$p = Start-Kiitou
foreach ($view in $views) {
    $nav = Find-ById $p $view.Nav
    if ($null -eq $nav) { throw "Navigation item $($view.Nav) was not found" }
    $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 700
    Assert-Alive $p "opening $($view.Name)"
    $control = Find-ById $p $view.Control
    if ($null -eq $control -or $control.Current.IsOffscreen) { Save-Shot $p "failure"; throw "View $($view.Name) did not appear" }
    Save-Shot $p "view-$($view.Name)"
    Write-Host "  $($view.Name) ok"
}

Write-Host "== Minimum window size"
(Find-ById $p "NavHome").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
[void][KiitouTourNative]::MoveWindow($p.MainWindowHandle, 40, 40, 300, 200, $true)
Start-Sleep -Milliseconds 700
$rect = New-Object KiitouTourNative+Rect
[void][KiitouTourNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
Write-Host "  window refused to shrink below $w x $h"
if ($w -lt 720 -or $h -lt 450) { throw "Window became smaller than 720 x 450 ($w x $h)" }
if ($w -gt 760 -or $h -gt 490) { throw "Minimum size is larger than expected ($w x $h)" }
Save-Shot $p "size-minimum-home"
Stop-Kiitou $p
$saved = Get-Content $configPath -Raw | ConvertFrom-Json
if ($saved.Websites.Count -ne 4 -or $saved.Profiles.Count -ne 2) { throw "Configuration changed during the tour" }

Write-Host "== All fourteen themes"
foreach ($color in "purple", "blue", "green", "gold", "red", "silver", "teal") {
    foreach ($mode in "dark", "light") {
        Write-Config $color $mode $false
        $p = Start-Kiitou
        Assert-Alive $p "starting with $color/$mode"
        Save-Shot $p "theme-$color-$mode"
        Stop-Kiitou $p
        Write-Host "  $color/$mode ok"
    }
}

Write-Host "== Notification-area icon option"
Write-Config "teal" "dark" $true
$p = Start-Kiitou
Assert-Alive $p "starting with the notification-area icon enabled"
Stop-Kiitou $p

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "UI tour passed."

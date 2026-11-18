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
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    public static void LeftDown() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
    public static void LeftUp() { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string ClassOf(IntPtr window) {
        System.Text.StringBuilder text = new System.Text.StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private static string TextOf(IntPtr window) {
        System.Text.StringBuilder text = new System.Text.StringBuilder(4096);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    // The visible message box (window class #32770) belonging to a process, or zero.
    public static IntPtr FindDialog(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr parameter) {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)processId && IsWindowVisible(window) && ClassOf(window) == "#32770") {
                found = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string DialogText(IntPtr dialog) {
        System.Text.StringBuilder all = new System.Text.StringBuilder();
        EnumChildWindows(dialog, delegate(IntPtr child, IntPtr parameter) {
            if (ClassOf(child) == "Static") { all.Append(TextOf(child)).Append(' '); }
            return true;
        }, IntPtr.Zero);
        return all.ToString();
    }

    // Presses the dialog button with the given caption (ignoring the accelerator mark).
    public static bool PressButton(IntPtr dialog, string caption) {
        IntPtr button = IntPtr.Zero;
        EnumChildWindows(dialog, delegate(IntPtr child, IntPtr parameter) {
            if (ClassOf(child) == "Button" && TextOf(child).Replace("&", "") == caption) {
                button = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (button == IntPtr.Zero) { return false; }
        const uint WM_COMMAND = 0x0111;
        return PostMessage(dialog, WM_COMMAND, (IntPtr)GetDlgCtrlID(button), button);
    }
}
"@

New-Item -ItemType Directory -Force $ShotDir | Out-Null
$ShotDir = (Resolve-Path $ShotDir).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("kiitou-tour-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$exeCopy = Join-Path $work "Kiitou.exe"
Copy-Item $Exe $exeCopy
# Kiitou keeps its settings in local application data; the tour points it at a folder of its own.
$data = Join-Path $work "data"
New-Item -ItemType Directory -Force $data | Out-Null
$env:KIITOU_DATA_DIR = $data
$env:KIITOU_NO_UPDATE_CHECK = "1"
$configPath = Join-Path $data "config.json"

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
    @{ Nav = "NavNetwork"; Control = "LogGrid"; Name = "5-network" },
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

Write-Host "== Default menu order"
$navNames = @("NavHome", "NavWebsites", "NavDiscovery", "NavProfiles", "NavNetwork", "NavHosts", "NavSettings")
function Get-NavOrder($process) {
    $items = foreach ($name in $navNames) {
        $element = Find-ById $process $name
        [pscustomobject]@{ Name = $name; Top = $element.Current.BoundingRectangle.Top }
    }
    return @($items | Sort-Object Top | ForEach-Object Name)
}
$order = Get-NavOrder $p
if (($order -join ",") -ne ($navNames -join ",")) { throw "Unexpected default menu order: $($order -join ', ')" }
Write-Host "  $($order -join ', ')"

Write-Host "== Dragging a menu item"
$settingsRect = (Find-ById $p "NavSettings").Current.BoundingRectangle
$homeRect = (Find-ById $p "NavHome").Current.BoundingRectangle
[void][KiitouTourNative]::SetForegroundWindow($p.MainWindowHandle)
$x = [int]($settingsRect.Left + $settingsRect.Width / 2)
[void][KiitouTourNative]::SetCursorPos($x, [int]($settingsRect.Top + $settingsRect.Height / 2))
Start-Sleep -Milliseconds 200
[KiitouTourNative]::LeftDown()
$fromY = [int]($settingsRect.Top + $settingsRect.Height / 2)
$toY = [int]($homeRect.Top + 4)
for ($y = $fromY; $y -ge $toY; $y -= 6) {
    [void][KiitouTourNative]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 15
}
[void][KiitouTourNative]::SetCursorPos($x, $toY)
Start-Sleep -Milliseconds 200
[KiitouTourNative]::LeftUp()
Start-Sleep -Milliseconds 600
Assert-Alive $p "dragging a menu item"
$order = Get-NavOrder $p
if ($order[0] -ne "NavSettings") { Save-Shot $p "failure"; throw "Dragging Settings to the top did not move it (order: $($order -join ', '))" }
$shown = Find-ById $p "TrayOption"
if ($null -eq $shown -or $shown.Current.IsOffscreen) { Save-Shot $p "failure"; throw "After dragging Settings, its page should still be shown" }
Save-Shot $p "menu-reordered"
$savedOrder = (Get-Content $configPath -Raw | ConvertFrom-Json).Settings.NavOrder
if ($savedOrder[0] -ne "settings") { throw "The new menu order was not saved (saved: $($savedOrder -join ', '))" }
Stop-Kiitou $p
$p = Start-Kiitou
$order = Get-NavOrder $p
if ($order[0] -ne "NavSettings") { throw "The menu order was not kept after restarting (order: $($order -join ', '))" }
Write-Host "  Settings dragged to the top; order saved and kept after a restart"
Stop-Kiitou $p
Write-Config "purple" "dark" $false
$p = Start-Kiitou

Write-Host "== Layout and spacing rules"
Stop-Kiitou $p
$report = Join-Path $work "layout-report.txt"
$check = Start-Process $exeCopy -ArgumentList "--layout-check", "`"$report`"" -PassThru
if (-not $check.WaitForExit(120000)) { $check.Kill(); throw "The layout check did not finish" }
if (Test-Path $report) { Get-Content $report | ForEach-Object { Write-Host "  $_" } }
if ($check.ExitCode -ne 0) { throw "The layout check found problems (exit code $($check.ExitCode))" }
$p = Start-Kiitou

Write-Host "== Fixed window size"
(Find-ById $p "NavHome").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$rect = New-Object KiitouTourNative+Rect
[void][KiitouTourNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
if ($w -ne 1040 -or $h -ne 622) { throw "Window opened at $w x $h instead of 1040 x 622" }
foreach ($attempt in @(@(300, 200), @(1600, 1000))) {
    [void][KiitouTourNative]::MoveWindow($p.MainWindowHandle, 20, 20, $attempt[0], $attempt[1], $true)
    Start-Sleep -Milliseconds 500
    [void][KiitouTourNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -ne 1040 -or $h -ne 622) { throw "Window changed size to $w x $h when asked for $($attempt[0]) x $($attempt[1])" }
}
Write-Host "  window stays at 1040 x 622"
Stop-Kiitou $p
$saved = Get-Content $configPath -Raw | ConvertFrom-Json
if ($saved.Websites.Count -ne 4 -or $saved.Profiles.Count -ne 2) { throw "Configuration changed during the tour" }

Write-Host "== Update window stages (design preview)"
$homeShot = Join-Path $ShotDir "view-1-home.png"
foreach ($stage in "downloading", "installing", "error") {
    $preview = Start-Process $exeCopy -ArgumentList "--update-preview", $stage, "`"$homeShot`"", "purple", "dark" -PassThru
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 60) {
        $preview.Refresh()
        if ($preview.HasExited -or $preview.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($preview.HasExited) { throw "The update window preview exited (stage $stage, exit code $($preview.ExitCode))" }
    if ($preview.MainWindowTitle -notlike "Kiitou*Updating") { throw "Unexpected update window title '$($preview.MainWindowTitle)'" }
    Start-Sleep -Milliseconds 900
    Save-Shot $preview "update-$stage"
    [void]$preview.CloseMainWindow()
    if (-not $preview.WaitForExit(8000)) { $preview.Kill() }
    Write-Host "  $stage ok"
}

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

function Invoke-Button($process, [string]$automationId) {
    $element = Find-ById $process $automationId
    if ($null -eq $element) { Save-Shot $process "failure"; throw "Button $automationId was not found" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Waits for a message box of the application, presses the named button and returns the box's text.
function Click-Dialog($process, [string]$button, [int]$seconds = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        $process.Refresh()
        if ($process.HasExited) { throw "Kiitou exited while waiting for a '$button' dialog" }
        $dialog = [KiitouTourNative]::FindDialog($process.Id)
        if ($dialog -ne [IntPtr]::Zero) {
            $message = [KiitouTourNative]::DialogText($dialog)
            if ([KiitouTourNative]::PressButton($dialog, $button)) {
                # Wait for this box to close before looking for the next one.
                $closing = [System.Diagnostics.Stopwatch]::StartNew()
                while ($closing.Elapsed.TotalSeconds -lt 10 -and [KiitouTourNative]::FindDialog($process.Id) -eq $dialog) {
                    Start-Sleep -Milliseconds 100
                }
                return $message
            }
            Save-Shot $process "failure"
            throw "The dialog has no '$button' button. Its text: $message"
        }
        Start-Sleep -Milliseconds 250
    }
    Save-Shot $process "failure"
    throw "A dialog with a '$button' button did not appear"
}

function Get-Button($process) { return (Find-ById $process "ApplyText").Current.Name }

function Select-Nav($process, [string]$automationId) {
    (Find-ById $process $automationId).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600
}

# Waits until a condition holds, or fails with the given description.
function Wait-Until([scriptblock]$condition, [string]$description, $process, [int]$seconds = 45) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 300
    }
    Save-Shot $process "failure"
    throw "Timed out waiting for: $description"
}

function Assert-NoDialog($process, [string]$after) {
    Start-Sleep -Milliseconds 500
    $dialog = [KiitouTourNative]::FindDialog($process.Id)
    if ($dialog -ne [IntPtr]::Zero) {
        Save-Shot $process "failure"
        throw "An unexpected message box appeared after ${after}: $([KiitouTourNative]::DialogText($dialog))"
    }
}

Write-Host "== End to end through the user interface (build runner only)"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($env:GITHUB_ACTIONS -eq "true" -and $elevated) {
    $hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
    function Get-HostsText { [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($hostsPath)) }
    function Get-ManagedCount { @(Select-String -Path $hostsPath -Pattern "# Kiitou$").Count }
    $hostsBefore = Get-HostsText
    if ((Get-ManagedCount) -ne 0) { throw "The runner's hosts file already contains Kiitou entries" }

    Write-Config "purple" "dark" $false
    $p = Start-Kiitou
    if ((Get-Button $p) -ne "Apply Profile") { throw "Expected the button to read 'Apply Profile', saw '$(Get-Button $p)'" }
    if (-not (Find-ById $p "ProfilePicker").Current.IsEnabled) { throw "The profile list should be free while nothing is applied" }

    # Apply from another view: it must write the profile and return to Home, with no message box.
    Select-Nav $p "NavWebsites"
    Invoke-Button $p "ApplyButton"
    Wait-Until { (Get-ManagedCount) -eq 4 } "4 managed hosts entries after applying" $p
    Wait-Until { (Get-Button $p) -eq "Disable Profile" } "button 'Disable Profile' after applying" $p
    Assert-NoDialog $p "applying"
    $homeGrid = Find-ById $p "HomeGrid"
    if ($null -eq $homeGrid -or $homeGrid.Current.IsOffscreen) { Save-Shot $p "failure"; throw "Apply did not return to Home" }
    if ((Find-ById $p "ProfilePicker").Current.IsEnabled) { throw "The profile list should be locked while a profile is applied" }
    $footer = (Find-ById $p "FooterMessage").Current.Name
    if ($footer -notlike "Applied*Gaming*4 mappings*") { throw "Unexpected footer after applying: $footer" }
    $resolved = [System.Net.Dns]::GetHostAddresses("media.example.com") | ForEach-Object { $_.ToString() }
    if ($resolved -notcontains "151.101.65.69") { throw "Windows did not resolve the applied mapping (got: $resolved)" }
    if (-not (Get-HostsText).StartsWith($hostsBefore.TrimEnd("`r", "`n"))) { throw "Unrelated hosts content changed while applying" }
    Save-Shot $p "e2e-1-applied"
    Write-Host "  applied through the UI: 4 mappings written, resolved by Windows, returned to Home, profile list locked"

    Write-Host "  unexpected termination"
    $p.Kill(); [void]$p.WaitForExit(5000)
    $p = Start-Kiitou
    if ((Get-Button $p) -ne "Disable Profile") { throw "State was not kept after an unexpected termination: '$(Get-Button $p)'" }
    $leftovers = @(Get-ChildItem $data -Force -Filter ".kiitou-*")
    if ($leftovers.Count -ne 0) { throw "Temporary files were left behind: $($leftovers.Name -join ', ')" }

    # Disable: removes the mappings and unlocks the profile list, with no message box.
    Invoke-Button $p "ApplyButton"
    Wait-Until { (Get-ManagedCount) -eq 0 } "no managed entries after disabling" $p
    Wait-Until { (Get-Button $p) -eq "Apply Profile" } "button 'Apply Profile' after disabling" $p
    Assert-NoDialog $p "disabling"
    if (-not (Find-ById $p "ProfilePicker").Current.IsEnabled) { throw "The profile list should unlock after disabling" }
    Write-Host "  disabled through the UI"

    Select-Nav $p "NavHosts"
    Invoke-Button $p "BackupBarRestore"
    [void](Click-Dialog $p "Yes")
    Wait-Until { (Get-ManagedCount) -eq 4 } "4 managed entries after restoring the latest backup" $p
    Wait-Until { (Get-Button $p) -eq "Disable Profile" } "button 'Disable Profile' after restore" $p
    Assert-NoDialog $p "restoring"
    Save-Shot $p "e2e-2-restored"
    Write-Host "  latest backup restored through the UI; profile recognised as applied again"

    Invoke-Button $p "ApplyButton"
    Wait-Until { (Get-ManagedCount) -eq 0 } "no managed entries after the final disable" $p
    Assert-NoDialog $p "the final disable"
    Stop-Kiitou $p

    $hostsAfter = Get-HostsText
    if (-not (($hostsAfter -ceq $hostsBefore) -or ($hostsAfter -ceq ($hostsBefore + "`r`n")) -or ($hostsAfter -ceq ($hostsBefore + "`n")))) {
        throw "The hosts file was not returned to its original content"
    }
    $backups = @(Get-ChildItem (Join-Path $data "backups") -Filter "hosts-*.bak")
    if ($backups.Count -lt 4 -or -not ($backups.Name -contains "hosts-original.bak")) { throw "Expected the original and at least three backups, found: $($backups.Name -join ', ')" }
    Write-Host "  hosts file returned to its original content; $($backups.Count) backups kept"
} else {
    Write-Host "  skipped (not an elevated build runner)"
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "UI tour passed."

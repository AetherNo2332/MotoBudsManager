param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MotoBudsWindowTest {
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
}
'@

function Resize-TestWindow([long]$Hwnd, [int]$Width, [int]$Height) {
    if (-not [MotoBudsWindowTest]::SetWindowPos([IntPtr]$Hwnd, [IntPtr]::Zero,
        60, 60, $Width, $Height, 0x0040)) {
        throw 'Window resize failed.'
    }
    Start-Sleep -Milliseconds 500
}

function Get-ElementLeft([long]$Hwnd, [string]$AutomationId) {
    $result = winapp ui get-property $AutomationId -w $Hwnd -p BoundingRectangle --json 2>$null | ConvertFrom-Json
    if (-not $result.properties.BoundingRectangle) {
        throw "Could not read bounds for $AutomationId."
    }
    return [int](($result.properties.BoundingRectangle -split ',')[0])
}

function Get-ElementRight([long]$Hwnd, [string]$AutomationId) {
    $result = winapp ui get-property $AutomationId -w $Hwnd -p BoundingRectangle --json 2>$null | ConvertFrom-Json
    if (-not $result.properties.BoundingRectangle) {
        throw "Could not read bounds for $AutomationId."
    }
    $bounds = $result.properties.BoundingRectangle -split ','
    return [int]$bounds[0] + [int]$bounds[2]
}

function Get-ContentInset([long]$Hwnd, [string]$AutomationId) {
    $windowRect = New-Object MotoBudsWindowTest+Rect
    if (-not [MotoBudsWindowTest]::GetWindowRect([IntPtr]$Hwnd, [ref]$windowRect)) {
        throw 'Could not read the test window bounds.'
    }
    return (Get-ElementLeft $Hwnd $AutomationId) - $windowRect.Left
}

function Assert-BatteryColumnAlignment([long]$Hwnd) {
    $imageCenter = ((Get-ElementLeft $Hwnd 'DeviceHeroImage') + (Get-ElementRight $Hwnd 'DeviceHeroImage')) / 2.0
    $centers = @('Left', 'Right', 'Case') | ForEach-Object {
        ((Get-ElementLeft $Hwnd "ControlBattery$_") + (Get-ElementRight $Hwnd "ControlBattery$_")) / 2.0
    }
    if ([Math]::Abs($centers[1] - $imageCenter) -gt 4 -or
        [Math]::Abs(($centers[0] + $centers[2]) / 2 - $imageCenter) -gt 4) {
        throw "Battery percentages are not visually centered: columns=$($centers -join ','), image center=$imageCenter."
    }
    foreach ($part in @('Left', 'Right', 'Case')) {
        $labelCenter = ((Get-ElementLeft $Hwnd "ControlBattery${part}Label") + (Get-ElementRight $Hwnd "ControlBattery${part}Label")) / 2.0
        $valueCenter = ((Get-ElementLeft $Hwnd "ControlBattery$part") + (Get-ElementRight $Hwnd "ControlBattery$part")) / 2.0
        if ([Math]::Abs($labelCenter - $valueCenter) -gt 4) {
            throw "The $part battery label is not centered above its percentage."
        }
    }
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $global:LASTEXITCODE = 0
        & $Script | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Command exited with $LASTEXITCODE."
        }

        $script:pass++
        $script:results += [pscustomobject]@{ name = $Name; status = 'PASS'; detail = '' }
    }
    catch {
        $script:fail++
        $script:results += [pscustomobject]@{
            name = $Name
            status = 'FAIL'
            detail = $_.Exception.Message
        }
    }
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null

$windowDeadline = (Get-Date).AddSeconds(15)
do {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    $mainWindow = $windows |
        Where-Object { $_.title -eq 'Moto Buds Manager' -and $_.className -eq 'WinUIDesktopWin32WindowClass' } |
        Sort-Object { $_.width * $_.height } -Descending |
        Select-Object -First 1
    if (-not $mainWindow) { Start-Sleep -Milliseconds 250 }
} until ($mainWindow -or (Get-Date) -ge $windowDeadline)
if (-not $mainWindow) {
    throw 'Moto Buds Manager main window was not found.'
}

$mainHwnd = $mainWindow.hwnd
$initialWindowRect = New-Object MotoBudsWindowTest+Rect
if (-not [MotoBudsWindowTest]::GetWindowRect([IntPtr]$mainHwnd, [ref]$initialWindowRect)) {
    throw 'The original Moto Buds Manager window bounds could not be read.'
}

Test-UI 'Home page and primary navigation are available' {
    winapp ui invoke 'NavHome' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Home navigation could not be invoked.' }
    winapp ui wait-for 'HomeRefreshButton' -w $mainHwnd -t 10000
    if ($LASTEXITCODE -ne 0) { throw 'Home page was not found.' }
    winapp ui wait-for 'NavHome' -w $mainHwnd -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Home navigation was not found.' }
    winapp ui wait-for 'NavDevices' -w $mainHwnd -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Device navigation was not found.' }
    winapp ui wait-for 'NavSettings' -w $mainHwnd -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Settings navigation was not found.' }
}

Test-UI 'Debug devices render as supported product cards' {
    winapp ui wait-for 'HomeDeviceCard-0' -w $mainHwnd -t 10000
    if ($LASTEXITCODE -ne 0) { throw 'XT2443 debug card was not rendered.' }
    winapp ui wait-for 'HomeDeviceCard-1' -w $mainHwnd -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Unverified debug card was not rendered.' }
}

winapp ui screenshot -w $mainHwnd -o 'screenshots/01-home.png' 2>$null | Out-Null

Test-UI 'Home card connects and opens verified controls' {
    winapp ui invoke 'HomeDeviceCard-0' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'XT2443 card could not be invoked.' }
    winapp ui wait-for 'DeviceControlTitle' -w $mainHwnd --value 'Moto Buds' -t 15000
    if ($LASTEXITCODE -ne 0) { throw 'Device control page did not open.' }
    winapp ui wait-for 'AncSelector' -w $mainHwnd -p IsEnabled --value 'True' -t 15000
    if ($LASTEXITCODE -ne 0) { throw 'Verified ANC control did not become available.' }
    winapp ui wait-for 'HeaderDisconnectButton' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Disconnect action did not become available.' }
}

Test-UI 'Home battery prefetch is reused by device controls' {
    winapp ui invoke 'NavSettings' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Settings navigation could not be invoked.' }
    winapp ui invoke 'OpenSettingsDiagnosticsButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostics entry could not be invoked.' }
    try {
        winapp ui wait-for 'DiagnosticLogList' -w $mainHwnd -t 5000
        if ($LASTEXITCODE -ne 0) { throw 'Diagnostics page did not appear.' }

        $diagnostics = winapp ui inspect -w $mainHwnd --interactive --json 2>$null | ConvertFrom-Json
        $connectedEntries = @($diagnostics.windows.elements | Where-Object { $_.name -like '*DEBUG fake transport connected.*' })
        $disconnectedEntries = @($diagnostics.windows.elements | Where-Object { $_.name -like '*DEBUG fake transport disconnected.*' })
        if ($connectedEntries.Count -ne 1) {
            throw "Expected one transport connection from home prefetch through device controls, found $($connectedEntries.Count)."
        }
        if ($disconnectedEntries.Count -ne 0) {
            throw "Home prefetch disconnected the reusable session $($disconnectedEntries.Count) time(s)."
        }
    }
    finally {
        winapp ui invoke 'DiagnosticsBackToSettingsButton' -w $mainHwnd | Out-Null
        winapp ui invoke 'NavDevices' -w $mainHwnd | Out-Null
    }

    winapp ui wait-for 'AncSelector' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Verified controls did not remain connected after diagnostics.' }
}

Test-UI 'Bose logo display modes apply immediately' {
    winapp ui wait-for 'SoundByBoseLogo' -w $mainHwnd --gone -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'By-model mode incorrectly showed Bose branding for XT2443-1.' }

    winapp ui invoke 'NavSettings' -w $mainHwnd
    winapp ui wait-for 'BoseLogoModeSelector' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Bose branding setting was not available.' }
    winapp ui invoke 'BoseLogoModeSelector' -w $mainHwnd
    winapp ui invoke 'BoseLogoAlways' -w $mainHwnd
    winapp ui invoke 'NavDevices' -w $mainHwnd
    winapp ui wait-for 'SoundByBoseLogo' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Always-show mode did not display Bose branding.' }

    winapp ui invoke 'NavSettings' -w $mainHwnd
    winapp ui invoke 'BoseLogoModeSelector' -w $mainHwnd
    winapp ui invoke 'BoseLogoOff' -w $mainHwnd
    winapp ui invoke 'NavDevices' -w $mainHwnd
    winapp ui wait-for 'SoundByBoseLogo' -w $mainHwnd --gone -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Off mode did not hide Bose branding.' }

    winapp ui invoke 'NavSettings' -w $mainHwnd
    winapp ui invoke 'BoseLogoModeSelector' -w $mainHwnd
    winapp ui invoke 'BoseLogoByModel' -w $mainHwnd
    winapp ui invoke 'NavDevices' -w $mainHwnd
    winapp ui wait-for 'SoundByBoseLogo' -w $mainHwnd --gone -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'By-model mode did not restore model-based visibility.' }
}

$windowedContentInset = Get-ContentInset $mainHwnd 'DeviceControlTitle'

winapp ui screenshot -w $mainHwnd -o 'screenshots/02-device-controls.png' 2>$null | Out-Null

Test-UI 'Feature cards reflow at the minimum supported width' {
    Resize-TestWindow $mainHwnd 760 900
    winapp ui wait-for 'RefreshStateCard' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Responsive feature layout did not remain available.' }
}

Test-UI 'Battery labels and percentages stay centered at minimum window width' {
    Assert-BatteryColumnAlignment $mainHwnd
}

winapp ui screenshot -w $mainHwnd -o 'screenshots/03-device-controls-narrow.png' 2>$null | Out-Null

Test-UI 'Maximized controls remain scrollable without horizontal clipping' {
    [MotoBudsWindowTest]::ShowWindow([IntPtr]$mainHwnd, 3) | Out-Null
    Start-Sleep -Milliseconds 500
    winapp ui scroll 'DeviceControlScroll' -w $mainHwnd --to top
    $maximizedContentInset = Get-ContentInset $mainHwnd 'DeviceControlTitle'
    if ($maximizedContentInset -gt ($windowedContentInset + 20)) {
        throw "Control content shifted right after maximize: windowed=$windowedContentInset, maximized=$maximizedContentInset."
    }
    $maximizedWindowRect = New-Object MotoBudsWindowTest+Rect
    [MotoBudsWindowTest]::GetWindowRect([IntPtr]$mainHwnd, [ref]$maximizedWindowRect) | Out-Null
    $disconnectRight = Get-ElementRight $mainHwnd 'HeaderDisconnectButton'
    if (($maximizedWindowRect.Right - $disconnectRight) -gt 80) {
        throw "Control content did not fill the maximized page: right margin=$($maximizedWindowRect.Right - $disconnectRight)."
    }
    winapp ui wait-for 'AncControlCard' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'The noise-control card disappeared in the wide layout.' }
    if ((Get-ElementLeft $mainHwnd 'AncControlCard') -le (Get-ElementRight $mainHwnd 'DeviceHeroImage')) {
        throw 'Maximized feature controls were not placed to the right of the product pane.'
    }
    winapp ui scroll 'DeviceControlScroll' -w $mainHwnd --to bottom
    if ($LASTEXITCODE -ne 0) { throw 'Maximized control page could not scroll to the final row.' }
    winapp ui wait-for 'RefreshStateButton' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'The final control row was clipped while maximized.' }
    winapp ui wait-for 'DeviceHeroImage' -w $mainHwnd -p IsOffscreen --value 'False' -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'The product image scrolled away with the feature controls.' }
    winapp ui wait-for 'ControlBatteryLeft' -w $mainHwnd -p IsOffscreen --value 'False' -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'The left-pane battery state did not remain visible.' }
}

Test-UI 'Battery summary stays compact and centered below the product image' {
    $batteryResult = winapp ui get-property 'BatterySummaryText' -w $mainHwnd -p BoundingRectangle --json 2>$null | ConvertFrom-Json
    $imageResult = winapp ui get-property 'DeviceHeroImage' -w $mainHwnd -p BoundingRectangle --json 2>$null | ConvertFrom-Json
    $batteryBounds = @($batteryResult.properties.BoundingRectangle -split ',' | ForEach-Object { [double]$_ })
    $imageBounds = @($imageResult.properties.BoundingRectangle -split ',' | ForEach-Object { [double]$_ })
    if ($batteryBounds.Count -ne 4 -or $imageBounds.Count -ne 4 -or $imageBounds[2] -le 0) {
        throw 'Could not measure the visible battery component and product image.'
    }
    if ($batteryBounds[2] -gt ($imageBounds[2] * 1.3)) {
        throw "Battery summary is spread across the product pane: width=$($batteryBounds[2]), image=$($imageBounds[2])."
    }
    $batteryCenter = $batteryBounds[0] + $batteryBounds[2] / 2
    $imageCenter = $imageBounds[0] + $imageBounds[2] / 2
    if ([Math]::Abs($batteryCenter - $imageCenter) -gt 8) {
        throw 'Battery summary is not centered below the product image.'
    }
    Assert-BatteryColumnAlignment $mainHwnd
}

winapp ui screenshot -w $mainHwnd -o 'screenshots/04-device-controls-maximized.png' 2>$null | Out-Null
[MotoBudsWindowTest]::ShowWindow([IntPtr]$mainHwnd, 9) | Out-Null
[MotoBudsWindowTest]::SetWindowPos(
    [IntPtr]$mainHwnd,
    [IntPtr]::Zero,
    $initialWindowRect.Left,
    $initialWindowRect.Top,
    $initialWindowRect.Right - $initialWindowRect.Left,
    $initialWindowRect.Bottom - $initialWindowRect.Top,
    0x0040) | Out-Null
Start-Sleep -Milliseconds 500

Test-UI 'Restored window returns to its original content alignment' {
    winapp ui scroll 'DeviceControlScroll' -w $mainHwnd --to top
    $restoredContentInset = Get-ContentInset $mainHwnd 'DeviceControlTitle'
    if ([Math]::Abs($restoredContentInset - $windowedContentInset) -gt 20) {
        throw "Restored alignment changed: original=$windowedContentInset, restored=$restoredContentInset."
    }
}

Test-UI 'Disconnect returns to a non-busy home state' {
    winapp ui invoke 'HeaderDisconnectButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Disconnect action could not be invoked.' }
    winapp ui wait-for 'HomeRefreshButton' -w $mainHwnd -t 10000
    if ($LASTEXITCODE -ne 0) { throw 'Home page did not return after disconnect.' }
    winapp ui wait-for 'HomeRefreshButton' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Home page remained busy after disconnect.' }
}

Test-UI 'Static model opens its device controls instead of the laboratory' {
    winapp ui invoke 'HomeDeviceCard-1' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Static model card could not be invoked.' }
    winapp ui wait-for 'DeviceControlTitle' -w $mainHwnd --value 'Moto Buds 2 Plus' -t 15000
    if ($LASTEXITCODE -ne 0) { throw 'Static model control page did not open.' }
    winapp ui wait-for 'StaticAdaptationInfo' -w $mainHwnd --gone -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Technical adaptation notice should not appear in device controls.' }
    winapp ui wait-for 'SpatialSelector' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Whitelisted spatial control was not enabled.' }
    winapp ui wait-for 'VolumeBoostToggle' -w $mainHwnd -p IsEnabled --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Unsupported volume boost control was not disabled.' }
    winapp ui wait-for 'SoundByBoseLogo' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Sound by Bose branding was not visible for XT2641-1.' }
    winapp ui invoke 'OpenFitTestDialogButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Fit-test dialog could not be opened.' }
    winapp ui wait-for 'FitTestStartButton' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Fit-test start action was not available.' }
    winapp ui invoke 'FitTestStartButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Fit test could not be started.' }
    Start-Sleep -Milliseconds 1200
    winapp ui wait-for 'FitTestLeftStatus' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Left fit-test result was not displayed.' }
    winapp ui wait-for 'FitTestStartButton' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Fit-test retry action was not restored after the result.' }
    winapp ui send-keys 'escape' -w $mainHwnd --via send-input
    if ($LASTEXITCODE -ne 0) { throw 'Fit-test dialog could not be closed.' }
    winapp ui invoke 'HeaderDisconnectButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Static model could not be disconnected.' }
    winapp ui wait-for 'HomeRefreshButton' -w $mainHwnd -t 10000
    if ($LASTEXITCODE -ne 0) { throw 'Home page did not return after static-model disconnect.' }
}

Test-UI 'Settings and diagnostics nested navigation work' {
    winapp ui invoke 'NavSettings' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Settings navigation could not be invoked.' }
    winapp ui wait-for 'ThemeSelector' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Settings page did not appear.' }
    winapp ui invoke 'OpenSettingsDiagnosticsButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostics entry could not be invoked.' }
    winapp ui wait-for 'DiagnosticLogList' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostics page did not appear.' }
    winapp ui invoke 'DiagnosticsBackToSettingsButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostics back action could not be invoked.' }
    winapp ui wait-for 'ThemeSelector' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Settings page did not return.' }
}

Test-UI 'Sign-in startup can be toggled and refreshed without losing system state' {
    winapp ui wait-for 'StartupToggle' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Startup switch was not available.' }
    $initialStartupState = (winapp ui get-value 'StartupToggle' -w $mainHwnd --json 2>$null | ConvertFrom-Json).text
    if ($initialStartupState -notin @('On', 'Off')) { throw 'Could not read the initial startup setting.' }
    $requestedStartupState = if ($initialStartupState -eq 'On') { 'Off' } else { 'On' }
    try {
        winapp ui invoke 'StartupToggle' -w $mainHwnd
        winapp ui wait-for 'StartupToggle' -w $mainHwnd --value $requestedStartupState -t 5000
        if ($LASTEXITCODE -ne 0) { throw 'Startup registration did not change.' }
        winapp ui invoke 'NavHome' -w $mainHwnd
        winapp ui invoke 'NavSettings' -w $mainHwnd
        winapp ui wait-for 'StartupToggle' -w $mainHwnd -p IsEnabled --value 'True' -t 5000
        winapp ui wait-for 'StartupToggle' -w $mainHwnd --value $requestedStartupState -t 5000
        if ($LASTEXITCODE -ne 0) { throw 'Startup registration was not retained after refreshing the Windows state.' }
    }
    finally {
        $currentStartupState = (winapp ui get-value 'StartupToggle' -w $mainHwnd --json 2>$null | ConvertFrom-Json).text
        if ($currentStartupState -ne $initialStartupState) { winapp ui invoke 'StartupToggle' -w $mainHwnd | Out-Null }
        winapp ui wait-for 'StartupToggle' -w $mainHwnd --value $initialStartupState -t 5000
        if ($LASTEXITCODE -ne 0) { throw 'Could not restore the original startup registration.' }
    }
}

Test-UI 'Laboratory requires confirmation and can be cancelled' {
    winapp ui invoke 'OpenSettingsLabButton' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Laboratory entry could not be invoked.' }
    Start-Sleep -Milliseconds 500
    winapp ui send-keys 'escape' -w $mainHwnd --via send-input
    if ($LASTEXITCODE -ne 0) { throw 'Laboratory confirmation could not be cancelled.' }
    winapp ui wait-for 'ThemeSelector' -w $mainHwnd -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'Settings page was not restored after cancelling.' }
}

winapp ui screenshot -w $mainHwnd -o 'screenshots/05-settings.png' 2>$null | Out-Null

$results | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 'test-results.json'
Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object status -eq 'FAIL' | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}
if ($fail -gt 0) { exit 1 }

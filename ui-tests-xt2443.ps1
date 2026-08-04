param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -ne 0) { throw "$output" }
        $script:pass++
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

function Assert-UiSuccess {
    param([scriptblock]$Command)
    $output = & $Command 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$output" }
}

New-Item -ItemType Directory -Force -Path '.\screenshots' | Out-Null

Test-UI 'Fake XT2443 and unverified device cards are visible' {
    Assert-UiSuccess { winapp ui wait-for 'HomeDeviceCard-0' -a $AppPid -t 10000 }
    Assert-UiSuccess { winapp ui wait-for 'HomeDeviceCard-1' -a $AppPid -t 3000 }
    winapp ui wait-for 'NavDevices' -a $AppPid -t 3000
}

Test-UI 'XT2443 card connects and opens stable controls' {
    Assert-UiSuccess { winapp ui invoke 'HomeDeviceCard-0' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'DeviceControlScroll' -a $AppPid -t 10000 }
    Assert-UiSuccess { winapp ui wait-for 'HeaderDisconnectButton' -a $AppPid -t 10000 -p IsEnabled --value true }
    winapp ui wait-for 'BatterySummaryText' -a $AppPid --value '20%' --contains -t 5000
}

Test-UI 'All XT2443 controls are exposed' {
    $ids = @(
        'AncSelector', 'AdaptiveHearingToggle', 'HiResToggle', 'GamingModeToggle',
        'VolumeBoostToggle', 'InEarDetectionToggle', 'DualConnectionToggle',
        'XtFindLeft', 'XtFindRight', 'XtFindStop', 'FitTestStart', 'FitTestStop',
        'RefreshStateButton'
    )
    foreach ($id in $ids) {
        Assert-UiSuccess { winapp ui wait-for $id -a $AppPid -t 3000 }
    }
    winapp ui wait-for 'RefreshStateButton' -a $AppPid -t 3000
}

Test-UI 'Control cards have equal height' {
    $xaml = Get-Content -Raw '.\src\MotoBudsManager\MainPage.xaml'
    $heightCount = ([regex]::Matches($xaml, 'Height="120"')).Count
    if ($heightCount -ne 10) { throw "Expected 10 cards at height 120, found $heightCount." }
    winapp ui screenshot -a $AppPid -o '.\screenshots\xt2443-equal-card-heights.png' --focus
}

Test-UI 'ANC transparency writes and reads back' {
    Assert-UiSuccess { winapp ui scroll-into-view 'AncTransparencyItem' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'AncTransparencyItem' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'AncTransparencyItem' -a $AppPid -t 5000 }
    Assert-UiSuccess { winapp ui invoke 'AncOnItem' -a $AppPid }
    winapp ui wait-for 'AncOnItem' -a $AppPid -t 5000
}

Test-UI 'Verified toggle and dual-connection controls read back' {
    Assert-UiSuccess { winapp ui scroll-into-view 'AdaptiveHearingToggle' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'AdaptiveHearingToggle' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'AdaptiveHearingToggle' -a $AppPid --value 'Off' -t 5000 }
    Assert-UiSuccess { winapp ui invoke 'AdaptiveHearingToggle' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'AdaptiveHearingToggle' -a $AppPid --value 'On' -t 5000 }
    Assert-UiSuccess { winapp ui invoke 'DualConnectionToggle' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'DualConnectionToggle' -a $AppPid --value 'On' -t 5000 }
    Assert-UiSuccess { winapp ui invoke 'DualConnectionToggle' -a $AppPid }
    winapp ui wait-for 'DualConnectionToggle' -a $AppPid --value 'Off' -t 5000
}

Test-UI 'Find and fit-test actions can be stopped' {
    Assert-UiSuccess { winapp ui scroll-into-view 'XtFindLeft' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'XtFindLeft' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'XtFindStop' -a $AppPid }
    Assert-UiSuccess { winapp ui scroll-into-view 'FitTestStart' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'FitTestStart' -a $AppPid }
    winapp ui invoke 'FitTestStop' -a $AppPid
}

winapp ui screenshot -a $AppPid -o '.\screenshots\xt2443-controls-system.png' --focus 2>$null | Out-Null

Test-UI 'Light theme applies to the whole window' {
    Assert-UiSuccess { winapp ui invoke 'NavSettings' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'ThemeSelector' -a $AppPid -t 3000 }
    Assert-UiSuccess { winapp ui invoke 'ThemeSelector' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'ThemeLight' -a $AppPid }
    Start-Sleep -Milliseconds 300
    Assert-UiSuccess { winapp ui invoke 'NavDevice-0' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'AncSelector' -a $AppPid -t 5000 }
    winapp ui screenshot -a $AppPid -o '.\screenshots\xt2443-controls-light.png' --focus 2>$null | Out-Null
}

Test-UI 'Dark theme applies to the whole window' {
    Assert-UiSuccess { winapp ui invoke 'NavSettings' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'ThemeSelector' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'ThemeDark' -a $AppPid }
    Start-Sleep -Milliseconds 300
    Assert-UiSuccess { winapp ui invoke 'NavDevice-0' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'AncSelector' -a $AppPid -t 5000 }
    winapp ui screenshot -a $AppPid -o '.\screenshots\xt2443-controls-dark.png' --focus 2>$null | Out-Null
}

Test-UI 'Unverified device warns then redirects to lab' {
    Assert-UiSuccess { winapp ui invoke 'NavHome' -a $AppPid }
    Assert-UiSuccess { winapp ui invoke 'HomeDeviceCard-1' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'UntestedDeviceDialog' -a $AppPid -t 3000 }
    Assert-UiSuccess { winapp ui invoke 'PrimaryButton' -a $AppPid }
    Assert-UiSuccess { winapp ui wait-for 'ExperimentalStatus' -a $AppPid -t 10000 }
    winapp ui wait-for 'LabBackToSettingsButton' -a $AppPid -t 3000
}

$results | ForEach-Object { [PSCustomObject]$_ } | Format-Table -AutoSize
$results | ConvertTo-Json | Set-Content -Encoding UTF8 '.\xt2443-ui-test-results.json'
Write-Host "Passed: $pass | Failed: $fail"
if ($fail -gt 0) { exit 1 }

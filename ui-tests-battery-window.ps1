param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
$results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "winapp exited with $LASTEXITCODE" }
        $script:pass++
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$($_.Exception.Message)" }
    }
}

Test-UI 'Home battery snapshot loaded' {
    winapp ui wait-for 'HomeBatteryRight-0' -a $AppPid --value '19%' -t 10000
}
Test-UI 'Home charging states shown' {
    winapp ui wait-for 'HomeBatteryLeft-0' -a $AppPid --value '20%' --contains -t 3000
    winapp ui wait-for 'HomeBatteryCase-0' -a $AppPid --value '54%' --contains -t 3000
    winapp ui wait-for 'HomeChargingLeft-0' -a $AppPid -t 3000
    winapp ui wait-for 'HomeChargingRight-0' -a $AppPid -t 3000
    winapp ui wait-for 'HomeChargingCase-0' -a $AppPid --gone -t 3000
}
Test-UI 'Control page charging states shown' {
    winapp ui invoke 'HomeDeviceCard-0' -a $AppPid
    winapp ui wait-for 'BatterySummaryText' -a $AppPid --value '20%' --contains -t 10000
    $value = winapp ui get-value 'BatterySummaryText' -a $AppPid --json | ConvertFrom-Json
    if ($value.text -notmatch '20%' -or $value.text -notmatch '54%') {
        throw "Unexpected battery summary: $($value.text)"
    }
    winapp ui wait-for 'ControlChargingLeft' -a $AppPid -t 3000
    winapp ui wait-for 'ControlChargingRight' -a $AppPid -t 3000
    winapp ui wait-for 'ControlChargingCase' -a $AppPid --gone -t 3000
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowSizingTest {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
'@

Test-UI 'Minimum window size enforced' {
    $windows = winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
    $window = $windows | Where-Object { $_.title -ne 'PopupHost' } | Select-Object -First 1
    $hwndValue = "$($window.hwnd)"
    $hwnd = if ($hwndValue.StartsWith('0x')) {
        [IntPtr]::new([Convert]::ToInt64($hwndValue.Substring(2), 16))
    } else {
        [IntPtr]::new([int64]$window.hwnd)
    }
    [WindowSizingTest]::MoveWindow($hwnd, 80, 80, 400, 300, $true) | Out-Null
    Start-Sleep -Milliseconds 500
    $rect = New-Object WindowSizingTest+RECT
    [WindowSizingTest]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 700 -or $height -lt 550) {
        throw "Window shrank below minimum: ${width}x${height}"
    }
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o 'screenshots/battery-charging-min-window.png' | Out-Null

Write-Host "Passed: $pass | Failed: $fail"
$results | ConvertTo-Json | Set-Content -Encoding utf8 'ui-test-results-battery-window.json'
if ($fail -gt 0) {
    $results | Where-Object status -eq 'FAIL' | ForEach-Object { Write-Host "FAIL: $($_.name) - $($_.detail)" }
    exit 1
}

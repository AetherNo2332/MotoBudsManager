param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$results = @()

function Test-DeviceControlUI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "winapp ui exited with $LASTEXITCODE" }
        $script:results += [PSCustomObject]@{ Name = $Name; Status = 'PASS'; Detail = '' }
    }
    catch {
        $script:results += [PSCustomObject]@{ Name = $Name; Status = 'FAIL'; Detail = $_.Exception.Message }
    }
}

Test-DeviceControlUI 'Home card is available' {
    winapp ui wait-for 'HomeDeviceCard-0' -a $AppPid -t 10000
}
Test-DeviceControlUI 'Home card connects and opens control page' {
    winapp ui invoke 'HomeDeviceCard-0' -a $AppPid
    winapp ui wait-for 'DeviceControlScroll' -a $AppPid -t 10000
    winapp ui wait-for 'DisconnectButton' -a $AppPid -t 10000 -p IsEnabled --value true
}
Test-DeviceControlUI 'Device control essentials are exposed' {
    winapp ui wait-for 'DeviceHeroImage' -a $AppPid -t 3000
    winapp ui wait-for 'AncSelector' -a $AppPid -t 3000
    $matches = winapp ui search 'Moto Buds' -a $AppPid --json 2>$null | ConvertFrom-Json
    if (@($matches).Count -eq 0) { throw 'Moto Buds product name was not exposed.' }
}
Test-DeviceControlUI 'Lab and diagnostics are absent from top navigation' {
    winapp ui wait-for 'NavLab' -a $AppPid -t 1000 --gone
    winapp ui wait-for 'NavDiagnostics' -a $AppPid -t 1000 --gone
}

New-Item -ItemType Directory -Force -Path '.\screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o '.\screenshots\device-control.png' --focus 2>$null | Out-Null

Test-DeviceControlUI 'Settings contains lab and diagnostics' {
    winapp ui invoke 'NavSettings' -a $AppPid
    winapp ui wait-for 'OpenSettingsLabButton' -a $AppPid -t 3000
    winapp ui wait-for 'OpenSettingsDiagnosticsButton' -a $AppPid -t 3000
}
Test-DeviceControlUI 'Diagnostics opens inside settings and returns' {
    winapp ui invoke 'OpenSettingsDiagnosticsButton' -a $AppPid
    winapp ui wait-for 'DiagnosticLogList' -a $AppPid -t 3000
    winapp ui invoke 'DiagnosticsBackToSettingsButton' -a $AppPid
    winapp ui wait-for 'OpenSettingsDiagnosticsButton' -a $AppPid -t 3000
}

winapp ui screenshot -a $AppPid -o '.\screenshots\settings-merged-tools.png' --focus 2>$null | Out-Null

$results | Format-Table -AutoSize
$results | ConvertTo-Json | Set-Content -Encoding UTF8 '.\device-control-ui-test-results.json'
$failed = @($results | Where-Object Status -eq 'FAIL')
if ($failed.Count -gt 0) { exit 1 }

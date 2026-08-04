param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$results = @()

function Test-HomeUI {
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

Test-HomeUI 'Home page is visible' { winapp ui wait-for 'HomeDeviceCards' -a $AppPid -t 5000 }
Test-HomeUI 'Home navigation exists' { winapp ui wait-for 'NavHome' -a $AppPid -t 3000 }
Test-HomeUI 'Device list navigation exists' { winapp ui wait-for 'NavDevices' -a $AppPid -t 3000 }
Test-HomeUI 'Device appears as second-level navigation' { winapp ui wait-for 'NavDevice-0' -a $AppPid -t 3000 }
Test-HomeUI 'Moto Buds product name is exposed' {
    $matches = winapp ui search 'Moto Buds' -a $AppPid --json 2>$null | ConvertFrom-Json
    if (@($matches).Count -eq 0) { throw 'No Moto Buds UIA element found.' }
}
Test-HomeUI 'Navigate to device list' {
    winapp ui invoke 'NavDevices' -a $AppPid | Out-Null
    winapp ui wait-for 'DeviceList' -a $AppPid -t 3000 | Out-Null
}
Test-HomeUI 'Return to home' {
    winapp ui invoke 'NavHome' -a $AppPid | Out-Null
    winapp ui wait-for 'HomeDeviceCards' -a $AppPid -t 3000 | Out-Null
}

New-Item -ItemType Directory -Force -Path '.\screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o '.\screenshots\home-redesign.png' 2>$null | Out-Null

$results | Format-Table -AutoSize
$failed = @($results | Where-Object Status -eq 'FAIL')
$results | ConvertTo-Json | Set-Content -Encoding UTF8 '.\home-ui-test-results.json'
if ($failed.Count -gt 0) { exit 1 }

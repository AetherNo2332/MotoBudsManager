param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = 'PASS' }
        } else {
            throw "$output"
        }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null

Test-UI 'Main navigation exists' {
    winapp ui wait-for 'NavDevices' -a $AppPid -t 5000
    winapp ui wait-for 'NavLab' -a $AppPid -t 5000
    winapp ui wait-for 'NavDiagnostics' -a $AppPid -t 5000
    winapp ui wait-for 'NavSettings' -a $AppPid -t 5000
}

Test-UI 'Fake device is listed' {
    $matches = winapp ui search 'moto buds' -a $AppPid --json 2>$null | ConvertFrom-Json
    if (-not ($matches.matches | Where-Object { $_.name -eq 'moto buds (test)' })) {
        throw 'DEBUG fake device was not listed.'
    }
}

Test-UI 'Connect and stable ANC controls' {
    winapp ui invoke 'ConnectButton' -a $AppPid
    winapp ui wait-for 'AncSelector' -a $AppPid -p IsEnabled --value 'True' -t 5000
    winapp ui invoke 'AncOffItem' -a $AppPid
    Start-Sleep -Milliseconds 1000
    winapp ui wait-for 'AncOffItem' -a $AppPid -p IsSelected --value 'True' -t 3000
    winapp ui invoke 'AncOnItem' -a $AppPid
    winapp ui wait-for 'DisconnectButton' -a $AppPid -p IsEnabled --value 'True' -t 5000
    winapp ui invoke 'DisconnectButton' -a $AppPid
}

Test-UI 'Experimental Lab requires consent' {
    winapp ui invoke 'NavLab' -a $AppPid
    winapp ui wait-for 'LabFindStop' -a $AppPid -t 3000
    Start-Sleep -Milliseconds 500
    winapp ui send-keys 'escape' -a $AppPid --via send-input
    winapp ui wait-for 'DeviceList' -a $AppPid -t 3000
}

Test-UI 'Settings and diagnostics navigation' {
    winapp ui invoke 'NavSettings' -a $AppPid
    winapp ui wait-for 'ThemeSelector' -a $AppPid -t 3000
    winapp ui invoke 'NavDiagnostics' -a $AppPid
    winapp ui wait-for 'DiagnosticLogList' -a $AppPid -t 3000
}

Test-UI 'Interactive controls expose AutomationId' {
    $tree = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $elements = @($tree.windows | ForEach-Object { $_.elements })
    $missing = @($elements | Where-Object {
        $_.type -match 'Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit' -and
        $_.name -notmatch 'Minimize|Maximize|Close|System|最小化|最大化|关闭' -and
        $_.className -notmatch 'PickerHost|#32770|CabinetWClass' -and
        -not $_.automationId
    })
    if ($missing.Count -gt 0) {
        throw "Controls missing AutomationId: $(($missing.name) -join ', ')"
    }
}

winapp ui screenshot -a $AppPid -o 'screenshots/ui-test-final.png' 2>$null

$results | ConvertTo-Json -Depth 4 | Out-File 'test-results.json' -Encoding utf8
Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}
if ($fail -gt 0) { exit 1 }

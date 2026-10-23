param(
    [string]$OutputDirectory = '.\src\MotoBudsManager\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64',
    [switch]$AutoMonitor
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path 'artifacts' | Out-Null
$manifest = [xml](Get-Content (Join-Path $OutputDirectory 'AppxManifest.xml') -Raw)
if ($manifest.Package.Identity.Name -ne 'MotoBudsManager.CodexUiTest') {
    throw 'Run this test only against the isolated CodexUiTest Debug package, not the installed application.'
}
$startupExtension = @($manifest.Package.Applications.Application.Extensions.Extension) |
    Where-Object Category -eq 'windows.startupTask'
if (-not $startupExtension -or -not (Test-Path -LiteralPath (Join-Path $OutputDirectory $startupExtension.Executable))) {
    throw 'The packaged startup task does not point to an existing executable.'
}

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class StartupWindowProbe {
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    public static IntPtr[] MainWindows(uint processId) {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, param) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (owner == processId && name.ToString() == "WinUIDesktopWin32WindowClass") result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
}
'@

function Launch-Preview([string]$Arguments) {
    $launch = winapp run $OutputDirectory --args $Arguments --detach --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Preview launch failed.' }
    return [int]$launch.ProcessId
}

$launchArguments = if ($AutoMonitor) { '--test-mode --test-auto-monitor --background' } else { '--test-mode --background' }
$appProcessId = Launch-Preview $launchArguments
$deadline = (Get-Date).AddSeconds(10)
do {
    $mainWindows = [StartupWindowProbe]::MainWindows($appProcessId)
    if ($mainWindows.Count -eq 0) { Start-Sleep -Milliseconds 100 }
} until ($mainWindows.Count -gt 0 -or (Get-Date) -gt $deadline)
if ($mainWindows.Count -ne 1) { throw 'A single hidden main window was not created.' }
$mainHwnd = $mainWindows[0]
if ([StartupWindowProbe]::IsWindowVisible($mainHwnd)) { throw 'Background launch showed the main window.' }
Write-Output 'PASS: background launch creates the tray session without showing the main window.'

$null = Launch-Preview '--test-mode --background'
Start-Sleep -Milliseconds 500
if ([StartupWindowProbe]::IsWindowVisible($mainHwnd)) { throw 'A second background launch showed the window.' }
if ([StartupWindowProbe]::MainWindows($appProcessId).Count -ne 1) { throw 'A second background launch duplicated the main window.' }
Write-Output 'PASS: a repeated background launch does not surface or duplicate the window.'

$null = Launch-Preview '--test-mode'
$deadline = (Get-Date).AddSeconds(10)
while (-not [StartupWindowProbe]::IsWindowVisible($mainHwnd) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 100
}
if (-not [StartupWindowProbe]::IsWindowVisible($mainHwnd)) { throw 'Normal activation did not restore the existing window.' }
winapp ui wait-for 'HomeRefreshButton' -w $mainHwnd -t 10000
if ($LASTEXITCODE -ne 0) { throw 'The restored window did not initialize its home page.' }
Write-Output "PASS: normal activation restores the same running instance (PID $appProcessId)."

$passed = 3
if ($AutoMonitor) {
    winapp ui invoke 'NavDevices' -w $mainHwnd
    winapp ui wait-for 'AncSelector' -w $mainHwnd -p IsEnabled --value 'True' -t 10000
    if ($LASTEXITCODE -ne 0) { throw 'Background monitoring did not establish the management session.' }
    winapp ui invoke 'AncOffItem' -w $mainHwnd
    if ($LASTEXITCODE -ne 0) { throw 'Could not change the fake ANC mode.' }
    # Two full polling intervals also verify that an unchanged mode does not repeat the notification.
    Start-Sleep -Seconds 5
    winapp ui invoke 'NavSettings' -w $mainHwnd
    winapp ui invoke 'OpenSettingsDiagnosticsButton' -w $mainHwnd
    winapp ui scroll 'DiagnosticLogList' -w $mainHwnd --to top
    $firstEntries = (winapp ui inspect -w $mainHwnd --interactive --json 2>$null | ConvertFrom-Json).windows.elements
    winapp ui scroll 'DiagnosticLogList' -w $mainHwnd --to bottom
    $lastEntries = (winapp ui inspect -w $mainHwnd --interactive --json 2>$null | ConvertFrom-Json).windows.elements
    $entries = @($firstEntries) + @($lastEntries) | Group-Object name | ForEach-Object { $_.Group[0] }
    $ancChanges = @($entries | Where-Object { $_.name -match 'DEBUG status notification suppressed \[anc\]:' })
    $connections = @($entries | Where-Object { $_.name -match 'DEBUG status notification suppressed \[connected\]:' })
    if ($connections.Count -ne 1 -or $ancChanges.Count -ne 1) {
        throw "Expected one connection and one mode-change notification; found $($connections.Count), $($ancChanges.Count)."
    }
    $reads = @($entries | Where-Object name -like '*DEBUG fake response opcode=0x0200*')
    if ($reads.Count -lt 3) { throw 'Repeated ANC polling was not observed.' }
    Write-Output 'PASS: automatic connection, repeated ANC reads, and deduplicated mode-change notifications.'
    $passed++
    winapp ui invoke 'DiagnosticsBackToSettingsButton' -w $mainHwnd
    winapp ui invoke 'NavDevices' -w $mainHwnd
}

[pscustomobject]@{ AppPid = $appProcessId; Hwnd = $mainHwnd.ToInt64(); Passed = $passed } |
    ConvertTo-Json | Set-Content 'artifacts/startup-smoke-results.json'

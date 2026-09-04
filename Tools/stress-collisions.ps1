[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [ValidateRange(10, 120)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

function Join-ProcessArguments([string[]]$Values) {
    return (($Values | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot "Builds\Windows\DragameDemo.exe"
} elseif (-not [System.IO.Path]::IsPathRooted($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot $ExecutablePath
}
$ExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Windows build not found: $ExecutablePath. Run Tools\build-windows.ps1 first."
}

$logPath = Join-Path $projectRoot "Logs\Collision-Stress.log"
$arguments = @(
    "-batchmode",
    "-nographics",
    "-sweepCollisionStress",
    "-logFile", $logPath
)

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($ExecutablePath)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Arguments = Join-ProcessArguments $arguments

$stressProcess = [System.Diagnostics.Process]::Start($startInfo)
if (-not $stressProcess.WaitForExit($TimeoutSeconds * 1000)) {
    $launchedPath = $stressProcess.MainModule.FileName
    if (-not [string]::Equals($launchedPath, $ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Collision stress test timed out, but PID $($stressProcess.Id) no longer belongs to the launched Player; refusing to terminate it."
    }
    $stressProcess.Kill()
    $stressProcess.WaitForExit()
    throw "Collision stress test timed out; terminated the Player PID started by this script: $($stressProcess.Id). See $logPath"
}

if ($stressProcess.ExitCode -ne 0) {
    throw "Collision stress test failed (exit $($stressProcess.ExitCode)). See $logPath"
}

$successLine = Select-String -LiteralPath $logPath -Pattern "SWEEP_COLLISION_STRESS_OK" | Select-Object -Last 1
if (-not $successLine) {
    throw "Player exited without the collision-stress success marker. See $logPath"
}

Write-Host $successLine.Line

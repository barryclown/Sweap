[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [ValidateRange(10, 180)]
    [int]$TimeoutSeconds = 90
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

$logPath = Join-Path $projectRoot "Logs\Player-Smoke.log"
$arguments = @(
    "-batchmode",
    "-nographics",
    "-sweepDemoSmoke",
    "-logFile", $logPath
)

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($ExecutablePath)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Arguments = Join-ProcessArguments $arguments

$playerProcess = [System.Diagnostics.Process]::Start($startInfo)
if (-not $playerProcess.WaitForExit($TimeoutSeconds * 1000)) {
    $launchedPath = $playerProcess.MainModule.FileName
    if (-not [string]::Equals($launchedPath, $ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Smoke test timed out, but PID $($playerProcess.Id) no longer belongs to the launched Player; refusing to terminate it."
    }
    $playerProcess.Kill()
    $playerProcess.WaitForExit()
    throw "Player smoke test timed out; terminated the Player PID started by this script: $($playerProcess.Id). See $logPath"
}

if ($playerProcess.ExitCode -ne 0) {
    throw "Player smoke test failed (exit $($playerProcess.ExitCode)). See $logPath"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Player did not produce a smoke-test log: $logPath"
}

$successLine = Select-String -LiteralPath $logPath -Pattern "SWEEP_SMOKE_OK" | Select-Object -Last 1
if (-not $successLine) {
    throw "Player exited without completing the exhibition round. See $logPath"
}

Write-Host $successLine.Line

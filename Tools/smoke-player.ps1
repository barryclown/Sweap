[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [ValidateRange(10, 180)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot "Builds\Windows\DragameDemo.exe"
} elseif (-not [System.IO.Path]::IsPathRooted($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot $ExecutablePath
}
$ExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "找不到 Windows Build：$ExecutablePath。請先執行 Tools\build-windows.ps1。"
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
foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }

$playerProcess = [System.Diagnostics.Process]::Start($startInfo)
if (-not $playerProcess.WaitForExit($TimeoutSeconds * 1000)) {
    $launchedPath = $playerProcess.MainModule.FileName
    if (-not [string]::Equals($launchedPath, $ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "測試逾時，但 PID $($playerProcess.Id) 已不是原本啟動的 Player；拒絕終止。"
    }
    $playerProcess.Kill()
    $playerProcess.WaitForExit()
    throw "Player 冒煙測試逾時，已終止本腳本啟動的 PID $($playerProcess.Id)。詳見 $logPath"
}

if ($playerProcess.ExitCode -ne 0) {
    throw "Player 冒煙測試失敗（exit $($playerProcess.ExitCode)）。詳見 $logPath"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Player 沒有產生冒煙測試 log：$logPath"
}

$successLine = Select-String -LiteralPath $logPath -Pattern "SWEEP_SMOKE_OK" | Select-Object -Last 1
if (-not $successLine) {
    throw "Player 已結束但沒有完成展演回合。詳見 $logPath"
}

Write-Host $successLine.Line

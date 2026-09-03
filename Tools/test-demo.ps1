[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt") |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1
$unityVersion = ($versionLine -split ":", 2)[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
$resultsPath = Join-Path $projectRoot "Logs\EditModeTests.xml"
$logPath = Join-Path $projectRoot "Logs\EditModeTests.log"

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "找不到專案指定的 Unity Editor：$unityExe"
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
if ($openEditor) {
    throw "Dragame 正在 Unity PID $($openEditor.ProcessId) 開啟中。請先正常關閉編輯器再測試，腳本不會強制終止它。"
}

$arguments = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $projectRoot,
    "-runTests",
    "-testPlatform", "EditMode",
    "-testResults", $resultsPath,
    "-logFile", $logPath
)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($unityExe)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode

if (-not (Test-Path -LiteralPath $resultsPath)) {
    throw "Unity 未產生測試結果（exit $unityExitCode）。詳見 $logPath"
}

[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$testRun = $results.'test-run'
$testCount = [int]$testRun.testcasecount
$failedCount = [int]$testRun.failed
if ($unityExitCode -ne 0 -or $testCount -lt 1 -or $failedCount -gt 0) {
    throw "EditMode tests failed: exit=$unityExitCode tests=$testCount failed=$failedCount。詳見 $resultsPath"
}

Write-Host "TESTS_OK tests=$testCount passed=$($testRun.passed) duration=$($testRun.duration)s"

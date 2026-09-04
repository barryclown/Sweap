[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Join-ProcessArguments([string[]]$Values) {
    return (($Values | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt") |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1
$unityVersion = ($versionLine -split ":", 2)[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
$resultsPath = Join-Path $projectRoot "Logs\EditModeTests.xml"
$logPath = Join-Path $projectRoot "Logs\EditModeTests.log"

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "Unity Editor required by this project was not found: $unityExe"
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
if ($openEditor) {
    throw "Dragame is already open in Unity PID $($openEditor.ProcessId). Close the editor normally before testing; this script will not terminate it."
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
$startInfo.Arguments = Join-ProcessArguments $arguments
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode

if (-not (Test-Path -LiteralPath $resultsPath)) {
    throw "Unity did not produce test results (exit $unityExitCode). See $logPath"
}

[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$testRun = $results.'test-run'
$testCount = [int]$testRun.testcasecount
$failedCount = [int]$testRun.failed
if ($unityExitCode -ne 0 -or $testCount -lt 1 -or $failedCount -gt 0) {
    throw "EditMode tests failed: exit=$unityExitCode tests=$testCount failed=$failedCount. See $resultsPath"
}

Write-Host "TESTS_OK tests=$testCount passed=$($testRun.passed) duration=$($testRun.duration)s"

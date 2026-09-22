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
$logPath = Join-Path $projectRoot "Logs\Presentation-Optimize.log"

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "Unity Editor required by this project was not found: $unityExe"
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
if ($openEditor) {
    throw "Dragame is already open in Unity PID $($openEditor.ProcessId). Close it normally before applying deterministic asset changes."
}

$arguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $projectRoot,
    "-executeMethod", "SweepPresentationOptimizer.ApplyAll",
    "-logFile", $logPath
)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($unityExe)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Arguments = Join-ProcessArguments $arguments
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()

if ($unityProcess.ExitCode -ne 0) {
    throw "Presentation optimization failed (exit $($unityProcess.ExitCode)). See $logPath"
}

$marker = Select-String -LiteralPath $logPath -Pattern "PRESENTATION_OPTIMIZATION_OK" | Select-Object -Last 1
if (-not $marker) {
    throw "Unity exited without the optimization success marker. See $logPath"
}

Write-Host $marker.Line.Trim()

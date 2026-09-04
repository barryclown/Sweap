[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$Development
)

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

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "Unity Editor required by this project was not found: $unityExe"
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
if ($openEditor) {
    throw "Dragame is already open in Unity PID $($openEditor.ProcessId). Close the editor normally before building; this script will not terminate it."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Builds\Windows\DragameDemo.exe"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$logPath = Join-Path $projectRoot "Logs\Build-Windows.log"
$arguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $projectRoot,
    "-executeMethod", "SweepDemoBuild.BuildWindows",
    "-sweepBuildOutput", $OutputPath,
    "-logFile", $logPath
)
if ($Development) { $arguments += "-sweepDevelopmentBuild" }

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($unityExe)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Arguments = Join-ProcessArguments $arguments
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity Windows build failed (exit $unityExitCode). See $logPath"
}
if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw "Unity returned success but the build output is missing: $OutputPath"
}

$builtFile = Get-Item -LiteralPath $OutputPath
Write-Host "BUILD_OK $($builtFile.FullName) $($builtFile.Length) bytes"

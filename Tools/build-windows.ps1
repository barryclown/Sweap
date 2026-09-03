[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$Development
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt") |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1
$unityVersion = ($versionLine -split ":", 2)[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "找不到專案指定的 Unity Editor：$unityExe"
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
if ($openEditor) {
    throw "Dragame 正在 Unity PID $($openEditor.ProcessId) 開啟中。請先正常關閉編輯器再建置，腳本不會強制終止它。"
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
foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity Windows 建置失敗（exit $unityExitCode）。詳見 $logPath"
}
if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw "Unity 回傳成功但找不到輸出：$OutputPath"
}

$builtFile = Get-Item -LiteralPath $OutputPath
Write-Host "BUILD_OK $($builtFile.FullName) $($builtFile.Length) bytes"

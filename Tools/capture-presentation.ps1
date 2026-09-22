[CmdletBinding()]
param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

function Join-ProcessArguments([string[]]$Values) {
    return (($Values | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$executablePath = Join-Path $projectRoot "Builds\Windows\DragameDemo.exe"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Logs\Sweep-Presentation-MaxGrowth.png"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$logPath = Join-Path $projectRoot "Logs\Presentation-Capture.log"

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Windows build not found: $executablePath"
}

$arguments = @(
    "-batchmode",
    "-sweepPresentationCapture",
    "-sweepCapturePath", $OutputPath,
    "-screen-fullscreen", "0",
    "-screen-width", "1600",
    "-screen-height", "900",
    "-logFile", $logPath
)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($executablePath)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Arguments = Join-ProcessArguments $arguments
$process = [System.Diagnostics.Process]::Start($startInfo)
if (-not $process.WaitForExit(30000)) {
    $process.Kill()
    $process.WaitForExit()
    throw "Presentation capture timed out. See $logPath"
}

if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "Presentation capture failed (exit $($process.ExitCode)). See $logPath"
}
$bottomWallPath = Join-Path ([System.IO.Path]::GetDirectoryName($OutputPath)) `
    (([System.IO.Path]::GetFileNameWithoutExtension($OutputPath)) + "-BottomWall" + ([System.IO.Path]::GetExtension($OutputPath)))
if (-not (Test-Path -LiteralPath $bottomWallPath -PathType Leaf)) {
    throw "Bottom-wall capture is missing: $bottomWallPath"
}
$marker = Select-String -LiteralPath $logPath -Pattern "SWEEP_PRESENTATION_CAPTURE_OK" | Select-Object -Last 1
if (-not $marker) { throw "Capture image exists without a success marker. See $logPath" }

$image = Get-Item -LiteralPath $OutputPath
$bottomImage = Get-Item -LiteralPath $bottomWallPath
Write-Host "CAPTURE_OK climax=$($image.FullName) ($($image.Length) bytes) bottomWall=$($bottomImage.FullName) ($($bottomImage.Length) bytes)"

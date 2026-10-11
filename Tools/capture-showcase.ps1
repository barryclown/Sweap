[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [ValidateRange(12, 60)]
    [int]$Fps = 30,
    [ValidateRange(6, 30)]
    [double]$Seconds = 16,
    [ValidateRange(60, 1200)]
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"

function Join-ProcessArguments([string[]]$Values) {
    return (($Values | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$executablePath = Join-Path $projectRoot "Builds\Windows\DragameDemo.exe"
$guardPath = Join-Path $env:USERPROFILE "Documents\Codex\shared-rules\tools\ai_desktop_guarded.ps1"
$windowsPowerShell = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "Logs\ShowcaseFrames"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$logPath = Join-Path $projectRoot "Logs\Showcase-Capture.log"

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Windows build not found: $executablePath"
}
if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
    throw "AI hidden-desktop guard not found: $guardPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Get-ChildItem -LiteralPath $OutputDirectory -Filter "frame-*.jpg" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force
Remove-Item -LiteralPath (Join-Path $OutputDirectory "capture.txt") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue

$playerArguments = @(
    "-batchmode",
    "-noaudio",
    "-force-d3d11",
    "-sweepShowcaseCapture",
    "-sweepShowcaseFrames", $OutputDirectory,
    "-sweepShowcaseFps", $Fps.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "-sweepShowcaseSeconds", $Seconds.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "-screen-fullscreen", "0",
    "-screen-width", "1280",
    "-screen-height", "720",
    "-logFile", $logPath
)
$joinedPlayerArguments = Join-ProcessArguments $playerArguments

$guardOutput = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $guardPath `
    -Action hidden `
    -FilePath $executablePath `
    -Arguments $joinedPlayerArguments `
    -WorkingDirectory (Split-Path -Parent $executablePath) `
    -NoConsole `
    -WaitSeconds 20

$guardText = ($guardOutput | Out-String).Trim()
$pidMatch = [regex]::Match($guardText, 'launched pid\s+(\d+)')
if (-not $pidMatch.Success) {
    throw "Hidden launcher did not return a process id. Output: $guardText"
}
$playerProcessId = [int]$pidMatch.Groups[1].Value
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

while ((Get-Date) -lt $deadline) {
    $process = Get-Process -Id $playerProcessId -ErrorAction SilentlyContinue
    if (-not $process) { break }
    Start-Sleep -Milliseconds 500
}

$process = Get-Process -Id $playerProcessId -ErrorAction SilentlyContinue
if ($process) {
    Stop-Process -Id $playerProcessId -Force -ErrorAction SilentlyContinue
    throw "Showcase capture timed out after $TimeoutSeconds seconds. See $logPath"
}

$manifestPath = Join-Path $OutputDirectory "capture.txt"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Showcase capture did not produce its manifest. See $logPath"
}

$marker = Select-String -LiteralPath $logPath -Pattern "SWEEP_SHOWCASE_CAPTURE_OK" |
    Select-Object -Last 1
if (-not $marker) {
    throw "Showcase frames exist without a success marker. See $logPath"
}

$expectedFrames = [int][Math]::Ceiling($Fps * $Seconds)
$frames = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter "frame-*.jpg" -File)
if ($frames.Count -ne $expectedFrames) {
    throw "Showcase frame count mismatch: expected=$expectedFrames actual=$($frames.Count). See $logPath"
}

$firstFrame = $frames | Sort-Object Name | Select-Object -First 1
$lastFrame = $frames | Sort-Object Name | Select-Object -Last 1
Write-Host "SHOWCASE_CAPTURE_OK pid=$playerProcessId frames=$($frames.Count) fps=$Fps seconds=$Seconds"
Write-Host "FIRST_FRAME $($firstFrame.FullName) $($firstFrame.Length) bytes"
Write-Host "LAST_FRAME $($lastFrame.FullName) $($lastFrame.Length) bytes"
Write-Host "LOG $logPath"

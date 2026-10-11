[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [ValidateRange(30, 240)]
    [int]$TimeoutSeconds = 150
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
if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
    throw "AI hidden-desktop guard not found: $guardPath"
}

$arguments = @(
    "-batchmode",
    "-noaudio",
    "-force-d3d11",
    "-sweepPresentationCapture",
    "-sweepCapturePath", $OutputPath,
    "-screen-fullscreen", "0",
    "-screen-width", "1600",
    "-screen-height", "900",
    "-logFile", $logPath
)
$joinedArguments = Join-ProcessArguments $arguments
$guardOutput = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $guardPath `
    -Action hidden `
    -FilePath $executablePath `
    -Arguments $joinedArguments `
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
    if (-not (Get-Process -Id $playerProcessId -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}

if (Get-Process -Id $playerProcessId -ErrorAction SilentlyContinue) {
    Stop-Process -Id $playerProcessId -Force -ErrorAction SilentlyContinue
    throw "Presentation capture timed out after $TimeoutSeconds seconds. See $logPath"
}
if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "Presentation capture failed. See $logPath"
}
$directory = [System.IO.Path]::GetDirectoryName($OutputPath)
$stem = [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
$extension = [System.IO.Path]::GetExtension($OutputPath)
# Keep in sync with SweepPresentationCapture.ExpectedSuffixes.
$suffixes = @(
    "",
    "-Base-Center", "-Base-BackWall", "-Base-BottomWall", "-Base-LeftWall", "-Base-BottomRightCorner",
    "-BackWall", "-BottomWall", "-BottomWall-21x9", "-BottomLeftCorner-21x9", "-LeftWall-21x9", "-TopRightCorner",
    "-Base-BehindDesk",
    "-Result-C", "-Result-B", "-Result-A", "-Result-S"
)
$expectedPaths = $suffixes | ForEach-Object { Join-Path $directory ($stem + $_ + $extension) }
foreach ($expectedPath in $expectedPaths) {
    if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
        throw "Presentation capture is missing: $expectedPath"
    }
}
$marker = Select-String -LiteralPath $logPath -Pattern "SWEEP_PRESENTATION_CAPTURE_OK" | Select-Object -Last 1
if (-not $marker) { throw "Capture image exists without a success marker. See $logPath" }

$images = $expectedPaths | ForEach-Object { Get-Item -LiteralPath $_ }
Write-Host "CAPTURE_OK pid=$playerProcessId"
$images | ForEach-Object { Write-Host "IMAGE $($_.FullName) $($_.Length) bytes" }

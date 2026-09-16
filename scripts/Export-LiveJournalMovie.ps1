[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][long]$ExpectedTick,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Call-Export([string[]]$Arguments) {
    $r = & $CliPath automation call @Arguments | ConvertFrom-Json
    if ($r.success -ne 'true') { throw "$($r.resultCode): $($r.detail)" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Output already exists; choose another path.' }
$s = Call-Export @('getState', 'observe.state.summary', 'statusOnly=true')
if ($s.controlMode -ne 'Paused' -or $s.playbackMode -ne 'Idle' -or [long]$s.movieTick -ne $ExpectedTick -or $s.recordingActive -ne 'false') {
    throw 'Expected idle paused boundary with recording stopped.'
}
$null = Call-Export @('startRecording', 'control.recording', '--expected-mode=Paused', "--expected-tick=$ExpectedTick")
$null = Call-Export @('stopRecording', 'control.recording', '--expected-mode=Paused', "--expected-tick=$ExpectedTick")
$movie = Call-Export @('getMovie', 'movie.read')
if ($movie.available -ne 'true') { throw 'Recorded movie is unavailable.' }
$bytes = [Convert]::FromBase64String($movie.movieBase64)
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256 | Select-Object Path, Hash

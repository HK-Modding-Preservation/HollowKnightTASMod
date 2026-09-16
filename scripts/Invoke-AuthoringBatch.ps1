#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][ValidateRange(0, [long]::MaxValue)][long]$ExpectedTick,
    [Parameter(Mandatory)][ValidateRange(1, 10000)][int]$Count,
    [Parameter(Mandatory)][ValidatePattern('^(-|[a-z]+(,[a-z]+)*)$')][string]$Hold,
    [string[]]$ObserveKeys = @('scene.name', 'hero.position.x', 'hero.position.y',
        'hero.animation.clip', 'hero.animation.controlEnabled', 'hero.velocity.x', 'hero.velocity.y',
        'hero.cState.onGround', 'hero.cState.attacking', 'hero.cState.doubleJumping',
        'hero.nailSlash.active', 'bossPractice.bench.atBench'),
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Call-Authoring([string[]]$CommandArgs) {
    $response = & $CliPath automation call @CommandArgs | ConvertFrom-Json
    if ($response.success -ne 'true') { throw "$($response.resultCode): $($response.detail)" }
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($response.dataBase64)) | ConvertFrom-Json
}
$status = Call-Authoring @('getState', 'observe.state.summary', 'statusOnly=true')
if ($status.controlMode -ne 'Paused' -or $status.playbackMode -ne 'Idle' -or
    [long]$status.movieTick -ne $ExpectedTick) { throw 'Expected idle paused tick does not match.' }
$movie = Call-Authoring @('getMovie', 'movie.read', 'includeLifecycle=true')
if ($movie.available -ne 'true') { throw 'Read or record a canonical movie with the current baseline first.' }
if ([string]::IsNullOrWhiteSpace($movie.movieBase64)) {
    throw 'Movie exceeds inline export size; use the SDK chunked export workflow. No input was submitted.'
}
$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($movie.movieBase64))
$parts = $source -split '(?m)^---\r?\n', 2
if ($parts.Count -ne 2) { throw 'Canonical movie separator is missing.' }
$candidate = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(
    $parts[0] + "---`nframes $Count hold=$Hold`n"))
$accepted = Call-Authoring @('queueInputBatch', 'control.input', "candidateMovieBase64=$candidate",
    "expectedSceneEpoch=$($status.sceneEpoch)", '--expected-mode=Paused', "--expected-tick=$ExpectedTick")
$target = $ExpectedTick + $Count
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    $status = Call-Authoring @('getState', 'observe.state.summary', 'statusOnly=true')
    if ($status.controlMode -eq 'Paused' -and $status.playbackMode -eq 'Idle') {
        if ([long]$status.movieTick -ne $target) { throw "Batch stopped at $($status.movieTick), expected $target. Do not resubmit." }
        break
    }
    if ([DateTime]::UtcNow -ge $deadline) { throw "Observation timed out for accepted batch $($accepted.batchMovieId); inspect the same session, do not resubmit." }
    Start-Sleep -Milliseconds 100
} while ($true)
$observation = Call-Authoring @('getCombatState', 'observe.state.deep')
$frame = $observation.json | ConvertFrom-Json -Depth 100
if ($frame.schemaVersion -ne 1 -or [long]$observation.movieTick -ne $target -or
    [long]$frame.movieTick -ne $target -or $frame.stamp.sceneEpoch -lt 0) {
    throw 'Observation is not at the requested completed boundary or has an unsupported schema.'
}
if (@($frame.failures).Count -ne 0) { throw 'Observation contains provider failures.' }
$values = @{}
foreach ($entry in $frame.entries) {
    if ($entry.fresh -isnot [bool] -or !$entry.fresh -or
        $entry.sampledAtMovieTick -ne $target -or $entry.ageMovieTicks -ne 0 -or
        [string]::IsNullOrWhiteSpace([string]$entry.key) -or $values.ContainsKey([string]$entry.key)) {
        throw 'Observation contains stale, duplicate, or invalid fields.'
    }
    $values[[string]$entry.key] = [string]$entry.displayValue
}
foreach ($key in $ObserveKeys) {
    if (!$values.ContainsKey($key)) { throw "Requested observation field is unavailable: $key" }
}
[pscustomobject]@{
    movieTick = [long]$frame.movieTick
    sceneEpoch = [int]$frame.stamp.sceneEpoch
    values = @($frame.entries | Where-Object key -In $ObserveKeys | Select-Object key, displayValue)
} | ConvertTo-Json -Compress -Depth 4

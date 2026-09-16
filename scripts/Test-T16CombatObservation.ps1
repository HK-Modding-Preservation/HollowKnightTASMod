[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'FalseKnightAdaptiveController.ps1')

$frame = [ordered]@{
    schemaVersion = 1; movieTick = 42; stamp = @{ sceneEpoch = 3 }
    entries = @('scene.name', 'hero.position.x', 'hero.position.y') | ForEach-Object {
        @{ key = $_; displayValue = 'fixture'; fresh = $true; sampledAtMovieTick = 42; ageMovieTicks = 0 }
    }
    failures = @()
}
$json = $frame | ConvertTo-Json -Depth 10 -Compress
$observation = Convert-AdaptiveCombatObservation -Json $json -ResponseMovieTick 42
if ($observation.movieTick -ne 42 -or $observation.sceneEpoch -ne 3 -or $observation.values.Count -ne 3) {
    throw 'Fresh observation identity was not preserved.'
}
$rejections = 0
foreach ($case in @('tick', 'stale', 'sample', 'age', 'duplicate', 'failure', 'missing')) {
    $broken = $json | ConvertFrom-Json -Depth 10
    switch ($case) {
        'tick' { $broken.movieTick = 41 }
        'stale' { $broken.entries[0].fresh = $false }
        'sample' { $broken.entries[0].sampledAtMovieTick = 41 }
        'age' { $broken.entries[0].ageMovieTicks = 1 }
        'duplicate' { $broken.entries += $broken.entries[0] }
        'failure' { $broken.failures = @(@{ key = 'hero'; message = 'unavailable' }) }
        'missing' { $broken.entries = @($broken.entries | Where-Object key -ne 'hero.position.x') }
    }
    $rejected = $false
    try { $null = Convert-AdaptiveCombatObservation -Json ($broken | ConvertTo-Json -Depth 10 -Compress) -ResponseMovieTick 42 }
    catch { $rejected = $true; $rejections++ }
    if (-not $rejected) { throw "Invalid combat observation was accepted: $case" }
}
[pscustomobject]@{ result = 'PASS'; freshObservations = 1; rejectedObservations = $rejections; gameLaunched = $false }

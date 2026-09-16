[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][long]$ExpectedTick,
    [ValidateRange(1, 300)][int]$Decisions = 100
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'FalseKnightAdaptiveController.ps1')
function Call-Live([string[]]$Arguments) {
    $r = & $CliPath automation call @Arguments | ConvertFrom-Json
    if ($r.success -ne 'true') { throw "$($r.resultCode): $($r.detail)" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
function Read-Snapshot {
    $r = Call-Live @('getCombatState', 'observe.state.deep')
    $o = Convert-AdaptiveCombatObservation -Json $r.json -ResponseMovieTick ([long]$r.movieTick)
    New-FalseKnightSnapshot -Values $o.values -MovieTick $o.movieTick -SceneEpoch $o.sceneEpoch
}
$status = Call-Live @('getState', 'observe.state.summary')
if ($status.controlMode -ne 'Paused' -or $status.playbackMode -ne 'Idle' -or [long]$status.movieTick -ne $ExpectedTick) {
    throw 'Expected idle paused boundary does not match. No input submitted.'
}
$identity = ($status.stateJson | ConvertFrom-Json).sessionId
$movie = Call-Live @('getMovie', 'movie.read')
if ($movie.available -ne 'true') { throw 'Current canonical movie is unavailable.' }
$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($movie.movieBase64))
$parts = $source -split '(?m)^---\r?\n', 2
if ($parts.Count -ne 2) { throw 'Canonical movie separator is missing.' }
$header = $parts[0]
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = [IO.Directory]::CreateDirectory($OutputDirectory)
$statePath = Join-Path $OutputDirectory 'controller.json'
$tracePath = Join-Path $OutputDirectory 'decisions.jsonl'
$controller = New-FalseKnightControllerState
$fightStart = $ExpectedTick
if (Test-Path -LiteralPath $statePath) {
    $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($saved.sessionId -ne $identity -or $saved.tick -ne $ExpectedTick) {
        throw 'Controller belongs to another session or boundary. Inspect before resuming.'
    }
    $controller = $saved.controller
    $fightStart = [long]$saved.fightStart
}
$before = Read-Snapshot
if ($before.Scene -ne 'GG_False_Knight' -or $before.EncounterLevel -ne 0 -or $before.EncounterDifficulty -ne 'Attuned') {
    throw 'Expected Attuned False Knight. No input submitted.'
}
for ($i = 0; $i -lt $Decisions; $i++) {
    if (Test-FalseKnightTerminalSuccess $before) { Write-Output "VICTORY tick=$($before.MovieTick) fightTicks=$($before.MovieTick-$fightStart)"; break }
    if ($before.HeroHealth -le 0 -or $before.MovieTick-$fightStart -ge 6000) { throw 'Death or fight frame budget reached. Do not retry automatically.' }
    $decision = Select-FalseKnightDecision -Snapshot $before -ControllerState $controller
    $candidate = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($header + "---`nframes $($decision.Ticks) hold=$($decision.Hold)`n"))
    $accepted = Call-Live @('queueInputBatch', 'control.input', "candidateMovieBase64=$candidate", "expectedSceneEpoch=$($before.SceneEpoch)", '--expected-mode=Paused', "--expected-tick=$($before.MovieTick)")
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $s = Call-Live @('getState', 'observe.state.summary', 'statusOnly=true')
        if ($s.controlMode -eq 'Paused' -and $s.playbackMode -eq 'Idle') {
            if ([long]$s.movieTick -ne $before.MovieTick + $decision.Ticks) { throw 'Batch stopped at unexpected boundary. Do not resubmit.' }
            break
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Observe timeout for accepted batch $($accepted.batchMovieId). Inspect same session; do not resubmit." }
        Start-Sleep -Milliseconds 50
    } while ($true)
    $after = Read-Snapshot
    Update-FalseKnightControllerState -ControllerState $controller -Before $before -After $after -Decision $decision
    $row = [ordered]@{ tick=$before.MovieTick; count=$decision.Ticks; hold=$decision.Hold; rule=$decision.RuleId; bossState=$before.BossState; bossHp=$before.BossHp; heroHp=$before.HeroHealth; heroX=$before.HeroX; heroY=$before.HeroY; bossX=$before.BossX; bossY=$before.BossY; bossVelocityX=$before.BossVelocityX; bossFacing=$before.BossFacing; rngSha256=$before.RngSha256; afterTick=$after.MovieTick; afterBossHp=$after.BossHp; afterHeroHp=$after.HeroHealth; afterRngSha256=$after.RngSha256 }
    [IO.File]::AppendAllText($tracePath, ($row | ConvertTo-Json -Compress) + "`n")
    [IO.File]::WriteAllText($statePath, ([ordered]@{ sessionId=$identity; tick=$after.MovieTick; fightStart=$fightStart; controller=$controller } | ConvertTo-Json -Depth 5))
    if ($before.BossState -ne $after.BossState -or $before.BossHp -ne $after.BossHp -or $before.HeroHealth -ne $after.HeroHealth -or $i % 25 -eq 0) {
        Write-Output ($row | ConvertTo-Json -Compress)
    }
    $before = $after
}
Write-Output "PAUSED tick=$($before.MovieTick) boss=$($before.BossState) bossHp=$($before.BossHp) heroHp=$($before.HeroHealth)"

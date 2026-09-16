[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$MoviePath,
    [Parameter(Mandatory)][string]$DecisionTracePath,
    [Parameter(Mandatory)][long]$ExpectedTick,
    [Parameter(Mandatory)][long]$TargetTick
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Call-Probe([string[]]$Arguments) {
    $r = & $CliPath automation call @Arguments | ConvertFrom-Json
    if ($r.success -ne 'true') { throw "$($r.resultCode): $($r.detail)" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
$s = Call-Probe @('getState', 'observe.state.summary', 'statusOnly=true')
if ($s.controlMode -ne 'Paused' -or $s.playbackMode -ne 'Idle' -or [long]$s.movieTick -ne $ExpectedTick) { throw 'Expected paused boundary does not match.' }
$source = Get-Content -LiteralPath $MoviePath -Raw
$parts = $source -split '(?m)^---\r?\n', 2
if ($parts.Count -ne 2) { throw 'Missing canonical movie header.' }
$frames = [Collections.Generic.List[string]]::new()
foreach ($line in ($parts[1] -split '\r?\n')) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^frames (\d+) hold=(-|[a-z]+(,[a-z]+)*)$') { throw 'Probe accepts frame-only movies.' }
    for ($i=0; $i -lt [int]$Matches[1]; $i++) { $frames.Add($Matches[2]) }
}
if ($TargetTick -le $ExpectedTick -or $TargetTick -ge $frames.Count) { throw 'Invalid probe range.' }
$expected = @(Get-Content -LiteralPath $DecisionTracePath | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object tick -eq $TargetTick)
if ($expected.Count -ne 1) { throw 'A unique source observation at target is required.' }
$body = [Text.StringBuilder]::new($parts[0] + "---`n")
for ($i=$ExpectedTick+1; $i -le $TargetTick; $i++) { [void]$body.AppendLine("frames 1 hold=$($frames[[int]$i])") }
$candidate = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($body.ToString()))
$null = Call-Probe @('queueInputBatch','control.input',"candidateMovieBase64=$candidate", "expectedSceneEpoch=$($s.sceneEpoch)",'--expected-mode=Paused',"--expected-tick=$ExpectedTick")
$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    $s = Call-Probe @('getState','observe.state.summary','statusOnly=true')
    if ($s.controlMode -eq 'Paused' -and $s.playbackMode -eq 'Idle') {
        if ([long]$s.movieTick -ne $TargetTick) { throw 'Probe stopped at wrong boundary; do not resubmit.' }
        break
    }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Probe observation timed out; inspect same accepted batch.' }
    Start-Sleep -Milliseconds 100
} while ($true)
$o = Call-Probe @('getCombatState','observe.state.deep')
$v = @{}
foreach ($e in (($o.json | ConvertFrom-Json).entries)) { $v[$e.key]=$e.displayValue }
$actual = [ordered]@{tick=$o.movieTick;heroX=$v['hero.position.x'];heroY=$v['hero.position.y'];heroHp=$v['player.health'];bossX=$v['combat.primaryBoss.position.x'];bossY=$v['combat.primaryBoss.position.y'];bossHp=$v['combat.primaryBoss.hp'];bossState=$v['combat.primaryBoss.mainState'];bossVelocityX=$v['combat.primaryBoss.velocity.x'];rngSha256=$v['rng.state.sha256']}
[ordered]@{expected=$expected[0];actual=$actual} | ConvertTo-Json -Depth 4 -Compress

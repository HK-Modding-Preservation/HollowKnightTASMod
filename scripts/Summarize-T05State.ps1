[CmdletBinding()]
param(
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\state'
}
if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "Evidence root does not exist: $EvidenceRoot"
}

$runs = @(
    Get-ChildItem -LiteralPath $EvidenceRoot -Directory |
        ForEach-Object {
            $resultPath = Join-Path $_.FullName 'result.json'
            if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
                [pscustomobject]@{
                    Directory = $_.FullName
                    Result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                }
            }
        }
)

function Read-DiffKeys {
    param(
        [string]$Directory,
        [string]$Name
    )

    $path = Join-Path $Directory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return @()
    }
    return @(
        (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).entries |
            ForEach-Object key
    )
}

$stable = @($runs | Where-Object { $_.Result.profile -eq 'STABLE' })
$health = @($runs | Where-Object { $_.Result.profile -eq 'HEALTH' })
$scene = @($runs | Where-Object { $_.Result.profile -eq 'SCENE' })

$stablePass = $stable.Count -ge 1 -and @(
    $stable | Where-Object {
        -not $_.Result.runPass `
            -or $_.Result.stableCaptureCount -ne 100 `
            -or $_.Result.distinctStableHashCount -ne 1 `
            -or -not (Test-Path -LiteralPath (Join-Path $_.Directory 'capture-failure.json')) `
            -or @((Read-DiffKeys -Directory $_.Directory -Name 'hero-x.diff.json')).Count -ne 1 `
            -or @((Read-DiffKeys -Directory $_.Directory -Name 'hero-x.diff.json'))[0] -ne 'hero.position.x'
    }
).Count -eq 0

$healthPass = $health.Count -ge 1 -and @(
    $health | Where-Object {
        -not $_.Result.runPass `
            -or @((Read-DiffKeys -Directory $_.Directory -Name 'health.diff.json')) -notcontains 'player.health'
    }
).Count -eq 0

$scenePass = $scene.Count -ge 1 -and @(
    $scene | Where-Object {
        -not $_.Result.runPass `
            -or $_.Result.sceneEpoch -le 0 `
            -or $_.Result.initialScene -eq $_.Result.finalScene `
            -or @((Read-DiffKeys -Directory $_.Directory -Name 'scene.diff.json')) -notcontains 'scene.name'
    }
).Count -eq 0

$gatePass = $stablePass -and $healthPass -and $scenePass
$matrix = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    resultCount = $runs.Count
    stablePass = $stablePass
    healthPass = $healthPass
    scenePass = $scenePass
    gatePass = $gatePass
    runs = @(
        $runs | ForEach-Object {
            [ordered]@{
                profile = $_.Result.profile
                runId = $_.Result.runId
                stopReason = $_.Result.stopReason
                stableCaptureCount = $_.Result.stableCaptureCount
                distinctStableHashCount = $_.Result.distinctStableHashCount
                sceneEpoch = $_.Result.sceneEpoch
                initialScene = $_.Result.initialScene
                finalScene = $_.Result.finalScene
                runPass = $_.Result.runPass
                evidenceDirectory = $_.Directory
            }
        }
    )
}
$matrixPath = Join-Path $EvidenceRoot 'state-matrix.json'
$matrix | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $matrixPath -Encoding utf8NoBOM

$verdictPath = Join-Path $EvidenceRoot 'verdict.md'
@(
    '# T05 Semantic State Verdict'
    ''
    "- Results: $($runs.Count)"
    "- Stable/hash/failure: $stablePass"
    "- Hero health diff: $healthPass"
    "- Scene name/epoch diff: $scenePass"
    "- G2 prerequisite: $gatePass"
    ''
    $(if ($gatePass) {
        '**T05 semantic snapshot verification: PASS.**'
    }
    else {
        '**T05 semantic snapshot verification: FAIL / NO-GO.**'
    })
) | Set-Content -LiteralPath $verdictPath -Encoding utf8NoBOM

[pscustomobject]@{
    ResultCount = $runs.Count
    StablePass = $stablePass
    HealthPass = $healthPass
    ScenePass = $scenePass
    GatePass = $gatePass
    MatrixPath = $matrixPath
    VerdictPath = $verdictPath
} | ConvertTo-Json -Depth 6

if (-not $gatePass) {
    exit 4
}

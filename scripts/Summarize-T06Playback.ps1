[CmdletBinding()]
param(
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\playback'
}
if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "Evidence root does not exist: $EvidenceRoot"
}

$runs = @(
    Get-ChildItem -LiteralPath $EvidenceRoot -Directory |
        ForEach-Object {
            $path = Join-Path $_.FullName 'result.json'
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                [pscustomobject]@{
                    Directory = $_.FullName
                    Result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                }
            }
        }
)

$expectedReasons = @{
    ORIGINAL = 'Completed'
    EDITED = 'Completed'
    PHYSICAL = 'Completed'
    MANUAL = 'Manual'
    EMERGENCY = 'Emergency'
    SCENE = 'SceneChanged'
    FAULT = 'AdapterFault'
}
$expectedCounts = @{
    ORIGINAL = 176
    EDITED = 186
    PHYSICAL = 176
}

$profileSummaries = foreach ($profile in @(
        'ORIGINAL',
        'EDITED',
        'PHYSICAL',
        'MANUAL',
        'EMERGENCY',
        'SCENE',
        'FAULT',
        'SHADOW'
    )) {
    $profileRuns = @($runs | Where-Object { $_.Result.profile -eq $profile })
    $requiredCount = if ($profile -in @('ORIGINAL', 'EDITED')) { 5 } else { 1 }
    $failed = @(
        $profileRuns | Where-Object {
            -not $_.Result.runPass `
                -or $_.Result.shadowHasGap `
                -or (
                    $profile -ne 'SHADOW' `
                        -and (
                            $_.Result.playbackStopReason -ne $expectedReasons[$profile] `
                                -or $_.Result.mismatchCount -ne 0 `
                                -or -not $_.Result.bindingRestoreEquivalent
                        )
                ) `
                -or (
                    $expectedCounts.ContainsKey($profile) `
                        -and $_.Result.observationCount -ne $expectedCounts[$profile]
                ) `
                -or (
                    $profile -eq 'PHYSICAL' `
                        -and -not $_.Result.physicalNoiseDetected
                ) `
                -or (
                    $profile -in @('ORIGINAL', 'EDITED', 'PHYSICAL') `
                        -and -not $_.Result.endpointMilestonePass
                ) `
                -or (
                    $profile -eq 'SHADOW' `
                        -and (
                            $_.Result.shadowLastCommittedMovieTick -lt 30 `
                                -or $_.Result.shadowLastPersistedMovieTick `
                                    -ne $_.Result.shadowLastCommittedMovieTick
                        )
                )
        }
    )
    $endpointHashes = @(
        $profileRuns.Result.endpointSha256 |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
    $endpointMilestonePassCount = @(
        $profileRuns |
            Where-Object { $_.Result.endpointMilestonePass }
    ).Count
    $endpointConsistent = if ($profile -in @('ORIGINAL', 'EDITED')) {
        $endpointMilestonePassCount -eq $profileRuns.Count
    } else { $true }
    $pass = $profileRuns.Count -ge $requiredCount `
        -and $failed.Count -eq 0 `
        -and $endpointConsistent

    [pscustomobject]@{
        Profile = $profile
        RunCount = $profileRuns.Count
        RequiredCount = $requiredCount
        EndpointHashCount = $endpointHashes.Count
        EndpointMilestonePassCount = $endpointMilestonePassCount
        EndpointConsistent = $endpointConsistent
        FailedCount = $failed.Count
        ProfilePass = $pass
    }
}

$originalEndpoints = @(
    $runs |
        Where-Object { $_.Result.profile -eq 'ORIGINAL' } |
        ForEach-Object { [double]$_.Result.endpointX }
)
$editedEndpoints = @(
    $runs |
        Where-Object { $_.Result.profile -eq 'EDITED' } |
        ForEach-Object { [double]$_.Result.endpointX }
)
$editEndpointMeanDelta = if (
    $originalEndpoints.Count -ge 5 -and $editedEndpoints.Count -ge 5
) {
    [Math]::Abs(
        ($originalEndpoints | Measure-Object -Average).Average `
            - ($editedEndpoints | Measure-Object -Average).Average
    )
} else { $null }
$allPass = @($profileSummaries | Where-Object { -not $_.ProfilePass }).Count -eq 0
$matrix = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    resultCount = $runs.Count
    gatePass = $allPass
    editEndpointMeanDelta = $editEndpointMeanDelta
    profileSummaries = $profileSummaries
    runs = @(
        $runs | ForEach-Object {
            [ordered]@{
                profile = $_.Result.profile
                runId = $_.Result.runId
                playbackStopReason = $_.Result.playbackStopReason
                observationCount = $_.Result.observationCount
                mismatchCount = $_.Result.mismatchCount
                physicalNoiseDetected = $_.Result.physicalNoiseDetected
                bindingRestoreEquivalent = $_.Result.bindingRestoreEquivalent
                baselineSha256 = $_.Result.baselineSha256
                endpointSha256 = $_.Result.endpointSha256
                endpointMilestonePass = $_.Result.endpointMilestonePass
                endpointMilestoneError = $_.Result.endpointMilestoneError
                baselineX = $_.Result.baselineX
                baselineY = $_.Result.baselineY
                endpointX = $_.Result.endpointX
                endpointY = $_.Result.endpointY
                movieId = $_.Result.movieId
                shadowLastCommittedMovieTick = $_.Result.shadowLastCommittedMovieTick
                shadowLastPersistedMovieTick = $_.Result.shadowLastPersistedMovieTick
                shadowHasGap = $_.Result.shadowHasGap
                runPass = $_.Result.runPass
                evidenceDirectory = $_.Directory
            }
        }
    )
}
$matrixPath = Join-Path $EvidenceRoot 'playback-matrix.json'
$matrix | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $matrixPath -Encoding utf8NoBOM

$verdictPath = Join-Path $EvidenceRoot 'verdict.md'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# T06 Playback Matrix Verdict')
$lines.Add('')
$lines.Add("- Results: $($runs.Count)")
$lines.Add("- Gate: $allPass")
$lines.Add("- Original/edited endpoint mean X delta (diagnostic): $editEndpointMeanDelta")
$lines.Add('')
$lines.Add('| Profile | Runs/required | Endpoint milestones | Exact hashes (diagnostic) | Failures | PASS |')
$lines.Add('|---|---:|---:|---:|---:|---|')
foreach ($summary in $profileSummaries) {
    $lines.Add(
        "| $($summary.Profile) | $($summary.RunCount)/$($summary.RequiredCount) | $($summary.EndpointMilestonePassCount) | $($summary.EndpointHashCount) | $($summary.FailedCount) | $($summary.ProfilePass) |"
    )
}
$lines.Add('')
$lines.Add($(if ($allPass) {
    '**T06 record/edit/replay and input ownership: PASS.**'
}
else {
    '**T06 record/edit/replay and input ownership: FAIL / NO-GO.**'
}))
$lines | Set-Content -LiteralPath $verdictPath -Encoding utf8NoBOM

[pscustomobject]@{
    ResultCount = $runs.Count
    GatePass = $allPass
    EditEndpointMeanDelta = $editEndpointMeanDelta
    MatrixPath = $matrixPath
    VerdictPath = $verdictPath
    ProfileSummaries = $profileSummaries
} | ConvertTo-Json -Depth 8

if (-not $allPass) {
    exit 4
}

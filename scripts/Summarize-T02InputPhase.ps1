[CmdletBinding()]
param(
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\input-phase'
}

if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "Evidence root does not exist: $EvidenceRoot"
}

function Get-NormalizedEdgeMapping {
    param(
        [Parameter(Mandatory)]
        [string]$EventPath
    )

    $inputEvents = @(
        Get-Content -LiteralPath $EventPath |
            ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object eventType -eq 'input-observed'
    )
    if ($inputEvents.Count -eq 0) {
        return ''
    }

    $firstActualTick = [uint64]$inputEvents[0].fields.actualTick
    return @(
        $inputEvents |
            Where-Object {
                $_.fields.observedPressed -ne 'None' `
                    -or $_.fields.observedReleased -ne 'None'
            } |
            ForEach-Object {
                '{0}:{1}:{2}:{3}' -f `
                    $_.fields.logicalTick, `
                    ([uint64]$_.fields.actualTick - $firstActualTick), `
                    $_.fields.observedPressed, `
                    $_.fields.observedReleased
            }
    ) -join '|'
}

function Get-StringSha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

$resultFiles = @(
    Get-ChildItem -LiteralPath $EvidenceRoot -Directory |
        ForEach-Object {
            $candidateResult = Join-Path $_.FullName 'result.json'
            if (Test-Path -LiteralPath $candidateResult -PathType Leaf) {
                Get-Item -LiteralPath $candidateResult
            }
        }
)
if ($resultFiles.Count -eq 0) {
    throw "No T02 result.json files were found under $EvidenceRoot"
}

$results = @(
    $resultFiles | ForEach-Object {
        $value = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        $eventPath = Join-Path $_.DirectoryName 'phase-events.jsonl'
        $edgeMapping = if ($value.case -in @('normal', 'physical-noise', 'scene-change')) {
            Get-NormalizedEdgeMapping -EventPath $eventPath
        }
        else {
            ''
        }
        [pscustomobject]@{
            Path = $_.FullName
            SessionId = [string]$value.sessionId
            RunId = [string]$value.runId
            Candidate = [string]$value.candidate
            Case = [string]$value.case
            StopReason = [string]$value.stopReason
            InputObservationCount = [int]$value.inputObservationCount
            InputMismatchCount = [int]$value.inputMismatchCount
            HeroObservationCount = [int]$value.heroObservationCount
            HeroMismatchCount = [int]$value.heroMismatchCount
            EdgeTickCount = [int]$value.edgeTickCount
            EdgeTickPassCount = [int]$value.edgeTickPassCount
            PhysicalNoiseDetected = [bool]$value.physicalNoiseDetected
            BindingEquivalent = [bool]$value.bindingEquivalent
            SemanticPass = [bool]$value.semanticPass
            RunPass = [bool]$value.runPass
            EdgeMapping = $edgeMapping
            EdgeMappingSha256 = if ([string]::IsNullOrEmpty($edgeMapping)) {
                ''
            }
            else {
                Get-StringSha256 -Value $edgeMapping
            }
        }
    }
)

$candidateSummaries = foreach ($candidate in @('A', 'B', 'C')) {
    $candidateRuns = @($results | Where-Object Candidate -eq $candidate)
    $caseCounts = [ordered]@{
        normal = @($candidateRuns | Where-Object Case -eq 'normal').Count
        'physical-noise' = @($candidateRuns | Where-Object Case -eq 'physical-noise').Count
        'emergency-stop' = @($candidateRuns | Where-Object Case -eq 'emergency-stop').Count
        'scene-change' = @($candidateRuns | Where-Object Case -eq 'scene-change').Count
        'adapter-exception' = @($candidateRuns | Where-Object Case -eq 'adapter-exception').Count
    }

    $coveragePass = $candidateRuns.Count -ge 10 `
        -and $caseCounts.normal -ge 5 `
        -and $caseCounts.'physical-noise' -ge 2 `
        -and $caseCounts.'emergency-stop' -ge 1 `
        -and $caseCounts.'scene-change' -ge 1 `
        -and $caseCounts.'adapter-exception' -ge 1
    $allRunsPass = $candidateRuns.Count -gt 0 `
        -and @($candidateRuns | Where-Object { -not $_.RunPass }).Count -eq 0
    $edgeRuns = @(
        $candidateRuns |
            Where-Object {
                $_.Case -in @('normal', 'physical-noise', 'scene-change') `
                    -and -not [string]::IsNullOrEmpty($_.EdgeMapping)
            }
    )
    $edgeMappings = @($edgeRuns | Select-Object -ExpandProperty EdgeMapping -Unique)
    $edgeMappingConsistent = $edgeRuns.Count -ge 8 -and $edgeMappings.Count -eq 1

    [pscustomobject]@{
        Candidate = $candidate
        RunCount = $candidateRuns.Count
        CaseCounts = $caseCounts
        CoveragePass = $coveragePass
        AllRunsPass = $allRunsPass
        EdgeMappingRunCount = $edgeRuns.Count
        EdgeMappingConsistent = $edgeMappingConsistent
        EdgeMappingSha256 = if ($edgeMappings.Count -eq 1) {
            Get-StringSha256 -Value $edgeMappings[0]
        }
        else {
            $null
        }
        CandidatePass = $coveragePass -and $allRunsPass -and $edgeMappingConsistent
        FailedRuns = @(
            $candidateRuns |
                Where-Object { -not $_.RunPass } |
                Select-Object SessionId, RunId, Case, StopReason
        )
    }
}

$selected = @(
    $candidateSummaries |
        Where-Object CandidatePass |
        Sort-Object @{ Expression = {
            switch ($_.Candidate) {
                'A' { 0 }
                'B' { 1 }
                default { 2 }
            }
        }}
) | Select-Object -First 1

$matrix = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    resultCount = $results.Count
    selectedCandidate = if ($null -eq $selected) { $null } else { $selected.Candidate }
    candidateSummaries = $candidateSummaries
}

$matrixPath = Join-Path $EvidenceRoot 'phase-matrix.json'
$matrix | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $matrixPath -Encoding utf8NoBOM

$verdictPath = Join-Path $EvidenceRoot 'verdict.md'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# T02 Input Phase Matrix Verdict')
$lines.Add('')
$lines.Add("- Results: $($results.Count)")
$lines.Add("- Selected candidate: $(if ($null -eq $selected) { 'NONE' } else { $selected.Candidate })")
$lines.Add('')
$lines.Add('| Candidate | Runs | Coverage | All runs | Edge map | Gate |')
$lines.Add('|---|---:|---|---|---|---|')
foreach ($summary in $candidateSummaries) {
    $lines.Add(
        "| $($summary.Candidate) | $($summary.RunCount) | $($summary.CoveragePass) | $($summary.AllRunsPass) | $($summary.EdgeMappingConsistent) | $($summary.CandidatePass) |"
    )
}
$lines.Add('')
if ($null -eq $selected) {
    $lines.Add('**T02 G0: FAIL / NO-GO**')
}
else {
    $lines.Add("**T02 G0: PASS — promote candidate $($selected.Candidate).**")
}
$lines | Set-Content -LiteralPath $verdictPath -Encoding utf8NoBOM

[pscustomobject]@{
    ResultCount = $results.Count
    SelectedCandidate = if ($null -eq $selected) { $null } else { $selected.Candidate }
    MatrixPath = $matrixPath
    VerdictPath = $verdictPath
    CandidateSummaries = $candidateSummaries
} | ConvertTo-Json -Depth 8

if ($null -eq $selected) {
    exit 4
}

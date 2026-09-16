[CmdletBinding()]
param(
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\timing'
}
if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "Evidence root does not exist: $EvidenceRoot"
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
    throw "No T03 result.json files were found under $EvidenceRoot"
}

$results = @(
    $resultFiles | ForEach-Object {
        $value = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        $histogram = [ordered]@{}
        foreach ($property in $value.fixedStepsHistogram.PSObject.Properties) {
            $histogram[[string]$property.Name] = [int]$property.Value
        }
        [pscustomobject]@{
            Path = $_.FullName
            SessionId = [string]$value.sessionId
            RunId = [string]$value.runId
            Profile = [string]$value.profile
            StopReason = [string]$value.stopReason
            TargetInputTicks = [int]$value.targetInputTicks
            InputObservationCount = [int]$value.inputObservationCount
            RecordCount = [int]$value.recordCount
            VisualTickCount = [long]$value.visualTickCount
            FixedTickCount = [long]$value.fixedTickCount
            SceneEpoch = [int]$value.sceneEpoch
            DroppedCount = [long]$value.droppedCount
            ValidatorPass = [bool]$value.validatorPass
            ValidatorError = [string]$value.validatorError
            PhaseOrderSignature = [string]$value.phaseOrderSignature
            PhaseOrderSignatureSha256 = [string]$value.phaseOrderSignatureSha256
            FixedStepsHistogram = $histogram
            LifecyclePass = [bool]$value.lifecyclePass
            RestoreEquivalent = [bool]$value.restoreEquivalent
            RunPass = [bool]$value.runPass
        }
    }
)

function Merge-Histogram {
    param([object[]]$Runs)

    $merged = [ordered]@{}
    foreach ($run in $Runs) {
        foreach ($item in $run.FixedStepsHistogram.GetEnumerator()) {
            $key = ([int]$item.Key).ToString(
                [Globalization.CultureInfo]::InvariantCulture
            )
            if (-not $merged.Contains($key)) {
                $merged[$key] = 0
            }
            $merged[$key] += [int]$item.Value
        }
    }
    return $merged
}

$profileSummaries = foreach ($profile in @('P60', 'P30', 'PLOAD', 'PSCENE')) {
    $profileRuns = @($results | Where-Object Profile -eq $profile)
    $signatures = @(
        $profileRuns |
            Select-Object -ExpandProperty PhaseOrderSignatureSha256 -Unique
    )
    $coveragePass = $profileRuns.Count -ge 5 `
        -and @(
            $profileRuns |
                Where-Object {
                    $_.TargetInputTicks -lt 1000 `
                        -or $_.InputObservationCount -lt $_.TargetInputTicks
                }
        ).Count -eq 0
    $allRunsPass = $profileRuns.Count -gt 0 `
        -and @($profileRuns | Where-Object { -not $_.RunPass }).Count -eq 0
    $signatureConsistent = $profileRuns.Count -gt 0 `
        -and $signatures.Count -eq 1 `
        -and -not [string]::IsNullOrWhiteSpace($signatures[0])
    $mergedHistogram = Merge-Histogram -Runs $profileRuns

    [pscustomobject]@{
        Profile = $profile
        RunCount = $profileRuns.Count
        CoveragePass = $coveragePass
        AllRunsPass = $allRunsPass
        SignatureConsistent = $signatureConsistent
        PhaseOrderSignatureSha256 = if ($signatures.Count -eq 1) {
            $signatures[0]
        }
        else {
            $null
        }
        FixedStepsHistogram = $mergedHistogram
        MinimumFixedSteps = if ($mergedHistogram.Count -gt 0) {
            [int](@($mergedHistogram.Keys | Sort-Object { [int]$_ })[0])
        }
        else {
            $null
        }
        MaximumFixedSteps = if ($mergedHistogram.Count -gt 0) {
            [int](@($mergedHistogram.Keys | Sort-Object { [int]$_ })[-1])
        }
        else {
            $null
        }
        ProfilePass = $coveragePass -and $allRunsPass -and $signatureConsistent
        FailedRuns = @(
            $profileRuns |
                Where-Object { -not $_.RunPass } |
                Select-Object SessionId, RunId, StopReason, ValidatorError
        )
    }
}

$p60 = @($profileSummaries | Where-Object Profile -eq 'P60')[0]
$p30 = @($profileSummaries | Where-Object Profile -eq 'P30')[0]
$pload = @($profileSummaries | Where-Object Profile -eq 'PLOAD')[0]
$pscene = @($profileSummaries | Where-Object Profile -eq 'PSCENE')[0]
$hasZeroFixedVisual = @(
    $profileSummaries |
        Where-Object {
            $null -ne $_.MinimumFixedSteps -and $_.MinimumFixedSteps -eq 0
        }
).Count -gt 0
$p30HasCatchUp = $null -ne $p30.MaximumFixedSteps -and $p30.MaximumFixedSteps -ge 2
$ploadHasCatchUp = $null -ne $pload.MaximumFixedSteps -and $pload.MaximumFixedSteps -ge 2
$fixedModelPass = $hasZeroFixedVisual -and $p30HasCatchUp -and $ploadHasCatchUp
$sceneLifecyclePass = $pscene.ProfilePass
$allProfilesPass = @(
    $profileSummaries |
        Where-Object { -not $_.ProfilePass }
).Count -eq 0
$allSignatures = @(
    $profileSummaries |
        Select-Object -ExpandProperty PhaseOrderSignatureSha256 -Unique
)
$crossProfileSignatureConsistent = $allSignatures.Count -eq 1
$gatePass = $results.Count -ge 20 `
    -and $allProfilesPass `
    -and $fixedModelPass `
    -and $sceneLifecyclePass `
    -and @($results | Where-Object DroppedCount -gt 0).Count -eq 0

$matrix = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    resultCount = $results.Count
    gatePass = $gatePass
    movieInputUnit = if ($gatePass) { 'InControlCommittedTick' } else { $null }
    fixedModelPass = $fixedModelPass
    hasZeroFixedVisual = $hasZeroFixedVisual
    p30HasCatchUp = $p30HasCatchUp
    ploadHasCatchUp = $ploadHasCatchUp
    sceneLifecyclePass = $sceneLifecyclePass
    crossProfileSignatureConsistent = $crossProfileSignatureConsistent
    profileSummaries = $profileSummaries
}
$matrixPath = Join-Path $EvidenceRoot 'timing-matrix.json'
$matrix | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $matrixPath -Encoding utf8NoBOM

$terminologyPath = Join-Path $EvidenceRoot 'terminology.md'
$terminology = [System.Collections.Generic.List[string]]::new()
$terminology.Add('# T03 Tick Terminology')
$terminology.Add('')
if ($gatePass) {
    $terminology.Add('- **movie input tick**: one committed InControl update tick; this is the input sample unit.')
    $terminology.Add('- **visual tick**: one Runtime driver `Update` boundary; it is a ledger coordinate, not the movie input unit.')
    $terminology.Add('- **fixed tick**: one Runtime driver `FixedUpdate` boundary; zero or multiple fixed ticks may occur between visual ticks.')
    $terminology.Add('- **scene epoch**: increments exactly at `activeSceneChanged`; scene names may change only on that record.')
    $terminology.Add('- **T-FT**: `Time.time - Time.fixedTime`, retained with exact bits as a diagnostic field, not a standalone synchronization oracle.')
}
else {
    $terminology.Add('G1 failed; no formal movie tick terminology is promoted.')
}
$terminology | Set-Content -LiteralPath $terminologyPath -Encoding utf8NoBOM

$verdictPath = Join-Path $EvidenceRoot 'verdict.md'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# T03 Timing Matrix Verdict')
$lines.Add('')
$lines.Add("- Results: $($results.Count)")
$lines.Add("- G1: $($gatePass)")
$lines.Add("- Fixed model: $fixedModelPass")
$lines.Add("- Scene lifecycle: $sceneLifecyclePass")
$lines.Add('')
$lines.Add('| Profile | Runs | Coverage | All runs | Signature | Fixed min/max | Gate |')
$lines.Add('|---|---:|---|---|---|---|---|')
foreach ($summary in $profileSummaries) {
    $lines.Add(
        "| $($summary.Profile) | $($summary.RunCount) | $($summary.CoveragePass) | $($summary.AllRunsPass) | $($summary.SignatureConsistent) | $($summary.MinimumFixedSteps)/$($summary.MaximumFixedSteps) | $($summary.ProfilePass) |"
    )
}
$lines.Add('')
if ($gatePass) {
    $lines.Add(
        '**T03 G1: PASS — promote InControl committed tick as the movie input unit.**'
    )
}
else {
    $lines.Add('**T03 G1: FAIL / NO-GO**')
}
$lines | Set-Content -LiteralPath $verdictPath -Encoding utf8NoBOM

[pscustomobject]@{
    ResultCount = $results.Count
    GatePass = $gatePass
    MatrixPath = $matrixPath
    TerminologyPath = $terminologyPath
    VerdictPath = $verdictPath
    ProfileSummaries = $profileSummaries
} | ConvertTo-Json -Depth 10

if (-not $gatePass) {
    exit 4
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateCount(2, 20)]
    [string[]]$MatrixRoots,

    [Parameter(Mandatory)]
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T24 reference-matrix join requires PowerShell 7 or newer.'
}

function Get-T24FileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash `
        -LiteralPath $Path `
        -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-T24SamePath {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right,
        [Parameter(Mandatory)][string]$Contract
    )

    if (-not [string]::Equals(
            [IO.Path]::GetFullPath($Left),
            [IO.Path]::GetFullPath($Right),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "T24 source matrices differ in $Contract."
    }
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$outputPath = Join-Path $OutputRoot 'run-matrix.json'
if (Test-Path -LiteralPath $OutputRoot) {
    throw "T24 joined matrix output root already exists: $OutputRoot"
}

$seenMatrixPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$seenEvidenceRoots = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$sources = [Collections.Generic.List[object]]::new()
$joinedAttempts = [Collections.Generic.List[object]]::new()
$common = $null
$joinedAttemptNumber = 0

for ($sourceIndex = 0; $sourceIndex -lt $MatrixRoots.Count; $sourceIndex++) {
    $matrixRoot = [IO.Path]::GetFullPath($MatrixRoots[$sourceIndex])
    $matrixPath = Join-Path $matrixRoot 'run-matrix.json'
    if (-not $seenMatrixPaths.Add($matrixPath)) {
        throw "Duplicate T24 source matrix: $matrixPath"
    }
    if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf)) {
        throw "T24 source matrix is missing: $matrixPath"
    }

    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
    $attempts = @($matrix.attempts)
    if ([int]$matrix.schemaVersion -ne 2 `
            -or [string]$matrix.verdict -ne 'CAPTURED' `
            -or [string]$matrix.mode -ne 'vanilla-reference' `
            -or -not [bool]$matrix.strictFirstAttemptCohort `
            -or -not [bool]$matrix.stoppedAtFirstFailure `
            -or [int]$matrix.requiredSuccessfulRuns -le 0 `
            -or [int]$matrix.successfulRuns `
                -ne [int]$matrix.requiredSuccessfulRuns `
            -or [int]$matrix.attemptedRuns `
                -ne [int]$matrix.requiredSuccessfulRuns `
            -or $attempts.Count -ne [int]$matrix.requiredSuccessfulRuns) {
        throw "T24 source matrix is not a strict first-attempt cohort: $matrixPath"
    }

    $matrixGenerator = [IO.Path]::GetFullPath(
        [string]$matrix.matrixGeneratorScript)
    $captureScript = [IO.Path]::GetFullPath([string]$matrix.captureScript)
    $physicalInputTrace = [IO.Path]::GetFullPath(
        [string]$matrix.physicalInputTracePath)
    $scenarioContractRaw = [string]$matrix.scenarioContractPath
    $scenarioContract = if ([string]::IsNullOrWhiteSpace(
            $scenarioContractRaw)) {
        ''
    }
    else {
        [IO.Path]::GetFullPath($scenarioContractRaw)
    }
    $clockBundleRoot = [IO.Path]::GetFullPath(
        [string]$matrix.clockBundleRoot)
    $fileContracts = [Collections.Generic.List[object]]::new()
    foreach ($fileContract in @(
            [pscustomobject]@{
                path = $matrixGenerator
                sha256 = [string]$matrix.matrixGeneratorScriptSha256
                name = 'matrix generator'
            },
            [pscustomobject]@{
                path = $captureScript
                sha256 = [string]$matrix.captureScriptSha256
                name = 'capture script'
            },
            [pscustomobject]@{
                path = $physicalInputTrace
                sha256 = [string]$matrix.physicalInputTraceSha256
                name = 'physical input trace'
            })) {
        $fileContracts.Add($fileContract)
    }
    if (-not [string]::IsNullOrWhiteSpace($scenarioContract)) {
        $fileContracts.Add([pscustomobject]@{
            path = $scenarioContract
            sha256 = [string]$matrix.scenarioContractSha256
            name = 'scenario contract'
        })
    }
    foreach ($fileContract in $fileContracts) {
        if (-not (Test-Path `
                -LiteralPath $fileContract.path `
                -PathType Leaf) `
                -or (Get-T24FileSha256 -Path $fileContract.path) `
                    -ne $fileContract.sha256) {
            throw (
                'T24 source ' + $fileContract.name `
                + ' is missing or changed: ' + $matrixPath)
        }
    }
    if (-not (Test-Path -LiteralPath $clockBundleRoot -PathType Container)) {
        throw "T24 source clock bundle is missing: $clockBundleRoot"
    }

    if ($null -eq $common) {
        $common = [pscustomobject][ordered]@{
            matrixGenerator = $matrixGenerator
            matrixGeneratorSha256 = `
                [string]$matrix.matrixGeneratorScriptSha256
            captureScript = $captureScript
            captureScriptSha256 = [string]$matrix.captureScriptSha256
            maxTicks = [int]$matrix.maxTicks
            fixtureSlot = [int]$matrix.fixtureSlot
            fixtureReadyTimeoutSeconds = `
                [int]$matrix.fixtureReadyTimeoutSeconds
            clockBundleRoot = $clockBundleRoot
            physicalInputTracePath = $physicalInputTrace
            physicalInputTraceSha256 = `
                [string]$matrix.physicalInputTraceSha256
            scenarioContractPath = $scenarioContract
            scenarioContractSha256 = `
                [string]$matrix.scenarioContractSha256
        }
    }
    else {
        Assert-T24SamePath `
            -Left $matrixGenerator `
            -Right $common.matrixGenerator `
            -Contract 'matrix generator path'
        Assert-T24SamePath `
            -Left $captureScript `
            -Right $common.captureScript `
            -Contract 'capture script path'
        Assert-T24SamePath `
            -Left $physicalInputTrace `
            -Right $common.physicalInputTracePath `
            -Contract 'physical input trace path'
        if ([string]::IsNullOrWhiteSpace($scenarioContract) `
                -ne [string]::IsNullOrWhiteSpace(
                    [string]$common.scenarioContractPath)) {
            throw 'T24 source matrices differ in scenario contract presence.'
        }
        if (-not [string]::IsNullOrWhiteSpace($scenarioContract)) {
            Assert-T24SamePath `
                -Left $scenarioContract `
                -Right $common.scenarioContractPath `
                -Contract 'scenario contract path'
        }
        Assert-T24SamePath `
            -Left $clockBundleRoot `
            -Right $common.clockBundleRoot `
            -Contract 'clock bundle root'
        foreach ($valueContract in @(
                @('matrix generator hash',
                    [string]$matrix.matrixGeneratorScriptSha256,
                    [string]$common.matrixGeneratorSha256),
                @('capture script hash',
                    [string]$matrix.captureScriptSha256,
                    [string]$common.captureScriptSha256),
                @('physical input trace hash',
                    [string]$matrix.physicalInputTraceSha256,
                    [string]$common.physicalInputTraceSha256),
                @('scenario contract hash',
                    [string]$matrix.scenarioContractSha256,
                    [string]$common.scenarioContractSha256),
                @('max ticks',
                    [string]$matrix.maxTicks,
                    [string]$common.maxTicks),
                @('fixture slot',
                    [string]$matrix.fixtureSlot,
                    [string]$common.fixtureSlot),
                @('fixture ready timeout',
                    [string]$matrix.fixtureReadyTimeoutSeconds,
                    [string]$common.fixtureReadyTimeoutSeconds))) {
            if ($valueContract[1] -cne $valueContract[2]) {
                throw "T24 source matrices differ in $($valueContract[0])."
            }
        }
    }

    $sourceMatrixHash = Get-T24FileSha256 -Path $matrixPath
    $sources.Add([ordered]@{
        ordinal = $sourceIndex + 1
        matrixRoot = $matrixRoot
        matrixPath = $matrixPath
        matrixSha256 = $sourceMatrixHash
        requiredSuccessfulRuns = [int]$matrix.requiredSuccessfulRuns
        matrixGeneratorScript = $matrixGenerator
        matrixGeneratorScriptSha256 = `
            [string]$matrix.matrixGeneratorScriptSha256
    })

    $expectedSourceAttempt = 0
    foreach ($attempt in @($attempts | Sort-Object attempt)) {
        $expectedSourceAttempt++
        if (-not [bool]$attempt.success `
                -or [int]$attempt.exitCode -ne 0 `
                -or [int]$attempt.attempt -ne $expectedSourceAttempt) {
            throw "T24 source matrix contains an invalid attempt: $matrixPath"
        }
        $evidenceRoot = [IO.Path]::GetFullPath(
            [string]$attempt.evidenceRoot)
        if (-not $seenEvidenceRoots.Add($evidenceRoot) `
                -or -not (Test-Path `
                    -LiteralPath $evidenceRoot `
                    -PathType Container)) {
            throw "T24 source attempt root is duplicate or missing: $evidenceRoot"
        }
        $traceRoot = Join-Path $evidenceRoot 'traces\vanilla-reference'
        foreach ($artifactContract in @(
                @('baseline.json', [string]$attempt.baselineSha256),
                @('trace.jsonl', [string]$attempt.traceSha256),
                @('input-phase.jsonl', [string]$attempt.phaseSha256),
                @('result.json', [string]$attempt.resultSha256))) {
            $artifactPath = Join-Path $traceRoot $artifactContract[0]
            if (-not (Test-Path `
                    -LiteralPath $artifactPath `
                    -PathType Leaf) `
                    -or (Get-T24FileSha256 -Path $artifactPath) `
                        -ne $artifactContract[1]) {
                throw "T24 source attempt artifact is missing or changed: $artifactPath"
            }
        }

        $joinedAttemptNumber++
        $joinedAttempts.Add([ordered]@{
            attempt = $joinedAttemptNumber
            run = ('source-{0:D2}-{1}' -f `
                ($sourceIndex + 1),
                [string]$attempt.run)
            success = $true
            exitCode = 0
            startedUtc = [string]$attempt.startedUtc
            completedUtc = [string]$attempt.completedUtc
            evidenceRoot = $evidenceRoot
            baselineSha256 = [string]$attempt.baselineSha256
            traceSha256 = [string]$attempt.traceSha256
            phaseSha256 = [string]$attempt.phaseSha256
            resultSha256 = [string]$attempt.resultSha256
            sourceMatrixOrdinal = $sourceIndex + 1
            sourceMatrixSha256 = $sourceMatrixHash
            sourceAttempt = [int]$attempt.attempt
        })
    }
}

$joinedMatrix = [ordered]@{
    schemaVersion = 2
    verdict = 'CAPTURED'
    mode = 'vanilla-reference'
    requiredSuccessfulRuns = $joinedAttempts.Count
    successfulRuns = $joinedAttempts.Count
    attemptedRuns = $joinedAttempts.Count
    strictFirstAttemptCohort = $true
    stoppedAtFirstFailure = $true
    maxTicks = [int]$common.maxTicks
    fixtureSlot = [int]$common.fixtureSlot
    fixtureReadyTimeoutSeconds = [int]$common.fixtureReadyTimeoutSeconds
    matrixGeneratorScript = $PSCommandPath
    matrixGeneratorScriptSha256 = Get-T24FileSha256 -Path $PSCommandPath
    captureScript = [string]$common.captureScript
    captureScriptSha256 = [string]$common.captureScriptSha256
    clockBundleRoot = [string]$common.clockBundleRoot
    physicalInputTracePath = [string]$common.physicalInputTracePath
    physicalInputTraceSha256 = [string]$common.physicalInputTraceSha256
    scenarioContractPath = [string]$common.scenarioContractPath
    scenarioContractSha256 = [string]$common.scenarioContractSha256
    selectionPolicy = 'ordered-union-of-strict-first-attempt-vanilla-matrices'
    sourceMatrixCount = $sources.Count
    sourceMatrices = @($sources)
    updatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    attempts = @($joinedAttempts)
}

New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$joinedMatrix |
    ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
Write-Output "T24 joined reference matrix captured: $outputPath"

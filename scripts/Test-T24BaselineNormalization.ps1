[CmdletBinding()]
param(
    [ValidateSet('tas-continuous', 'tas-sequential', 'tas-batch')]
    [string]$Mode = 'tas-sequential',

    [Parameter(Mandatory)]
    [string]$CandidateTracePath,

    [Parameter(Mandatory)]
    [string]$ReferenceTracePath,

    [Parameter(Mandatory)]
    [string]$NegativeControlEnvelopePath,

    [Parameter(Mandatory)]
    [string]$SupplementalReferenceCatalogPath,

    [Parameter(Mandatory)]
    [string]$EvidenceRoot,

    [ValidateRange(120, 2000)]
    [int]$ExpectedFrameCount = 120,

    [ValidateRange(1, 2)]
    [int]$ExpectedRigidbodyPositionDifferenceCount = 1,

    [string]$CandidateScriptPath = '',

    [string]$ObserverBuildRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T24 baseline-normalization verification requires PowerShell 7.'
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
if ([string]::IsNullOrWhiteSpace($CandidateScriptPath)) {
    $CandidateScriptPath = Join-Path `
        $PSScriptRoot `
        'Invoke-T24CandidateSmoke.ps1'
}
if ([string]::IsNullOrWhiteSpace($ObserverBuildRoot)) {
    $ObserverBuildRoot = Join-Path `
        $repoRoot `
        'src\HollowKnightTAS.ReferenceObserver\bin\Debug'
}

$CandidateScriptPath = [IO.Path]::GetFullPath($CandidateScriptPath)
$CandidateTracePath = [IO.Path]::GetFullPath($CandidateTracePath)
$ReferenceTracePath = [IO.Path]::GetFullPath($ReferenceTracePath)
$NegativeControlEnvelopePath =
    [IO.Path]::GetFullPath($NegativeControlEnvelopePath)
$SupplementalReferenceCatalogPath =
    [IO.Path]::GetFullPath($SupplementalReferenceCatalogPath)
$ObserverBuildRoot = [IO.Path]::GetFullPath($ObserverBuildRoot)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

$candidateDirectory = Split-Path -Parent $CandidateTracePath
$candidateBaselinePath = Join-Path $candidateDirectory 'baseline.json'
$candidatePhasePath = Join-Path $candidateDirectory 'input-phase.jsonl'
$candidateEvidenceRoot = Split-Path `
    -Parent (Split-Path -Parent $candidateDirectory)
$candidateVerdictPath = Join-Path $candidateEvidenceRoot 'final-verdict.json'
$referenceBaselinePath = Join-Path `
    (Split-Path -Parent $ReferenceTracePath) `
    'baseline.json'
$observerFiles = @(
    'HollowKnightTAS.ReferenceObserver.dll',
    'HollowKnightTAS.GameObservation.dll',
    'HollowKnightTAS.Core.dll'
)

foreach ($requiredFile in @(
        $CandidateScriptPath,
        $CandidateTracePath,
        $candidateBaselinePath,
        $candidatePhasePath,
        $candidateVerdictPath,
        $ReferenceTracePath,
        $referenceBaselinePath,
        $NegativeControlEnvelopePath,
        $SupplementalReferenceCatalogPath
    )) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required verification file is missing: $requiredFile"
    }
}
foreach ($requiredDirectory in @($ObserverBuildRoot)) {
    if (-not (Test-Path `
            -LiteralPath $requiredDirectory `
            -PathType Container)) {
        throw "Required verification directory is missing: $requiredDirectory"
    }
}

New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

$tokens = $null
$parseErrors = $null
$candidateAst = [Management.Automation.Language.Parser]::ParseFile(
    $CandidateScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw 'Candidate smoke script has PowerShell parse errors.'
}
$topLevelFunctions = @(
    $candidateAst.EndBlock.Statements |
        Where-Object {
            $_ -is [Management.Automation.Language.FunctionDefinitionAst]
        })
$requiredFunctions = @(
    'Import-T24NegativeControlEnvelope',
    'Import-T24SupplementalReferenceCatalog',
    'Resolve-T24ReferenceForBaseline',
    'Compare-T24Baselines',
    'Compare-T24PhaseTraces',
    'Compare-T24TracesWithEnvelope',
    'Test-T24BaselineRigidbodyComponentCatalog'
)
foreach ($requiredFunction in $requiredFunctions) {
    if (@(
            $topLevelFunctions |
                Where-Object Name -eq $requiredFunction).Count -ne 1) {
        throw "Candidate function is missing or duplicated: $requiredFunction"
    }
}
foreach ($functionAst in $topLevelFunctions) {
    . ([scriptblock]::Create($functionAst.Extent.Text))
}

$candidateVerdict = Get-Content `
    -LiteralPath $candidateVerdictPath `
    -Raw |
    ConvertFrom-Json
$candidateTraceSha256 = (Get-FileHash `
        -LiteralPath $CandidateTracePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
$candidateBaselineSha256 = (Get-FileHash `
        -LiteralPath $candidateBaselinePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
if ([string]$candidateVerdict.mode -ne $Mode `
        -or [string]$candidateVerdict.candidateTraceSha256 `
            -ne $candidateTraceSha256 `
        -or [string]$candidateVerdict.candidateBaselineSha256 `
            -ne $candidateBaselineSha256 `
        -or -not [bool]$candidateVerdict.mutationClean) {
    throw 'Historical candidate provenance or mutation audit is invalid.'
}

$policy = Import-T24NegativeControlEnvelope `
    -Path $NegativeControlEnvelopePath `
    -ReferenceBaseline $referenceBaselinePath `
    -ObserverBuildRoot $ObserverBuildRoot `
    -ObserverAssemblyFiles $observerFiles `
    -ExpectedFrameCount $ExpectedFrameCount
$supplementalReferences = @(
    Import-T24SupplementalReferenceCatalog `
        -Path $SupplementalReferenceCatalogPath `
        -EnvelopePolicy $policy `
        -EnvelopePath $NegativeControlEnvelopePath `
        -ObserverBuildRoot $ObserverBuildRoot `
        -ObserverAssemblyFiles $observerFiles `
        -ExpectedFrameCount $ExpectedFrameCount)
$policy.referenceCatalog = @(
    @($policy.referenceCatalog) + $supplementalReferences)

$matchedReference = Resolve-T24ReferenceForBaseline `
    -EnvelopePolicy $policy `
    -CandidateBaselinePath $candidateBaselinePath
if ($null -eq $matchedReference) {
    throw 'The historical candidate did not resolve to an eligible reference.'
}

$positiveDifferenceRoot = Join-Path `
    $EvidenceRoot `
    'positive-first-differences'
New-Item `
    -ItemType Directory `
    -Path $positiveDifferenceRoot `
    -Force |
    Out-Null
$firstDifferenceDirectory = $positiveDifferenceRoot
$baselineComparison = Compare-T24Baselines `
    -ReferencePath $matchedReference.baselinePath `
    -CandidatePath $candidateBaselinePath `
    -EnvelopePolicy $policy
$phaseComparison = Compare-T24PhaseTraces `
    -ReferencePath $matchedReference.phasePath `
    -CandidatePath $candidatePhasePath
$normalizedComparison = Compare-T24TracesWithEnvelope `
    -ReferencePath $matchedReference.tracePath `
    -CandidatePath $CandidateTracePath `
    -EnvelopePolicy $policy `
    -AllowBaselineNormalizedRigidbody

$disabledDifferenceRoot = Join-Path `
    $EvidenceRoot `
    'normalization-disabled-first-differences'
New-Item `
    -ItemType Directory `
    -Path $disabledDifferenceRoot `
    -Force |
    Out-Null
$firstDifferenceDirectory = $disabledDifferenceRoot
$disabledComparison = Compare-T24TracesWithEnvelope `
    -ReferencePath $matchedReference.tracePath `
    -CandidatePath $CandidateTracePath `
    -EnvelopePolicy $policy

$syntheticBaselinePath = Join-Path `
    $EvidenceRoot `
    'negative-unobserved-rigidbody-baseline.json'
$syntheticBaseline = Get-Content `
    -LiteralPath $candidateBaselinePath `
    -Raw |
    ConvertFrom-Json -AsHashtable
$syntheticBaseline['rigidbodyPositionX']['canonicalHex'] = '00000000'
$syntheticBaseline['rigidbodyPositionX']['displayValue'] = '0'
$syntheticBaseline |
    ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath $syntheticBaselinePath -Encoding utf8NoBOM
$unobservedComponentComparison = `
    Test-T24BaselineRigidbodyComponentCatalog `
        -CandidatePath $syntheticBaselinePath `
        -EnvelopePolicy $policy
$unobservedReference = Resolve-T24ReferenceForBaseline `
    -EnvelopePolicy $policy `
    -CandidateBaselinePath $syntheticBaselinePath

$syntheticTracePath = Join-Path `
    $EvidenceRoot `
    'negative-position-delta-mismatch.jsonl'
$syntheticLines = @([IO.File]::ReadAllLines($CandidateTracePath))
$syntheticFirstFrame = $syntheticLines[0] |
    ConvertFrom-Json -AsHashtable
$deltaField = @(
    $syntheticFirstFrame['fields'] |
        Where-Object {
            [string]$_['key'] -eq 'hero.rigidbody.positionDelta.x'
        })
if ($deltaField.Count -ne 1 `
        -or [string]$deltaField[0]['canonicalHex'] -eq '3f800000') {
    throw 'The synthetic delta-mismatch fixture cannot be constructed.'
}
$deltaField[0]['canonicalHex'] = '3f800000'
$deltaField[0]['displayValue'] = '1'
$canonicalFields = @(
    foreach ($field in $syntheticFirstFrame['fields']) {
        if (-not [bool]$field['comparable']) {
            continue
        }
        [ordered]@{
            key = [string]$field['key']
            kind = [string]$field['kind']
            canonicalHex = [string]$field['canonicalHex']
        }
    })
$canonicalFirstFrame = [ordered]@{
    logicalTick = [long]$syntheticFirstFrame['logicalTick']
    fields = $canonicalFields
} | ConvertTo-Json -Compress -Depth 10
$syntheticFirstFrame['comparisonSha256'] = Get-T24Sha256Text `
    -Text $canonicalFirstFrame
$syntheticLines[0] = $syntheticFirstFrame |
    ConvertTo-Json -Compress -Depth 30
[IO.File]::WriteAllLines($syntheticTracePath, $syntheticLines)

$deltaMismatchDifferenceRoot = Join-Path `
    $EvidenceRoot `
    'delta-mismatch-first-differences'
New-Item `
    -ItemType Directory `
    -Path $deltaMismatchDifferenceRoot `
    -Force |
    Out-Null
$firstDifferenceDirectory = $deltaMismatchDifferenceRoot
$deltaMismatchComparison = Compare-T24TracesWithEnvelope `
    -ReferencePath $matchedReference.tracePath `
    -CandidatePath $syntheticTracePath `
    -EnvelopePolicy $policy `
    -AllowBaselineNormalizedRigidbody

$positivePass = `
    [string]$matchedReference.matchKind `
        -eq 'semantic-exact-rigidbody-components-observed-render-envelope' `
    -and [bool]$matchedReference.rigidbodyComponentsObserved `
    -and [int]$matchedReference.rigidbodyPositionDifferenceCount `
        -eq $ExpectedRigidbodyPositionDifferenceCount `
    -and -not [bool]$baselineComparison.authoritativeExactEquivalent `
    -and [bool]$baselineComparison.semanticExactEquivalent `
    -and [bool]$baselineComparison.rigidbodyComponentsObserved `
    -and [bool]$baselineComparison.renderTransformEnvelopeEquivalent `
    -and [bool]$baselineComparison.equivalent `
    -and [bool]$phaseComparison.equivalent `
    -and [bool]$normalizedComparison.inputEquivalent `
    -and [bool]$normalizedComparison.baselineNormalizedRigidbodyEquivalent `
    -and [long]$normalizedComparison.acceptedBaselineNormalizedRigidbodyDifferenceCount `
        -gt 0 `
    -and [bool]$normalizedComparison.gameplayEquivalent `
    -and $null -eq $normalizedComparison.firstUnacceptedDifferenceKey
$disabledNegativePass = `
    -not [bool]$disabledComparison.baselineNormalizedRigidbodyEquivalent `
    -and -not [bool]$disabledComparison.gameplayEquivalent `
    -and [string]$disabledComparison.firstUnacceptedDifferenceKey `
        -eq 'hero.rigidbody.position.x'
$unobservedNegativePass = `
    -not [bool]$unobservedComponentComparison.equivalent `
    -and -not [bool]$unobservedComponentComparison.xObserved `
    -and $null -eq $unobservedReference
$deltaMismatchNegativePass = `
    -not [bool]$deltaMismatchComparison.baselineNormalizedRigidbodyEquivalent `
    -and -not [bool]$deltaMismatchComparison.gameplayEquivalent `
    -and [string]$deltaMismatchComparison.firstUnacceptedDifferenceKey `
        -eq 'hero.rigidbody.position.x'
$verdict = if ($positivePass `
        -and $disabledNegativePass `
        -and $unobservedNegativePass `
        -and $deltaMismatchNegativePass) {
    'PASS'
}
else {
    'FAIL'
}

$artifact = [ordered]@{
    schemaVersion = 1
    verdict = $verdict
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    policy = [ordered]@{
        semanticStateExact = $true
        rigidbodyComponentsMustBeExactObservedValues = $true
        numericRigidbodyToleranceUsed = $false
        matchingPositionDeltaMustBeBitwiseExactEveryDifferingFrame = $true
    }
    provenance = [ordered]@{
        verifierScript = $MyInvocation.MyCommand.Path
        verifierScriptSha256 = (Get-FileHash `
            -LiteralPath $MyInvocation.MyCommand.Path `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        candidateScript = $CandidateScriptPath
        candidateScriptSha256 = (Get-FileHash `
            -LiteralPath $CandidateScriptPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        candidateTrace = $CandidateTracePath
        candidateTraceSha256 = $candidateTraceSha256
        candidateBaseline = $candidateBaselinePath
        candidateBaselineSha256 = $candidateBaselineSha256
        negativeControlEnvelope = $NegativeControlEnvelopePath
        negativeControlEnvelopeSha256 = $policy.sha256
        supplementalReferenceCatalog = $SupplementalReferenceCatalogPath
        supplementalReferenceCatalogSha256 = (Get-FileHash `
            -LiteralPath $SupplementalReferenceCatalogPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    selectedReference = $matchedReference
    positive = [ordered]@{
        pass = $positivePass
        baseline = $baselineComparison
        phaseEquivalent = [bool]$phaseComparison.equivalent
        trace = $normalizedComparison
    }
    negativeControls = [ordered]@{
        normalizationDisabled = [ordered]@{
            pass = $disabledNegativePass
            trace = $disabledComparison
        }
        unobservedRigidbodyComponent = [ordered]@{
            pass = $unobservedNegativePass
            comparison = $unobservedComponentComparison
            referenceResolved = $null -ne $unobservedReference
        }
        positionDeltaMismatch = [ordered]@{
            pass = $deltaMismatchNegativePass
            trace = $deltaMismatchComparison
        }
    }
}
$artifactPath = Join-Path $EvidenceRoot 'offline-verdict.json'
$artifact |
    ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM

Write-Output "T24 baseline normalization verification: $verdict; $artifactPath"
if ($verdict -ne 'PASS') {
    exit 1
}

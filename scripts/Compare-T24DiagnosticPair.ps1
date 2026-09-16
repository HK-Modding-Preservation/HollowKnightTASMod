[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReferenceTracePath,
    [Parameter(Mandatory)][string]$CandidateTracePath,
    [Parameter(Mandatory)][string]$EvidenceRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }

# Import only the existing pure trace validators/comparer, not the launch harness.
# This keeps diagnostic canonical comparisons identical to the formal raw path.
$sourcePath = Join-Path $PSScriptRoot 'Invoke-T24CandidateSmoke.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $sourcePath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Candidate comparer source cannot be parsed.' }
foreach ($name in @('Get-FieldMap', 'Get-T24Sha256Text',
        'Assert-T24TraceContract', 'Compare-T24Traces')) {
    $definitions = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $name
    })
    if ($definitions.Count -ne 1) { throw "Expected one top-level function: $name" }
    . ([scriptblock]::Create($definitions[0].Extent.Text))
}

$ReferenceTracePath = (Resolve-Path -LiteralPath $ReferenceTracePath).Path
$CandidateTracePath = (Resolve-Path -LiteralPath $CandidateTracePath).Path
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw 'Use a new diagnostic output directory; existing evidence must not be overwritten.'
}
# Validate before creating output, including hashes, ticks and incomplete markers.
Assert-T24TraceContract -Path $ReferenceTracePath
Assert-T24TraceContract -Path $CandidateTracePath
if ((Get-Item -LiteralPath $ReferenceTracePath).Length -eq 0 -or
    (Get-Item -LiteralPath $CandidateTracePath).Length -eq 0) {
    throw 'Empty traces cannot be compared.'
}
$Mode = 'diagnostic-only'
$firstDifferenceDirectory = $EvidenceRoot
$null = New-Item -ItemType Directory -Path $EvidenceRoot
$comparison = Compare-T24Traces -ReferencePath $ReferenceTracePath -CandidatePath $CandidateTracePath
$report = [ordered]@{
    schemaVersion = 1
    classification = 'DIAGNOSTIC_ONLY'
    formalAcceptance = $false
    outcome = if ($comparison.gameplayEquivalent -and $comparison.inputEquivalent) {
        'NO_RAW_DIFFERENCE'
    } else { 'RAW_DIFFERENCE' }
    referenceTrace = $ReferenceTracePath
    referenceSha256 = (Get-FileHash -LiteralPath $ReferenceTracePath -Algorithm SHA256).Hash
    candidateTrace = $CandidateTracePath
    candidateSha256 = (Get-FileHash -LiteralPath $CandidateTracePath -Algorithm SHA256).Hash
    comparerSourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    sameTrace = $ReferenceTracePath -ieq $CandidateTracePath
    comparison = $comparison
    limitations = @(
        'Raw trace diagnosis only; no baseline compatibility or instrument audit acceptance.',
        'No envelope normalization, reference substitution, formal matrix or negative-control acceptance.',
        'No successful reference-smoke summary is generated or inferred.'
    )
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (
    Join-Path $EvidenceRoot 'diagnostic-pair.json') -Encoding utf8NoBOM
$report | ConvertTo-Json -Depth 12

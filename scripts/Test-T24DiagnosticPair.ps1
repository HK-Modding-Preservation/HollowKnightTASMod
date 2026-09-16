[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Join-Path ([IO.Path]::GetTempPath()) ('hktas-diagnostic-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$tool = Join-Path $PSScriptRoot 'Compare-T24DiagnosticPair.ps1'
function Write-Trace([string]$Name, [string]$TimeBits) {
    $fields = @(
        foreach ($key in @('input.axisX','input.axisY','input.held','input.pressed','input.released','time.fixedRaw','time.raw')) {
            [ordered]@{ key=$key; kind='Float32Bits'; canonicalHex=$TimeBits; comparable=$true; displayValue=$TimeBits }
        }
    )
    $canonical = [ordered]@{ logicalTick=0; fields=@($fields | ForEach-Object {
        [ordered]@{ key=$_.key; kind=$_.kind; canonicalHex=$_.canonicalHex }
    }) } | ConvertTo-Json -Compress -Depth 10
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    $path = Join-Path $root $Name
    [ordered]@{ schemaVersion=1; sequence=1; logicalTick=0; comparisonSha256=$hash; fields=$fields } |
        ConvertTo-Json -Compress -Depth 10 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    return $path
}
$left = Write-Trace 'left.jsonl' '00000000'
$right = Write-Trace 'right.jsonl' '3f800000'
$same = & $tool -ReferenceTracePath $left -CandidateTracePath $left -EvidenceRoot (Join-Path $root 'same') | ConvertFrom-Json
if ($same.outcome -ne 'NO_RAW_DIFFERENCE' -or $same.formalAcceptance -ne $false -or -not $same.sameTrace) {
    throw 'Self comparison must be explicitly diagnostic, never formal acceptance.'
}
$different = & $tool -ReferenceTracePath $left -CandidateTracePath $right -EvidenceRoot (Join-Path $root 'different') | ConvertFrom-Json
if ($different.outcome -ne 'RAW_DIFFERENCE' -or $different.comparison.firstDifferenceTick -ne 0 -or
    $different.comparison.inputEquivalent -ne $false -or $different.formalAcceptance -ne $false) {
    throw 'A canonical change must produce a diagnostic first difference.'
}
[IO.File]::WriteAllText($right + '.incomplete', 'fixture')
try {
    $null = & $tool -ReferenceTracePath $left -CandidateTracePath $right -EvidenceRoot (Join-Path $root 'rejected')
    throw 'Incomplete trace was accepted.'
} catch {
    if ($_.Exception.Message -notlike 'Incomplete trace cannot be verified:*') { throw }
}
if (Test-Path -LiteralPath (Join-Path $root 'rejected')) { throw 'Rejected input created output.' }
Write-Output 'PASS: identical/different/incomplete traces; diagnostic results never grant formal acceptance.'

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'T24ClockHarness.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Clock harness parse failed.' }
$function = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Test-T24NativeSceneLifecycleContract'
}, $false)
if ($null -eq $function) { throw 'Native lifecycle contract is missing.' }
. ([scriptblock]::Create($function.Extent.Text))

$baseline = [ordered]@{
    externalRngTransitionStartCount = 0
    externalRngGameplayReadyCount = 0
    externalRngSceneEpoch = 0
    externalRngGameplayReadyAlignmentHoldUpdateCount = 0
    externalSceneClockExclusionPreSynchronizationCount = 0
    externalSceneClockExclusionBeginCount = 0
    externalSceneClockExclusionFinishCount = 0
    externalSceneClockExclusionFrozenTimeUpdateCount = 0
    externalSceneClockExclusionFaultCode = 0
    externalSceneActivationAlignmentBeginCount = 0
    externalSceneActivationAlignmentHoldFrameCount = 0
    externalSceneActivationAlignmentReleaseCount = 0
    externalSceneFinishAlignmentBeginCount = 0
    externalSceneFinishAlignmentHoldFrameCount = 0
    externalSceneFinishAlignmentReleaseCount = 0
    externalSceneFrameAlignmentFaultCode = 0
    externalDeterministicClockSceneLoadFrameSkipCount = 0
    externalSceneActivationAlignmentFirstFrameCount = -1
    externalSceneActivationAlignmentFirstFramePhase = -1
    externalSceneActivationAlignmentLastFrameCount = -1
    externalSceneActivationAlignmentLastFramePhase = -1
    externalSceneFinishAlignmentLastFrameCount = -1
    externalSceneFinishAlignmentLastFramePhase = -1
    externalRngFirstGameplayReadyFrameCount = -1
    externalRngFirstGameplayReadyFramePhase = -1
    externalSceneRngPending = $false
    externalSceneClockExclusionActive = $false
}
if (-not (Test-T24NativeSceneLifecycleContract ([pscustomobject]$baseline))) {
    throw 'Native scene lifecycle should pass.'
}
foreach ($key in @($baseline.Keys)) {
    foreach ($variant in @('changed', 'missing', 'null', 'string')) {
        $test = [ordered]@{}
        foreach ($name in $baseline.Keys) { $test[$name] = $baseline[$name] }
        switch ($variant) {
            'changed' { $test[$key] = if ($baseline[$key] -is [bool]) { $true } else { $baseline[$key] + 1 } }
            'missing' { $test.Remove($key) }
            'null' { $test[$key] = $null }
            'string' { $test[$key] = [string]$baseline[$key] }
        }
        if (Test-T24NativeSceneLifecycleContract ([pscustomobject]$test)) {
            throw "Native lifecycle accepted invalid telemetry: $key/$variant"
        }
    }
}
Write-Output 'PASS: native lifecycle accepted; each mutation, missing field, null and string rejected.'

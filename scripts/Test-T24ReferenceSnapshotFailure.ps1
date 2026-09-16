[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tokens = $null
$errors = $null
$path = Join-Path $PSScriptRoot 'Invoke-T24ReferenceSmoke.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Reference script parse failed.' }
$guards = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.IfStatementAst] -and
    $node.Clauses[0].Item1.Extent.Text -eq '$slotSnapshotComplete'
}, $true))
if ($guards.Count -ne 1) { throw 'Expected exactly one guarded slot cleanup.' }
$guard = $guards[0]
if ($guard.Parent -isnot [Management.Automation.Language.StatementBlockAst] -or
    $guard.Parent.Parent -isnot [Management.Automation.Language.TryStatementAst] -or
    $guard.Parent.Parent.Finally -ne $guard.Parent) {
    throw 'Slot cleanup guard must be inside finally.'
}
$source = $ast.Extent.Text
$complete = $source.IndexOf('$slotSnapshotComplete = $true', [StringComparison]::Ordinal)
$names = $source.IndexOf('$slotNamesBefore = @($slotFilesBefore.Keys)', [StringComparison]::Ordinal)
$move = $source.IndexOf('Move-Item -LiteralPath $modsDirectory -Destination $modsBackup', [StringComparison]::Ordinal)
if ($names -lt 0 -or $complete -le $names -or $move -le $complete) {
    throw 'Complete snapshot must precede all fixture mutations.'
}
if ($guard.Extent.Text -notmatch 'Remove-Item' -or $guard.Extent.Text -notmatch 'WriteAllBytes') {
    throw 'Guard must include both slot removal and restoration.'
}
& {
    $slotSnapshotComplete = $false
    # Model a read failure after one file: the baseline exists but is partial.
    $slotFilesBefore = @{ 'user4.dat' = [byte[]]@(1, 2, 3) }
    $slotNamesBefore = @()
    function Get-ChildItem { throw 'Incomplete snapshot attempted slot enumeration.' }
    function Remove-Item { throw 'Incomplete snapshot attempted deletion.' }
    & ([scriptblock]::Create($guard.Extent.Text))
}
Write-Output 'PASS: incomplete snapshot performs no slot cleanup; complete snapshot precedes fixture mutation.'

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'FalseKnightAdaptiveController.ps1')

$header = @('hktas 1', 'game 1.5.78.11833', 'api 1.5.78.11833-77',
    ('manifest-sha256 ' + ('a' * 64)), ('baseline saved-root ' + ('b' * 64)),
    'tick-unit input', '---') -join "`n"
$cases = @(
    "$header`nframes 2415 hold=-`nframes 1 hold=jump`nframes 90 hold=right`n",
    "$header`nframes 1 hold=attack`nframes 1 hold=-`n",
    "$header`nmarker `"recorded marker`"`nframes 8 hold=-`ncheckpoint saved-point`nframes 2 hold=left`n"
)
foreach ($source in $cases) {
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($source)
    $candidate = Convert-FalseKnightRecordingText -RecordedMovieBase64 ([Convert]::ToBase64String($bytes))
    if ($candidate.Source -cne $source -or $candidate.JournalPrefixTicks -ne 0) {
        throw 'Export altered the recorded root, input prefix, or annotations.'
    }
    $roundTrip = [Text.UTF8Encoding]::new($false, $true).GetBytes($candidate.Source)
    if ([Convert]::ToBase64String($roundTrip) -cne [Convert]::ToBase64String($bytes)) {
        throw 'Export is not byte-preserving.'
    }
}

foreach ($invalid in @('not-base64', '/w==',
        [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$header`n")))) {
    $rejected = $false
    try { $null = Convert-FalseKnightRecordingText -RecordedMovieBase64 $invalid }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Malformed or empty recording was accepted.' }
}
[pscustomobject]@{ result = 'PASS'; preservedRecordings = $cases.Count; rejectedInputs = 3; gameLaunched = $false }

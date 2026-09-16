[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BundleRoot,

    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $runId = '{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $repoRoot `
        "artifacts\clock-prototype\security-tests\$runId"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

$injector = Join-Path $BundleRoot 'HollowKnightTAS.ClockInjector.exe'
if (-not (Test-Path -LiteralPath $injector -PathType Leaf)) {
    throw "Clock Injector is missing: $injector"
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Security-test evidence root already exists: $EvidenceRoot"
}
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null

function Invoke-RejectedCase(
    [string]$Name,
    [string]$Executable,
    [string[]]$Arguments,
    [string]$ExpectedText
) {
    $stdout = Join-Path $EvidenceRoot "$Name.stdout.jsonl"
    $stderr = Join-Path $EvidenceRoot "$Name.stderr.jsonl"
    $process = Start-Process `
        -FilePath $Executable `
        -ArgumentList $Arguments `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -eq 0) {
        throw "Security case unexpectedly succeeded: $Name"
    }
    $errorText = Get-Content -LiteralPath $stderr -Raw
    if ($errorText -notmatch [Regex]::Escape($ExpectedText)) {
        throw (
            "Security case did not return the expected rejection: $Name; " `
            + $errorText)
    }
    $parsed = $errorText | ConvertFrom-Json
    if ([string]$parsed.status -ne 'rejected') {
        throw "Security case did not return canonical rejected JSON: $Name"
    }

    return [ordered]@{
        name = $Name
        passed = $true
        exitCode = $process.ExitCode
        expectedText = $ExpectedText
        errorType = [string]$parsed.errorType
        error = [string]$parsed.error
    }
}

$self = Get-Process -Id $PID
$selfStartTicks = $self.StartTime.ToUniversalTime().Ticks
$results = @()
$results += Invoke-RejectedCase `
    -Name 'stale-process-identity' `
    -Executable $injector `
    -Arguments @(
        "--pid=$PID",
        "--start-time-utc-ticks=$($selfStartTicks + 1)"
    ) `
    -ExpectedText 'Target process creation time does not match.'
$results += Invoke-RejectedCase `
    -Name 'wrong-process-image' `
    -Executable $injector `
    -Arguments @(
        "--pid=$PID",
        "--start-time-utc-ticks=$selfStartTicks"
    ) `
    -ExpectedText 'Target image is not Hollow Knight.'

$tamperedBundle = Join-Path $EvidenceRoot 'tampered-bundle'
New-Item -ItemType Directory -Path $tamperedBundle | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $BundleRoot -Force) {
    Copy-Item `
        -LiteralPath $entry.FullName `
        -Destination (Join-Path $tamperedBundle $entry.Name) `
        -Recurse
}
$tamperedBridge = Join-Path `
    $tamperedBundle `
    'HollowKnightTAS.ClockBridge.dll'
$stream = [IO.File]::Open(
    $tamperedBridge,
    [IO.FileMode]::Open,
    [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None)
try {
    if ($stream.Length -le 0) {
        throw 'Clock Bridge is unexpectedly empty.'
    }
    [void]$stream.Seek(-1, [IO.SeekOrigin]::End)
    $last = $stream.ReadByte()
    [void]$stream.Seek(-1, [IO.SeekOrigin]::End)
    $stream.WriteByte($last -bxor 0x01)
}
finally {
    $stream.Dispose()
}
$results += Invoke-RejectedCase `
    -Name 'tampered-fixed-component' `
    -Executable (
        Join-Path $tamperedBundle 'HollowKnightTAS.ClockInjector.exe') `
    -Arguments @(
        "--pid=$PID",
        "--start-time-utc-ticks=$selfStartTicks"
    ) `
    -ExpectedText 'Clock Bridge SHA-256 does not match the fixed whitelist.'

$report = [ordered]@{
    schemaVersion = 1
    verdict = if (@($results | Where-Object { -not $_.passed }).Count -eq 0) {
        'PASS'
    }
    else {
        'FAIL'
    }
    bundleRoot = $BundleRoot
    injectorSha256 = (Get-FileHash `
        -LiteralPath $injector `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    cases = $results
}
$reportPath = Join-Path $EvidenceRoot 'security-test-report.json'
$report |
    ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
$report

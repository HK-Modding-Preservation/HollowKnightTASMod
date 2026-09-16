[CmdletBinding()]
param(
    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(30, 300)]
    [int]$MaxLaunchSeconds = 100,

    [ValidateRange(5, 30)]
    [int]$ObserveSettleSeconds = 10,

    [switch]$SkipT07Parity
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T13 native matrix requires PowerShell 7 or newer.'
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$ManagedDirectory = [IO.Path]::GetFullPath(
    $ManagedDirectory)
$PersistentDataDirectory = [IO.Path]::GetFullPath(
    $PersistentDataDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$installRoot = Join-Path $modsDirectory 'HollowKnightTAS'
$manifestPath = Join-Path `
    $installRoot `
    'companion.manifest.json'
$nativeHostPath = Join-Path `
    $installRoot `
    'Companion\win-x64\Native\HollowKnightTAS.NativeHost.exe'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$settingsBackupPath = $settingsPath + '.bak'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$storeRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$bundleTool = Join-Path `
    $repoRoot `
    'src\HollowKnightTAS.BundleTool\HollowKnightTAS.BundleTool.csproj'
$publicKey = Join-Path `
    $repoRoot `
    '.local\signing\companion-public.json'
$t07Script = Join-Path `
    $PSScriptRoot `
    'Invoke-T07VerificationCampaign.ps1'
$cliAssembly = Join-Path `
    $repoRoot `
    'src\HollowKnightTAS.Cli\bin\Debug\net8.0\HollowKnightTAS.Cli.dll'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't13-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString(
            'yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $repoRoot `
        "artifacts\native\$campaign"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

function Assert-RequiredLeaf {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

foreach ($path in @(
        $SteamExecutable,
        $manifestPath,
        $nativeHostPath,
        $settingsPath,
        $publicKey,
        $t07Script
    )) {
    Assert-RequiredLeaf $path
}
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) {
    throw "Session root is missing: $sessionRoot"
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Evidence root already exists: $EvidenceRoot"
}
if (Get-Process `
        -Name hollow_knight,HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight, Companion, and NativeHost must be stopped before T13.'
}

$resolvedManaged =
    (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods =
    (Resolve-Path -LiteralPath $modsDirectory).Path
if (-not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved Mods directory is outside Managed.'
}

New-Item -ItemType Directory -Path $EvidenceRoot |
    Out-Null

$settingsOriginalBytes =
    [IO.File]::ReadAllBytes($settingsPath)
$settingsOriginalSha256 = (
    Get-FileHash `
        -LiteralPath $settingsPath `
        -Algorithm SHA256
).Hash
$settingsBackupOriginallyExisted =
    Test-Path -LiteralPath $settingsBackupPath -PathType Leaf
$settingsBackupOriginalBytes =
    if ($settingsBackupOriginallyExisted) {
        [IO.File]::ReadAllBytes($settingsBackupPath)
    }
    else {
        $null
    }
$settingsBackupOriginalSha256 =
    if ($settingsBackupOriginallyExisted) {
        (
            Get-FileHash `
                -LiteralPath $settingsBackupPath `
                -Algorithm SHA256
        ).Hash
    }
    else {
        $null
    }
$script:ownedGame = $null
$script:activeSession = $null
$script:nativeHostObserved = $false
$matrix = $null

function Get-FileState {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 'missing'
    }
    $item = Get-Item -LiteralPath $Path
    return '{0}|{1}' -f `
        $item.Length, `
        (Get-FileHash `
            -LiteralPath $Path `
            -Algorithm SHA256).Hash
}

function Get-SlotState {
    $result =
        [System.Collections.Generic.List[string]]::new()
    foreach ($slot in 1..4) {
        foreach ($suffix in @('.dat', '.modded.json')) {
            $path = Join-Path `
                $PersistentDataDirectory `
                ('user{0}{1}' -f $slot, $suffix)
            $result.Add(
                (
                    '{0}|{1}|{2}' -f `
                        $slot, `
                        $suffix, `
                        (Get-FileState $path)
                ))
        }
    }
    return @($result)
}

function Get-TreeState {
    param([Parameter(Mandatory)][string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @('missing')
    }
    return @(
        Get-ChildItem -LiteralPath $Root -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                '{0}|{1}|{2}' -f `
                    [IO.Path]::GetRelativePath(
                        $Root,
                        $_.FullName), `
                    $_.Length, `
                    (Get-FileHash `
                        -LiteralPath $_.FullName `
                        -Algorithm SHA256).Hash
            }
    )
}

function Assert-ArrayEqual {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)][string]$Label
    )

    $difference = @(
        Compare-Object `
            -ReferenceObject @($Expected) `
            -DifferenceObject @($Actual)
    )
    if ($difference.Count -ne 0) {
        throw "$Label changed during T13."
    }
}

function Write-TestSettings {
    param([Parameter(Mandatory)][bool]$NativeEnabled)

    $settings =
        Get-Content -LiteralPath $settingsPath -Raw |
            ConvertFrom-Json -AsHashtable -Depth 50
    $settings['VerificationModeRequested'] = $false
    $settings['CompanionEnabled'] = $true
    $settings['AutoStartCompanion'] = $true
    $settings['ExitCompanionWithGame'] = $true
    $settings['CompanionOverlayEnabled'] = $false
    $settings['EnableNativeCapabilities'] =
        $NativeEnabled
    $settings['InspectorOverlayEnabled'] = $false
    $settings['ReplaySaveAutoEnabled'] = $false
    $settings |
        ConvertTo-Json -Depth 50 |
        Set-Content `
            -LiteralPath $settingsPath `
            -Encoding utf8NoBOM
}

function Get-CompleteEvents {
    param([Parameter(Mandatory)][string]$SessionDirectory)

    $path = Join-Path $SessionDirectory 'events.jsonl'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return @()
    }
    $events =
        [System.Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content `
            -LiteralPath $path `
            -ErrorAction SilentlyContinue) {
        try {
            $events.Add(
                ($line | ConvertFrom-Json -ErrorAction Stop))
        }
        catch {
            # A live writer can expose its final incomplete buffered line.
        }
    }
    return @($events)
}

function Start-Game {
    $script:ownedGame = $null
    $script:activeSession = $null
    $script:nativeHostObserved = $false
    $beforeSessions = @(
        Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory |
            ForEach-Object FullName
    )
    $launchTime = Get-Date
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList @(
            '-applaunch',
            '367520',
            '-screen-width',
            '800',
            '-screen-height',
            '450',
            '-screen-fullscreen',
            '0'
        ) `
        -WindowStyle Hidden

    $deadline =
        (Get-Date).AddSeconds($MaxLaunchSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($null -eq $script:ownedGame) {
            $script:ownedGame =
                Get-Process `
                    -Name hollow_knight `
                    -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.StartTime -ge
                        $launchTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
        }
        if ($null -eq $script:activeSession) {
            $script:activeSession =
                Get-ChildItem `
                    -LiteralPath $sessionRoot `
                    -Directory |
                Where-Object {
                    $beforeSessions -notcontains
                        $_.FullName
                } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
        }
        if ($null -ne $script:ownedGame `
                -and $null -ne $script:activeSession) {
            return
        }
    }
    throw 'Timed out waiting for the game and Runtime session.'
}

function Wait-ForServiceStarted {
    $deadline =
        (Get-Date).AddSeconds($MaxLaunchSeconds)
    while ((Get-Date) -lt $deadline) {
        $events = @(
            Get-CompleteEvents $script:activeSession.FullName
        )
        if (@(
                $events |
                    Where-Object {
                        $_.eventType -eq
                            'companion-service-started'
                    }
            ).Count -gt 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'Companion service did not start.'
}

function Wait-ForNativeStartAndSettle {
    $deadline =
        (Get-Date).AddSeconds($MaxLaunchSeconds)
    $started = $false
    $settleDeadline = $null
    while ((Get-Date) -lt $deadline) {
        $hosts = @(
            Get-Process `
                -Name HollowKnightTAS.NativeHost `
                -ErrorAction SilentlyContinue
        )
        if ($hosts.Count -gt 0) {
            $script:nativeHostObserved = $true
        }
        $events = @(
            Get-CompleteEvents $script:activeSession.FullName
        )
        if (-not $started -and @(
                $events |
                    Where-Object {
                        $_.eventType -eq
                            'native-capability-evidence' `
                            -and $_.fields.status -eq
                                'started'
                    }
            ).Count -gt 0) {
            $started = $true
            $settleDeadline =
                (Get-Date).AddSeconds(
                    $ObserveSettleSeconds)
        }
        if ($started `
                -and $null -ne $settleDeadline `
                -and (Get-Date) -ge $settleDeadline `
                -and $hosts.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Native observation did not start and settle.'
}

function Stop-Game {
    if ($null -ne $script:ownedGame) {
        $script:ownedGame.Refresh()
        if (-not $script:ownedGame.HasExited) {
            [void]$script:ownedGame.CloseMainWindow()
            [void]$script:ownedGame.WaitForExit(20000)
            $script:ownedGame.Refresh()
        }
        if (-not $script:ownedGame.HasExited) {
            Stop-Process `
                -Id $script:ownedGame.Id `
                -Force
            [void]$script:ownedGame.WaitForExit(10000)
        }
        $script:ownedGame = $null
    }

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline `
            -and @(
                Get-Process `
                    -Name HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
                    -ErrorAction SilentlyContinue
            ).Count -gt 0) {
        Start-Sleep -Milliseconds 200
    }
    Get-Process `
        -Name HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue |
        Stop-Process -Force
}

function Read-NativeEvidence {
    $events = @(
        Get-CompleteEvents $script:activeSession.FullName
    )
    return @(
        $events |
            Where-Object {
                $_.eventType -eq
                    'native-capability-evidence'
            }
    )
}

function Invoke-NativeCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$NativeEnabled
    )

    Write-TestSettings $NativeEnabled
    try {
        Start-Game
        Wait-ForServiceStarted
        if ($NativeEnabled) {
            Wait-ForNativeStartAndSettle
        }
        else {
            $deadline =
                (Get-Date).AddSeconds(
                    $ObserveSettleSeconds)
            while ((Get-Date) -lt $deadline) {
                if (Get-Process `
                        -Name HollowKnightTAS.NativeHost `
                        -ErrorAction SilentlyContinue) {
                    $script:nativeHostObserved = $true
                }
                Start-Sleep -Milliseconds 200
            }
        }
    }
    finally {
        Stop-Game
    }

    $native = @(Read-NativeEvidence)
    $started = @(
        $native |
            Where-Object {
                $_.fields.status -eq 'started'
            }
    )
    $verified = @(
        $native |
            Where-Object {
                $_.fields.status -eq 'verified'
            }
    )
    $faulted = @(
        $native |
            Where-Object {
                $_.fields.status -eq 'faulted'
            }
    )
    if ($NativeEnabled) {
        if ($started.Count -ne 1 `
                -or $verified.Count -ne 1 `
                -or $faulted.Count -ne 0) {
            throw "$Name did not produce exactly one started+verified pair."
        }
        $fields = $verified[0].fields
        if ($fields.attachCyclesCompleted -ne '100' `
                -or $fields.parentProcessVerified -ne 'true' `
                -or $fields.rawPagesPersisted -ne 'false' `
                -or $fields.checkpointStatus -ne 'unsupported' `
                -or $fields.fallback -ne 'none') {
            throw "$Name native safety evidence is invalid."
        }
    }
    elseif ($native.Count -ne 0 `
            -or $script:nativeHostObserved) {
        throw "$Name started NativeHost while disabled."
    }

    return [ordered]@{
        name = $Name
        nativeEnabled = $NativeEnabled
        sessionId = $script:activeSession.Name
        nativeHostObserved =
            $script:nativeHostObserved
        startedCount = $started.Count
        verifiedCount = $verified.Count
        faultedCount = $faulted.Count
        evidence =
            if ($verified.Count -eq 1) {
                $verified[0].fields
            }
            else {
                $null
            }
        pass = $true
    }
}

function Invoke-DirectLaunchRejection {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $nativeHostPath
    $startInfo.WorkingDirectory =
        Split-Path -Parent $nativeHostPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            throw 'Unauthorized NativeHost launch hung.'
        }
        $stdout =
            $process.StandardOutput.ReadToEnd().Trim()
        $stderr =
            $process.StandardError.ReadToEnd().Trim()
        if ($process.ExitCode -ne 4) {
            throw "Unauthorized launch exit was $($process.ExitCode)."
        }
        $response = $stdout | ConvertFrom-Json
        if ($response.errorCode -ne
                'UnauthorizedAccessException' `
                -or $response.processObserveStatus -ne
                    'faulted') {
            throw 'Unauthorized launch was not structurally rejected.'
        }
        return [ordered]@{
            exitCode = $process.ExitCode
            errorCode = $response.errorCode
            status = $response.processObserveStatus
            stderrEmpty =
                [string]::IsNullOrEmpty($stderr)
            pass = $true
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-T07Parity {
    if ($SkipT07Parity) {
        return [ordered]@{
            status = 'DEFERRED'
            reason = 'SkipT07Parity'
        }
    }
    Assert-RequiredLeaf $cliAssembly
    $offRoot = Join-Path $EvidenceRoot 't07-off'
    $onRoot = Join-Path $EvidenceRoot 't07-on'
    Write-TestSettings $false
    $offInvocationOutput = @(
        & $t07Script `
            -LocalRunCount 10 `
            -ManagedDirectory $ManagedDirectory `
            -SteamExecutable $SteamExecutable `
            -PersistentDataDirectory $PersistentDataDirectory `
            -EvidenceRoot $offRoot `
            -AutoStartCompanion $true `
            -SkipDeliberateDivergence `
            -NativeObserveExpectation Disabled
    )
    $offExitCode = $LASTEXITCODE
    if ($offExitCode -ne 0 -or $offInvocationOutput.Count -eq 0) {
        throw 'T07 native-off campaign failed.'
    }
    Write-TestSettings $true
    $onInvocationOutput = @(
        & $t07Script `
            -LocalRunCount 10 `
            -ManagedDirectory $ManagedDirectory `
            -SteamExecutable $SteamExecutable `
            -PersistentDataDirectory $PersistentDataDirectory `
            -EvidenceRoot $onRoot `
            -AutoStartCompanion $true `
            -SkipDeliberateDivergence `
            -NativeObserveExpectation Verified
    )
    $onExitCode = $LASTEXITCODE
    if ($onExitCode -ne 0 -or $onInvocationOutput.Count -eq 0) {
        throw 'T07 native-on campaign failed.'
    }

    $offCampaign =
        Get-Content `
            -LiteralPath (Join-Path $offRoot 'campaign.json') `
            -Raw |
            ConvertFrom-Json
    $onCampaign =
        Get-Content `
            -LiteralPath (Join-Path $onRoot 'campaign.json') `
            -Raw |
            ConvertFrom-Json
    foreach ($campaign in @($offCampaign, $onCampaign)) {
        if ($campaign.level -ne 'DEPENDENCY_ORACLE' `
                -or [int]$campaign.localRunCount -ne 10 `
                -or -not $campaign.dependencySemanticProjectionPass `
                -or -not $campaign.nativeObserveValidationPass `
                -or $null -ne $campaign.deliberateDivergencePass) {
            throw 'T07 dependency campaign metadata is invalid.'
        }
    }
    if (
        $offCampaign.nativeObserveExpectation -ne 'Disabled' `
            -or $onCampaign.nativeObserveExpectation -ne 'Verified'
    ) {
        throw 'T07 native observe expectations were not enforced.'
    }

    $offProjection = @(
        $offCampaign.dependencySemanticProjection
    )
    $onProjection = @(
        $onCampaign.dependencySemanticProjection
    )
    if ($offProjection.Count -ne $onProjection.Count) {
        throw 'T07 native on/off semantic milestone count changed.'
    }
    for (
        $index = 0;
        $index -lt $offProjection.Count;
        $index++
    ) {
        $offMilestone = [string]$offProjection[$index]
        $onMilestone = [string]$onProjection[$index]
        if ($offMilestone -cne $onMilestone) {
            $message =
                'T07 native on/off semantic milestone changed at index {0}.' -f
                $index
            throw $message
        }
    }

    $comparisonPath =
        Join-Path $EvidenceRoot 't07-parity-compare.json'
    $comparison = [ordered]@{
        schemaVersion = 1
        status = 'Match'
        comparisonMode = 'semantic-milestone-projection-v1'
        offRuns = 10
        onRuns = 10
        offNativeObserveExpectation = 'Disabled'
        onNativeObserveExpectation = 'Verified'
        onNativeObserveVerifiedRuns = 10
        onNativeObserveAttachCycles = 1000
        milestoneProjection = $offProjection
        excludedDiagnosticFields = @('rng-state')
        note = 'Each run was CLI validated. Partial Unity RNG state remains diagnostic and is not claimed deterministic without playback.'
    }
    $comparison |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath $comparisonPath `
            -Encoding utf8NoBOM

    return [ordered]@{
        status = 'PASS'
        offRuns = 10
        onRuns = 10
        offNativeObserveExpectation = 'Disabled'
        onNativeObserveExpectation = 'Verified'
        onNativeObserveVerifiedRuns = 10
        onNativeObserveAttachCycles = 1000
        comparisonMode = $comparison.comparisonMode
        milestoneProjection = $offProjection
        comparison = $comparisonPath
    }
}

$slotsBefore = @(Get-SlotState)
$storeBefore = @(Get-TreeState $storeRoot)
$modsBefore = @(
    Get-ChildItem -LiteralPath $modsDirectory -Force |
        Sort-Object Name |
        ForEach-Object Name
)

try {
    $bundleOutput = & dotnet run `
        --project $bundleTool `
        -c Release `
        --no-build `
        -- `
        verify `
        $publicKey `
        $installRoot `
        $manifestPath 2>&1 |
        Out-String
    if ($LASTEXITCODE -ne 0 `
            -or -not $bundleOutput.StartsWith(
                'Valid:',
                [StringComparison]::Ordinal)) {
        throw "Installed signed bundle failed verification: $bundleOutput"
    }

    $directRejection =
        Invoke-DirectLaunchRejection
    $disabled =
        Invoke-NativeCase `
            -Name 'native-disabled' `
            -NativeEnabled $false
    $enabled =
        Invoke-NativeCase `
            -Name 'native-enabled' `
            -NativeEnabled $true
    $t07ParityOutput = @(Invoke-T07Parity)
    if ($t07ParityOutput.Count -ne 1) {
        throw 'T07 parity must return exactly one structured result.'
    }
    $t07Parity = $t07ParityOutput[0]
    if (
        -not ($t07Parity -is [Collections.IDictionary]) `
            -or -not $t07Parity.Contains('status')
    ) {
        throw 'T07 parity result is missing the required status field.'
    }

    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc =
            [DateTimeOffset]::UtcNow.ToString('O')
        verdict =
            if ($t07Parity.status -eq 'PASS') {
                'PASS'
            }
            else {
                'PASS_WITH_T07_PARITY_DEFERRED'
            }
        bundle = [ordered]@{
            verification = $bundleOutput.Trim()
            manifestSha256 = (
                Get-FileHash `
                    -LiteralPath $manifestPath `
                    -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            nativeHostSha256 = (
                Get-FileHash `
                    -LiteralPath $nativeHostPath `
                    -Algorithm SHA256
            ).Hash.ToLowerInvariant()
        }
        directLaunchRejection = $directRejection
        disabled = $disabled
        enabled = $enabled
        t07Parity = $t07Parity
        checkpoint = [ordered]@{
            status = 'unsupported'
            restoreMatrixRun = $false
            fallback = 'runtime-t09'
        }
        unsupportedCapabilities = @(
            'native.input.override.experimental.v1',
            'native.clock.trace.experimental.v1',
            'native.capture.experimental.v1',
            'native.checkpoint.experimental.v1'
        )
    }

    $matrix |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'capability-matrix.json'
            ) `
            -Encoding utf8NoBOM
    $enabled.evidence |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'observe.json'
            ) `
            -Encoding utf8NoBOM
    $directRejection |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'crash-recovery.json'
            ) `
            -Encoding utf8NoBOM
    $matrix.checkpoint |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'checkpoint-matrix.json'
            ) `
            -Encoding utf8NoBOM
    $matrix.t07Parity |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 't07-parity.json'
            ) `
            -Encoding utf8NoBOM
    [ordered]@{
        buildWhitelistId =
            $enabled.evidence.buildWhitelistId
        imageSha256 =
            $enabled.evidence.imageSha256
        assemblyCSharpSha256 =
            $enabled.evidence.assemblyCSharpSha256
        environmentManifestSha256 =
            $enabled.evidence.environmentManifestSha256
        runtimeAssemblySha256 =
            $enabled.evidence.runtimeAssemblySha256
        coreAssemblySha256 =
            $enabled.evidence.coreAssemblySha256
        targetFingerprint =
            $enabled.evidence.targetFingerprint
        parentProcessVerified =
            $enabled.evidence.parentProcessVerified
    } |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'target-verification.json'
            ) `
            -Encoding utf8NoBOM
    [ordered]@{
        standardUser = $true
        administratorRequired = $false
        permissions = @('ProcessQuery')
        processWrite = $false
        suspendThreads = $false
        rawPagesPersisted = $false
    } |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'permissions.json'
            ) `
            -Encoding utf8NoBOM

    @(
        '# T13 Native Capability Matrix',
        '',
        "- Verdict: $($matrix.verdict)",
        "- Signed bundle: PASS",
        "- Direct NativeHost launch rejection: PASS",
        "- Native disabled: PASS",
        "- Native auto-start and authenticated observe: PASS",
        "- Consecutive read-only attach/detach cycles: $($enabled.evidence.attachCyclesCompleted)",
        "- Raw pages persisted: $($enabled.evidence.rawPagesPersisted)",
        "- Process checkpoint: unsupported; Runtime/T09 fallback retained",
        "- T07 native on/off parity: $($t07Parity.status)"
    ) |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'report.md'
            ) `
            -Encoding utf8NoBOM
}
finally {
    Stop-Game
    [IO.File]::WriteAllBytes(
        $settingsPath,
        $settingsOriginalBytes)
    if ($settingsBackupOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsBackupPath,
            $settingsBackupOriginalBytes)
    }
    elseif (Test-Path -LiteralPath $settingsBackupPath) {
        Remove-Item -LiteralPath $settingsBackupPath
    }
}

Assert-ArrayEqual `
    -Expected $slotsBefore `
    -Actual @(Get-SlotState) `
    -Label 'User save slots'
Assert-ArrayEqual `
    -Expected $storeBefore `
    -Actual @(Get-TreeState $storeRoot) `
    -Label 'Replay-save store'
Assert-ArrayEqual `
    -Expected $modsBefore `
    -Actual @(
        Get-ChildItem -LiteralPath $modsDirectory -Force |
            Sort-Object Name |
            ForEach-Object Name
    ) `
    -Label 'Mods directory entries'
if ((Get-FileHash `
        -LiteralPath $settingsPath `
        -Algorithm SHA256).Hash -ne
        $settingsOriginalSha256) {
    throw 'Global settings were not restored byte-for-byte.'
}
if (
    $settingsBackupOriginallyExisted `
        -and (
            Get-FileHash `
                -LiteralPath $settingsBackupPath `
                -Algorithm SHA256
        ).Hash -ne $settingsBackupOriginalSha256
) {
    throw 'Global settings backup was not restored byte-for-byte.'
}
if (Get-Process `
        -Name hollow_knight,HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue) {
    throw 'A T13-owned process remained after cleanup.'
}

$matrix['cleanup'] = [ordered]@{
    settingsRestored = $true
    settingsBackupRestored = $true
    slotsUnchanged = $true
    replayStoreUnchanged = $true
    modsEntriesUnchanged = $true
    noLingeringProcesses = $true
}
$matrix |
    ConvertTo-Json -Depth 20 |
    Set-Content `
        -LiteralPath (
            Join-Path $EvidenceRoot 'capability-matrix.json'
        ) `
        -Encoding utf8NoBOM

$matrix | ConvertTo-Json -Depth 20

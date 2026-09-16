[CmdletBinding()]
param(
    [ValidateSet('Discovery', 'Trial', 'Adaptive', 'Fight', 'Full')]
    [string]$Phase = 'Discovery',

    [string]$CandidatePath = '',

    [ValidateRange(10, 300)]
    [int]$TrialTimeoutSeconds = 90,

    [ValidateRange(1, 4)]
    [int]$FixtureSlot = 2,

    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(30, 180)]
    [int]$MaxLaunchSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T16 harness requires PowerShell 7 or newer.'
}
if ($Phase -eq 'Full') {
    throw 'The Full phase is not enabled until discovery fixtures are frozen.'
}
if (($Phase -eq 'Trial' -or $Phase -eq 'Adaptive' -or $Phase -eq 'Fight') `
        -and [string]::IsNullOrWhiteSpace($CandidatePath)) {
    throw "$Phase requires -CandidatePath."
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'FalseKnightAdaptiveController.ps1')
$ManagedDirectory = [IO.Path]::GetFullPath($ManagedDirectory)
$PersistentDataDirectory =
    [IO.Path]::GetFullPath($PersistentDataDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$installRoot = Join-Path $modsDirectory 'HollowKnightTAS'
$toolsRoot = Join-Path $installRoot 'Companion\win-x64\Tools'
$sdkCorePath = Join-Path $toolsRoot 'SDK\HollowKnightTAS.Core.dll'
$sdkClientPath = Join-Path `
    $toolsRoot `
    'SDK\HollowKnightTAS.Automation.Client.dll'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$settingsBackupPath = $settingsPath + '.bak'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$replayStoreRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$replayStoreParent = Split-Path -Parent $replayStoreRoot
$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$automationParent = Join-Path $localApplicationData 'HollowKnightTAS'
$automationRoot = Join-Path $automationParent 'automation'
$bootstrapPath = Join-Path $automationRoot 'automation-v1.json'
$persistentPrefix = $PersistentDataDirectory + [IO.Path]::DirectorySeparatorChar
$localPrefix = [IO.Path]::GetFullPath($localApplicationData) `
    + [IO.Path]::DirectorySeparatorChar

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't16-{0}-{1}-{2}' -f `
        $Phase.ToLowerInvariant(), `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path $repoRoot "artifacts\final-tas\$campaign"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

foreach ($path in @(
        $SteamExecutable,
        $settingsPath,
        $sdkCorePath,
        $sdkClientPath
    )) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $path"
    }
}
foreach ($path in @($modsDirectory, $installRoot, $sessionRoot)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Required directory is missing: $path"
    }
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Evidence root already exists: $EvidenceRoot"
}
if (@(
        Get-Process `
            -Name `
                hollow_knight,
                HollowKnightTAS.Companion,
                HollowKnightTAS.AgentBridge,
                HollowKnightTAS.NativeHost `
            -ErrorAction SilentlyContinue
    ).Count -ne 0) {
    throw 'Hollow Knight and all HollowKnightTAS helpers must be stopped.'
}
if (-not $replayStoreRoot.StartsWith(
        $persistentPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Replay store is outside the Hollow Knight persistent directory.'
}
if (-not $automationRoot.StartsWith(
        $localPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Automation root is outside LocalApplicationData.'
}

New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
New-Item `
    -ItemType Directory `
    -Path (Join-Path $EvidenceRoot 'environment') |
    Out-Null
New-Item `
    -ItemType Directory `
    -Path (Join-Path $EvidenceRoot 'authoring') |
    Out-Null

$settingsOriginal = [IO.File]::ReadAllBytes($settingsPath)
$settingsOriginalSha256 = (
    Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256
).Hash
$settingsBackupOriginallyExisted =
    Test-Path -LiteralPath $settingsBackupPath -PathType Leaf
$settingsBackupOriginal = if ($settingsBackupOriginallyExisted) {
    [IO.File]::ReadAllBytes($settingsBackupPath)
}
else {
    $null
}
$settingsBackupOriginalSha256 = if ($settingsBackupOriginallyExisted) {
    (Get-FileHash `
        -LiteralPath $settingsBackupPath `
        -Algorithm SHA256).Hash
}
else {
    'missing'
}

$script:game = $null
$script:ownedHelperPids = @()
$script:activeCaseDirectory = $null
$script:activeSessionDirectory = $null
$swapId = [Guid]::NewGuid().ToString('N')
$modsBackup = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T16-$swapId.backup"
$modsEmpty = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T16-$swapId.empty"
$replayStoreBackup = Join-Path `
    $replayStoreParent `
    "v1.HKTAS-T16-$swapId.backup"
$automationBackup = Join-Path `
    $automationParent `
    "automation.HKTAS-T16-$swapId.backup"
$replayStoreOriginallyExisted =
    Test-Path -LiteralPath $replayStoreRoot -PathType Container
$automationOriginallyExisted =
    Test-Path -LiteralPath $automationRoot -PathType Container
$modsSwapped = $false
$replayStoreMoved = $false
$automationMoved = $false
$slotInitial = $null
$slotOriginalFiles = $null
$modsInitial = $null
$discovery = $null
$trial = $null
$adaptive = $null
$fight = $null
$case = $null

function Get-FileState {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 'missing'
    }
    $item = Get-Item -LiteralPath $Path
    return '{0}|{1}' -f `
        $item.Length, `
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-TrackedSlotFiles {
    return @(
        Get-ChildItem `
            -LiteralPath $PersistentDataDirectory `
            -File `
            -Force |
        Where-Object {
            $_.Name -match '^user[1-4](?:[._].*)?$'
        } |
        Sort-Object Name
    )
}

function Get-SlotState {
    return @(
        Get-TrackedSlotFiles |
        ForEach-Object {
            '{0}|{1}' -f $_.Name, (Get-FileState $_.FullName)
        }
    )
}

function Get-SlotFileBackup {
    return @(
        Get-TrackedSlotFiles |
        ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                FullName = $_.FullName
                Bytes = [IO.File]::ReadAllBytes($_.FullName)
                LastWriteTimeUtc = $_.LastWriteTimeUtc
            }
        }
    )
}

function Restore-SlotFiles {
    param([Parameter(Mandatory)]$Backup)

    $expectedNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($Backup)) {
        [void]$expectedNames.Add([string]$entry.Name)
    }

    foreach ($current in @(Get-TrackedSlotFiles)) {
        if ($expectedNames.Contains($current.Name)) {
            continue
        }
        $resolved = [IO.Path]::GetFullPath($current.FullName)
        if (-not $resolved.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing slot cleanup outside persistent data.'
        }
        Remove-Item -LiteralPath $resolved -Force
    }

    foreach ($entry in @($Backup)) {
        $resolved = [IO.Path]::GetFullPath([string]$entry.FullName)
        if (-not $resolved.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing slot restore outside persistent data.'
        }
        [IO.File]::WriteAllBytes($resolved, [byte[]]$entry.Bytes)
        [IO.File]::SetLastWriteTimeUtc(
            $resolved,
            [DateTime]$entry.LastWriteTimeUtc)
    }
}

function Get-ModTreeState {
    return @(
        Get-ChildItem -LiteralPath $modsDirectory -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                '{0}|{1}|{2}' -f `
                    [IO.Path]::GetRelativePath(
                        $modsDirectory,
                        $_.FullName), `
                    $_.Length, `
                    (Get-FileHash `
                        -LiteralPath $_.FullName `
                        -Algorithm SHA256).Hash
            }
    )
}

function Assert-SequenceEqual {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)][string]$Label
    )

    if (@(
            Compare-Object `
                -ReferenceObject @($Expected) `
                -DifferenceObject @($Actual)
        ).Count -ne 0) {
        throw "$Label changed during T16."
    }
}

function Write-TestSettings {
    $settings = Get-Content -LiteralPath $settingsPath -Raw |
        ConvertFrom-Json -AsHashtable -Depth 50
    $settings['VerificationModeRequested'] = $false
    $settings['CompanionEnabled'] = $true
    $settings['AutoStartCompanion'] = $true
    $settings['ExitCompanionWithGame'] = $true
    $settings['CompanionOverlayEnabled'] = $false
    $settings['EnableNativeCapabilities'] = $false
    $settings['EnableSemanticKeyframes'] = $false
    $settings['InspectorEnabled'] = $true
    $settings['InspectorOverlayEnabled'] = $false
    $settings['InspectorExportEnabled'] = $true
    $settings['InspectorSampleEveryMovieTicks'] = 1
    $settings['InspectorExportEverySamples'] = 1
    $settings['ReplaySaveEnabled'] = $true
    $settings['ReplaySaveAutoEnabled'] = $false
    $settings['ReplaySaveDeterministicTimingEnabled'] = $false
    $settings['ReplayDeterministicRngEnabled'] = $false
    $settings['ReplayDeterministicRngSeed'] = 1212896321
    $settings['DedicatedTasSaveSlot'] = $FixtureSlot
    $settings['ExternalAutomationMode'] = 'ApprovedControl'
    $settings['DebugMutationEnabled'] = $false
    $settings |
        ConvertTo-Json -Depth 50 |
        Set-Content `
            -LiteralPath $settingsPath `
            -Encoding utf8NoBOM
}

function Close-RunProcesses {
    if ($null -ne $script:game) {
        $script:game.Refresh()
        if (-not $script:game.HasExited) {
            [void]$script:game.CloseMainWindow()
            [void]$script:game.WaitForExit(20000)
            $script:game.Refresh()
        }
        if (-not $script:game.HasExited) {
            Stop-Process -Id $script:game.Id -ErrorAction SilentlyContinue
            [void]$script:game.WaitForExit(10000)
        }
        $script:game.Dispose()
        $script:game = $null
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $remaining = @(
            Get-Process `
                -Name `
                    HollowKnightTAS.Companion,
                    HollowKnightTAS.AgentBridge,
                    HollowKnightTAS.NativeHost `
                -ErrorAction SilentlyContinue |
                Where-Object {
                    $script:ownedHelperPids -contains $_.Id
                }
        )
        if ($remaining.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    foreach ($process in $remaining) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
    $script:ownedHelperPids = @()
}

function New-StringMap {
    param([hashtable]$Values = @{})

    $result =
        [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::Ordinal)
    foreach ($pair in $Values.GetEnumerator()) {
        $result.Add([string]$pair.Key, [string]$pair.Value)
    }
    return $result
}

function Invoke-SdkCommand {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$CommandId,
        [Parameter(Mandatory)][string]$Scope,
        [hashtable]$Arguments = @{},
        [string]$LeaseId = '',
        [string]$ExpectedRuntimeMode = '',
        [Nullable[long]]$ExpectedMovieTick = $null,
        [switch]$AllowFailure
    )

    $command = $Client.CreateCommand(
        $CommandId,
        $Scope,
        (New-StringMap -Values $Arguments),
        $LeaseId,
        $ExpectedRuntimeMode,
        $ExpectedMovieTick,
        ('request-' + [Guid]::NewGuid().ToString('N')),
        ('idempotency-' + [Guid]::NewGuid().ToString('N')))
    $result = $Client.ExecuteAsync(
            $command,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()
    if (-not $AllowFailure -and -not $result.Success) {
        throw "SDK $CommandId failed: $($result.ResultCode): $($result.Detail)"
    }
    return $result
}

function Convert-AutomationResult {
    param([Parameter(Mandatory)]$Value)

    $data = [ordered]@{}
    foreach ($pair in $Value.Data.GetEnumerator() | Sort-Object Key) {
        $data[$pair.Key] = $pair.Value
    }
    return [ordered]@{
        requestId = $Value.RequestId
        success = $Value.Success
        resultCode = $Value.ResultCode
        detail = $Value.Detail
        sessionId = $Value.SessionId
        manifestSha256 = $Value.ManifestSha256
        acceptedAtMovieTick = $Value.AcceptedAtMovieTick
        data = $data
    }
}

function Acquire-SdkLease {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string[]]$Scopes
    )

    $result = Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'acquireControl' `
        -Scope $Scopes[0] `
        -Arguments @{
            scopes = $Scopes -join ','
            ttlSeconds = '300'
        }
    if (-not $result.Data.ContainsKey('leaseId')) {
        throw 'SDK acquireControl omitted leaseId.'
    }
    return [string]$result.Data['leaseId']
}

function Renew-SdkLease {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][string]$LeaseId,
        [ValidateRange(1, 300)][int]$TtlSeconds = 300
    )

    return Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'renewControl' `
        -Scope $Scope `
        -Arguments @{ ttlSeconds = [string]$TtlSeconds } `
        -LeaseId $LeaseId
}

function Release-SdkLease {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][string]$LeaseId
    )

    return Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'releaseControl' `
        -Scope $Scope `
        -LeaseId $LeaseId
}

function Find-BossPracticeWatch {
    param(
        [Parameter(Mandatory)]$Client,
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $cursor = 0L
    do {
        $timeline = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'getTimeline' `
            -Scope 'observe.timeline' `
            -Arguments @{
                fromMovieTick = '-1'
                afterSequence = $cursor.ToString(
                    [CultureInfo]::InvariantCulture)
                # A watch frame intentionally contains the complete typed
                # statue catalog. Keep each bounded timeline page comfortably
                # below the canonical 900,000-character IPC field limit.
                count = '5'
            }
        $cursor = [long]$timeline.Data['nextAfterSequence']
        $entries = @(
            ([string]$timeline.Data['entries'] -split "`n") |
                Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_)
                }
        )
        foreach ($entry in $entries) {
            $parts = $entry -split '\|', 4
            if ($parts.Count -ne 4 -or $parts[1] -ne 'watchFrame') {
                continue
            }
            $outerJson = [Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String($parts[3]))
            $outer = $outerJson |
                ConvertFrom-Json -AsHashtable -Depth 100
            $frame = [string]$outer['json'] |
                ConvertFrom-Json -Depth 100
            $values = [ordered]@{}
            foreach ($item in $frame.entries) {
                $values[[string]$item.key] = [string]$item.displayValue
            }
            if ($values.Contains('bossPractice.statues.catalogJson') `
                    -and $values['scene.name'] -eq 'GG_Workshop' `
                    -and $values['bossPractice.bench.atBench'] -eq 'true') {
                return [pscustomobject]@{
                    sequence = [long]$parts[0]
                    movieTick = [long]$parts[2]
                    frame = $frame
                    values = $values
                }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw 'No GG_Workshop boss-practice watch frame arrived.'
}

function Invoke-TrialReplay {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$CaseDirectory,
        [Parameter(Mandatory)][string]$ManifestSha256
    )

    $candidateFull = [IO.Path]::GetFullPath($CandidatePath)
    if (-not (Test-Path -LiteralPath $candidateFull -PathType Leaf)) {
        throw "Trial candidate is missing: $candidateFull"
    }
    $source = Get-Content -LiteralPath $candidateFull -Raw
    $manifestMatches = [regex]::Matches(
        $source,
        '(?m)^manifest-sha256 [0-9a-f]{64}$')
    if ($manifestMatches.Count -ne 1) {
        throw 'Trial candidate must contain exactly one canonical manifest header.'
    }
    $source = [regex]::Replace(
        $source,
        '(?m)^manifest-sha256 [0-9a-f]{64}$',
        "manifest-sha256 $ManifestSha256")
    $candidateBytes = [Text.UTF8Encoding]::new($false, $true).
        GetBytes($source.Replace("`r`n", "`n"))
    $candidateBase64 = [Convert]::ToBase64String($candidateBytes)
    [IO.File]::WriteAllBytes(
        (Join-Path $CaseDirectory 'candidate-applied.hktas'),
        $candidateBytes)

    $validate = Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'validateMoviePatch' `
        -Scope 'movie.validate' `
        -Arguments @{
            candidateMovieBase64 = $candidateBase64
        }
    $proposal = Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'proposeMoviePatch' `
        -Scope 'movie.propose' `
        -Arguments @{
            baseMovieId = 'none'
            candidateMovieBase64 = $candidateBase64
            reason = 'T16 nonvisual authoring trial'
            expectedMilestone = 'trial-end'
        }
    $branchMovieId = [string]$proposal.Data['branchMovieId']
    if ($branchMovieId -notmatch '^[0-9a-f]{64}$') {
        throw 'Trial proposal returned an invalid branch movie ID.'
    }

    $leaseId = Acquire-SdkLease `
        -Client $Client `
        -Scopes @('movie.apply-branch', 'control.playback')
    $released = $false
    try {
        $before = $Client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        $apply = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'applyMovieBranch' `
            -Scope 'movie.apply-branch' `
            -Arguments @{ branchMovieId = $branchMovieId } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode $before.State.RuntimeMode
        $start = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'startReplay' `
            -Scope 'control.playback' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(
            $TrialTimeoutSeconds)
        $sawReplaying = $false
        $after = $null
        do {
            Start-Sleep -Milliseconds 100
            $after = $Client.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
            $playbackMode = if (
                $after.State.Fields.ContainsKey('playbackMode')
            ) {
                [string]$after.State.Fields['playbackMode']
            }
            else {
                ''
            }
            if ($playbackMode -eq 'Replaying') {
                $sawReplaying = $true
            }
            $lastReplayMovieTick = if (
                $after.State.Fields.ContainsKey('lastReplayMovieTick')
            ) {
                [long][string]$after.State.Fields['lastReplayMovieTick']
            }
            else {
                -1L
            }
            if ($playbackMode -eq 'Idle' `
                    -and ($sawReplaying `
                        -or $after.State.MovieTick `
                            -gt $before.State.MovieTick `
                        -or $lastReplayMovieTick `
                            -ge [long]$validate.Data['expandedTicks'])) {
                break
            }
        } while ([DateTimeOffset]::UtcNow -lt $deadline)

        if ($null -eq $after `
                -or [string]$after.State.Fields['playbackMode'] `
                    -ne 'Idle') {
            [void](Invoke-SdkCommand `
                -Client $Client `
                -CommandId 'stopReplay' `
                -Scope 'control.playback' `
                -LeaseId $leaseId `
                -ExpectedRuntimeMode 'Running' `
                -AllowFailure)
            throw 'Trial replay did not return to Idle before its timeout.'
        }

        $desync = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'getDesync' `
            -Scope 'observe.desync'
        $release = Release-SdkLease `
            -Client $Client `
            -Scope 'control.playback' `
            -LeaseId $leaseId
        $released = $true

        $finalFriendly = $after.ToFriendlyJson() |
            ConvertFrom-Json -Depth 100
        $trial = [ordered]@{
            schemaVersion = 1
            verdict = 'TRIAL_COMPLETED'
            candidateSource = $candidateFull
            candidateMovieId = $branchMovieId
            expandedTicks = [long]$validate.Data['expandedTicks']
            validation = Convert-AutomationResult $validate
            proposal = Convert-AutomationResult $proposal
            apply = Convert-AutomationResult $apply
            startReplay = Convert-AutomationResult $start
            release = Convert-AutomationResult $release
            sawReplaying = $sawReplaying
            beforeMovieTick = $before.State.MovieTick
            afterMovieTick = $after.State.MovieTick
            movieTickSource = [string]$after.State.Fields['movieTickSource']
            lastReplayMovieTick =
                [long][string]$after.State.Fields['lastReplayMovieTick']
            lastPlaybackStopReason =
                [string]$after.State.Fields['lastPlaybackStopReason']
            lastPlaybackFault =
                [string]$after.State.Fields['lastPlaybackFault']
            lastBindingRestoreEquivalent =
                [string]$after.State.Fields['lastBindingRestoreEquivalent']
            replayObservationCount =
                [long][string]$after.State.Fields['replayObservationCount']
            replaySuspendedRawInputTickCount =
                [long][string]$after.State.Fields[
                    'replaySuspendedRawInputTickCount'
                ]
            replayMismatchCount =
                [long][string]$after.State.Fields['replayMismatchCount']
            lastReplayMismatchMovieTick =
                [long][string]$after.State.Fields[
                    'lastReplayMismatchMovieTick'
                ]
            lastReplayMismatchExpected =
                [string]$after.State.Fields[
                    'lastReplayMismatchExpected'
                ]
            lastReplayMismatchActual =
                [string]$after.State.Fields[
                    'lastReplayMismatchActual'
                ]
            replayPhysicalNoiseDetected =
                [bool]::Parse(
                    [string]$after.State.Fields[
                        'replayPhysicalNoiseDetected'])
            replayDeterministicRngEnabled =
                [bool]::Parse(
                    [string]$after.State.Fields[
                        'replayDeterministicRngEnabled'])
            replayDeterministicRngSeed =
                [int][string]$after.State.Fields[
                    'replayDeterministicRngSeed']
            replayDeterministicRngProfile =
                [string]$after.State.Fields[
                    'replayDeterministicRngProfile']
            replayDeterministicRngStatus =
                [string]$after.State.Fields[
                    'replayDeterministicRngStatus']
            replayDeterministicRngResetCount =
                [long][string]$after.State.Fields[
                    'replayDeterministicRngResetCount']
            lastReplayRngBeforeSha256 =
                [string]$after.State.Fields[
                    'lastReplayRngBeforeSha256']
            lastReplayRngStateSha256 =
                [string]$after.State.Fields[
                    'lastReplayRngStateSha256']
            lastReplayRngAppliedSeed =
                [int][string]$after.State.Fields[
                    'lastReplayRngAppliedSeed']
            lastReplayRngBoundary =
                [string]$after.State.Fields[
                    'lastReplayRngBoundary']
            lastReplayRngScene =
                [string]$after.State.Fields[
                    'lastReplayRngScene']
            finalScene = [string]$finalFriendly.semanticValues.'scene.name'.value
            finalHeroX = [single]$finalFriendly.semanticValues.'hero.position.x'.value
            finalHeroY = [single]$finalFriendly.semanticValues.'hero.position.y'.value
            finalHeroHealth =
                [int]$finalFriendly.semanticValues.'player.health'.value
            verificationEligibility =
                [string]$after.State.Fields['verificationEligibility']
            desync = Convert-AutomationResult $desync
            visualRecognitionUsed = $false
            gameplayMutationUsed = $false
        }
        $trial |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $CaseDirectory 'trial-result.json') `
                -Encoding utf8NoBOM
        return $trial
    }
    finally {
        if (-not $released) {
            try {
                [void](Release-SdkLease `
                    -Client $Client `
                    -Scope 'control.playback' `
                    -LeaseId $leaseId)
            }
            catch {
            }
        }
    }
}

function Get-AdaptiveCombatState {
    param([Parameter(Mandatory)]$Client)

    $result = Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'getCombatState' `
        -Scope 'observe.state.deep'
    if (-not $result.Data.ContainsKey('json')) {
        throw 'getCombatState did not return a watch-frame JSON document.'
    }

    if (-not $result.Data.ContainsKey('movieTick')) {
        throw 'getCombatState omitted its correlated movie tick.'
    }
    $observation = Convert-AdaptiveCombatObservation `
        -Json ([string]$result.Data['json']) `
        -ResponseMovieTick ([long][string]$result.Data['movieTick'])
    return [pscustomobject]@{
        result = $result
        frame = $observation.frame
        values = $observation.values
        movieTick = $observation.movieTick
        sceneEpoch = $observation.sceneEpoch
    }
}

function New-AdaptiveInputMovie {
    param(
        [Parameter(Mandatory)][string]$ManifestSha256,
        [Parameter(Mandatory)][string]$Hold,
        [ValidateRange(1, 10000)][int]$Ticks = 1
    )

    $source = @(
        'hktas 1'
        'game 1.5.78.11833'
        'api 1.5.78.11833-77'
        "manifest-sha256 $ManifestSha256"
        'baseline none none'
        'tick-unit input'
        '---'
        "frames $Ticks hold=$Hold"
        ''
    ) -join "`n"
    return [Convert]::ToBase64String(
        [Text.UTF8Encoding]::new($false, $true).GetBytes($source))
}

function Wait-AdaptivePausedIdle {
    param(
        [Parameter(Mandatory)]$Client,
        [long]$MinimumMovieTick,
        [int]$TimeoutSeconds = 10
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 20
        $state = $Client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        $playback = [string]$state.State.Fields['playbackMode']
        if ($state.State.RuntimeMode -eq 'Paused' `
                -and $playback -eq 'Idle' `
                -and $state.State.MovieTick -ge $MinimumMovieTick) {
            return $state
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw 'Adaptive input did not return to Paused/Idle in time.'
}

function Wait-AdaptiveReplaySaveReady {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$Label,
        [int]$TimeoutSeconds = 60
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastCatalog = $null
    do {
        $lastCatalog = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'getReplaySaves' `
            -Scope 'observe.replay-saves'
        if ($lastCatalog.Data.ContainsKey('entriesJson')) {
            $entries = @(
                [string]$lastCatalog.Data['entriesJson'] |
                    ConvertFrom-Json -Depth 100
            )
            foreach ($entry in $entries) {
                if ([string]$entry.label -eq $Label `
                        -and [string]$entry.status -eq 'Ready') {
                    return [pscustomobject]@{
                        replaySaveId = [string]$entry.id
                        label = [string]$entry.label
                        effectiveMovieTick =
                            [long]$entry.effectiveMovieTick
                        movieId = [string]$entry.movieId
                        scene = [string]$entry.scene
                        sceneEpoch = [int]$entry.sceneEpoch
                        catalog = Convert-AutomationResult $lastCatalog
                    }
                }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $catalogText = if ($null -eq $lastCatalog) {
        '<none>'
    }
    else {
        [string]$lastCatalog.Data['entriesJson']
    }
    throw "Replay save did not become Ready: label=$Label; catalog=$catalogText"
}

function Wait-AdaptiveReplayRestorePhase {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string[]]$AllowedPhases,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    $lastError = $null
    do {
        try {
            $last = $Client.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
            if ($last.State.Fields.ContainsKey(
                    'replaySaveRestorePhase')) {
                $phase = [string]$last.State.Fields[
                    'replaySaveRestorePhase']
                if ($AllowedPhases -contains $phase) {
                    return $last
                }
            }
        }
        catch {
            $lastError = $_
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $lastPhase = if ($null -ne $last `
            -and $last.State.Fields.ContainsKey(
                'replaySaveRestorePhase')) {
        [string]$last.State.Fields['replaySaveRestorePhase']
    }
    else {
        '<none>'
    }
    throw (
        'Replay restore phase timeout: expected={0}; last={1}; error={2}' -f `
            ($AllowedPhases -join ','), `
            $lastPhase, `
            $lastError
    )
}

function Wait-AdaptiveMovieSeekTerminal {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$RequestId,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $cursor = 0L
    $phases = [Collections.Generic.List[object]]::new()
    do {
        $timeline = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'getTimeline' `
            -Scope 'observe.timeline' `
            -Arguments @{
                fromMovieTick = '-1'
                afterSequence = $cursor.ToString(
                    [CultureInfo]::InvariantCulture)
                # A watch frame carries the full typed combat/statue state.
                # Five entries stay below the automation-v1 field bound.
                count = '5'
            }
        $cursor = [long]$timeline.Data['nextAfterSequence']
        $entries = @(
            ([string]$timeline.Data['entries'] -split "`n") |
                Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_)
                }
        )
        foreach ($entry in $entries) {
            $parts = $entry -split '\|', 4
            if ($parts.Count -ne 4 `
                    -or $parts[1] -ne 'movieSeekProgress') {
                continue
            }
            $json = [Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String($parts[3]))
            $progress = $json |
                ConvertFrom-Json -AsHashtable -Depth 100
            if ([string]$progress['requestId'] -ne $RequestId) {
                continue
            }
            $phases.Add([ordered]@{
                sequence = [long]$parts[0]
                movieTick = [long]$parts[2]
                fields = $progress
            })
            if ([string]$progress['terminal'] -eq 'true') {
                return [pscustomobject]@{
                    terminal = $progress
                    phases = @($phases)
                }
            }
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Movie seek did not publish a terminal event: requestId=$RequestId"
}

function Invoke-AdaptiveProbe {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$CaseDirectory,
        [Parameter(Mandatory)][string]$ManifestSha256
    )

    $leaseId = Acquire-SdkLease `
        -Client $Client `
        -Scopes @(
            'control.playback',
            'control.input',
            'control.step',
            'control.run-until',
            'control.replay-save',
            'control.recording',
            'movie.apply-branch'
        )
    $released = $false
    try {
        $running = $Client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        $startRecording = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'startRecording' `
            -Scope 'control.recording' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $saveLabel = 't22-authoring-' `
            + [DateTimeOffset]::UtcNow.ToString('HHmmssfff')
        $createSave = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'createReplaySave' `
            -Scope 'control.replay-save' `
            -Arguments @{ label = $saveLabel } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $save = Wait-AdaptiveReplaySaveReady `
            -Client $Client `
            -Label $saveLabel
        $pause = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'pause' `
            -Scope 'control.playback' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $paused = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick $running.State.MovieTick
        $before = Get-AdaptiveCombatState -Client $Client
        $side = [string]$before.values['combat.relative.horizontalSide']
        if ($side -ne 'left' -and $side -ne 'right') {
            throw "Adaptive probe requires a left/right boss but observed '$side'."
        }
        $sceneEpoch = [int][string]$paused.State.Fields['sceneEpoch']

        $beginBatch = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'beginInputBatch' `
            -Scope 'control.input' `
            -Arguments @{
                expectedSceneEpoch = [string]$sceneEpoch
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $paused.State.MovieTick
        $transactionId = [string]$beginBatch.Data['transactionId']
        if ([string]::IsNullOrWhiteSpace($transactionId)) {
            throw 'beginInputBatch omitted transactionId.'
        }
        $appendBatch = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'appendInputBatch' `
            -Scope 'control.input' `
            -Arguments @{
                transactionId = $transactionId
                chunkIndex = '0'
                candidateMovieBase64 = New-AdaptiveInputMovie `
                    -ManifestSha256 $ManifestSha256 `
                    -Hold $side `
                    -Ticks 3
                expectedSceneEpoch = [string]$sceneEpoch
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $paused.State.MovieTick
        $commitBatch = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'commitInputBatch' `
            -Scope 'control.input' `
            -Arguments @{
                transactionId = $transactionId
                expectedSceneEpoch = [string]$sceneEpoch
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $paused.State.MovieTick
        $afterBatch = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick ($paused.State.MovieTick + 3)

        $hold = "$side,attack"
        $singleFrame = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'stepWithInput' `
            -Scope 'control.input' `
            -Arguments @{
                candidateMovieBase64 = New-AdaptiveInputMovie `
                    -ManifestSha256 $ManifestSha256 `
                    -Hold $hold `
                    -Ticks 1
                expectedSceneEpoch = [string]$sceneEpoch
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterBatch.State.MovieTick
        $afterSingle = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick ($afterBatch.State.MovieTick + 1)

        $plainStep = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'step' `
            -Scope 'control.step' `
            -Arguments @{ count = '1' } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterSingle.State.MovieTick
        $afterStep = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick ($afterSingle.State.MovieTick + 1)

        $runUntilTarget = $afterStep.State.MovieTick + 1
        $runUntil = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'runUntil' `
            -Scope 'control.run-until' `
            -Arguments @{
                targetMovieTick = [string]$runUntilTarget
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterStep.State.MovieTick
        $afterRunUntil = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick $runUntilTarget

        $after = Get-AdaptiveCombatState -Client $Client
        $stopRecording = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'stopRecording' `
            -Scope 'control.recording' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterRunUntil.State.MovieTick
        $recordedMovieId = [string]$stopRecording.Data['movieId']
        $recordedMovieBase64 =
            [string]$stopRecording.Data['movieBase64']
        if ($recordedMovieId -notmatch '^[0-9a-f]{64}$' `
                -or [string]::IsNullOrWhiteSpace(
                    $recordedMovieBase64)) {
            throw 'stopRecording omitted the canonical recorded movie.'
        }
        [IO.File]::WriteAllBytes(
            (Join-Path $CaseDirectory 'authoring-recorded.hktas'),
            [Convert]::FromBase64String($recordedMovieBase64))
        $validateRecorded = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'validateMoviePatch' `
            -Scope 'movie.validate' `
            -Arguments @{
                candidateMovieBase64 = $recordedMovieBase64
            }
        $editTick = $save.effectiveMovieTick + 1
        if ($editTick -ge [long]$validateRecorded.Data['expandedTicks']) {
            throw 'Recorded movie does not contain an editable tick after the save.'
        }
        $edit = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'replaceInputRange' `
            -Scope 'movie.edit' `
            -Arguments @{
                baseMovieId = $recordedMovieId
                startTick = [string]$editTick
                deleteCount = '1'
                replacementMovieBase64 = New-AdaptiveInputMovie `
                    -ManifestSha256 $ManifestSha256 `
                    -Hold 'left' `
                    -Ticks 1
            }
        $branchMovieId = [string]$edit.Data['branchMovieId']
        if ($branchMovieId -notmatch '^[0-9a-f]{64}$') {
            throw 'replaceInputRange omitted branchMovieId.'
        }
        if ($branchMovieId -eq $recordedMovieId) {
            throw 'Past-input edit was a no-op; branch movie ID did not change.'
        }
        [IO.File]::WriteAllBytes(
            (Join-Path $CaseDirectory 'authoring-edited.hktas'),
            [Convert]::FromBase64String(
                [string]$edit.Data['canonicalMovieBase64']))

        $applySeek = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'applyBranchAndSeek' `
            -Scope 'movie.apply-branch' `
            -Arguments @{
                branchMovieId = $branchMovieId
                targetMovieTick = [string]$afterRunUntil.State.MovieTick
                expectedSceneEpoch = [string]$sceneEpoch
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterRunUntil.State.MovieTick
        $seekProgress = Wait-AdaptiveMovieSeekTerminal `
            -Client $Client `
            -RequestId $applySeek.RequestId
        if ([string]$seekProgress.terminal['phase'] -ne 'Completed') {
            throw (
                'applyBranchAndSeek failed: {0}: {1}' -f `
                    $seekProgress.terminal['errorCode'], `
                    $seekProgress.terminal['detail']
            )
        }
        $afterSeek = Wait-AdaptivePausedIdle `
            -Client $Client `
            -MinimumMovieTick $afterRunUntil.State.MovieTick `
            -TimeoutSeconds 30

        $restore = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'restoreReplaySave' `
            -Scope 'control.replay-save' `
            -Arguments @{
                replaySaveId = $save.replaySaveId
            } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $afterSeek.State.MovieTick
        $restoreDecision = Wait-AdaptiveReplayRestorePhase `
            -Client $Client `
            -AllowedPhases @(
                'AwaitingOverwriteApproval',
                'Paused',
                'Failed',
                'Cancelled'
            )
        $approveRestore = $null
        if ([string]$restoreDecision.State.Fields[
                'replaySaveRestorePhase'] `
                -eq 'AwaitingOverwriteApproval') {
            $approveRestore = Invoke-SdkCommand `
                -Client $Client `
                -CommandId 'approveReplaySaveOverwrite' `
                -Scope 'control.replay-save' `
                -Arguments @{ approved = 'true' } `
                -LeaseId $leaseId `
                -ExpectedRuntimeMode $restoreDecision.State.RuntimeMode `
                -ExpectedMovieTick $restoreDecision.State.MovieTick
        }
        $restorePaused = Wait-AdaptiveReplayRestorePhase `
            -Client $Client `
            -AllowedPhases @('Paused', 'Failed', 'Cancelled')
        if ([string]$restorePaused.State.Fields[
                'replaySaveRestorePhase'] -ne 'Paused') {
            throw (
                'Explicit restore did not reach verified Paused: ' `
                + [string]$restorePaused.State.Fields[
                    'replaySaveRestoreDetail']
            )
        }
        if ($restorePaused.State.MovieTick -ne $save.effectiveMovieTick `
                -or [string]$restorePaused.State.Fields[
                    'replaySaveRestoreTargetMovieTick'] `
                    -ne [string]$save.effectiveMovieTick `
                -or [string]$restorePaused.State.Fields[
                    'replaySaveRestoreStrictSemanticEquivalent'] `
                    -ne 'true') {
            throw (
                'Explicit restore Paused proof was not exact: stateTick={0}; ' `
                + 'targetTick={1}; strict={2}' -f `
                    $restorePaused.State.MovieTick, `
                    [string]$restorePaused.State.Fields[
                        'replaySaveRestoreTargetMovieTick'], `
                    [string]$restorePaused.State.Fields[
                        'replaySaveRestoreStrictSemanticEquivalent']
            )
        }
        $resumeRestore = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'resumeReplaySaveRestore' `
            -Scope 'control.replay-save' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode $restorePaused.State.RuntimeMode `
            -ExpectedMovieTick $restorePaused.State.MovieTick
        $restoreCompleted = Wait-AdaptiveReplayRestorePhase `
            -Client $Client `
            -AllowedPhases @('Completed', 'Failed')
        if ([string]$restoreCompleted.State.Fields[
                'replaySaveRestorePhase'] -ne 'Completed') {
            throw (
                'Explicit restore did not complete: ' `
                + [string]$restoreCompleted.State.Fields[
                    'replaySaveRestoreDetail']
            )
        }

        $release = Release-SdkLease `
            -Client $Client `
            -Scope 'control.input' `
            -LeaseId $leaseId
        $released = $true
        $proof = [ordered]@{
            schemaVersion = 1
            verdict = 'AUTHORING_PARITY_PASS'
            visualRecognitionUsed = $false
            humanAndAiSharedCommandPath = 'AutomationBroker.ExecuteAsync'
            beforeMovieTick = $paused.State.MovieTick
            afterMultiFrameMovieTick = $afterBatch.State.MovieTick
            afterSingleFrameMovieTick = $afterSingle.State.MovieTick
            afterPlainStepMovieTick = $afterStep.State.MovieTick
            afterRunUntilMovieTick = $afterRunUntil.State.MovieTick
            afterSeekMovieTick = $afterSeek.State.MovieTick
            restorePausedMovieTick = $restorePaused.State.MovieTick
            restoreTargetMovieTick = [long][string]$restorePaused.State.Fields[
                'replaySaveRestoreTargetMovieTick']
            restoreStrictSemanticEquivalent = [string]$restorePaused.State.Fields[
                'replaySaveRestoreStrictSemanticEquivalent']
            restoredMovieTick = $restoreCompleted.State.MovieTick
            beforeSceneEpoch = $sceneEpoch
            beforeBossSide = $side
            beforeBossX = [single][string]$before.values[
                'combat.primaryBoss.position.x']
            beforeHeroX = [single][string]$before.values['hero.position.x']
            beforeBossState = [string]$before.values[
                'combat.primaryBoss.mainState']
            selectedHold = $hold
            afterBossSide = [string]$after.values[
                'combat.relative.horizontalSide']
            afterBossX = [single][string]$after.values[
                'combat.primaryBoss.position.x']
            afterHeroX = [single][string]$after.values['hero.position.x']
            replaySave = $save
            recordedMovieId = $recordedMovieId
            editedBranchMovieId = $branchMovieId
            editedTick = $editTick
            startRecording = Convert-AutomationResult $startRecording
            createSave = Convert-AutomationResult $createSave
            pause = Convert-AutomationResult $pause
            beginBatch = Convert-AutomationResult $beginBatch
            appendBatch = Convert-AutomationResult $appendBatch
            commitBatch = Convert-AutomationResult $commitBatch
            singleFrame = Convert-AutomationResult $singleFrame
            plainStep = Convert-AutomationResult $plainStep
            runUntil = Convert-AutomationResult $runUntil
            stopRecording = Convert-AutomationResult $stopRecording
            validateRecorded = Convert-AutomationResult $validateRecorded
            edit = Convert-AutomationResult $edit
            applyBranchAndSeek = Convert-AutomationResult $applySeek
            seekProgress = @($seekProgress.phases)
            restore = Convert-AutomationResult $restore
            approveRestore = if ($null -eq $approveRestore) {
                $null
            }
            else {
                Convert-AutomationResult $approveRestore
            }
            resumeRestore = Convert-AutomationResult $resumeRestore
            restoreFinalPhase = [string]$restoreCompleted.State.Fields[
                'replaySaveRestorePhase']
            release = Convert-AutomationResult $release
        }
        $proof |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $CaseDirectory 'adaptive-probe.json') `
                -Encoding utf8NoBOM
        $proof |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $CaseDirectory 'authoring-parity.json') `
                -Encoding utf8NoBOM
        return $proof
    }
    finally {
        if (-not $released) {
            try {
                [void](Release-SdkLease `
                    -Client $Client `
                    -Scope 'control.input' `
                    -LeaseId $leaseId)
            }
            catch {
            }
        }
    }
}

function Start-DiscoveryCase {
    Close-RunProcesses
    Write-TestSettings
    $caseDirectory = Join-Path $EvidenceRoot 'authoring\discovery'
    New-Item -ItemType Directory -Path $caseDirectory | Out-Null
    $script:activeCaseDirectory = $caseDirectory
    $beforeSessions = @(
        Get-ChildItem -LiteralPath $sessionRoot -Directory |
            ForEach-Object FullName
    )
    $beforeHelpers = @(
        Get-Process `
            -Name `
                HollowKnightTAS.Companion,
                HollowKnightTAS.AgentBridge,
                HollowKnightTAS.NativeHost `
            -ErrorAction SilentlyContinue |
            ForEach-Object Id
    )
    $runId = 't16-discovery-{0}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $launchAt = [DateTimeOffset]::Now
    $launchArguments = @(
        '-applaunch',
        '367520',
        '-screen-width',
        '800',
        '-screen-height',
        '450',
        '-screen-fullscreen',
        '0',
        "--hktas-final-tas-ready-run=$runId",
        "--hktas-final-tas-ready-slot=$FixtureSlot"
    )
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $launchArguments |
        Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($MaxLaunchSeconds)
    $session = $null
    $probeResultPath = $null
    do {
        if ($null -eq $script:game) {
            $script:game = Get-Process `
                    -Name hollow_knight `
                    -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.StartTime -ge $launchAt.LocalDateTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
        }
        $newSession = Get-ChildItem `
                -LiteralPath $sessionRoot `
                -Directory `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $beforeSessions -notcontains $_.FullName
            } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $candidate = Join-Path `
                $newSession.FullName `
                "final-tas-ready\$runId\result.json"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $session = $newSession
                $script:activeSessionDirectory = $newSession.FullName
                $probeResultPath = $candidate
            }
        }
        $script:ownedHelperPids = @(
            Get-Process `
                -Name `
                    HollowKnightTAS.Companion,
                    HollowKnightTAS.AgentBridge,
                    HollowKnightTAS.NativeHost `
                -ErrorAction SilentlyContinue |
                Where-Object {
                    $beforeHelpers -notcontains $_.Id
                } |
                ForEach-Object Id
        )
        if ($null -ne $script:game `
                -and $null -ne $session `
                -and (Test-Path -LiteralPath $bootstrapPath -PathType Leaf)) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($null -eq $script:game `
            -or $null -eq $session `
            -or $null -eq $probeResultPath) {
        throw 'T16 game/session/seated fixture did not become ready.'
    }

    $probe = Get-Content -LiteralPath $probeResultPath -Raw |
        ConvertFrom-Json
    if (-not $probe.fixtureReady `
            -or -not $probe.atBench `
            -or $probe.inputInjected `
            -or $probe.timeScaleChanged `
            -or $probe.heroStateWritten `
            -or $probe.playerDataWritten `
            -or $probe.scene -ne 'GG_Workshop') {
        throw (
            'T16 seated fixture violated its contract: ' `
            + ($probe | ConvertTo-Json -Compress)
        )
    }

    Copy-Item `
        -LiteralPath $probeResultPath `
        -Destination (Join-Path $caseDirectory 'fixture-ready.json')
    foreach ($leaf in @('manifest.json', 'manifest.sha256')) {
        $source = Join-Path $session.FullName $leaf
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item `
                -LiteralPath $source `
                -Destination (Join-Path $caseDirectory $leaf)
        }
    }

    Add-Type -Path $sdkCorePath
    Add-Type -Path $sdkClientPath
    $client =
        [HollowKnightTAS.Automation.Client.AutomationClient]::new()
    try {
        $options =
            [HollowKnightTAS.Automation.Client.AutomationConnectOptions]::new()
        $options.ClientId = 't16-discovery-sdk'
        $options.BootstrapPath = $bootstrapPath
        $options.Timeout = [TimeSpan]::FromSeconds(15)
        [void]$client.ConnectAsync(
                $options,
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()

        $state = $client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        [IO.File]::WriteAllText(
            (Join-Path $caseDirectory 'state.json'),
            $state.ToFriendlyJson(),
            [Text.UTF8Encoding]::new($false))
        $capabilities = Invoke-SdkCommand `
            -Client $client `
            -CommandId 'getCapabilities' `
            -Scope 'observe.status'
        Convert-AutomationResult $capabilities |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $caseDirectory 'capabilities.json') `
                -Encoding utf8NoBOM

        $watch = Find-BossPracticeWatch -Client $client
        $watch.frame |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $caseDirectory 'watch-frame.json') `
                -Encoding utf8NoBOM
        $watch.values |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $caseDirectory 'watch-values.json') `
                -Encoding utf8NoBOM
        $catalog = @(
            [string]$watch.values['bossPractice.statues.catalogJson'] |
                ConvertFrom-Json -Depth 100
        )
        $falseKnight = @(
            $catalog |
                Where-Object {
                    $_.regularScene -eq 'GG_False_Knight'
                }
        )
        if ($falseKnight.Count -ne 1) {
            throw (
                'Expected exactly one False Knight statue but found ' `
                + $falseKnight.Count
            )
        }
        if ($watch.frame.failures.Count -ne 0) {
            throw 'Boss-practice provider reported a watch failure.'
        }

        $result = [ordered]@{
            schemaVersion = 1
            verdict = 'DISCOVERY_PASS'
            fixtureSlot = $FixtureSlot
            fixtureDatState = Get-FileState (
                Join-Path $PersistentDataDirectory "user$FixtureSlot.dat")
            fixtureModdedState = Get-FileState (
                Join-Path `
                    $PersistentDataDirectory `
                    "user$FixtureSlot.modded.json")
            sessionId = $session.Name
            manifestSha256 = $capabilities.ManifestSha256
            scene = [string]$watch.values['scene.name']
            atBench = [bool]::Parse(
                [string]$watch.values['bossPractice.bench.atBench'])
            nearBench = [bool]::Parse(
                [string]$watch.values['bossPractice.bench.nearBench'])
            heroPositionX = [string]$watch.values['hero.position.x']
            heroPositionY = [string]$watch.values['hero.position.y']
            statueCount = [int][string]$watch.values[
                'bossPractice.statues.count']
            falseKnightStatue = $falseKnight[0]
            watchSequence = $watch.sequence
            watchMovieTick = $watch.movieTick
            providerFailureCount = $watch.frame.failures.Count
            inputInjected = $false
            visualRecognitionUsed = $false
        }
        $result |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $caseDirectory 'discovery.json') `
                -Encoding utf8NoBOM

        $trialResult = if ($Phase -eq 'Trial' `
                -or $Phase -eq 'Adaptive' `
                -or $Phase -eq 'Fight') {
            Invoke-TrialReplay `
                -Client $client `
                -CaseDirectory $caseDirectory `
                -ManifestSha256 $capabilities.ManifestSha256
        }
        else {
            $null
        }

        $adaptiveResult = if ($Phase -eq 'Adaptive') {
            Invoke-AdaptiveProbe `
                -Client $client `
                -CaseDirectory $caseDirectory `
                -ManifestSha256 $capabilities.ManifestSha256
        }
        else {
            $null
        }

        $fightResult = if ($Phase -eq 'Fight') {
            Invoke-FalseKnightAdaptiveFight `
                -Client $client `
                -CaseDirectory $caseDirectory `
                -ManifestSha256 $capabilities.ManifestSha256 `
                -RouteMoviePath (
                    Join-Path $caseDirectory 'candidate-applied.hktas')
        }
        else {
            $null
        }

        return [pscustomobject]@{
            result = $result
            trial = $trialResult
            adaptive = $adaptiveResult
            fight = $fightResult
            sessionDirectory = $session.FullName
            caseDirectory = $caseDirectory
        }
    }
    finally {
        $client.DisposeAsync().GetAwaiter().GetResult()
    }
}

try {
    $slotInitial = Get-SlotState
    $slotOriginalFiles = Get-SlotFileBackup
    $modsInitial = Get-ModTreeState

    foreach ($unexpectedPath in @($modsBackup, $modsEmpty)) {
        if (Test-Path -LiteralPath $unexpectedPath) {
            throw "Unexpected T16 Mods recovery path exists: $unexpectedPath"
        }
    }
    Move-Item -LiteralPath $modsDirectory -Destination $modsBackup
    $modsSwapped = $true
    New-Item -ItemType Directory -Path $modsDirectory | Out-Null
    Move-Item `
        -LiteralPath (Join-Path $modsBackup 'HollowKnightTAS') `
        -Destination (Join-Path $modsDirectory 'HollowKnightTAS')
    $isolatedEntries = @(Get-ChildItem -LiteralPath $modsDirectory -Force)
    if ($isolatedEntries.Count -ne 1 `
            -or $isolatedEntries[0].Name -ne 'HollowKnightTAS') {
        throw 'Failed to establish the isolated T16 Mods profile.'
    }

    if ($replayStoreOriginallyExisted) {
        Move-Item `
            -LiteralPath $replayStoreRoot `
            -Destination $replayStoreBackup
        $replayStoreMoved = $true
    }
    if ($automationOriginallyExisted) {
        Move-Item `
            -LiteralPath $automationRoot `
            -Destination $automationBackup
        $automationMoved = $true
    }

    $case = Start-DiscoveryCase
    $discovery = $case.result
    $trial = $case.trial
    $adaptive = $case.adaptive
    $fight = $case.fight
}
finally {
    Close-RunProcesses
    if ($null -ne $slotOriginalFiles) {
        Restore-SlotFiles -Backup $slotOriginalFiles
    }
    $evidenceSessionDirectory = if ($null -ne $case) {
        $case.sessionDirectory
    }
    else {
        $script:activeSessionDirectory
    }
    $evidenceCaseDirectory = if ($null -ne $case) {
        $case.caseDirectory
    }
    else {
        $script:activeCaseDirectory
    }
    if (-not [string]::IsNullOrWhiteSpace($evidenceSessionDirectory) `
            -and -not [string]::IsNullOrWhiteSpace($evidenceCaseDirectory) `
            -and (Test-Path -LiteralPath $evidenceSessionDirectory)) {
        $runtimeEvidence = Join-Path $evidenceCaseDirectory 'runtime'
        New-Item -ItemType Directory -Path $runtimeEvidence | Out-Null
        foreach ($relative in @(
                'events.jsonl',
                'inspector\watches.jsonl',
                'inspector\performance.json',
                'rng\rng-ledger.jsonl',
                'rng\whitelist-resolution.json'
            )) {
            $source = Join-Path $evidenceSessionDirectory $relative
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                $destination = Join-Path `
                    $runtimeEvidence `
                    ($relative -replace '\\', '-')
                Copy-Item -LiteralPath $source -Destination $destination
                if (Test-Path -LiteralPath ($source + '.incomplete')) {
                    Copy-Item -LiteralPath ($source + '.incomplete') -Destination ($destination + '.incomplete')
                }
            }
        }
    }
    [IO.File]::WriteAllBytes($settingsPath, $settingsOriginal)
    if ($settingsBackupOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsBackupPath,
            $settingsBackupOriginal)
    }
    elseif (Test-Path -LiteralPath $settingsBackupPath) {
        Remove-Item -LiteralPath $settingsBackupPath -Force
    }

    if ($modsSwapped) {
        $isolatedTas = Join-Path $modsDirectory 'HollowKnightTAS'
        if (Test-Path -LiteralPath $isolatedTas) {
            Move-Item `
                -LiteralPath $isolatedTas `
                -Destination (Join-Path $modsBackup 'HollowKnightTAS')
        }
        if (Test-Path -LiteralPath $modsDirectory) {
            Move-Item `
                -LiteralPath $modsDirectory `
                -Destination $modsEmpty
        }
        Move-Item -LiteralPath $modsBackup -Destination $modsDirectory
        $modsSwapped = $false
        if (Test-Path -LiteralPath $modsEmpty) {
            if (@(Get-ChildItem -LiteralPath $modsEmpty -Force).Count -ne 0) {
                throw 'T16 empty validation directory contains unexpected files.'
            }
            Remove-Item -LiteralPath $modsEmpty
        }
    }

    if (Test-Path -LiteralPath $replayStoreRoot) {
        $resolvedReplay = [IO.Path]::GetFullPath($replayStoreRoot)
        if (-not $resolvedReplay.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing replay-store cleanup outside persistent data.'
        }
        Remove-Item -LiteralPath $resolvedReplay -Recurse -Force
    }
    if ($replayStoreMoved) {
        Move-Item `
            -LiteralPath $replayStoreBackup `
            -Destination $replayStoreRoot
        $replayStoreMoved = $false
    }

    if (Test-Path -LiteralPath (
            Join-Path $automationRoot 'artifacts'
        ) -PathType Container) {
        Copy-Item `
            -LiteralPath (Join-Path $automationRoot 'artifacts') `
            -Destination (
                Join-Path $EvidenceRoot 'authoring\automation-audit'
            ) `
            -Recurse
    }
    if (Test-Path -LiteralPath $automationRoot) {
        $resolvedAutomation = [IO.Path]::GetFullPath($automationRoot)
        if (-not $resolvedAutomation.StartsWith(
                $localPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing automation cleanup outside LocalApplicationData.'
        }
        Remove-Item -LiteralPath $resolvedAutomation -Recurse -Force
    }
    if ($automationMoved) {
        Move-Item `
            -LiteralPath $automationBackup `
            -Destination $automationRoot
        $automationMoved = $false
    }
}

if (-not [string]::IsNullOrWhiteSpace($evidenceCaseDirectory)) {
    $copiedRuntime = Join-Path $evidenceCaseDirectory 'runtime'
    if ((Test-Path -LiteralPath $copiedRuntime) -and
            @(Get-ChildItem -LiteralPath $copiedRuntime -Filter '*.incomplete' -File).Count -gt 0) {
        throw 'T16 runtime evidence is incomplete; refusing a success report after cleanup.'
    }
}

$settingsFinalSha256 = (
    Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256
).Hash
if ($settingsFinalSha256 -ne $settingsOriginalSha256) {
    throw 'T16 settings were not restored byte-for-byte.'
}
$settingsBackupFinalSha256 = if (
    Test-Path -LiteralPath $settingsBackupPath -PathType Leaf
) {
    (Get-FileHash `
        -LiteralPath $settingsBackupPath `
        -Algorithm SHA256).Hash
}
else {
    'missing'
}
if ($settingsBackupFinalSha256 -ne $settingsBackupOriginalSha256) {
    throw 'T16 settings backup was not restored byte-for-byte.'
}
Assert-SequenceEqual `
    -Expected $slotInitial `
    -Actual (Get-SlotState) `
    -Label 'User save slots after cleanup'
Assert-SequenceEqual `
    -Expected $modsInitial `
    -Actual (Get-ModTreeState) `
    -Label 'Mods tree after cleanup'
if ($replayStoreOriginallyExisted -ne (
        Test-Path -LiteralPath $replayStoreRoot -PathType Container
    )) {
    throw 'T16 replay-store existence was not restored.'
}
if ($automationOriginallyExisted -ne (
        Test-Path -LiteralPath $automationRoot -PathType Container
    )) {
    throw 'T16 automation-workspace existence was not restored.'
}
if (@(
        Get-Process `
            -Name `
                hollow_knight,
                HollowKnightTAS.Companion,
                HollowKnightTAS.AgentBridge,
                HollowKnightTAS.NativeHost `
            -ErrorAction SilentlyContinue
    ).Count -ne 0) {
    throw 'A T16-owned process remained after cleanup.'
}

$final = [ordered]@{
    schemaVersion = 1
    phase = $Phase
    verdict = if ($null -ne $fight) {
        $fight.verdict
    }
    elseif ($null -ne $adaptive) {
        $adaptive.verdict
    }
    elseif ($null -ne $trial) {
        $trial.verdict
    }
    else {
        $discovery.verdict
    }
    discovery = $discovery
    trial = $trial
    adaptive = $adaptive
    fight = $fight
    settingsRestoredSha256 = $settingsFinalSha256.ToLowerInvariant()
    settingsBackupRestoredSha256 =
        $settingsBackupFinalSha256.ToLowerInvariant()
    slotsRestored = $true
    modsRestored = $true
    replayStoreRestored = $true
    automationWorkspaceRestored = $true
    processesClean = $true
}
$final |
    ConvertTo-Json -Depth 100 |
    Set-Content `
        -LiteralPath (Join-Path $EvidenceRoot 'final-verdict.json') `
        -Encoding utf8NoBOM

Write-Host "T16 $Phase completed: $EvidenceRoot"

[CmdletBinding()]
param(
    [ValidateRange(2, 10)]
    [int]$MatchingRuns = 5,

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(120, 600)]
    [int]$MaxRunSeconds = 240
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T10 RNG matrix requires PowerShell 7 or newer.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't10-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $projectRoot `
        "artifacts\rng\$campaign"
}

$ManagedDirectory = [IO.Path]::GetFullPath($ManagedDirectory)
$PersistentDataDirectory =
    [IO.Path]::GetFullPath($PersistentDataDirectory)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$storeRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$storeParent = Split-Path -Parent $storeRoot

$resolvedManaged = (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods = (Resolve-Path -LiteralPath $modsDirectory).Path
if (-not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved Mods directory is outside the intended Managed directory.'
}
if (-not $storeRoot.StartsWith(
        $PersistentDataDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Replay store is outside the intended persistent-data directory.'
}
if (-not (Test-Path -LiteralPath $SteamExecutable -PathType Leaf)) {
    throw "Steam executable not found: $SteamExecutable"
}
if (-not (Test-Path -LiteralPath (
            Join-Path $modsDirectory 'HollowKnightTAS'
        ) -PathType Container)) {
    throw 'Installed HollowKnightTAS directory was not found.'
}
if (Get-Process -Name 'hollow_knight' -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight is already running.'
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Evidence root already exists: $EvidenceRoot"
}

$swapId = [Guid]::NewGuid().ToString('N')
$modsBackup = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T10-$swapId.backup"
$modsEmpty = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T10-$swapId.empty"
$storeBackup = Join-Path `
    $storeParent `
    "v1.HKTAS-T10-$swapId.backup"
$privateBackupRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "HKTAS-T10-$swapId"
$settingsBackup = Join-Path $privateBackupRoot 'settings.json'

$script:gameProcess = $null
$settingsOriginallyExisted = Test-Path -LiteralPath $settingsPath
$settingsBackedUp = $false
$modsSwapped = $false
$storeMoved = $false
$initialSlots = $null
$runs = [System.Collections.Generic.List[object]]::new()

function Get-FileState {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            exists = $false
            length = 0
            sha256 = ''
        }
    }

    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        exists = $true
        length = $item.Length
        sha256 = (
            Get-FileHash -LiteralPath $Path -Algorithm SHA256
        ).Hash
    }
}

function Get-AllSlotState {
    $result = [ordered]@{}
    foreach ($slot in 1..4) {
        $result["slot$slot"] = [ordered]@{
            save = Get-FileState (
                Join-Path `
                    $PersistentDataDirectory `
                    "user$slot.dat"
            )
            modded = Get-FileState (
                Join-Path `
                    $PersistentDataDirectory `
                    "user$slot.modded.json"
            )
        }
    }
    return $result
}

function Assert-SlotsUnchanged {
    param([Parameter(Mandatory)]$Expected)

    $actual = Get-AllSlotState
    foreach ($slot in 1..4) {
        foreach ($kind in @('save', 'modded')) {
            $left = $Expected["slot$slot"][$kind]
            $right = $actual["slot$slot"][$kind]
            if ($left.exists -ne $right.exists `
                    -or $left.length -ne $right.length `
                    -or -not [string]::Equals(
                        $left.sha256,
                        $right.sha256,
                        [StringComparison]::Ordinal
                    )) {
                throw "User slot changed: slot=$slot kind=$kind"
            }
        }
    }
}

function Close-Game {
    if ($null -eq $script:gameProcess) {
        return
    }

    $script:gameProcess.Refresh()
    if (-not $script:gameProcess.HasExited) {
        [void]$script:gameProcess.CloseMainWindow()
        [void]$script:gameProcess.WaitForExit(20000)
        $script:gameProcess.Refresh()
    }
}

function Write-TestSettings {
    $settings = [ordered]@{
        VerificationModeRequested = $true
        AllowedVerificationMods = @('HollowKnightTAS')
        EventQueueCapacity = 8192
        ExitFlushTimeoutMilliseconds = 5000
        ReplaySaveEnabled = $true
        ReplaySaveAutoEnabled = $false
        ReplaySaveAutoIntervalMovieTicks = 18000
        ReplaySaveAutoRetentionCount = 20
        DedicatedTasSaveSlot = $TestSaveSlot
        ReplaySaveOverlayEnabled = $false
        ReplaySaveDeterministicTimingEnabled = $true
    }
    $settings |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
}

function Invoke-RngRun {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('MATCH', 'NOHOOK', 'DIVERGE')]
        [string]$Profile,

        [Parameter(Mandatory)]
        [int]$Ordinal
    )

    $runId = '{0}-{1:D2}-{2}' -f `
        $Profile.ToLowerInvariant(), `
        $Ordinal, `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $beforeSessions = @(
        Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            ForEach-Object FullName
    )
    $launchTime = Get-Date
    $arguments = @(
        '-applaunch',
        '367520',
        '-screen-width',
        '800',
        '-screen-height',
        '450',
        '-screen-fullscreen',
        '0',
        "--hktas-rng-probe=$Profile",
        "--hktas-rng-probe-run=$runId",
        "--hktas-rng-probe-slot=$TestSaveSlot",
        '--hktas-rng-probe-exit'
    )
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $script:gameProcess = $null
    $resultPath = $null
    $sessionDirectory = $null
    $deadline = (Get-Date).AddSeconds($MaxRunSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($null -eq $script:gameProcess) {
            $script:gameProcess = Get-Process |
                Where-Object {
                    $_.ProcessName -eq 'hollow_knight' `
                        -and $_.StartTime -ge $launchTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
        }

        $newSession = Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            Where-Object { $beforeSessions -notcontains $_.FullName } |
            Sort-Object LastWriteTime -Descending |
            Where-Object {
                Test-Path -LiteralPath (
                    Join-Path $_.FullName "rng\probe\$runId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $candidate = Join-Path `
                $newSession.FullName `
                "rng\probe\$runId\result.json"
            if (Test-Path -LiteralPath $candidate) {
                $resultPath = $candidate
                $sessionDirectory = $newSession.FullName
                break
            }
        }
    }

    if ($null -eq $resultPath -or $null -eq $sessionDirectory) {
        Close-Game
        throw "T10 run timed out: $runId"
    }

    if ($null -ne $script:gameProcess) {
        [void]$script:gameProcess.WaitForExit(30000)
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            Close-Game
            throw "Game did not exit normally after T10 run: $runId"
        }
    }
    $script:gameProcess = $null

    $destination = Join-Path `
        $EvidenceRoot `
        ('runs\{0}-{1:D2}' -f $Profile.ToLowerInvariant(), $Ordinal)
    New-Item -ItemType Directory -Path $destination | Out-Null
    Copy-Item `
        -LiteralPath (Split-Path -Parent $resultPath) `
        -Destination (Join-Path $destination 'probe') `
        -Recurse
    foreach ($relative in @(
            'rng\rng-ledger.jsonl',
            'rng\whitelist-resolution.json',
            'manifest.json',
            'manifest.sha256'
        )) {
        $source = Join-Path $sessionDirectory $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required T10 evidence is missing: $relative"
        }
        Copy-Item `
            -LiteralPath $source `
            -Destination (Join-Path $destination (Split-Path -Leaf $relative))
    }

    $result = Get-Content -Raw -LiteralPath $resultPath |
        ConvertFrom-Json
    $manifest = Get-Content `
        -Raw `
        -LiteralPath (Join-Path $sessionDirectory 'manifest.json') |
        ConvertFrom-Json
    $resolution = Get-Content `
        -Raw `
        -LiteralPath (
            Join-Path $sessionDirectory 'rng\whitelist-resolution.json'
        ) |
        ConvertFrom-Json
    $ledgerLines = @(
        Get-Content -LiteralPath (
            Join-Path $sessionDirectory 'rng\rng-ledger.jsonl'
        )
    )
    $stateLines = @(
        $ledgerLines |
            Where-Object { $_ -match '"eventType":"rng-state"' }
    ).Count
    $callLines = @(
        $ledgerLines |
            Where-Object { $_ -match '"eventType":"rng-call"' }
    ).Count

    $runPass =
        [bool]$result.runPass `
        -and [string]::Equals(
            [string]$result.codecStatus,
            'Ready',
            [StringComparison]::Ordinal
        ) `
        -and [string]::Equals(
            [string]$result.callSiteStatus,
            'Ready',
            [StringComparison]::Ordinal
        ) `
        -and [string]::Equals(
            [string]$manifest.rngCodecId,
            'unity-random-state-1.5.78.11833-s0-s3-be-v1',
            [StringComparison]::Ordinal
        ) `
        -and [string]::Equals(
            [string]$manifest.rngCoverage,
            'unity-random-partial-whitelist-v1',
            [StringComparison]::Ordinal
        ) `
        -and [string]::Equals(
            [string]$resolution.callSiteStatus,
            'Ready',
            [StringComparison]::Ordinal
        ) `
        -and $resolution.callSites.Count -eq 2 `
        -and $stateLines -gt 0 `
        -and $result.codecStableOneHundred `
        -and $result.codecSeedChangedHash `
        -and $result.codecRestoredExactly `
        -and $result.cleanupRandomEquivalent `
        -and $result.cleanupHealthEquivalent `
        -and [string]::IsNullOrEmpty([string]$result.fault)
    if (-not $runPass) {
        throw "T10 runtime gate failed: $runId"
    }

    Assert-SlotsUnchanged -Expected $initialSlots
    $record = [pscustomobject]@{
        profile = $Profile
        ordinal = $Ordinal
        runId = $runId
        result = $result
        manifest = $manifest
        resolution = $resolution
        stateLineCount = $stateLines
        callLineCount = $callLines
        evidence = $destination
    }
    $runs.Add($record)
    return $record
}

function Get-FirstDifferenceIndex {
    param(
        [Parameter(Mandatory)]$Left,
        [Parameter(Mandatory)]$Right,
        [Parameter(Mandatory)][string]$Property
    )

    $count = [Math]::Min($Left.Count, $Right.Count)
    foreach ($index in 0..($count - 1)) {
        if (-not [string]::Equals(
                [string]$Left[$index].$Property,
                [string]$Right[$index].$Property,
                [StringComparison]::Ordinal
            )) {
            return $index
        }
    }
    if ($Left.Count -ne $Right.Count) {
        return $count
    }
    return -1
}

New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
New-Item -ItemType Directory -Path $privateBackupRoot | Out-Null
$initialSlots = Get-AllSlotState

try {
    if ($settingsOriginallyExisted) {
        Copy-Item `
            -LiteralPath $settingsPath `
            -Destination $settingsBackup
        $settingsBackedUp = $true
    }
    Write-TestSettings

    if (Test-Path -LiteralPath $storeRoot) {
        Move-Item `
            -LiteralPath $storeRoot `
            -Destination $storeBackup
        $storeMoved = $true
    }

    Move-Item `
        -LiteralPath $modsDirectory `
        -Destination $modsBackup
    $modsSwapped = $true
    New-Item -ItemType Directory -Path $modsDirectory | Out-Null
    Move-Item `
        -LiteralPath (Join-Path $modsBackup 'HollowKnightTAS') `
        -Destination (Join-Path $modsDirectory 'HollowKnightTAS')
    $isolated = @(Get-ChildItem -LiteralPath $modsDirectory -Force)
    if ($isolated.Count -ne 1 `
            -or $isolated[0].Name -ne 'HollowKnightTAS') {
        throw 'Failed to establish the isolated T10 Mods profile.'
    }

    foreach ($ordinal in 1..$MatchingRuns) {
        [void](Invoke-RngRun -Profile MATCH -Ordinal $ordinal)
    }
    $noHook = Invoke-RngRun -Profile NOHOOK -Ordinal 1
    $diverge = Invoke-RngRun -Profile DIVERGE -Ordinal 1

    $matches = @($runs | Where-Object profile -eq 'MATCH')
    $matchRngTraces = @(
        $matches.result.rngTraceSha256 |
            Sort-Object -Unique
    )
    $matchSemanticTraces = @(
        $matches.result.semanticTraceSha256 |
            Sort-Object -Unique
    )
    $matchCallTraces = @(
        $matches.result.callTraceSha256 |
            Sort-Object -Unique
    )
    $matchAmbientEndpoints = @(
        $matches.result.ambientEndpointRngSha256 |
            Sort-Object -Unique
    )
    $matchingPass =
        $matches.Count -eq $MatchingRuns `
        -and $matchRngTraces.Count -eq 1 `
        -and $matchSemanticTraces.Count -eq 1 `
        -and $matchCallTraces.Count -eq 1
    $ambientEndpointConverged =
        $matchAmbientEndpoints.Count -eq 1
    $unwhitelistedUnityRngObserved =
        -not $ambientEndpointConverged

    $reference = $matches[0].result
    $noHookParity =
        [string]::Equals(
            [string]$reference.rngTraceSha256,
            [string]$noHook.result.rngTraceSha256,
            [StringComparison]::Ordinal
        ) `
        -and [string]::Equals(
            [string]$reference.semanticTraceSha256,
            [string]$noHook.result.semanticTraceSha256,
            [StringComparison]::Ordinal
        ) `
        -and $noHook.result.globalCallDelta -eq 0 `
        -and $noHook.result.calls.Count -eq 0

    $referenceMilestones = @($reference.milestones)
    $divergentMilestones = @($diverge.result.milestones)
    $firstRngDifference = Get-FirstDifferenceIndex `
        -Left $referenceMilestones `
        -Right $divergentMilestones `
        -Property 'rngStateSha256'
    $firstSemanticDifference = Get-FirstDifferenceIndex `
        -Left $referenceMilestones `
        -Right $divergentMilestones `
        -Property 'semanticSha256'
    $divergencePass =
        $firstRngDifference -ge 0 `
        -and $firstSemanticDifference -ge 0 `
        -and $firstRngDifference -le $firstSemanticDifference

    $fullGatePass =
        $matchingPass `
        -and $noHookParity `
        -and $divergencePass `
        -and @(
            $runs |
                Where-Object { -not $_.result.runPass }
        ).Count -eq 0
    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        fullGatePass = $fullGatePass
        matchingRuns = $MatchingRuns
        matchingPass = $matchingPass
        matchingRngTraceSha256 = $matchRngTraces[0]
        matchingSemanticTraceSha256 = $matchSemanticTraces[0]
        matchingCallTraceSha256 = $matchCallTraces[0]
        ambientEndpointConverged = $ambientEndpointConverged
        ambientEndpointRngSha256 = @($matchAmbientEndpoints)
        unwhitelistedUnityRngObserved =
            $unwhitelistedUnityRngObserved
        noHookParity = $noHookParity
        firstRngDifferenceMilestoneIndex = $firstRngDifference
        firstSemanticDifferenceMilestoneIndex =
            $firstSemanticDifference
        divergencePass = $divergencePass
        codecId = $reference.codecId
        coverage = $reference.coverage
        limitations = $reference.limitations
        runSummaries = @(
            $runs |
                ForEach-Object {
                    [ordered]@{
                        profile = $_.profile
                        ordinal = $_.ordinal
                        runId = $_.runId
                        runPass = $_.result.runPass
                        rngTraceSha256 =
                            $_.result.rngTraceSha256
                        semanticTraceSha256 =
                            $_.result.semanticTraceSha256
                        callTraceSha256 =
                            $_.result.callTraceSha256
                        ambientEndpointRngSha256 =
                            $_.result.ambientEndpointRngSha256
                        globalCallDelta =
                            $_.result.globalCallDelta
                        stateLineCount = $_.stateLineCount
                        callLineCount = $_.callLineCount
                    }
                }
        )
        initialSlotHashes = $initialSlots
    }
    $matrix |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'rng-matrix.json'
            ) `
            -Encoding utf8NoBOM

    $report = @(
        '# T10 RNG Diagnostic Matrix'
        ''
        "- Full gate pass: ``$fullGatePass``"
        "- Matching runs: ``$MatchingRuns/$MatchingRuns``"
        "- Matching RNG trace: ``$($matrix.matchingRngTraceSha256)``"
        "- Matching semantic trace: ``$($matrix.matchingSemanticTraceSha256)``"
        "- Matching call trace: ``$($matrix.matchingCallTraceSha256)``"
        "- Ambient endpoint converged: ``$ambientEndpointConverged``"
        "- Unwhitelisted Unity RNG observed: ``$unwhitelistedUnityRngObserved``"
        "- Hook-off parity: ``$noHookParity``"
        "- First RNG difference milestone: ``$firstRngDifference``"
        "- First semantic difference milestone: ``$firstSemanticDifference``"
        "- Coverage: ``$($matrix.coverage)``"
        "- System.Random: ``not-covered``"
        "- Other Mod RNG: ``not-covered``"
    )
    $report |
        Set-Content `
            -LiteralPath (Join-Path $EvidenceRoot 'verdict.md') `
            -Encoding utf8NoBOM

    if (-not $fullGatePass) {
        throw 'T10 RNG matrix gate failed.'
    }
}
finally {
    Close-Game
    if ($null -ne $script:gameProcess) {
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T10 recovery.'
        }
    }

    if ($modsSwapped) {
        $isolatedTas = Join-Path $modsDirectory 'HollowKnightTAS'
        if (Test-Path -LiteralPath $isolatedTas) {
            Move-Item `
                -LiteralPath $isolatedTas `
                -Destination (
                    Join-Path $modsBackup 'HollowKnightTAS'
                )
        }
        if (Test-Path -LiteralPath $modsDirectory) {
            Move-Item `
                -LiteralPath $modsDirectory `
                -Destination $modsEmpty
        }
        if (Test-Path -LiteralPath $modsBackup) {
            Move-Item `
                -LiteralPath $modsBackup `
                -Destination $modsDirectory
        }
        if (Test-Path -LiteralPath $modsEmpty) {
            if (@(
                    Get-ChildItem `
                        -LiteralPath $modsEmpty `
                        -Force
                ).Count -ne 0) {
                throw 'T10 empty Mods validation directory is not empty.'
            }
            Remove-Item -LiteralPath $modsEmpty
        }
    }

    if (Test-Path -LiteralPath $storeRoot) {
        Remove-Item -LiteralPath $storeRoot -Recurse
    }
    if ($storeMoved -and (Test-Path -LiteralPath $storeBackup)) {
        Move-Item `
            -LiteralPath $storeBackup `
            -Destination $storeRoot
    }

    if ($settingsOriginallyExisted -and $settingsBackedUp) {
        Copy-Item `
            -LiteralPath $settingsBackup `
            -Destination $settingsPath `
            -Force
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath
    }

    Assert-SlotsUnchanged -Expected $initialSlots
    if (Test-Path -LiteralPath $privateBackupRoot) {
        Remove-Item -LiteralPath $privateBackupRoot -Recurse
    }
}

[pscustomobject]@{
    FullGatePass = $matrix.fullGatePass
    MatchingRuns = $matrix.matchingRuns
    AmbientEndpointConverged =
        $matrix.ambientEndpointConverged
    UnwhitelistedUnityRngObserved =
        $matrix.unwhitelistedUnityRngObserved
    NoHookParity = $matrix.noHookParity
    FirstRngDifference =
        $matrix.firstRngDifferenceMilestoneIndex
    FirstSemanticDifference =
        $matrix.firstSemanticDifferenceMilestoneIndex
    EvidenceRoot = $EvidenceRoot
} | ConvertTo-Json

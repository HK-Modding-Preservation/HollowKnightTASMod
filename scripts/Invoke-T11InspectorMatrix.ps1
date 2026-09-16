[CmdletBinding()]
param(
    [ValidateRange(2, 10)]
    [int]$FunctionalRuns = 10,

    [ValidateRange(60, 7200)]
    [int]$PerformanceSeconds = 600,

    [switch]$SkipPerformance,

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(180, 1800)]
    [int]$MaxRunSeconds = 900
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T11 Inspector matrix requires PowerShell 7 or newer.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't11-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $projectRoot `
        "artifacts\inspector\$campaign"
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
    "Mods.HKTAS-T11-$swapId.backup"
$modsEmpty = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T11-$swapId.empty"
$storeBackup = Join-Path `
    $storeParent `
    "v1.HKTAS-T11-$swapId.backup"
$privateBackupRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "HKTAS-T11-$swapId"
$settingsBackup = Join-Path $privateBackupRoot 'settings.json'

$script:gameProcess = $null
$settingsOriginallyExisted = Test-Path -LiteralPath $settingsPath
$settingsBackedUp = $false
$modsSwapped = $false
$storeMoved = $false
$initialSlots = $null
$runs = [System.Collections.Generic.List[object]]::new()
$matrix = $null

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

    if (-not $script:gameProcess.HasExited) {
        Stop-Process -Id $script:gameProcess.Id
        [void]$script:gameProcess.WaitForExit(10000)
    }
}

function Write-TestSettings {
    param(
        [ValidateRange(1, 1000)]
        [int]$InspectorSampleEveryMovieTicks = 120
    )

    $settings = [ordered]@{
        VerificationModeRequested = $true
        AllowedVerificationMods = @('HollowKnightTAS')
        EventQueueCapacity = 8192
        ExitFlushTimeoutMilliseconds = 5000
        CompanionEnabled = $true
        AutoStartCompanion = $true
        ExitCompanionWithGame = $true
        CompanionOverlayEnabled = $false
        CompanionCommandQueueCapacity = 1024
        CompanionOutboundQueueCapacity = 2048
        CompanionMainThreadBudgetMilliseconds = 2.0
        CompanionHandshakeTimeoutMilliseconds = 10000
        EnableNativeCapabilities = $false
        InspectorEnabled = $true
        InspectorOverlayEnabled = $true
        InspectorExportEnabled = $true
        InspectorSampleEveryMovieTicks =
            $InspectorSampleEveryMovieTicks
        InspectorExportEverySamples = 2
        InspectorExportQueueCapacity = 4096
        ReplaySaveEnabled = $false
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

function Get-StringSha256 {
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Invoke-InspectorRun {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('FUNCTIONAL', 'PERFORMANCE')]
        [string]$Profile,

        [Parameter(Mandatory)]
        [int]$Ordinal
    )

    $sampleInterval =
        if ($Profile -eq 'FUNCTIONAL') {
            5
        }
        else {
            120
        }
    Write-TestSettings `
        -InspectorSampleEveryMovieTicks $sampleInterval

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
        "--hktas-inspector-probe=$Profile",
        "--hktas-inspector-probe-run=$runId",
        "--hktas-inspector-probe-slot=$TestSaveSlot",
        "--hktas-inspector-performance-seconds=$PerformanceSeconds",
        '--hktas-inspector-probe-exit'
    )
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $script:gameProcess = $null
    $resultPath = $null
    $sessionDirectory = $null
    $runTimeout =
        if ($Profile -eq 'PERFORMANCE') {
            [Math]::Max(
                $MaxRunSeconds,
                $PerformanceSeconds + 180
            )
        }
        else {
            $MaxRunSeconds
        }
    $deadline = (Get-Date).AddSeconds($runTimeout)
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
                    Join-Path `
                        $_.FullName `
                        "inspector\probe\$runId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $candidate = Join-Path `
                $newSession.FullName `
                "inspector\probe\$runId\result.json"
            if (Test-Path -LiteralPath $candidate) {
                $resultPath = $candidate
                $sessionDirectory = $newSession.FullName
                break
            }
        }
    }

    if ($null -eq $resultPath -or $null -eq $sessionDirectory) {
        Close-Game
        throw "T11 run timed out: $runId"
    }

    if ($null -ne $script:gameProcess) {
        [void]$script:gameProcess.WaitForExit(30000)
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            Close-Game
            throw "Game did not exit normally after T11 run: $runId"
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
            'inspector\watches.jsonl',
            'inspector\performance.json',
            'rng\whitelist-resolution.json',
            'manifest.json',
            'manifest.sha256',
            'events.jsonl'
        )) {
        $source = Join-Path $sessionDirectory $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required T11 evidence is missing: $relative"
        }
        $targetName = $relative.Replace('\', '-')
        Copy-Item `
            -LiteralPath $source `
            -Destination (Join-Path $destination $targetName)
    }

    $result = Get-Content -Raw -LiteralPath $resultPath |
        ConvertFrom-Json
    $manifest = Get-Content `
        -Raw `
        -LiteralPath (Join-Path $sessionDirectory 'manifest.json') |
        ConvertFrom-Json
    $watchPath = Join-Path `
        $sessionDirectory `
        'inspector\watches.jsonl'
    $watchLineCount = @(
        [IO.File]::ReadLines($watchPath)
    ).Count
    $stableKeyText = @(
        $result.verificationStableKeys |
            Sort-Object
    ) -join "`n"
    $stableKeySha256 = Get-StringSha256 (
        $stableKeyText + "`n"
    )

    $commonPass =
        [bool]$result.runPass `
        -and $result.providerFailureCount -eq 0 `
        -and $result.exporterDroppedCount -eq 0 `
        -and $watchLineCount -gt 0 `
        -and [string]::Equals(
            [string]$manifest.rngCoverage,
            'unity-random-partial-whitelist-v1',
            [StringComparison]::Ordinal
        )
    if ($Profile -eq 'FUNCTIONAL') {
        $profilePass =
            $commonPass `
            -and $result.directParity `
            -and $result.overlayGroupChanged `
            -and $result.overlayRendered `
            -and $result.colliderRendered `
            -and $result.exportSameSource `
            -and $result.exportPauseHeld `
            -and $result.exportResumed `
            -and $result.disableStoppedSampling `
            -and $result.disableStoppedRendering `
            -and $result.sceneClearObserved `
            -and $result.sceneRebound `
            -and $result.screenshotWritten `
            -and $result.verificationStableKeys.Count -gt 0 `
            -and $result.displayOnlyKeys.Count -gt 0
    }
    else {
        $profilePass =
            $commonPass `
            -and $result.sampleP95Milliseconds -lt 1 `
            -and $result.inspectorAttributableAverageBytesPerFrame -lt 1024 `
            -and $result.enabledAllocationFrames -gt 0 `
            -and $result.baselineAllocationFrames -gt 0
    }
    if (-not $profilePass) {
        throw "T11 runtime gate failed: $runId"
    }

    Assert-SlotsUnchanged -Expected $initialSlots
    $record = [pscustomobject]@{
        profile = $Profile
        ordinal = $Ordinal
        runId = $runId
        result = $result
        manifest = $manifest
        watchLineCount = $watchLineCount
        stableKeySha256 = $stableKeySha256
        evidence = $destination
    }
    $runs.Add($record)
    return $record
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
    Write-TestSettings -InspectorSampleEveryMovieTicks 120

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
        throw 'Failed to establish the isolated T11 Mods profile.'
    }

    foreach ($ordinal in 1..$FunctionalRuns) {
        [void](Invoke-InspectorRun `
            -Profile FUNCTIONAL `
            -Ordinal $ordinal)
    }
    $performance = $null
    if (-not $SkipPerformance) {
        $performance = Invoke-InspectorRun `
            -Profile PERFORMANCE `
            -Ordinal 1
    }

    $functional = @(
        $runs |
            Where-Object profile -eq 'FUNCTIONAL'
    )
    $stableHashes = @(
        $functional.stableKeySha256 |
            Sort-Object -Unique
    )
    $stableIdentityPass =
        $functional.Count -eq $FunctionalRuns `
        -and $stableHashes.Count -eq 1
    $functionalPass =
        @(
            $functional |
                Where-Object { -not $_.result.runPass }
        ).Count -eq 0 `
        -and $stableIdentityPass
    $performancePass =
        $SkipPerformance `
        -or (
            $null -ne $performance `
            -and [bool]$performance.result.runPass
        )
    $fullGatePass =
        $functionalPass `
        -and $performancePass `
        -and @(
            $runs |
                Where-Object { -not $_.result.runPass }
        ).Count -eq 0

    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        fullGatePass = $fullGatePass
        functionalRuns = $FunctionalRuns
        functionalPass = $functionalPass
        stableIdentityPass = $stableIdentityPass
        stableKeySha256 = $stableHashes[0]
        performanceIncluded = -not $SkipPerformance
        performanceSeconds =
            if ($null -ne $performance) {
                $performance.result.performanceSeconds
            }
            else {
                0
            }
        performancePass = $performancePass
        performanceSampleP95Milliseconds =
            if ($null -ne $performance) {
                $performance.result.sampleP95Milliseconds
            }
            else {
                0
            }
        inspectorAttributableAverageBytesPerFrame =
            if ($null -ne $performance) {
                $performance.result.inspectorAttributableAverageBytesPerFrame
            }
            else {
                0
            }
        runSummaries = @(
            $runs |
                ForEach-Object {
                    [ordered]@{
                        profile = $_.profile
                        ordinal = $_.ordinal
                        runId = $_.runId
                        runPass = $_.result.runPass
                        stableKeySha256 = $_.stableKeySha256
                        stableKeyCount =
                            $_.result.verificationStableKeys.Count
                        displayOnlyKeyCount =
                            $_.result.displayOnlyKeys.Count
                        watchLineCount = $_.watchLineCount
                        sampleP95Milliseconds =
                            $_.result.sampleP95Milliseconds
                        attributableAverageBytesPerFrame =
                            $_.result.inspectorAttributableAverageBytesPerFrame
                    }
                }
        )
        initialSlotHashes = $initialSlots
    }
    $matrix |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'inspector-matrix.json'
            ) `
            -Encoding utf8NoBOM

    $report = @(
        '# T11 Inspector Matrix'
        ''
        "- Full gate pass: ``$fullGatePass``"
        "- Functional runs: ``$FunctionalRuns/$FunctionalRuns``"
        "- Stable identity: ``$stableIdentityPass``"
        "- Stable key SHA-256: ``$($matrix.stableKeySha256)``"
        "- Performance included: ``$($matrix.performanceIncluded)``"
        "- Performance seconds: ``$($matrix.performanceSeconds)``"
        "- Sample p95 ms: ``$($matrix.performanceSampleP95Milliseconds)``"
        "- Attributable average bytes/frame: ``$($matrix.inspectorAttributableAverageBytesPerFrame)``"
    )
    $report |
        Set-Content `
            -LiteralPath (Join-Path $EvidenceRoot 'verdict.md') `
            -Encoding utf8NoBOM

    if (-not $fullGatePass) {
        throw 'T11 Inspector matrix gate failed.'
    }
}
finally {
    Close-Game
    if ($null -ne $script:gameProcess) {
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T11 recovery.'
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
                throw 'T11 empty Mods validation directory is not empty.'
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
    FunctionalRuns = $matrix.functionalRuns
    StableIdentityPass = $matrix.stableIdentityPass
    PerformanceIncluded = $matrix.performanceIncluded
    PerformancePass = $matrix.performancePass
    EvidenceRoot = $EvidenceRoot
} | ConvertTo-Json

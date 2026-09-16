[CmdletBinding()]
param(
    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(30, 180)]
    [int]$MaxLaunchSeconds = 100,

    [switch]$SkipOfflineTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T14 keyframe matrix requires PowerShell 7 or newer.'
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$ManagedDirectory = [IO.Path]::GetFullPath($ManagedDirectory)
$PersistentDataDirectory =
    [IO.Path]::GetFullPath($PersistentDataDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$installRoot = Join-Path $modsDirectory 'HollowKnightTAS'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$storeRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$storeParent = Split-Path -Parent $storeRoot
$persistentPrefix = (
    $PersistentDataDirectory +
    [IO.Path]::DirectorySeparatorChar
)

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't14-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString(
            'yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $repoRoot `
        "artifacts\keyframes\$campaign"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

foreach ($path in @($SteamExecutable, $settingsPath)) {
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
if (Get-Process `
        -Name hollow_knight,HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight, Companion, and NativeHost must be stopped.'
}

$resolvedManaged =
    (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods = (Resolve-Path -LiteralPath $modsDirectory).Path
if (-not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved Mods directory is outside Managed.'
}
if (-not $storeRoot.StartsWith(
        $persistentPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Replay store is outside persistent data.'
}

New-Item -ItemType Directory -Path $EvidenceRoot |
    Out-Null

$settingsOriginal = [IO.File]::ReadAllBytes($settingsPath)
$settingsOriginalSha256 = (
    Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256
).Hash
$slotInitial = $null
$modsInitial = $null
$script:game = $null
$script:runCompanionPids = @()
$swapId = [Guid]::NewGuid().ToString('N')
$storeBackup = Join-Path `
    $storeParent `
    "v1.HKTAS-T14-$swapId.backup"
$storeOriginallyExisted =
    Test-Path -LiteralPath $storeRoot -PathType Container
$storeMoved = $false
$runs = [System.Collections.Generic.List[object]]::new()
$result = $null

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

function Get-SlotState {
    $values = [System.Collections.Generic.List[string]]::new()
    foreach ($slot in 1..4) {
        foreach ($suffix in @('.dat', '.modded.json')) {
            $values.Add(
                (
                    '{0}|{1}|{2}' -f `
                        $slot, `
                        $suffix, `
                        (Get-FileState (
                            Join-Path `
                                $PersistentDataDirectory `
                                "user$slot$suffix"
                        ))
                ))
        }
    }
    return @($values)
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
        throw "$Label changed during T14."
    }
}

function Write-TestSettings {
    param([Parameter(Mandatory)][bool]$Enabled)

    $settings =
        Get-Content -LiteralPath $settingsPath -Raw |
            ConvertFrom-Json -AsHashtable -Depth 50
    $settings['VerificationModeRequested'] = $false
    $settings['CompanionEnabled'] = $true
    $settings['AutoStartCompanion'] = $true
    $settings['ExitCompanionWithGame'] = $true
    $settings['CompanionOverlayEnabled'] = $false
    $settings['EnableNativeCapabilities'] = $false
    $settings['EnableSemanticKeyframes'] = $Enabled
    $settings['InspectorOverlayEnabled'] = $false
    $settings['ReplaySaveEnabled'] = $true
    $settings['ReplaySaveAutoEnabled'] = $false
    $settings |
        ConvertTo-Json -Depth 50 |
        Set-Content `
            -LiteralPath $settingsPath `
            -Encoding utf8NoBOM
}

function Get-Events {
    param([Parameter(Mandatory)][string]$SessionDirectory)

    $path = Join-Path $SessionDirectory 'events.jsonl'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return @()
    }
    $events = [System.Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content `
            -LiteralPath $path `
            -ErrorAction SilentlyContinue) {
        try {
            $events.Add(
                ($line | ConvertFrom-Json -ErrorAction Stop))
        }
        catch {
            # The live writer may expose one incomplete final line.
        }
    }
    return @($events)
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
            Stop-Process -Id $script:game.Id
            [void]$script:game.WaitForExit(10000)
        }
        $script:game = $null
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $remaining = @(
            Get-Process `
                -Name HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
                -ErrorAction SilentlyContinue |
                Where-Object {
                    $script:runCompanionPids -contains $_.Id
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
    $script:runCompanionPids = @()
}

function Invoke-RuntimeCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Enabled
    )

    Write-TestSettings -Enabled $Enabled
    $beforeSessions = @(
        Get-ChildItem -LiteralPath $sessionRoot -Directory |
            ForEach-Object FullName
    )
    $beforeCompanions = @(
        Get-Process `
            -Name HollowKnightTAS.Companion `
            -ErrorAction SilentlyContinue |
            ForEach-Object Id
    )
    $launchAt = [DateTimeOffset]::Now
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList @('-applaunch', '367520') |
        Out-Null

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($MaxLaunchSeconds)
    do {
        $candidate = Get-Process `
                -Name hollow_knight `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $_.StartTime -ge $launchAt.LocalDateTime.AddSeconds(-2)
            } |
            Sort-Object StartTime -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            $script:game = $candidate
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($null -eq $script:game) {
        throw "Game did not launch for case $Name."
    }

    $session = $null
    $tierEvent = $null
    do {
        $session = Get-ChildItem `
                -LiteralPath $sessionRoot `
                -Directory |
            Where-Object {
                $beforeSessions -notcontains $_.FullName
            } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $session) {
            $tierEvent = Get-Events $session.FullName |
                Where-Object {
                    $_.eventType -eq 'semantic-keyframe-tier'
                } |
                Select-Object -Last 1
            if ($null -ne $tierEvent) {
                break
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($null -eq $tierEvent -or $null -eq $session) {
        throw "No semantic-keyframe-tier event for case $Name."
    }

    $script:runCompanionPids = @(
        Get-Process `
            -Name HollowKnightTAS.Companion `
            -ErrorAction SilentlyContinue |
            Where-Object {
                $beforeCompanions -notcontains $_.Id
            } |
            ForEach-Object Id
    )

    $expectedEnabled = $Enabled.ToString().ToLowerInvariant()
    $expectedRegistered = $expectedEnabled
    if ($tierEvent.fields.enabled -ne $expectedEnabled `
            -or $tierEvent.fields.tier -ne 'ReplayOnly' `
            -or $tierEvent.fields.acceleratorRegistered `
                -ne $expectedRegistered `
            -or $tierEvent.fields.captureAllowed -ne 'false' `
            -or $tierEvent.fields.restorePlan -ne 'FullReplay') {
        throw "Unexpected tier event for case $Name."
    }
    if ($Enabled) {
        foreach ($reason in @(
                'no-verified-room-entry-gates',
                'rng-coverage-not-complete'
            )) {
            if ($tierEvent.fields.reasonCodes -notmatch `
                    "(^|,)$([Regex]::Escape($reason))(,|$)") {
                throw "Missing reason $reason for case $Name."
            }
        }
    }
    else {
        if ($tierEvent.fields.reasonCodes -ne 'disabled-by-setting') {
            throw "Disabled case did not report disabled-by-setting."
        }
    }

    if (Test-Path `
            -LiteralPath (Join-Path $storeRoot 'keyframes')) {
        throw "Case $Name created keyframe artifacts in ReplayOnly."
    }

    $caseDirectory = Join-Path $EvidenceRoot $Name
    New-Item -ItemType Directory -Path $caseDirectory |
        Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $session.FullName 'events.jsonl') `
        -Destination (Join-Path $caseDirectory 'events.jsonl')
    foreach ($leaf in @('manifest.json', 'manifest.sha256')) {
        $source = Join-Path $session.FullName $leaf
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item `
                -LiteralPath $source `
                -Destination (Join-Path $caseDirectory $leaf)
        }
    }
    $runs.Add(
        [pscustomobject]@{
            name = $Name
            enabled = $Enabled
            sessionId = $session.Name
            tier = $tierEvent.fields.tier
            acceleratorRegistered =
                $tierEvent.fields.acceleratorRegistered
            captureAllowed = $tierEvent.fields.captureAllowed
            reasonCodes = $tierEvent.fields.reasonCodes
            restorePlan = $tierEvent.fields.restorePlan
            keyframeDirectoryCreated = $false
        })
    Close-RunProcesses
}

try {
    $slotInitial = Get-SlotState
    $modsInitial = Get-ModTreeState

    if ($storeOriginallyExisted) {
        if (Test-Path -LiteralPath $storeBackup) {
            throw "Store backup path already exists: $storeBackup"
        }
        Move-Item `
            -LiteralPath $storeRoot `
            -Destination $storeBackup
        $storeMoved = $true
    }

    foreach ($schema in @(
            'schemas\semantic-keyframe-v1.schema.json',
            'schemas\keyframe-adapter-manifest-v1.schema.json'
        )) {
        Get-Content `
                -LiteralPath (Join-Path $repoRoot $schema) `
                -Raw |
            ConvertFrom-Json -Depth 100 |
            Out-Null
    }

    if (-not $SkipOfflineTests) {
        $testLog = Join-Path $EvidenceRoot 'offline-tests.txt'
        & dotnet test `
            (Join-Path `
                $repoRoot `
                'tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj') `
            -c Debug `
            --no-restore `
            --filter 'FullyQualifiedName~Keyframes' `
            --logger 'console;verbosity=normal' *>&1 |
            Tee-Object -LiteralPath $testLog
        if ($LASTEXITCODE -ne 0) {
            throw "Offline keyframe tests failed with exit code $LASTEXITCODE."
        }
    }

    Invoke-RuntimeCase -Name 'disabled' -Enabled $false
    Invoke-RuntimeCase -Name 'enabled-replay-only' -Enabled $true

    Assert-SequenceEqual `
        -Expected $slotInitial `
        -Actual (Get-SlotState) `
        -Label 'User save slots'
    Assert-SequenceEqual `
        -Expected $modsInitial `
        -Actual (Get-ModTreeState) `
        -Label 'Mods tree'

    $result = [ordered]@{
        schemaVersion = 1
        campaign = Split-Path -Leaf $EvidenceRoot
        verdict = 'DOWNGRADED_REPLAY_ONLY_VERIFIED'
        offlineTests = -not $SkipOfflineTests
        runtimeCases = @($runs)
        t09TruthSource = $true
        roomEntryGateCount = 0
        keyframesCaptured = 0
        cleanFallback = 'FullReplay'
        ordinarySlotsUnchanged = $true
        modsUnchanged = $true
    }
    $result |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath (Join-Path $EvidenceRoot 'matrix.json') `
            -Encoding utf8NoBOM
}
finally {
    Close-RunProcesses
    [IO.File]::WriteAllBytes(
        $settingsPath,
        $settingsOriginal)

    if (Test-Path -LiteralPath $storeRoot) {
        $resolvedStore =
            [IO.Path]::GetFullPath($storeRoot)
        if (-not $resolvedStore.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a replay store outside persistent data.'
        }
        Remove-Item `
            -LiteralPath $resolvedStore `
            -Recurse `
            -Force
    }
    if ($storeMoved) {
        Move-Item `
            -LiteralPath $storeBackup `
            -Destination $storeRoot
        $storeMoved = $false
    }
}

$settingsFinalSha256 = (
    Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256
).Hash
if (-not [string]::Equals(
        $settingsOriginalSha256,
        $settingsFinalSha256,
        [StringComparison]::Ordinal)) {
    throw 'Settings were not restored byte-for-byte.'
}
Assert-SequenceEqual `
    -Expected $slotInitial `
    -Actual (Get-SlotState) `
    -Label 'User save slots after cleanup'
Assert-SequenceEqual `
    -Expected $modsInitial `
    -Actual (Get-ModTreeState) `
    -Label 'Mods tree after cleanup'
if ($storeOriginallyExisted -ne (
        Test-Path -LiteralPath $storeRoot -PathType Container
    )) {
    throw 'Replay-store existence was not restored.'
}
if (Get-Process `
        -Name hollow_knight,HollowKnightTAS.Companion,HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue) {
    throw 'A T14-owned process remained after cleanup.'
}

$result['settingsRestoredSha256'] =
    $settingsFinalSha256.ToLowerInvariant()
$result['replayStoreRestored'] = $true
$result |
    ConvertTo-Json -Depth 20 |
    Set-Content `
        -LiteralPath (Join-Path $EvidenceRoot 'matrix.json') `
        -Encoding utf8NoBOM

Write-Host (
    'T14 keyframe matrix passed: {0}' -f $EvidenceRoot
)

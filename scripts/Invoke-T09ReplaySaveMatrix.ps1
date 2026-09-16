[CmdletBinding()]
param(
    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [ValidateRange(1, 4)]
    [int]$DedicatedTasSlot = 4,

    [ValidateRange(1, 100)]
    [int]$RestoreAttempts = 10,

    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(120, 900)]
    [int]$MaxCaptureSeconds = 420,

    [ValidateRange(300, 3600)]
    [int]$MaxRestoreSeconds = 1500,

    [switch]$CaptureOnly,

    [switch]$AllowDedicatedSlotOverwrite
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T09 matrix requires PowerShell 7 or newer.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$campaignId = 't09-{0}-{1}' -f `
    [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
    [Guid]::NewGuid().ToString('N').Substring(0, 10)
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path `
        $projectRoot `
        "artifacts\replay-save\$campaignId"
}

$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$PersistentDataDirectory =
    [IO.Path]::GetFullPath($PersistentDataDirectory)
$ManagedDirectory = [IO.Path]::GetFullPath($ManagedDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$tasModDirectory = Join-Path $modsDirectory 'HollowKnightTAS'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$storeRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$storeParent = Split-Path -Parent $storeRoot
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$steamConsoleLog = Join-Path `
    (Split-Path -Parent $SteamExecutable) `
    'logs\console_log.txt'

$swapId = [Guid]::NewGuid().ToString('N')
$modsBackup = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T09-$swapId.backup"
$modsEmpty = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T09-$swapId.empty"
$storeBackup = Join-Path `
    $storeParent `
    "v1.HKTAS-T09-$swapId.backup"
$privateBackupRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "HKTAS-T09-$swapId"
$settingsBackup = Join-Path $privateBackupRoot 'settings.json'
$slotBackup = Join-Path $privateBackupRoot 'dedicated-slot'

$script:gameProcess = $null
$modsSwapped = $false
$storeMoved = $false
$settingsBackedUp = $false
$settingsOriginallyExisted = $false
$dedicatedBackedUp = $false
$initialSlotState = $null
$summaries = [System.Collections.Generic.List[object]]::new()

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

    return [pscustomobject]$result
}

function Test-FileStateEqual {
    param(
        [Parameter(Mandatory)]$Left,
        [Parameter(Mandatory)]$Right
    )

    return $Left.exists -eq $Right.exists `
        -and $Left.length -eq $Right.length `
        -and [string]::Equals(
            [string]$Left.sha256,
            [string]$Right.sha256,
            [StringComparison]::Ordinal
        )
}

function Assert-SlotsUnchanged {
    param(
        [Parameter(Mandatory)]$Expected,
        [int[]]$Slots = @(1, 2, 3, 4)
    )

    $actual = Get-AllSlotState
    foreach ($slot in $Slots) {
        $name = "slot$slot"
        if (
            -not (Test-FileStateEqual `
                -Left $Expected.$name.save `
                -Right $actual.$name.save) `
                -or -not (Test-FileStateEqual `
                    -Left $Expected.$name.modded `
                    -Right $actual.$name.modded)
        ) {
            throw "User save slot $slot changed during T09 verification."
        }
    }
}

function Close-GameNormally {
    $running = Get-Process `
        -Name 'hollow_knight' `
        -ErrorAction SilentlyContinue
    foreach ($process in @($running)) {
        try {
            if (-not $process.HasExited) {
                $requested = $process.CloseMainWindow()
                if ($requested) {
                    [void]$process.WaitForExit(20000)
                }
            }
        }
        catch {
            # The final recovery gate performs the authoritative process check.
        }
    }
}

function Get-SyncFailureCount {
    if (-not (Test-Path -LiteralPath $steamConsoleLog -PathType Leaf)) {
        return 0
    }

    return @(
        Select-String `
            -LiteralPath $steamConsoleLog `
            -SimpleMatch `
            'SynchronizingCloud "syncfailed"'
    ).Count
}

function Copy-ProbeEvidence {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$SessionDirectory,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ProcessInstanceId
    )

    if (Test-Path -LiteralPath $Destination) {
        throw "Evidence destination already exists: $Destination"
    }

    Copy-Item `
        -LiteralPath $SourceDirectory `
        -Destination $Destination `
        -Recurse
    foreach ($name in @('manifest.json', 'manifest.sha256')) {
        Copy-Item `
            -LiteralPath (Join-Path $SessionDirectory $name) `
            -Destination $Destination
    }

    $resultPath = Join-Path $Destination 'result.json'
    $result = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    $result |
        Add-Member `
            -NotePropertyName sessionId `
            -NotePropertyValue (Split-Path -Leaf $SessionDirectory)
    $result |
        Add-Member `
            -NotePropertyName processInstanceId `
            -NotePropertyValue $ProcessInstanceId
    $result |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath $resultPath `
            -Encoding utf8NoBOM
    return $result
}

function Invoke-ReplaySaveProbe {
    param(
        [Parameter(Mandatory)][string]$Profile,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string[]]$AdditionalArguments,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $beforeSessions = @(
        Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            ForEach-Object FullName
    )
    $syncFailureCountBefore = Get-SyncFailureCount
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
        "--hktas-replay-save-probe=$Profile",
        "--hktas-replay-save-probe-run=$RunId",
        '--hktas-replay-save-probe-exit'
    ) + $AdditionalArguments

    $launchAttempts = 1
    $lastLaunchAttempt = Get-Date
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $script:gameProcess = $null
    $processInstanceId = ''
    $sourceDirectory = $null
    $sessionDirectory = $null
    $resultPath = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if (
            $null -eq $script:gameProcess `
                -and (Get-SyncFailureCount) -gt $syncFailureCountBefore
        ) {
            throw 'Steam blocked Hollow Knight launch on a cloud-sync warning.'
        }

        if ($null -eq $script:gameProcess) {
            $script:gameProcess = Get-Process |
                Where-Object {
                    $_.ProcessName -eq 'hollow_knight' `
                        -and $_.StartTime -ge $launchTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if ($null -ne $script:gameProcess) {
                $processInstanceId = '{0}-{1}' -f `
                    $script:gameProcess.Id, `
                    $script:gameProcess.StartTime.ToUniversalTime().Ticks
            }
            elseif (
                $launchAttempts -lt 5 `
                    -and (
                        ((Get-Date) - $lastLaunchAttempt).TotalSeconds `
                            -ge 20
                    )
            ) {
                Start-Process `
                    -FilePath $SteamExecutable `
                    -ArgumentList $arguments `
                    -WindowStyle Hidden
                $launchAttempts++
                $lastLaunchAttempt = Get-Date
            }
        }

        $newSession = Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            Where-Object { $beforeSessions -notcontains $_.FullName } |
            Sort-Object LastWriteTime -Descending |
            Where-Object {
                Test-Path -LiteralPath (
                    Join-Path $_.FullName "replay-save\$RunId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $sessionDirectory = $newSession.FullName
            $sourceDirectory = Join-Path `
                $sessionDirectory `
                "replay-save\$RunId"
            $candidate = Join-Path $sourceDirectory 'result.json'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $resultPath = $candidate
                break
            }
        }
    }

    if ($null -eq $resultPath) {
        Close-GameNormally
        throw "T09 $Profile probe timed out: $RunId"
    }

    if ($null -ne $script:gameProcess) {
        [void]$script:gameProcess.WaitForExit(30000)
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            Close-GameNormally
            throw "Game did not exit normally after T09 run: $RunId"
        }
    }

    $script:gameProcess = $null
    Start-Sleep -Seconds 1
    $result = Copy-ProbeEvidence `
        -SourceDirectory $sourceDirectory `
        -SessionDirectory $sessionDirectory `
        -Destination $Destination `
        -ProcessInstanceId $processInstanceId
    return [pscustomobject]@{
        result = $result
        sessionDirectory = $sessionDirectory
        evidenceDirectory = $Destination
    }
}

function Copy-StoreMetadata {
    param([Parameter(Mandatory)][string]$Destination)

    if (Test-Path -LiteralPath $Destination) {
        throw "Store metadata destination already exists: $Destination"
    }

    New-Item -ItemType Directory -Path $Destination | Out-Null
    $catalogPath = Join-Path $storeRoot 'catalog.json'
    if (Test-Path -LiteralPath $catalogPath -PathType Leaf) {
        Copy-Item -LiteralPath $catalogPath -Destination $Destination
    }

    $entriesSource = Join-Path $storeRoot 'entries'
    $entriesDestination = Join-Path $Destination 'entries'
    New-Item -ItemType Directory -Path $entriesDestination | Out-Null
    foreach ($entry in @(
        Get-ChildItem `
            -LiteralPath $entriesSource `
            -File `
            -Filter '*.json' `
            -ErrorAction SilentlyContinue
    )) {
        Copy-Item `
            -LiteralPath $entry.FullName `
            -Destination $entriesDestination
    }

    $inventory = [System.Collections.Generic.List[object]]::new()
    $objectsRoot = Join-Path $storeRoot 'objects\sha256'
    foreach ($object in @(
        Get-ChildItem `
            -LiteralPath $objectsRoot `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue
    )) {
        $actual = (
            Get-FileHash `
                -LiteralPath $object.FullName `
                -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        if (
            -not [string]::Equals(
                $actual,
                $object.Name,
                [StringComparison]::Ordinal
            )
        ) {
            throw "Content-addressed object failed verification: $($object.Name)"
        }

        $inventory.Add([pscustomobject]@{
            sha256 = $actual
            length = $object.Length
        })
    }

    @($inventory) |
        Sort-Object sha256 |
        ConvertTo-Json -Depth 5 |
        Set-Content `
            -LiteralPath (
                Join-Path $Destination 'object-inventory.json'
            ) `
            -Encoding utf8NoBOM
}

function Backup-DedicatedSlot {
    New-Item -ItemType Directory -Path $slotBackup | Out-Null
    foreach ($name in @(
        "user$DedicatedTasSlot.dat",
        "user$DedicatedTasSlot.modded.json"
    )) {
        $source = Join-Path $PersistentDataDirectory $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item `
                -LiteralPath $source `
                -Destination (Join-Path $slotBackup $name)
        }
    }

    $script:dedicatedBackedUp = $true
}

function Restore-DedicatedSlot {
    if (-not $script:dedicatedBackedUp) {
        return
    }

    $initial = $script:initialSlotState."slot$DedicatedTasSlot"
    foreach ($kind in @(
        [pscustomobject]@{
            name = "user$DedicatedTasSlot.dat"
            state = $initial.save
        },
        [pscustomobject]@{
            name = "user$DedicatedTasSlot.modded.json"
            state = $initial.modded
        }
    )) {
        $destination = Join-Path `
            $PersistentDataDirectory `
            $kind.name
        $backup = Join-Path $slotBackup $kind.name
        $current = Get-FileState $destination
        if (Test-FileStateEqual -Left $kind.state -Right $current) {
            continue
        }

        if ($kind.state.exists) {
            if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
                throw "Dedicated-slot backup is missing: $backup"
            }

            Copy-Item `
                -LiteralPath $backup `
                -Destination $destination `
                -Force
        }
        elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
            Remove-Item -LiteralPath $destination
        }
    }
}

function Write-TestSettings {
    $script:settingsOriginallyExisted =
        Test-Path -LiteralPath $settingsPath -PathType Leaf
    if ($script:settingsOriginallyExisted) {
        Copy-Item `
            -LiteralPath $settingsPath `
            -Destination $settingsBackup
    }

    $settings = if ($script:settingsOriginallyExisted) {
        Get-Content -LiteralPath $settingsPath -Raw |
            ConvertFrom-Json
    }
    else {
        [pscustomobject]@{}
    }
    $values = [ordered]@{
        VerificationModeRequested = $false
        ReplaySaveEnabled = $true
        ReplaySaveAutoEnabled = $false
        ReplaySaveAutoIntervalMovieTicks = 18000
        ReplaySaveAutoRetentionCount = 20
        DedicatedTasSaveSlot = $DedicatedTasSlot
        ReplaySaveOverlayEnabled = $false
        ReplaySaveDeterministicTimingEnabled = $true
    }
    foreach ($entry in $values.GetEnumerator()) {
        $settings |
            Add-Member `
                -NotePropertyName $entry.Key `
                -NotePropertyValue $entry.Value `
                -Force
    }

    $settings |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath $settingsPath `
            -Encoding utf8NoBOM
    $script:settingsBackedUp = $true
}

function Restore-TestSettings {
    if (-not $script:settingsBackedUp) {
        return
    }

    if ($script:settingsOriginallyExisted) {
        Copy-Item `
            -LiteralPath $settingsBackup `
            -Destination $settingsPath `
            -Force
    }
    elseif (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        Remove-Item -LiteralPath $settingsPath
    }
}

function Remove-ValidatedDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedFullPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (
        -not [string]::Equals(
            $resolved,
            [IO.Path]::GetFullPath($ExpectedFullPath),
            [StringComparison]::OrdinalIgnoreCase
        )
    ) {
        throw "Refusing recursive removal of unexpected path: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse
}

function Assert-EvidencePrivacy {
    $forbidden = @(
        Get-ChildItem `
            -LiteralPath $EvidenceRoot `
            -File `
            -Recurse |
            Where-Object {
                $_.Name -match '^user[1-4]\.dat$' `
                    -or $_.Name -match '^user[1-4]\.modded\.json$' `
                    -or $_.Name -eq 'original.dat' `
                    -or $_.Name -eq 'original.modded.json'
            }
    )
    if ($forbidden.Count -ne 0) {
        throw 'Raw save bytes were detected in T09 repository evidence.'
    }
}

if (-not (Test-Path -LiteralPath $SteamExecutable -PathType Leaf)) {
    throw "Steam executable not found: $SteamExecutable"
}
if (-not (Test-Path -LiteralPath $tasModDirectory -PathType Container)) {
    throw 'Installed HollowKnightTAS directory was not found.'
}
if (Get-Process -Name 'hollow_knight' -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight is already running.'
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    if (@(Get-ChildItem -LiteralPath $EvidenceRoot -Force).Count -ne 0) {
        throw "Evidence root must be absent or empty: $EvidenceRoot"
    }
}
else {
    New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
}
if (Test-Path -LiteralPath $modsBackup) {
    throw "Unexpected Mods backup path already exists: $modsBackup"
}
if (Test-Path -LiteralPath $storeBackup) {
    throw "Unexpected replay-store backup path already exists: $storeBackup"
}

$resolvedManaged = (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods = (Resolve-Path -LiteralPath $modsDirectory).Path
if (
    -not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase
    )
) {
    throw 'Resolved Mods directory is outside the intended Managed directory.'
}

$initialSlotState = Get-AllSlotState
$dedicatedState = $initialSlotState."slot$DedicatedTasSlot"
if (
    -not $CaptureOnly `
        -and $TestSaveSlot -ne $DedicatedTasSlot `
        -and (
            $dedicatedState.save.exists `
                -or $dedicatedState.modded.exists
        ) `
        -and -not $AllowDedicatedSlotOverwrite
) {
    throw (
        "Dedicated TAS slot $DedicatedTasSlot contains user data. " +
        'Re-run only after explicit approval with ' +
        '-AllowDedicatedSlotOverwrite.'
    )
}

New-Item -ItemType Directory -Path $privateBackupRoot | Out-Null
try {
    Write-TestSettings
    Backup-DedicatedSlot

    if (Test-Path -LiteralPath $storeRoot -PathType Container) {
        New-Item `
            -ItemType Directory `
            -Path $storeParent `
            -Force |
            Out-Null
        Move-Item `
            -LiteralPath $storeRoot `
            -Destination $storeBackup
        $storeMoved = $true
    }

    Move-Item `
        -LiteralPath $modsDirectory `
        -Destination $modsBackup
    New-Item -ItemType Directory -Path $modsDirectory | Out-Null
    Move-Item `
        -LiteralPath (
            Join-Path $modsBackup 'HollowKnightTAS'
        ) `
        -Destination (
            Join-Path $modsDirectory 'HollowKnightTAS'
        )
    $modsSwapped = $true

    $isolatedEntries = @(Get-ChildItem -LiteralPath $modsDirectory)
    if (
        $isolatedEntries.Count -ne 1 `
            -or $isolatedEntries[0].Name -ne 'HollowKnightTAS'
    ) {
        throw 'Failed to establish the isolated T09 Mods profile.'
    }

    $captureRunId = 'capture-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('HHmmssfff'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $capture = Invoke-ReplaySaveProbe `
        -Profile 'CAPTURE' `
        -RunId $captureRunId `
        -AdditionalArguments @(
            "--hktas-replay-save-probe-slot=$TestSaveSlot"
        ) `
        -Destination (Join-Path $EvidenceRoot 'capture') `
        -TimeoutSeconds $MaxCaptureSeconds
    if (-not $capture.result.runPass) {
        throw "T09 capture probe failed: $($capture.result.error)"
    }
    if (
        -not $capture.result.slotSafetyPass `
            -or $capture.result.manualCount -ne 5 `
            -or $capture.result.automaticCount -ne 3
    ) {
        throw 'T09 capture result did not satisfy slot, manual, or auto gates.'
    }

    Assert-SlotsUnchanged -Expected $initialSlotState
    Copy-StoreMetadata `
        -Destination (Join-Path $EvidenceRoot 'store-metadata')

    $captureEntries = @($capture.result.entries)
    if ($captureEntries.Count -ne 8) {
        throw 'T09 capture did not publish exactly eight retained entries.'
    }

    $restoreDenialApplicable =
        $TestSaveSlot -ne $DedicatedTasSlot `
        -and (
            $dedicatedState.save.exists `
                -or $dedicatedState.modded.exists
        )
    $restoreDenialPass = -not $restoreDenialApplicable
    $restoreDenialSummary = [ordered]@{
        applicable = $restoreDenialApplicable
        pass = $restoreDenialPass
        replaySaveId = ''
        overwriteApprovalObserved = $false
        phase = if ($restoreDenialApplicable) { '' } else { 'NOT_APPLICABLE' }
        status = if ($restoreDenialApplicable) { '' } else { 'NOT_APPLICABLE' }
        processInstanceId = ''
        sessionId = ''
    }
    if ($restoreDenialApplicable) {
        $denialEntry = @(
            $captureEntries |
                Sort-Object `
                    @{ Expression = 'effectiveMovieTick'; Ascending = $true }, `
                    @{ Expression = 'id'; Ascending = $true }
        )[0]
        $denialRunId = 'restore-deny-{0}' -f `
            [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $denial = Invoke-ReplaySaveProbe `
            -Profile 'RESTORE_DENY' `
            -RunId $denialRunId `
            -AdditionalArguments @(
                "--hktas-replay-save-id=$($denialEntry.id)"
            ) `
            -Destination (Join-Path $EvidenceRoot 'restore-denied') `
            -TimeoutSeconds $MaxRestoreSeconds
        $restoreDenialPass =
            $denial.result.runPass `
            -and $denial.result.overwriteApprovalObserved `
            -and [string]::Equals(
                [string]$denial.result.phase,
                'Cancelled',
                [StringComparison]::Ordinal
            ) `
            -and [string]::Equals(
                [string]$denial.result.status,
                'Cancelled',
                [StringComparison]::Ordinal
            )
        $restoreDenialSummary = [ordered]@{
            applicable = $true
            pass = $restoreDenialPass
            replaySaveId = $denialEntry.id
            overwriteApprovalObserved =
                [bool]$denial.result.overwriteApprovalObserved
            phase = $denial.result.phase
            status = $denial.result.status
            processInstanceId = $denial.result.processInstanceId
            sessionId = $denial.result.sessionId
        }
        Assert-SlotsUnchanged -Expected $initialSlotState
        if (-not $restoreDenialPass) {
            throw 'T09 runtime overwrite-denial gate failed.'
        }
    }

    $restorePass = $true
    $totalAttempts = 0
    $strictRawExactAttempts = 0
    if (-not $CaptureOnly) {
        $sorted = @(
            $captureEntries |
                Sort-Object `
                    @{ Expression = 'effectiveMovieTick'; Ascending = $true }, `
                    @{ Expression = 'id'; Ascending = $true }
        )
        $indices = @(7, 0, 4, 2, 6, 1, 5, 3)
        $ordered = foreach ($index in $indices) {
            $sorted[$index]
        }

        $restoreRoot = Join-Path $EvidenceRoot 'restore-runs'
        New-Item -ItemType Directory -Path $restoreRoot | Out-Null
        for ($index = 0; $index -lt $ordered.Count; $index++) {
            $entry = $ordered[$index]
            $ordinal = ($index + 1).ToString('D2')
            $runId = 'restore-{0}-{1}' -f `
                $ordinal, `
                [Guid]::NewGuid().ToString('N').Substring(0, 8)
            $restoreArguments = @(
                "--hktas-replay-save-id=$($entry.id)",
                "--hktas-replay-save-attempts=$RestoreAttempts"
            )
            if ($AllowDedicatedSlotOverwrite) {
                $restoreArguments +=
                    '--hktas-replay-save-approve-overwrite'
            }
            $run = Invoke-ReplaySaveProbe `
                -Profile 'RESTORE' `
                -RunId $runId `
                -AdditionalArguments $restoreArguments `
                -Destination (
                    Join-Path $restoreRoot "restore-$ordinal"
                ) `
                -TimeoutSeconds $MaxRestoreSeconds

            $attempts = @($run.result.attempts)
            $attemptPass =
                $run.result.runPass `
                -and $run.result.attemptCount -eq $RestoreAttempts `
                -and $attempts.Count -eq $RestoreAttempts `
                -and @(
                    $attempts |
                        Where-Object {
                            $_.strictSemanticEquivalent -ne $true `
                                -or [string]::IsNullOrEmpty(
                                    $_.expectedSemanticSha256
                                ) `
                                -or -not [string]::Equals(
                                    $_.expectedSemanticSha256,
                                    $_.actualSemanticSha256,
                                    [StringComparison]::Ordinal
                                ) `
                                -or -not [string]::Equals(
                                $_.semanticProjectionId,
                                'v1-float32-decimal-4',
                                [StringComparison]::Ordinal
                            ) `
                                -or [string]::IsNullOrEmpty(
                                    $_.expectedVerificationSha256
                                ) `
                                -or -not [string]::Equals(
                                $_.expectedVerificationSha256,
                                $_.actualVerificationSha256,
                                [StringComparison]::Ordinal
                            ) `
                                -or -not $_.bindingRestoreEquivalent `
                                -or -not $_.settingsRestoreEquivalent `
                                -or $_.nextMovieTick -ne (
                                    $_.targetMovieTick + 1
                                )
                        }
                ).Count -eq 0
            $restorePass = $restorePass -and $attemptPass
            $totalAttempts += $attempts.Count
            $strictCount = @(
                $attempts |
                    Where-Object { $_.strictSemanticEquivalent -eq $true }
            ).Count
            $strictRawExactAttempts += $strictCount
            $summaries.Add([pscustomobject]@{
                order = $index + 1
                replaySaveId = $entry.id
                label = $entry.label
                reason = $entry.reason
                effectiveMovieTick = $entry.effectiveMovieTick
                attemptCount = $attempts.Count
                strictRawExactCount = $strictCount
                pass = $attemptPass
                processInstanceId = $run.result.processInstanceId
                sessionId = $run.result.sessionId
            })
            if (-not $attemptPass) {
                throw "T09 restore gate failed for $($entry.id)."
            }

            Restore-DedicatedSlot
            Assert-SlotsUnchanged -Expected $initialSlotState
        }
    }

    $capturePass = [bool]$capture.result.runPass
    $expectedAttempts = if ($CaptureOnly) {
        0
    }
    else {
        8 * $RestoreAttempts
    }
    $fullGatePass =
        -not $CaptureOnly `
        -and $capturePass `
        -and $restoreDenialPass `
        -and $restorePass `
        -and $totalAttempts -eq $expectedAttempts `
        -and $strictRawExactAttempts -eq $expectedAttempts
    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        campaignId = $campaignId
        mode = if ($CaptureOnly) {
            'CAPTURE_ONLY'
        }
        else {
            'FULL'
        }
        capturePass = $capturePass
        fullGatePass = $fullGatePass
        sourceSlot = $TestSaveSlot
        dedicatedTasSlot = $DedicatedTasSlot
        dedicatedOverwriteApproved =
            [bool]$AllowDedicatedSlotOverwrite
        manualCount = $capture.result.manualCount
        automaticCount = $capture.result.automaticCount
        pauseMovieTickStable = (
            $capture.result.pauseJournalTickBefore `
                -eq $capture.result.pauseJournalTickAfter
        )
        pauseAutomaticCountStable = (
            $capture.result.pauseAutoCountBefore `
                -eq $capture.result.pauseAutoCountAfter
        )
        slotSafetyPass = $capture.result.slotSafetyPass
        stationarySemanticPass =
            [bool]$capture.result.stationarySemanticPass
        jumpSemanticPass = [bool]$capture.result.jumpSemanticPass
        dashSemanticPass = [bool]$capture.result.dashSemanticPass
        combatSemanticPass = [bool]$capture.result.combatSemanticPass
        heroControlRestoreEquivalent =
            [bool]$capture.result.heroControlRestoreEquivalent
        restoreDenial = $restoreDenialSummary
        restoreAttemptsPerEntry = $RestoreAttempts
        expectedRestoreAttemptCount = $expectedAttempts
        actualRestoreAttemptCount = $totalAttempts
        strictRawExactAttemptCount = $strictRawExactAttempts
        restoreRuns = @($summaries)
        initialSlotHashes = $initialSlotState
    }
    $matrixPath = Join-Path $EvidenceRoot 'replay-save-matrix.json'
    $matrix |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath $matrixPath `
            -Encoding utf8NoBOM

    $report = [System.Collections.Generic.List[string]]::new()
    $report.Add('# T09 Persistent Replay Save Matrix')
    $report.Add('')
    $report.Add("- Mode: ``$($matrix.mode)``")
    $report.Add("- Capture pass: ``$capturePass``")
    $report.Add("- Full gate pass: ``$fullGatePass``")
    $report.Add(
        "- Published entries: ``$($matrix.manualCount)`` manual + " +
        "``$($matrix.automaticCount)`` automatic"
    )
    $report.Add(
        "- Synthetic slot safety: ``$($matrix.slotSafetyPass)``"
    )
    $report.Add(
        "- Runtime overwrite denial: ``$restoreDenialPass`` " +
        "(applicable: ``$restoreDenialApplicable``)"
    )
    $report.Add(
        "- Semantic points (stationary/jump/dash/combat): " +
        "``$($matrix.stationarySemanticPass)`` / " +
        "``$($matrix.jumpSemanticPass)`` / " +
        "``$($matrix.dashSemanticPass)`` / " +
        "``$($matrix.combatSemanticPass)``"
    )
    $report.Add(
        "- Hero control restored after capture: " +
        "``$($matrix.heroControlRestoreEquivalent)``"
    )
    $report.Add(
        "- Restore attempts: ``$totalAttempts/$expectedAttempts``"
    )
    $report.Add(
        "- Exact raw semantic SHA-256 matches: " +
        "``$strictRawExactAttempts/$totalAttempts``"
    )
    if ($CaptureOnly) {
        $report.Add('')
        $report.Add(
            'Capture-only mode intentionally does not approve or write the ' +
            'occupied dedicated TAS slot; it is not the final G3 verdict.'
        )
    }
    else {
        $report.Add('')
        $report.Add(
            '| Order | Reason | Label | Tick | Attempts | Strict raw | Result |'
        )
        $report.Add('|---:|---|---|---:|---:|---:|---|')
        foreach ($run in $summaries) {
            $report.Add(
                "| $($run.order) | $($run.reason) | " +
                "$($run.label) | $($run.effectiveMovieTick) | " +
                "$($run.attemptCount) | $($run.strictRawExactCount) | " +
                "$(if ($run.pass) { 'PASS' } else { 'FAIL' }) |"
            )
        }
    }

    $report |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'verdict.md'
            ) `
            -Encoding utf8NoBOM
    Assert-EvidencePrivacy

    [pscustomobject]@{
        CapturePass = $capturePass
        RestoreDenialPass = $restoreDenialPass
        FullGatePass = $fullGatePass
        Mode = $matrix.mode
        RestoreAttempts = $totalAttempts
        EvidenceRoot = $EvidenceRoot
        MatrixPath = $matrixPath
    } | ConvertTo-Json
}
finally {
    $recoveryErrors = [System.Collections.Generic.List[string]]::new()
    Close-GameNormally
    $running = @(
        Get-Process `
            -Name 'hollow_knight' `
            -ErrorAction SilentlyContinue
    )
    if ($running.Count -ne 0) {
        $recoveryErrors.Add(
            'Hollow Knight is still running; filesystem recovery was not attempted.'
        )
    }
    else {
        try {
            Restore-DedicatedSlot
        }
        catch {
            $recoveryErrors.Add(
                "Dedicated-slot recovery failed: $($_.Exception.Message)"
            )
        }

        try {
            Restore-TestSettings
        }
        catch {
            $recoveryErrors.Add(
                "Settings recovery failed: $($_.Exception.Message)"
            )
        }

        try {
            if (Test-Path -LiteralPath $storeRoot -PathType Container) {
                Remove-ValidatedDirectory `
                    -Path $storeRoot `
                    -ExpectedFullPath $storeRoot
            }
            if ($storeMoved) {
                Move-Item `
                    -LiteralPath $storeBackup `
                    -Destination $storeRoot
            }
        }
        catch {
            $recoveryErrors.Add(
                "Replay-store recovery failed: $($_.Exception.Message)"
            )
        }

        try {
            if ($modsSwapped) {
                $isolatedTas = Join-Path `
                    $modsDirectory `
                    'HollowKnightTAS'
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
                Move-Item `
                    -LiteralPath $modsBackup `
                    -Destination $modsDirectory
                if (Test-Path -LiteralPath $modsEmpty) {
                    if (
                        @(Get-ChildItem `
                            -LiteralPath $modsEmpty `
                            -Force).Count -ne 0
                    ) {
                        throw 'T09 empty validation directory contains unexpected files.'
                    }

                    Remove-Item -LiteralPath $modsEmpty
                }
            }
        }
        catch {
            $recoveryErrors.Add(
                "Mods recovery failed: $($_.Exception.Message)"
            )
        }

        try {
            if ($null -ne $initialSlotState) {
                Assert-SlotsUnchanged -Expected $initialSlotState
            }
        }
        catch {
            $recoveryErrors.Add(
                "Final slot verification failed: $($_.Exception.Message)"
            )
        }

        try {
            if (Test-Path -LiteralPath $privateBackupRoot) {
                Remove-ValidatedDirectory `
                    -Path $privateBackupRoot `
                    -ExpectedFullPath $privateBackupRoot
            }
        }
        catch {
            $recoveryErrors.Add(
                "Private backup cleanup failed: $($_.Exception.Message)"
            )
        }
    }

    if ($recoveryErrors.Count -ne 0) {
        throw ($recoveryErrors -join ' ')
    }
}

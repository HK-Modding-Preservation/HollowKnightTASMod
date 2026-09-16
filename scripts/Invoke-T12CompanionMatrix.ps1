[CmdletBinding()]
param(
    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(30, 300)]
    [int]$MaxLaunchSeconds = 90,

    [ValidateRange(3600, 7200)]
    [int]$SoakSeconds = 3600,

    [switch]$SkipT07Parity,

    [switch]$SkipSoak
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T12 Companion matrix requires PowerShell 7 or newer.'
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$ManagedDirectory = [IO.Path]::GetFullPath(
    $ManagedDirectory)
$PersistentDataDirectory = [IO.Path]::GetFullPath(
    $PersistentDataDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$installRoot = Join-Path $modsDirectory 'HollowKnightTAS'
$entrypoint = Join-Path `
    $installRoot `
    'Companion\win-x64\HollowKnightTAS.Companion.exe'
$manifestPath = Join-Path `
    $installRoot `
    'companion.manifest.json'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$modLogPath = Join-Path `
    $PersistentDataDirectory `
    'ModLog.txt'
$sessionRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\sessions'
$storeRoot = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTAS\replay-saves\v1'
$coreTests = Join-Path `
    $repoRoot `
    'tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj'
$companionTests = Join-Path `
    $repoRoot `
    'tests\HollowKnightTAS.Companion.Tests\HollowKnightTAS.Companion.Tests.csproj'
$bundleTool = Join-Path `
    $repoRoot `
    'src\HollowKnightTAS.BundleTool\HollowKnightTAS.BundleTool.csproj'
$publicKey = Join-Path `
    $repoRoot `
    '.local\signing\companion-public.json'
$t07Script = Join-Path `
    $PSScriptRoot `
    'Invoke-T07VerificationCampaign.ps1'
$t11Script = Join-Path `
    $PSScriptRoot `
    'Invoke-T11InspectorMatrix.ps1'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't12-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString(
            'yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $repoRoot `
        "artifacts\companion\$campaign"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

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
if (-not (Test-Path -LiteralPath $entrypoint -PathType Leaf)) {
    throw "Installed Companion is missing: $entrypoint"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Installed Companion manifest is missing: $manifestPath"
}
if (-not (Test-Path -LiteralPath $SteamExecutable -PathType Leaf)) {
    throw "Steam executable is missing: $SteamExecutable"
}
if (-not (Test-Path -LiteralPath $publicKey -PathType Leaf)) {
    throw "Companion public key is missing: $publicKey"
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Evidence root already exists: $EvidenceRoot"
}
if (Get-Process `
        -Name hollow_knight,HollowKnightTAS.Companion `
        -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight and Companion must be stopped before T12.'
}

New-Item `
    -ItemType Directory `
    -Path $EvidenceRoot |
    Out-Null

$privateRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    ('HKTAS-T12-' + [Guid]::NewGuid().ToString('N'))
New-Item `
    -ItemType Directory `
    -Path $privateRoot |
    Out-Null
$storeBackup = Join-Path $privateRoot 'replay-store-v1'
$settingsOriginallyExisted =
    Test-Path -LiteralPath $settingsPath -PathType Leaf
$settingsOriginalBytes =
    if ($settingsOriginallyExisted) {
        [IO.File]::ReadAllBytes($settingsPath)
    }
    else {
        $null
    }
$storeMoved = $false
$script:ownedGame = $null
$script:activeSession = $null
$script:launchTime = $null
$cases = [System.Collections.Generic.List[object]]::new()
$matrix = $null

if (-not ('HkTasT12.NativeInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace HkTasT12
{
    public static class NativeInput
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(
            IntPtr handle,
            int command);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);
    }
}
'@
}

function Get-FileState {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            exists = $false
            length = 0
            sha256 = ''
        }
    }

    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        exists = $true
        length = $item.Length
        sha256 = (
            Get-FileHash `
                -LiteralPath $Path `
                -Algorithm SHA256
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
                        [StringComparison]::Ordinal)) {
                throw "User slot changed: slot=$slot kind=$kind"
            }
        }
    }
}

function Get-ModEntryNames {
    return @(
        Get-ChildItem -LiteralPath $modsDirectory -Force |
            Sort-Object Name |
            ForEach-Object Name
    )
}

function Write-TestSettings {
    param(
        [Parameter(Mandatory)]
        [bool]$AutoStart,

        [Parameter(Mandatory)]
        [bool]$ExitWithGame
    )

    $settings =
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            Get-Content -LiteralPath $settingsPath -Raw |
                ConvertFrom-Json -AsHashtable -Depth 50
        }
        else {
            [ordered]@{}
        }
    $settings['VerificationModeRequested'] = $false
    $settings['CompanionEnabled'] = $true
    $settings['AutoStartCompanion'] = $AutoStart
    $settings['ExitCompanionWithGame'] = $ExitWithGame
    $settings['CompanionOverlayEnabled'] = $false
    $settings['CompanionCommandQueueCapacity'] = 1024
    $settings['CompanionOutboundQueueCapacity'] = 2048
    $settings['CompanionMainThreadBudgetMilliseconds'] = 2.0
    $settings['CompanionHandshakeTimeoutMilliseconds'] = 10000
    $settings['EnableNativeCapabilities'] = $false
    $settings['InspectorEnabled'] = $true
    $settings['InspectorOverlayEnabled'] = $false
    $settings['InspectorExportEnabled'] = $true
    $settings['InspectorSampleEveryMovieTicks'] = 120
    $settings['ReplaySaveEnabled'] = $true
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

    $rows = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($line in Get-Content `
                -LiteralPath $path `
                -ErrorAction Stop) {
            try {
                $rows.Add(($line | ConvertFrom-Json))
            }
            catch {
                # The writer may expose its final incomplete line.
            }
        }
    }
    catch [IO.IOException] {
        return @()
    }
    return @($rows)
}

function Get-Companions {
    return @(
        Get-Process `
            -Name HollowKnightTAS.Companion `
            -ErrorAction SilentlyContinue
    )
}

function Start-Game {
    param([string[]]$ExtraArguments = @())

    if (Get-Process -Name hollow_knight -ErrorAction SilentlyContinue) {
        throw 'A Hollow Knight process is already running.'
    }

    $script:ownedGame = $null
    $script:activeSession = $null
    $script:launchTime = Get-Date
    $beforeSessions = @(
        Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            ForEach-Object FullName
    )
    $arguments = @(
        '-applaunch',
        '367520',
        '-screen-width',
        '800',
        '-screen-height',
        '450',
        '-screen-fullscreen',
        '0'
    ) + $ExtraArguments
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds($MaxLaunchSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($null -eq $script:ownedGame) {
            $script:ownedGame =
                Get-Process `
                    -Name hollow_knight `
                    -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.StartTime -ge
                        $script:launchTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
        }

        if ($null -eq $script:activeSession) {
            $script:activeSession =
                Get-ChildItem `
                    -LiteralPath $sessionRoot `
                    -Directory `
                    -ErrorAction SilentlyContinue |
                Where-Object {
                    $beforeSessions -notcontains $_.FullName
                } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
        }

        if ($null -ne $script:ownedGame `
                -and $null -ne $script:activeSession) {
            return
        }
    }

    throw 'Timed out waiting for the game process and T12 session.'
}

function Close-Game {
    if ($null -eq $script:ownedGame) {
        return
    }

    $script:ownedGame.Refresh()
    if (-not $script:ownedGame.HasExited) {
        [void]$script:ownedGame.CloseMainWindow()
        [void]$script:ownedGame.WaitForExit(20000)
        $script:ownedGame.Refresh()
    }
    if (-not $script:ownedGame.HasExited) {
        Stop-Process -Id $script:ownedGame.Id
        [void]$script:ownedGame.WaitForExit(10000)
    }
    $script:ownedGame = $null
}

function Wait-ForServiceStarted {
    $deadline = (Get-Date).AddSeconds($MaxLaunchSeconds)
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
    throw 'Companion service did not report startup.'
}

function Send-F10 {
    $deadline = (Get-Date).AddSeconds(15)
    $handle = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline) {
        $script:ownedGame.Refresh()
        $handle = $script:ownedGame.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if ($handle -eq [IntPtr]::Zero) {
        throw 'Hollow Knight main window handle is unavailable.'
    }

    [void][HkTasT12.NativeInput]::ShowWindowAsync($handle, 9)
    [void][HkTasT12.NativeInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 250
    [HkTasT12.NativeInput]::keybd_event(
        0x79,
        0,
        0,
        [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [HkTasT12.NativeInput]::keybd_event(
        0x79,
        0,
        2,
        [UIntPtr]::Zero)
}

function Wait-ForReadyAndWarmCommands {
    param(
        [Nullable[int]]$ExpectedCompanionPid = $null,
        [switch]$ExpectExisting
    )

    $deadline = (Get-Date).AddSeconds($MaxLaunchSeconds)
    $companions = @()
    $readyObserved = $false
    while ((Get-Date) -lt $deadline) {
        $companions = @(Get-Companions)
        if ($companions.Count -eq 1 `
                -and (
                    Test-Path `
                        -LiteralPath $modLogPath `
                        -PathType Leaf
                )) {
            $readyText =
                if ($ExpectExisting) {
                    'T12 Companion authenticated generation='
                }
                else {
                    'T12 Companion ready pid=' `
                    + $companions[0].Id `
                    + ' '
                }
            if (Select-String `
                    -LiteralPath $modLogPath `
                    -SimpleMatch $readyText `
                    -Quiet) {
                $readyObserved = $true
                break
            }
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not $readyObserved -or $companions.Count -ne 1) {
        throw 'Companion did not reach authenticated Ready state.'
    }
    if ($null -ne $ExpectedCompanionPid `
            -and $companions[0].Id -ne
                [int]$ExpectedCompanionPid) {
        throw 'Runtime did not reuse the expected Companion PID.'
    }

    if (-not $ExpectExisting) {
        $processInfo = Get-CimInstance `
            -ClassName Win32_Process `
            -Filter "ProcessId = $($companions[0].Id)"
        if ($processInfo.ParentProcessId -ne
                $script:ownedGame.Id) {
            throw 'Companion parent is not the game process.'
        }
        if ($processInfo.ExecutablePath -ne $entrypoint) {
            throw 'Companion executable path differs from fixed bundle path.'
        }
        $argumentMatches = @(
            [regex]::Matches(
                [string]$processInfo.CommandLine,
                '--bootstrap-pipe=')
        ).Count
        if ($argumentMatches -ne 1 `
                -or [string]$processInfo.CommandLine -notmatch
                    '--bootstrap-pipe=HollowKnightTAS\.Bootstrap\.[0-9a-f]{32}') {
            throw 'Companion command line is outside the fixed bootstrap shape.'
        }
    }

    # Studio sends its immediate warm commands, then retries a semantic
    # snapshot after eight seconds so an active HeroController can appear.
    Start-Sleep -Seconds 10
    return [pscustomobject]@{
        companionPid = $companions[0].Id
    }
}

function Get-FinalWarmEvidence {
    param([switch]$ExpectExisting)

    $finalEvents = @(
        Get-CompleteEvents $script:activeSession.FullName
    )
    $ready = @(
        $finalEvents |
            Where-Object {
                $_.eventType -eq 'companion-launch-state' `
                    -and $_.fields.state -eq 'Ready'
            }
    )
    $accepted = @(
        $finalEvents |
            Where-Object {
                $_.eventType -eq 'companion-command-accepted'
            }
    )
    $snapshotOutcome = @(
        $finalEvents |
            Where-Object {
                ($_.eventType -eq
                    'companion-command-accepted' `
                    -or $_.eventType -eq
                        'companion-command-rejected') `
                    -and $_.fields.command -eq
                        'requestSnapshot'
            }
    )
    if ($ready.Count -eq 0 `
            -or $accepted.Count -lt 5 `
            -or $snapshotOutcome.Count -lt 2) {
        throw 'Final Ready/warm-command evidence is incomplete.'
    }

    $commands = @(
        $accepted |
            ForEach-Object {
                [string]$_.fields.command
            }
    )
    foreach ($required in @(
            'ping',
            'listReplaySaves',
            'requestCapabilityCatalog'
        )) {
        if ($commands -notcontains $required) {
            throw "Warm command was not accepted: $required"
        }
    }
    if (@($commands | Where-Object { $_ -eq 'subscribe' }).Count `
            -lt 2) {
        throw 'Both watch and ledger subscriptions were not accepted.'
    }

    $states = @(
        $finalEvents |
            Where-Object {
                $_.eventType -eq 'companion-launch-state'
            } |
            ForEach-Object {
                [string]$_.fields.state
            }
    )
    if ($ExpectExisting) {
        if ($states -contains 'Launching') {
            throw 'Existing-instance attach unexpectedly launched a process.'
        }
        if (-not [string]::IsNullOrEmpty(
                [string]$ready[-1].fields.ownedProcessId)) {
            throw 'Existing-instance Ready state claimed process ownership.'
        }
    }
    elseif ($states -notcontains 'Launching') {
        throw 'Verified launch state was not observed.'
    }

    $stopped = @(
        $finalEvents |
            Where-Object {
                $_.eventType -eq 'companion-service-stopped'
            }
    )
    if ($stopped.Count -ne 1 `
            -or [long]$stopped[0].fields.commandRejectedCount -ne 0 `
            -or [long]$stopped[0].fields.outboundRejectedCount -ne 0) {
        throw 'Companion shutdown reported queue rejection or missing cleanup.'
    }

    return [pscustomobject]@{
        acceptedCount = $accepted.Count
        snapshotOutcomeCount = $snapshotOutcome.Count
        commands = $commands
        states = $states
    }
}

function Wait-ForCompanionExit {
    param([Parameter(Mandatory)][int]$ProcessId)

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process `
                -Id $ProcessId `
                -ErrorAction SilentlyContinue)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Companion PID $ProcessId did not exit with the game."
}

function Save-CaseEvidence {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)]$Result
    )

    $destination = Join-Path `
        $EvidenceRoot `
        "launch\$Name"
    New-Item `
        -ItemType Directory `
        -Path $destination `
        -Force |
        Out-Null
    foreach ($name in @(
            'events.jsonl',
            'manifest.json',
            'manifest.sha256'
        )) {
        $source = Join-Path $script:activeSession.FullName $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item `
                -LiteralPath $source `
                -Destination (Join-Path $destination $name)
        }
    }
    $Result |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath (Join-Path $destination 'result.json') `
            -Encoding utf8NoBOM
}

function Invoke-ManualLaunchCase {
    Write-TestSettings `
        -AutoStart $false `
        -ExitWithGame $true
    Start-Game
    $companionProcessId = 0
    try {
        Start-Sleep -Seconds 5
        if (@(Get-Companions).Count -ne 0) {
            throw 'AutoStartCompanion=false created a process.'
        }
        $zeroProcessPass = $true

        Send-F10
        $live = Wait-ForReadyAndWarmCommands
        $companionProcessId = $live.companionPid
    }
    finally {
        Close-Game
    }
    Wait-ForCompanionExit $companionProcessId
    $warm = Get-FinalWarmEvidence
    $result = [ordered]@{
        name = 'manual-from-disabled-autostart'
        pass = $true
        zeroProcessBeforeF10 = $zeroProcessPass
        companionPid = $companionProcessId
        acceptedCount = $warm.acceptedCount
        snapshotOutcomeCount =
            $warm.snapshotOutcomeCount
        commands = $warm.commands
        states = $warm.states
        sessionDirectory =
            $script:activeSession.FullName
    }
    Save-CaseEvidence `
        -Name 'manual-from-disabled-autostart' `
        -Result $result
    $cases.Add([pscustomobject]$result)
}

function Invoke-AutoLaunchCase {
    Write-TestSettings `
        -AutoStart $true `
        -ExitWithGame $true
    Start-Game
    $companionProcessId = 0
    try {
        $live = Wait-ForReadyAndWarmCommands
        $companionProcessId = $live.companionPid
    }
    finally {
        Close-Game
    }
    Wait-ForCompanionExit $companionProcessId
    $warm = Get-FinalWarmEvidence
    $result = [ordered]@{
        name = 'verified-autostart'
        pass = $true
        companionPid = $companionProcessId
        acceptedCount = $warm.acceptedCount
        snapshotOutcomeCount =
            $warm.snapshotOutcomeCount
        commands = $warm.commands
        states = $warm.states
        sessionDirectory =
            $script:activeSession.FullName
    }
    Save-CaseEvidence `
        -Name 'verified-autostart' `
        -Result $result
    $cases.Add([pscustomobject]$result)
}

function Invoke-ExistingInstanceCase {
    Write-TestSettings `
        -AutoStart $true `
        -ExitWithGame $true
    $existing = Start-Process `
        -FilePath $entrypoint `
        -ArgumentList @('--headless') `
        -PassThru
    try {
        Start-Sleep -Seconds 2
        $existing.Refresh()
        if ($existing.HasExited) {
            throw 'Standalone Companion exited before attach.'
        }

        Start-Game
        try {
            $live = Wait-ForReadyAndWarmCommands `
                -ExpectedCompanionPid $existing.Id `
                -ExpectExisting
        }
        finally {
            Close-Game
        }
        Wait-ForCompanionExit $existing.Id
        $warm = Get-FinalWarmEvidence -ExpectExisting
        $result = [ordered]@{
            name = 'existing-instance-reuse'
            pass = $true
            companionPid = $live.companionPid
            acceptedCount = $warm.acceptedCount
            snapshotOutcomeCount =
                $warm.snapshotOutcomeCount
            commands = $warm.commands
            states = $warm.states
            sessionDirectory =
                $script:activeSession.FullName
        }
        Save-CaseEvidence `
            -Name 'existing-instance-reuse' `
            -Result $result
        $cases.Add([pscustomobject]$result)
    }
    finally {
        $existing.Refresh()
        if (-not $existing.HasExited) {
            [void]$existing.CloseMainWindow()
            [void]$existing.WaitForExit(5000)
            $existing.Refresh()
        }
        if (-not $existing.HasExited) {
            Stop-Process -Id $existing.Id
            [void]$existing.WaitForExit(5000)
        }
        $existing.Dispose()
    }
}

function Invoke-T07Parity {
    $root = Join-Path $EvidenceRoot 't07-parity'
    $off = Join-Path $root 'companion-off'
    $on = Join-Path $root 'companion-on'
    New-Item -ItemType Directory -Path $root | Out-Null

    $offOutput = & $t07Script `
        -Smoke `
        -LocalRunCount 1 `
        -TestSaveSlot $TestSaveSlot `
        -ManagedDirectory $ManagedDirectory `
        -SteamExecutable $SteamExecutable `
        -PersistentDataDirectory $PersistentDataDirectory `
        -EvidenceRoot $off `
        -SkipDeliberateDivergence `
        -AutoStartCompanion $false 2>&1 |
        Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "T07 Companion-off run failed: $offOutput"
    }

    $onOutput = & $t07Script `
        -Smoke `
        -LocalRunCount 1 `
        -TestSaveSlot $TestSaveSlot `
        -ManagedDirectory $ManagedDirectory `
        -SteamExecutable $SteamExecutable `
        -PersistentDataDirectory $PersistentDataDirectory `
        -EvidenceRoot $on `
        -SkipDeliberateDivergence `
        -AutoStartCompanion $true 2>&1 |
        Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "T07 Companion-on run failed: $onOutput"
    }

    $offRun = Get-Content `
        -LiteralPath (Join-Path $off 'run-01\run.json') `
        -Raw |
        ConvertFrom-Json
    $onRun = Get-Content `
        -LiteralPath (Join-Path $on 'run-01\run.json') `
        -Raw |
        ConvertFrom-Json
    $offProjection = @(
        $offRun.milestones |
            ForEach-Object {
                '{0}|{1}|{2}' -f `
                    $_.milestoneId, `
                    $_.movieTick, `
                    $_.semanticSha256
            }
    )
    $onProjection = @(
        $onRun.milestones |
            ForEach-Object {
                '{0}|{1}|{2}' -f `
                    $_.milestoneId, `
                    $_.movieTick, `
                    $_.semanticSha256
            }
    )
    $pass =
        $offProjection.Count -eq $onProjection.Count `
        -and -not (
            Compare-Object `
                -ReferenceObject $offProjection `
                -DifferenceObject $onProjection
        )
    if (-not $pass) {
        throw 'Companion on/off changed T07 milestone hashes.'
    }

    $result = [ordered]@{
        pass = $true
        offManifestSha256 = $offRun.manifestSha256
        onManifestSha256 = $onRun.manifestSha256
        milestoneProjection = $offProjection
        note =
            'Manifest hashes differ by the Companion setting; semantic milestone hashes are identical.'
    }
    $result |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (Join-Path $root 'parity.json') `
            -Encoding utf8NoBOM
    return [pscustomobject]$result
}

function Invoke-SubscriptionSoak {
    $root = Join-Path $EvidenceRoot 'soak'
    $inspectorRoot = Join-Path $root 'inspector'
    New-Item -ItemType Directory -Path $root | Out-Null
    $job = Start-Job `
        -ScriptBlock {
            param(
                $ScriptPath,
                $Seconds,
                $Slot,
                $Managed,
                $Steam,
                $Persistent,
                $Output
            )
            & $ScriptPath `
                -FunctionalRuns 2 `
                -PerformanceSeconds $Seconds `
                -TestSaveSlot $Slot `
                -ManagedDirectory $Managed `
                -SteamExecutable $Steam `
                -PersistentDataDirectory $Persistent `
                -EvidenceRoot $Output
        } `
        -ArgumentList @(
            $t11Script,
            $SoakSeconds,
            $TestSaveSlot,
            $ManagedDirectory,
            $SteamExecutable,
            $PersistentDataDirectory,
            $inspectorRoot
        )
    $samples = [System.Collections.Generic.List[object]]::new()
    try {
        while ($job.State -eq 'Running' `
                -or $job.State -eq 'NotStarted') {
            foreach ($process in Get-Companions) {
                $process.Refresh()
                $samples.Add(
                    [pscustomobject]@{
                        timestampUtc =
                            [DateTimeOffset]::UtcNow.ToString('O')
                        processId = $process.Id
                        privateBytes =
                            $process.PrivateMemorySize64
                        workingSetBytes =
                            $process.WorkingSet64
                    })
            }
            Start-Sleep -Seconds 10
            $job = Get-Job -Id $job.Id
        }
        $jobOutput = Receive-Job -Job $job 2>&1 |
            Out-String
        if ($job.State -ne 'Completed') {
            throw "T11-backed soak failed: $jobOutput"
        }
    }
    finally {
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }

    @($samples) |
        ConvertTo-Json -Depth 5 |
        Set-Content `
            -LiteralPath (Join-Path $root 'memory-samples.json') `
            -Encoding utf8NoBOM
    $matrixPath = Join-Path `
        $inspectorRoot `
        'inspector-matrix.json'
    $inspectorMatrix =
        Get-Content -LiteralPath $matrixPath -Raw |
        ConvertFrom-Json
    if (-not $inspectorMatrix.fullGatePass `
            -or -not $inspectorMatrix.performancePass) {
        throw 'T11 active-game performance gate failed during soak.'
    }

    $groups = @(
        $samples |
            Group-Object processId |
            Sort-Object Count -Descending
    )
    if ($groups.Count -eq 0) {
        throw 'No Companion memory samples were captured.'
    }
    $longest = @(
        $groups[0].Group |
            Sort-Object timestampUtc
    )
    $firstTime =
        [DateTimeOffset]::Parse($longest[0].timestampUtc)
    $lastTime =
        [DateTimeOffset]::Parse($longest[-1].timestampUtc)
    $durationSeconds =
        ($lastTime - $firstTime).TotalSeconds
    if ($durationSeconds -lt $SoakSeconds * 0.80) {
        throw 'Companion was not sampled for most of the soak.'
    }

    $window = [Math]::Max(
        3,
        [Math]::Floor($longest.Count * 0.1))
    $firstAverage = (
        $longest |
            Select-Object -First $window |
            Measure-Object privateBytes -Average
    ).Average
    $lastAverage = (
        $longest |
            Select-Object -Last $window |
            Measure-Object privateBytes -Average
    ).Average
    $growthBytes = $lastAverage - $firstAverage
    $maximumBytes = (
        $longest |
            Measure-Object privateBytes -Maximum
    ).Maximum
    $growthLimit = 384MB
    $maximumLimit = 2GB
    $pass =
        $growthBytes -le $growthLimit `
        -and $maximumBytes -le $maximumLimit
    if (-not $pass) {
        throw 'Companion memory exceeded the bounded-soak gate.'
    }

    $result = [ordered]@{
        pass = $true
        requestedSeconds = $SoakSeconds
        sampledSeconds = $durationSeconds
        sampleCount = $longest.Count
        processId = [int]$groups[0].Name
        firstWindowPrivateBytes = $firstAverage
        lastWindowPrivateBytes = $lastAverage
        growthBytes = $growthBytes
        maximumPrivateBytes = $maximumBytes
        growthLimitBytes = $growthLimit
        maximumLimitBytes = $maximumLimit
        inspectorFullGatePass =
            [bool]$inspectorMatrix.fullGatePass
    }
    $result |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath (Join-Path $root 'soak.json') `
            -Encoding utf8NoBOM
    return [pscustomobject]$result
}

$initialSlots = Get-AllSlotState
$initialModEntries = Get-ModEntryNames
$initialStoreExisted =
    Test-Path -LiteralPath $storeRoot -PathType Container

try {
    if ($initialStoreExisted) {
        Move-Item `
            -LiteralPath $storeRoot `
            -Destination $storeBackup
        $storeMoved = $true
    }

    $testLog = Join-Path $EvidenceRoot 'automated-tests.txt'
    $testOutput = & dotnet test `
        $coreTests `
        -c Release `
        --nologo 2>&1 |
        Tee-Object -FilePath $testLog
    if ($LASTEXITCODE -ne 0) {
        throw "Core tests failed: $($testOutput | Out-String)"
    }
    $testOutput = & dotnet test `
        $companionTests `
        -c Release `
        --nologo 2>&1 |
        Tee-Object -FilePath $testLog -Append
    if ($LASTEXITCODE -ne 0) {
        throw "Companion tests failed: $($testOutput | Out-String)"
    }

    $verifyOutput = & dotnet run `
        --project $bundleTool `
        -c Release `
        -- `
        verify `
        $publicKey `
        $installRoot `
        $manifestPath 2>&1 |
        Out-String
    if ($LASTEXITCODE -ne 0 `
            -or -not $verifyOutput.StartsWith(
                'Valid:',
                [StringComparison]::Ordinal)) {
        throw "Installed bundle verification failed: $verifyOutput"
    }
    $verifyOutput |
        Set-Content `
            -LiteralPath (
                Join-Path $EvidenceRoot 'bundle-verification.txt'
            ) `
            -Encoding utf8NoBOM

    $headless = Start-Process `
        -FilePath $entrypoint `
        -ArgumentList @(
            '--headless',
            '--exit-after-seconds=1'
        ) `
        -PassThru `
        -Wait
    if ($headless.ExitCode -ne 0) {
        throw "Signed headless Studio smoke failed: $($headless.ExitCode)"
    }
    $headless.Dispose()

    Invoke-ManualLaunchCase
    Assert-SlotsUnchanged $initialSlots
    Invoke-AutoLaunchCase
    Assert-SlotsUnchanged $initialSlots
    Invoke-ExistingInstanceCase
    Assert-SlotsUnchanged $initialSlots

    $parity =
        if ($SkipT07Parity) {
            [pscustomobject]@{
                pass = $null
                skipped = $true
            }
        }
        else {
            Invoke-T07Parity
        }
    Assert-SlotsUnchanged $initialSlots

    $soak =
        if ($SkipSoak) {
            [pscustomobject]@{
                pass = $null
                skipped = $true
            }
        }
        else {
            Invoke-SubscriptionSoak
        }
    Assert-SlotsUnchanged $initialSlots

    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc =
            [DateTimeOffset]::UtcNow.ToString('O')
        pass = $true
        bundleVerification = $verifyOutput.Trim()
        automatedTestProjects = 2
        launchCases = @($cases)
        t07Parity = $parity
        subscriptionSoak = $soak
        initialReplayStoreExisted =
            $initialStoreExisted
        saveSlotsUnchanged = $true
    }
    $matrix |
        ConvertTo-Json -Depth 30 |
        Set-Content `
            -LiteralPath (Join-Path $EvidenceRoot 'matrix.json') `
            -Encoding utf8NoBOM
}
finally {
    Close-Game
    foreach ($process in Get-Companions) {
        [void]$process.CloseMainWindow()
        [void]$process.WaitForExit(5000)
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            [void]$process.WaitForExit(5000)
        }
    }

    if ($settingsOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsPath,
            $settingsOriginalBytes)
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath
    }

    if (Test-Path -LiteralPath $storeRoot) {
        $resolvedStore =
            [IO.Path]::GetFullPath($storeRoot)
        $persistentPrefix =
            $PersistentDataDirectory `
            + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedStore.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean a replay store outside persistent data.'
        }
        Remove-Item `
            -LiteralPath $storeRoot `
            -Recurse
    }
    if ($storeMoved) {
        New-Item `
            -ItemType Directory `
            -Path (Split-Path -Parent $storeRoot) `
            -Force |
            Out-Null
        Move-Item `
            -LiteralPath $storeBackup `
            -Destination $storeRoot
    }

    Assert-SlotsUnchanged $initialSlots
    $finalModEntries = Get-ModEntryNames
    if (Compare-Object `
            -ReferenceObject $initialModEntries `
            -DifferenceObject $finalModEntries) {
        throw 'Mods directory entries changed during T12.'
    }
    if (Test-Path -LiteralPath $privateRoot) {
        Remove-Item `
            -LiteralPath $privateRoot `
            -Recurse
    }
}

[pscustomobject]@{
    Pass = $matrix.pass
    LaunchCaseCount = @($matrix.launchCases).Count
    T07ParitySkipped = $SkipT07Parity.IsPresent
    SoakSkipped = $SkipSoak.IsPresent
    EvidenceRoot = $EvidenceRoot
} | ConvertTo-Json

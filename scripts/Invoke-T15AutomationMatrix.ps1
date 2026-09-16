[CmdletBinding()]
param(
    [string]$ManagedDirectory =
        'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable =
        'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory =
        'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(1, 10)]
    [int]$ParityIterations = 10,

    [ValidateRange(30, 180)]
    [int]$MaxLaunchSeconds = 120,

    [ValidateRange(1, 3600)]
    [int]$SubscriptionSeconds = 3600,

    [switch]$Smoke,

    [switch]$DiscoveryOnly,

    [switch]$ApprovedControlOnly,

    [switch]$MutationOnly,

    [switch]$StateSoakOnly,

    [switch]$AiOnly,

    [switch]$SecurityOnly,

    [switch]$SkipOfflineTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'T15 automation matrix requires PowerShell 7 or newer.'
}
if ($Smoke) {
    $ParityIterations = 1
    $SubscriptionSeconds = 5
}
if (@(
        [bool]$DiscoveryOnly,
        [bool]$ApprovedControlOnly,
        [bool]$MutationOnly,
        [bool]$StateSoakOnly,
        [bool]$AiOnly,
        [bool]$SecurityOnly
    ).Where({ $_ }).Count -gt 1) {
    throw 'T15 matrix-only switches are mutually exclusive.'
}

$repoRoot = [IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$ManagedDirectory = [IO.Path]::GetFullPath($ManagedDirectory)
$PersistentDataDirectory =
    [IO.Path]::GetFullPath($PersistentDataDirectory)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$installRoot = Join-Path $modsDirectory 'HollowKnightTAS'
$toolsRoot = Join-Path `
    $installRoot `
    'Companion\win-x64\Tools'
$cliPath = Join-Path $toolsRoot 'HollowKnightTAS.Cli.exe'
$agentBridgePath =
    Join-Path $toolsRoot 'HollowKnightTAS.AgentBridge.exe'
$sdkRoot = Join-Path $toolsRoot 'SDK'
$sdkCorePath = Join-Path $sdkRoot 'HollowKnightTAS.Core.dll'
$sdkClientPath =
    Join-Path $sdkRoot 'HollowKnightTAS.Automation.Client.dll'
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
$automationParent = Join-Path `
    $localApplicationData `
    'HollowKnightTAS'
$automationRoot = Join-Path $automationParent 'automation'
$bootstrapPath = Join-Path $automationRoot 'automation-v1.json'
$persistentPrefix = (
    $PersistentDataDirectory +
    [IO.Path]::DirectorySeparatorChar
)
$localPrefix = (
    [IO.Path]::GetFullPath($localApplicationData) +
    [IO.Path]::DirectorySeparatorChar
)

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaign = 't15-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString(
            'yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $EvidenceRoot = Join-Path `
        $repoRoot `
        "artifacts\automation\$campaign"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

foreach ($path in @(
        $SteamExecutable,
        $settingsPath,
        $cliPath,
        $agentBridgePath,
        $sdkCorePath,
        $sdkClientPath
    )) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $path"
    }
}
foreach ($path in @(
        $modsDirectory,
        $installRoot,
        $sessionRoot
    )) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Required directory is missing: $path"
    }
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    throw "Evidence root already exists: $EvidenceRoot"
}
$existingProcesses = @(
    Get-Process `
        -Name `
            hollow_knight,
            HollowKnightTAS.Companion,
            HollowKnightTAS.AgentBridge,
            HollowKnightTAS.NativeHost `
        -ErrorAction SilentlyContinue
)
if ($existingProcesses.Count -ne 0) {
    throw 'Hollow Knight and all HollowKnightTAS helper processes must be stopped.'
}
if (-not $replayStoreRoot.StartsWith(
        $persistentPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Replay store is outside persistent data.'
}
if (-not $automationRoot.StartsWith(
        $localPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Automation root is outside LocalApplicationData.'
}

New-Item -ItemType Directory -Path $EvidenceRoot |
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
$slotInitial = $null
$modsInitial = $null
$script:game = $null
$script:ownedHelperPids = @()
$script:mcp = $null
$script:mcpLog = $null
$swapId = [Guid]::NewGuid().ToString('N')
$replayStoreBackup = Join-Path `
    $replayStoreParent `
    "v1.HKTAS-T15-$swapId.backup"
$automationBackup = Join-Path `
    $automationParent `
    "automation.HKTAS-T15-$swapId.backup"
$modsBackup = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T15-$swapId.backup"
$modsEmpty = Join-Path `
    $ManagedDirectory `
    "Mods.HKTAS-T15-$swapId.empty"
$replayStoreOriginallyExisted =
    Test-Path -LiteralPath $replayStoreRoot -PathType Container
$automationOriginallyExisted =
    Test-Path -LiteralPath $automationRoot -PathType Container
$replayStoreMoved = $false
$automationMoved = $false
$modsSwapped = $false
$cases = [System.Collections.Generic.List[object]]::new()
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
        throw "$Label changed during T15."
    }
}

function Write-TestSettings {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Disabled', 'ReadOnly', 'ApprovedControl')]
        [string]$Mode,

        [Parameter(Mandatory)]
        [bool]$DebugMutationEnabled
    )

    $settings =
        Get-Content -LiteralPath $settingsPath -Raw |
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
    $settings['ReplaySaveEnabled'] = $true
    $settings['ReplaySaveAutoEnabled'] = $false
    $settings['ExternalAutomationMode'] = $Mode
    $settings['DebugMutationEnabled'] = $DebugMutationEnabled
    $settings |
        ConvertTo-Json -Depth 50 |
        Set-Content `
            -LiteralPath $settingsPath `
            -Encoding utf8NoBOM
}

function Stop-McpBridge {
    if ($null -eq $script:mcp) {
        return
    }

    try {
        $script:mcp.StandardInput.Close()
    }
    catch {
    }
    if (-not $script:mcp.WaitForExit(10000)) {
        Stop-Process `
            -Id $script:mcp.Id `
            -ErrorAction SilentlyContinue
        [void]$script:mcp.WaitForExit(5000)
    }
    $script:mcp.Dispose()
    $script:mcp = $null
    $script:mcpLog = $null
}

function Close-RunProcesses {
    Stop-McpBridge

    if ($null -ne $script:game) {
        $script:game.Refresh()
        if (-not $script:game.HasExited) {
            [void]$script:game.CloseMainWindow()
            [void]$script:game.WaitForExit(20000)
            $script:game.Refresh()
        }
        if (-not $script:game.HasExited) {
            Stop-Process `
                -Id $script:game.Id `
                -ErrorAction SilentlyContinue
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
        Stop-Process `
            -Id $process.Id `
            -ErrorAction SilentlyContinue
    }
    $script:ownedHelperPids = @()
}

function Invoke-ExternalProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [int]$TimeoutMilliseconds = 30000
    )

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) {
        throw "Could not start $FilePath."
    }
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
            throw "Process timed out: $FilePath"
        }
        return [pscustomobject]@{
            exitCode = $process.ExitCode
            stdout = $stdoutTask.GetAwaiter().GetResult()
            stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Start-GameCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)]
        [ValidateSet('Disabled', 'ReadOnly', 'ApprovedControl')]
        [string]$Mode,
        [Parameter(Mandatory)][bool]$DebugMutationEnabled,
        [switch]$InputNeutralGameplay
    )

    Close-RunProcesses
    Write-TestSettings `
        -Mode $Mode `
        -DebugMutationEnabled $DebugMutationEnabled
    $caseDirectory = Join-Path $EvidenceRoot $Name
    New-Item -ItemType Directory -Path $caseDirectory |
        Out-Null
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
    $runId = '{0}-{1}' -f `
        $Name, `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    if ($InputNeutralGameplay) {
        $probeDirectoryName = 'automation-ready'
        $probeEvidenceName = 'automation-ready-probe.json'
        $probeArguments = @(
            "--hktas-automation-ready-run=$runId",
            '--hktas-automation-ready-slot=4'
        )
    }
    else {
        $probeDirectoryName = 'input-phase'
        $probeEvidenceName = 'input-probe.json'
        $probeArguments = @(
            '--hktas-input-probe=A',
            "--hktas-input-probe-run=$runId",
            '--hktas-input-probe-case=normal',
            '--hktas-input-probe-slot=4',
            '--hktas-input-probe-live-hero'
        )
    }
    $launchAt = [DateTimeOffset]::Now
    $launchArguments = @(
        '-applaunch',
        '367520',
        '-screen-width',
        '800',
        '-screen-height',
        '450',
        '-screen-fullscreen',
        '0'
    ) + $probeArguments
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $launchArguments |
        Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(
        $MaxLaunchSeconds)
    $session = $null
    $probeResultPath = $null
    do {
        if ($null -eq $script:game) {
            $candidate = Get-Process `
                    -Name hollow_knight `
                    -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.StartTime -ge `
                        $launchAt.LocalDateTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if ($null -ne $candidate) {
                $script:game = $candidate
            }
        }

        $newSession = Get-ChildItem `
                -LiteralPath $sessionRoot `
                -Directory `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $beforeSessions -notcontains $_.FullName
            } |
            Sort-Object LastWriteTimeUtc -Descending |
            Where-Object {
                Test-Path -LiteralPath (
                    Join-Path `
                        $_.FullName `
                        "$probeDirectoryName\$runId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $candidateResult = Join-Path `
                $newSession.FullName `
                "$probeDirectoryName\$runId\result.json"
            if (Test-Path `
                    -LiteralPath $candidateResult `
                    -PathType Leaf) {
                $session = $newSession
                $probeResultPath = $candidateResult
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

        $bootstrapReady = if ($Mode -eq 'Disabled') {
            -not (Test-Path -LiteralPath $bootstrapPath)
        }
        else {
            Test-Path -LiteralPath $bootstrapPath -PathType Leaf
        }
        if ($null -ne $script:game `
                -and $null -ne $session `
                -and $bootstrapReady) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($null -eq $script:game `
            -or $null -eq $session `
            -or $null -eq $probeResultPath) {
        throw "Game/session/probe did not become ready for $Name."
    }
    if ($Mode -eq 'Disabled' `
            -and (Test-Path -LiteralPath $bootstrapPath)) {
        throw 'Disabled mode published an automation bootstrap.'
    }
    if ($Mode -ne 'Disabled' `
            -and -not (Test-Path `
                -LiteralPath $bootstrapPath `
                -PathType Leaf)) {
        throw "$Mode did not publish an automation bootstrap."
    }

    $probeResult = Get-Content `
            -LiteralPath $probeResultPath `
            -Raw |
        ConvertFrom-Json
    if ($InputNeutralGameplay) {
        $heroControlReacquireCount =
            [int]$probeResult.heroControlReacquireCount
        $heroLifecycleEvidenceValid =
            ($heroControlReacquireCount -eq 0 `
                -and -not $probeResult.heroStateWritten) `
            -or ($heroControlReacquireCount -ge 1 `
                -and $heroControlReacquireCount -le 3 `
                -and $probeResult.heroStateWritten)
        if (-not $probeResult.gameplayReady `
                -or $probeResult.inputInjected `
                -or $probeResult.timeScaleChanged `
                -or -not $heroLifecycleEvidenceValid) {
            throw (
                'Input-neutral readiness probe violated its contract: {0}' -f `
                    ($probeResult | ConvertTo-Json -Compress)
            )
        }
    }
    Copy-Item `
        -LiteralPath $probeResultPath `
        -Destination (Join-Path $caseDirectory $probeEvidenceName)
    foreach ($leaf in @('manifest.json', 'manifest.sha256')) {
        $source = Join-Path $session.FullName $leaf
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item `
                -LiteralPath $source `
                -Destination (Join-Path $caseDirectory $leaf)
        }
    }
    return [pscustomobject]@{
        name = $Name
        mode = $Mode
        debugMutationEnabled = $DebugMutationEnabled
        directory = $caseDirectory
        sessionDirectory = $session.FullName
        sessionId = $session.Name
        runId = $runId
    }
}

function New-SdkClient {
    param([Parameter(Mandatory)][string]$ClientId)

    $client =
        [HollowKnightTAS.Automation.Client.AutomationClient]::new()
    $options =
        [HollowKnightTAS.Automation.Client.AutomationConnectOptions]::new()
    $options.ClientId = $ClientId
    $options.BootstrapPath = $bootstrapPath
    $options.Timeout = [TimeSpan]::FromSeconds(15)
    [void]$client.ConnectAsync(
            $options,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()
    return $client
}

function Dispose-SdkClient {
    param($Client)

    if ($null -ne $Client) {
        $Client.DisposeAsync().GetAwaiter().GetResult()
    }
}

function Wait-SdkSemanticState {
    param(
        [Parameter(Mandatory)]$Client,
        [int]$TimeoutSeconds = 45
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            return $Client.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 500
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Fresh SDK state did not become available: $lastError"
}

function Get-StateField {
    param(
        [Parameter(Mandatory)]$SemanticState,
        [Parameter(Mandatory)][string]$Name
    )

    if ($SemanticState.State.Fields.ContainsKey($Name)) {
        return $SemanticState.State.Fields[$Name]
    }

    return ''
}

function Get-SemanticNativeValue {
    param(
        [Parameter(Mandatory)]$SemanticState,
        [Parameter(Mandatory)][string]$Name
    )

    $friendly = $SemanticState.ToFriendlyJson() |
        ConvertFrom-Json -Depth 100
    $property =
        $friendly.semanticValues.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Semantic state omitted $Name."
    }
    return $property.Value.value
}

function Get-ResultDataValue {
    param(
        [Parameter(Mandatory)]$Data,
        [Parameter(Mandatory)][string]$Name
    )

    if ($Data -is [Collections.IDictionary]) {
        if (-not $Data.Contains($Name)) {
            throw "Automation result data omitted $Name."
        }
        return $Data[$Name]
    }

    $property = $Data.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Automation result data omitted $Name."
    }
    return $property.Value
}

function Assert-RejectedMutationUnchanged {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After,
        [Parameter(Mandatory)][string[]]$ExpectedCodes,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Result.success) {
        throw "$Label unexpectedly succeeded."
    }
    if ($ExpectedCodes -notcontains [string]$Result.resultCode) {
        throw (
            '{0} returned {1}; expected one of {2}.' -f `
                $Label, `
                $Result.resultCode, `
                ($ExpectedCodes -join ',')
        )
    }
    if ($Before.State.SemanticSnapshotSha256 -ne
            $After.State.SemanticSnapshotSha256) {
        throw "$Label changed canonical semantic state."
    }
}

function Assert-MutationCommitted {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After,
        [Parameter(Mandatory)][hashtable]$ExpectedValues,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not $Result.success) {
        throw (
            '{0} failed: {1}: {2}' -f `
                $Label, `
                $Result.resultCode, `
                $Result.detail
        )
    }
    $data = $Result.data
    if ((Get-ResultDataValue -Data $data -Name 'beforeSha256') -ne
            $Before.State.SemanticSnapshotSha256 `
            -or (Get-ResultDataValue -Data $data -Name 'afterSha256') -ne
            $After.State.SemanticSnapshotSha256 `
            -or (Get-ResultDataValue -Data $data -Name 'committed') -ne
            'true' `
            -or (Get-ResultDataValue `
                -Data $data `
                -Name 'verificationEligibility') -ne
            'NonVerifiableDebugMutation' `
            -or [string]::IsNullOrWhiteSpace(
                [string](Get-ResultDataValue `
                    -Data $data `
                    -Name 'typedDiff'))) {
        throw "$Label returned inconsistent transaction evidence."
    }
    if ($Before.State.SemanticSnapshotSha256 -eq
            $After.State.SemanticSnapshotSha256) {
        throw "$Label did not change the canonical semantic state."
    }
    if ((Get-StateField `
                -SemanticState $After `
                -Name 'verificationEligibility') -ne
            'NonVerifiableDebugMutation') {
        throw "$Label did not permanently mark the session non-verifiable."
    }
    foreach ($pair in $ExpectedValues.GetEnumerator()) {
        $actual = Get-SemanticNativeValue `
            -SemanticState $After `
            -Name ([string]$pair.Key)
        if ($pair.Value -is [single]) {
            if ([single]$actual -ne [single]$pair.Value) {
                throw (
                    '{0} readback mismatch for {1}: expected={2}; actual={3}' -f `
                        $Label, `
                        $pair.Key, `
                        $pair.Value, `
                        $actual
                )
            }
        }
        elseif ([string]$actual -ne [string]$pair.Value) {
            throw (
                '{0} readback mismatch for {1}: expected={2}; actual={3}' -f `
                    $Label, `
                    $pair.Key, `
                    $pair.Value, `
                    $actual
            )
        }
    }
}

function New-HeroPoseMutationCase {
    param(
        [Parameter(Mandatory)]$SemanticState,
        [Parameter(Mandatory)][int]$Iteration
    )

    $x = [single](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'hero.position.x')
    $y = [single](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'hero.position.y')
    $velocityX = [single](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'hero.velocity.x')
    $velocityY = [single](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'hero.velocity.y')
    $offset = if (($Iteration % 2) -eq 1) {
        [single]0.125
    }
    else {
        [single]-0.125
    }
    $targetX = [single]($x + $offset)
    $invariant = [Globalization.CultureInfo]::InvariantCulture
    return [pscustomobject]@{
        arguments = @{
            expectedMovieTick =
                $SemanticState.State.MovieTick.ToString($invariant)
            expectedSnapshotSha256 =
                $SemanticState.State.SemanticSnapshotSha256
            positionX = $targetX.ToString('R', $invariant)
            positionY = $y.ToString('R', $invariant)
            velocityX = $velocityX.ToString('R', $invariant)
            velocityY = $velocityY.ToString('R', $invariant)
        }
        expectedValues = @{
            'hero.position.x' = $targetX
            'hero.position.y' = $y
            'hero.velocity.x' = $velocityX
            'hero.velocity.y' = $velocityY
        }
        targetX = $targetX
        targetY = $y
        velocityX = $velocityX
        velocityY = $velocityY
    }
}

function New-PlayerResourcesMutationCase {
    param(
        [Parameter(Mandatory)]$SemanticState,
        [Parameter(Mandatory)][int]$Iteration
    )

    $health = [int](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'player.health')
    $maximumHealth = [int](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'player.maxHealth')
    $soul = [int](Get-SemanticNativeValue `
        -SemanticState $SemanticState `
        -Name 'player.mp')
    $targetHealth = if ($maximumHealth -gt 1 `
            -and $health -eq $maximumHealth) {
        $maximumHealth - 1
    }
    else {
        $maximumHealth
    }
    $targetSoul = if ($soul -eq 0) { 1 } else { 0 }
    return [pscustomobject]@{
        arguments = @{
            expectedMovieTick =
                $SemanticState.State.MovieTick.ToString(
                    [Globalization.CultureInfo]::InvariantCulture)
            expectedSnapshotSha256 =
                $SemanticState.State.SemanticSnapshotSha256
            health = [string]$targetHealth
            soul = [string]$targetSoul
        }
        expectedValues = @{
            'player.health' = $targetHealth
            'player.mp' = $targetSoul
        }
        health = $targetHealth
        soul = $targetSoul
    }
}

function Invoke-SdkRejectedMutationCase {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$CommandId,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][hashtable]$Arguments,
        [Parameter(Mandatory)][string]$LeaseId,
        [Parameter(Mandatory)][string]$ExpectedRuntimeMode,
        [Parameter(Mandatory)][long]$ExpectedMovieTick,
        [Parameter(Mandatory)][string[]]$ExpectedCodes,
        [Parameter(Mandatory)][string]$Label
    )

    $before = Wait-SdkState `
        -Client $Client `
        -ControlMode 'Paused'
    $result = Invoke-SdkCommand `
        -Client $Client `
        -CommandId $CommandId `
        -Scope $Scope `
        -Arguments $Arguments `
        -LeaseId $LeaseId `
        -ExpectedRuntimeMode $ExpectedRuntimeMode `
        -ExpectedMovieTick $ExpectedMovieTick `
        -AllowFailure
    $after = Wait-SdkState `
        -Client $Client `
        -ControlMode 'Paused' `
        -ExactMovieTick $before.State.MovieTick
    $normalized = Convert-AutomationResult $result
    Assert-RejectedMutationUnchanged `
        -Result $normalized `
        -Before $before `
        -After $after `
        -ExpectedCodes $ExpectedCodes `
        -Label $Label
    return [ordered]@{
        label = $Label
        commandId = $CommandId
        resultCode = $result.ResultCode
        beforeSha256 = $before.State.SemanticSnapshotSha256
        afterSha256 = $after.State.SemanticSnapshotSha256
        unchanged = $true
    }
}

function Wait-SdkState {
    param(
        [Parameter(Mandatory)]$Client,
        [string]$ControlMode = '',
        [string]$PlaybackMode = '',
        [Nullable[long]]$ExactMovieTick = $null,
        [Nullable[long]]$MinimumMovieTick = $null,
        [hashtable]$Fields = @{},
        [int]$TimeoutSeconds = 30
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    $lastError = $null
    do {
        try {
            $last = $Client.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
            $modeMatches =
                [string]::IsNullOrEmpty($ControlMode) `
                -or $last.State.RuntimeMode -eq $ControlMode
            $playbackMatches =
                [string]::IsNullOrEmpty($PlaybackMode) `
                -or (Get-StateField `
                    -SemanticState $last `
                    -Name 'playbackMode') -eq $PlaybackMode
            $exactMatches =
                $null -eq $ExactMovieTick `
                -or $last.State.MovieTick -eq $ExactMovieTick
            $minimumMatches =
                $null -eq $MinimumMovieTick `
                -or $last.State.MovieTick -ge $MinimumMovieTick
            $fieldsMatch = $true
            foreach ($pair in $Fields.GetEnumerator()) {
                if ((Get-StateField `
                            -SemanticState $last `
                            -Name ([string]$pair.Key)) `
                        -ne [string]$pair.Value) {
                    $fieldsMatch = $false
                    break
                }
            }
            if ($modeMatches `
                    -and $playbackMatches `
                    -and $exactMatches `
                    -and $minimumMatches `
                    -and $fieldsMatch) {
                return $last
            }
        }
        catch {
            $lastError = $_
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $description = if ($null -eq $last) {
        "no state; last error=$lastError"
    }
    else {
        'mode={0}; playback={1}; tick={2}' -f `
            $last.State.RuntimeMode, `
            (Get-StateField `
                -SemanticState $last `
                -Name 'playbackMode'), `
            $last.State.MovieTick
    }
    throw "Expected Runtime state was not observed: $description"
}

function Wait-ReplaySaveReady {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$Label,
        [int]$TimeoutSeconds = 45
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastCatalog = $null
    do {
        $lastCatalog = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'getReplaySaves' `
            -Scope 'observe.replay-saves'
        $lines = @(
            $lastCatalog.Data['entries'] -split "`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        foreach ($line in $lines) {
            $parts = @($line -split ' · ')
            if ($parts.Count -ge 5 `
                    -and $parts[1] -eq $Label `
                    -and $parts[$parts.Count - 1] -eq 'Ready') {
                return [pscustomobject]@{
                    replaySaveId = $parts[0]
                    label = $parts[1]
                    reason = $parts[2]
                    effectiveMovieTick = [long](
                        $parts[3].Substring('tick='.Length))
                    status = $parts[$parts.Count - 1]
                    catalog = Convert-AutomationResult $lastCatalog
                }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $catalogText = if ($null -eq $lastCatalog) {
        '<none>'
    }
    else {
        $lastCatalog.Data['entries']
    }
    throw (
        'Replay save did not become Ready: label={0}; catalog={1}' -f `
            $Label, `
            $catalogText
    )
}

function Wait-ReplayRestorePhase {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string[]]$AllowedPhases,
        [int]$TimeoutSeconds = 120
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    $lastError = $null
    do {
        try {
            $last = $Client.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
            $phase = Get-StateField `
                -SemanticState $last `
                -Name 'replaySaveRestorePhase'
            if ($AllowedPhases -contains $phase) {
                return $last
            }
        }
        catch {
            $lastError = $_
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $lastPhase = if ($null -eq $last) {
        '<none>'
    }
    else {
        Get-StateField `
            -SemanticState $last `
            -Name 'replaySaveRestorePhase'
    }
    throw (
        'Replay restore phase timeout: expected={0}; last={1}; error={2}' -f `
            ($AllowedPhases -join ','), `
            $lastPhase, `
            $lastError
    )
}

function Assert-VerifiedRestorePause {
    param(
        [Parameter(Mandatory)]$SemanticState,
        [Parameter(Mandatory)][string]$Channel
    )

    $phase = Get-StateField `
        -SemanticState $SemanticState `
        -Name 'replaySaveRestorePhase'
    if ($phase -ne 'Paused') {
        throw (
            '{0} replay restore ended in {1}: {2}' -f `
                $Channel, `
                $phase, `
                (Get-StateField `
                    -SemanticState $SemanticState `
                    -Name 'replaySaveRestoreDetail')
        )
    }
    if ((Get-StateField `
                -SemanticState $SemanticState `
                -Name 'replaySaveRestoreStrictSemanticEquivalent') `
            -ne 'true') {
        throw "$Channel replay restore did not prove strict semantic equivalence."
    }
}

function New-StringMap {
    param([hashtable]$Values = @{})

    $result =
        [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::Ordinal)
    foreach ($pair in $Values.GetEnumerator()) {
        $result.Add(
            [string]$pair.Key,
            [string]$pair.Value)
    }
    return $result
}

function Convert-AutomationResult {
    param([Parameter(Mandatory)]$Value)

    $data = [ordered]@{}
    foreach ($pair in $Value.Data.GetEnumerator() |
            Sort-Object Key) {
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

function Assert-AutomationSuccess {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not $Result.Success) {
        throw (
            '{0} failed: {1}: {2}' -f `
                $Label, `
                $Result.ResultCode, `
                $Result.Detail
        )
    }
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
    if (-not $AllowFailure) {
        Assert-AutomationSuccess `
            -Result $result `
            -Label "SDK $CommandId"
    }
    return $result
}

function Acquire-SdkLease {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string[]]$Scopes
    )

    $acquired = Invoke-SdkCommand `
        -Client $Client `
        -CommandId 'acquireControl' `
        -Scope $Scopes[0] `
        -Arguments @{
            scopes = $Scopes -join ','
            ttlSeconds = '300'
        }
    if (-not $acquired.Data.ContainsKey('leaseId')) {
        throw 'SDK acquireControl omitted leaseId.'
    }
    return [pscustomobject]@{
        leaseId = $acquired.Data['leaseId']
        result = $acquired
    }
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

function Invoke-CliCall {
    param(
        [Parameter(Mandatory)][string]$CommandId,
        [Parameter(Mandatory)][string]$Scope,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$ExpectedRuntimeMode,
        [Nullable[long]]$ExpectedMovieTick = $null,
        [switch]$AllowFailure
    )

    $commandArguments = @(
        'automation',
        'call',
        $CommandId,
        $Scope
    ) + $Arguments + @(
        "--expected-mode=$ExpectedRuntimeMode",
        "--bootstrap=$bootstrapPath"
    )
    if ($null -ne $ExpectedMovieTick) {
        $commandArguments +=
            "--expected-tick=$ExpectedMovieTick"
    }
    $result = Invoke-ExternalProcess `
        -FilePath $cliPath `
        -ArgumentList $commandArguments
    if (-not $AllowFailure -and $result.exitCode -ne 0) {
        throw (
            'CLI {0} failed ({1}): {2}{3}' -f `
                $CommandId, `
                $result.exitCode, `
                $result.stdout, `
                $result.stderr
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($result.stdout)) {
        $envelope = $result.stdout |
            ConvertFrom-Json -Depth 100
        $dataJson = [Text.Encoding]::UTF8.GetString(
            [Convert]::FromBase64String(
                $envelope.dataBase64))
        $data = $dataJson |
            ConvertFrom-Json -Depth 100
        $result |
            Add-Member `
                -NotePropertyName envelope `
                -NotePropertyValue $envelope
        $result |
            Add-Member `
                -NotePropertyName data `
                -NotePropertyValue $data
    }
    return $result
}

function Start-McpBridge {
    param([Parameter(Mandatory)][string]$LogPath)

    Stop-McpBridge
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $agentBridgePath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    [void]$start.ArgumentList.Add(
        "--bootstrap=$bootstrapPath")
    $script:mcp = [Diagnostics.Process]::Start($start)
    if ($null -eq $script:mcp) {
        throw 'AgentBridge did not start.'
    }
    $script:ownedHelperPids += $script:mcp.Id
    $script:mcpLog = $LogPath
    Set-Content `
        -LiteralPath $script:mcpLog `
        -Value '' `
        -Encoding utf8NoBOM
}

function Send-McpNotification {
    param(
        [Parameter(Mandatory)][string]$Method
    )

    $request = [ordered]@{
        jsonrpc = '2.0'
        method = $Method
    }
    $json = $request |
        ConvertTo-Json -Compress -Depth 50
    $script:mcp.StandardInput.WriteLine($json)
    $script:mcp.StandardInput.Flush()
    Add-Content `
        -LiteralPath $script:mcpLog `
        -Value ("request " + $json) `
        -Encoding utf8NoBOM
}

function Send-McpRequest {
    param(
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)]$Parameters
    )

    $request = [ordered]@{
        jsonrpc = '2.0'
        id = $Id
        method = $Method
        params = $Parameters
    }
    $json = $request |
        ConvertTo-Json -Compress -Depth 50
    $script:mcp.StandardInput.WriteLine($json)
    $script:mcp.StandardInput.Flush()
    Add-Content `
        -LiteralPath $script:mcpLog `
        -Value ("request " + $json) `
        -Encoding utf8NoBOM
    $readTask = $script:mcp.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait(20000)) {
        throw "MCP request timed out: $Method"
    }
    $line = $readTask.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) {
        $stderr = $script:mcp.StandardError.ReadToEnd()
        throw "MCP response was empty: $stderr"
    }
    Add-Content `
        -LiteralPath $script:mcpLog `
        -Value ("response " + $line) `
        -Encoding utf8NoBOM
    return $line | ConvertFrom-Json -Depth 100
}

function Initialize-McpBridge {
    $initialize = Send-McpRequest `
        -Id 1 `
        -Method 'initialize' `
        -Parameters ([ordered]@{
            protocolVersion = '2025-11-25'
            capabilities = [ordered]@{}
            clientInfo = [ordered]@{
                name = 'HollowKnightTAS-T15-Acceptance'
                version = '1'
            }
        })
    if ($initialize.result.protocolVersion -ne '2025-11-25') {
        throw 'AgentBridge negotiated the wrong MCP version.'
    }
    Send-McpNotification -Method 'notifications/initialized'
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][string]$Name,
        [hashtable]$Arguments = @{},
        [switch]$AllowFailure
    )

    $response = Send-McpRequest `
        -Id $Id `
        -Method 'tools/call' `
        -Parameters ([ordered]@{
            name = $Name
            arguments = $Arguments
        })
    $errorProperty = $response.PSObject.Properties['error']
    if ($null -ne $errorProperty) {
        if (-not $AllowFailure) {
            throw (
                'MCP {0} returned protocol error: {1}' -f `
                    $Name, `
                    ($errorProperty.Value |
                        ConvertTo-Json -Compress)
            )
        }
        return $response
    }
    $resultProperty = $response.PSObject.Properties['result']
    if ($null -eq $resultProperty `
            -or $null -eq $resultProperty.Value `
            -or $null -eq $resultProperty.Value.
                structuredContent) {
        throw "MCP $Name omitted structuredContent."
    }
    $toolResult = $resultProperty.Value
    if (-not $AllowFailure `
            -and -not $toolResult.structuredContent.success) {
        throw (
            'MCP {0} failed: {1}: {2}' -f `
                $Name, `
                $toolResult.structuredContent.resultCode, `
                $toolResult.structuredContent.detail
        )
    }
    return $response
}

function Invoke-DisabledDiscovery {
    $case = Start-GameCase `
        -Name 'disabled' `
        -Mode 'Disabled' `
        -DebugMutationEnabled $false
    try {
        if (Test-Path -LiteralPath $bootstrapPath) {
            throw 'Disabled mode unexpectedly exposed a bootstrap.'
        }

        $sdkRejected = $false
        $sdkError = ''
        try {
            $unexpected = New-SdkClient `
                -ClientId 't15-sdk-disabled'
            Dispose-SdkClient -Client $unexpected
        }
        catch {
            $sdkRejected = $true
            $sdkError = $_.Exception.GetType().Name
        }
        if (-not $sdkRejected) {
            throw 'Disabled mode accepted an SDK connection.'
        }

        $cli = Invoke-ExternalProcess `
            -FilePath $cliPath `
            -ArgumentList @(
                'automation',
                'state',
                "--bootstrap=$bootstrapPath"
            )
        if ($cli.exitCode -eq 0) {
            throw 'Disabled mode accepted a CLI state request.'
        }

        Start-McpBridge `
            -LogPath (
                Join-Path $case.directory 'mcp-disabled.jsonl'
            )
        $mcp = Send-McpRequest `
            -Id 1 `
            -Method 'initialize' `
            -Parameters ([ordered]@{
                protocolVersion = '2025-11-25'
                capabilities = [ordered]@{}
                clientInfo = [ordered]@{
                    name = 'HollowKnightTAS-T15-Disabled'
                    version = '1'
                }
            })
        if ($null -eq $mcp.PSObject.Properties['error']) {
            throw 'Disabled mode accepted an AgentBridge connection.'
        }
        Stop-McpBridge

        $proof = [ordered]@{
            bootstrapAbsent = $true
            sdkRejected = $sdkRejected
            sdkErrorType = $sdkError
            cliRejected = $true
            cliExitCode = $cli.exitCode
            agentBridgeRejected = $true
            agentBridgeErrorCode = $mcp.error.code
            runtimeProbePassed = $true
        }
        $proof |
            ConvertTo-Json -Depth 20 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'disabled-proof.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add(
            [pscustomobject]@{
                name = $case.name
                mode = $case.mode
                sessionId = $case.sessionId
                bootstrapAbsent = $true
                sdkRejected = $true
                cliRejected = $true
                mcpRejected = $true
                runtimeUnaffected = $true
            })
        return $case
    }
    finally {
        Close-RunProcesses
    }
}

function Invoke-ReadOnlyDiscovery {
    $case = Start-GameCase `
        -Name 'read-only' `
        -Mode 'ReadOnly' `
        -DebugMutationEnabled $false
    $sdk = $null
    try {
        $sdk = New-SdkClient -ClientId 't15-sdk-read-only'
        $sdkState = Wait-SdkSemanticState -Client $sdk
        $sdkJson = $sdkState.ToFriendlyJson()
        Set-Content `
            -LiteralPath (Join-Path $case.directory 'sdk-state.json') `
            -Value $sdkJson `
            -Encoding utf8NoBOM
        $sdkObject = $sdkJson |
            ConvertFrom-Json -Depth 100

        $cli = Invoke-ExternalProcess `
            -FilePath $cliPath `
            -ArgumentList @(
                'automation',
                'state',
                "--bootstrap=$bootstrapPath"
            )
        if ($cli.exitCode -ne 0) {
            throw "CLI state failed: $($cli.stderr)"
        }
        Set-Content `
            -LiteralPath (Join-Path $case.directory 'cli-state.json') `
            -Value $cli.stdout.Trim() `
            -Encoding utf8NoBOM
        $cliObject = $cli.stdout |
            ConvertFrom-Json -Depth 100

        Start-McpBridge `
            -LogPath (Join-Path $case.directory 'mcp.jsonl')
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $mcpState = Send-McpRequest `
            -Id 3 `
            -Method 'tools/call' `
            -Parameters ([ordered]@{
                name = 'hktas_get_state'
                arguments = [ordered]@{}
            })
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $case.directory 'mcp-tools.json') `
                -Encoding utf8NoBOM
        $mcpState |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $case.directory 'mcp-state.json') `
                -Encoding utf8NoBOM

        $sdkHash = $sdkObject.state.semanticSnapshotSha256
        $cliHash = $cliObject.state.semanticSnapshotSha256
        $mcpHash = $mcpState.result.structuredContent.state.
            semanticSnapshotSha256
        if ($sdkHash -ne $cliHash -or $sdkHash -ne $mcpHash) {
            throw 'SDK, CLI, and MCP semantic snapshot hashes differ.'
        }
        $sdkKeys = @(
            $sdkObject.semanticValues.PSObject.Properties.Name |
                Sort-Object
        )
        $cliKeys = @(
            $cliObject.semanticValues.PSObject.Properties.Name |
                Sort-Object
        )
        $mcpKeys = @(
            $mcpState.result.structuredContent.semanticValues.
                PSObject.Properties.Name |
                Sort-Object
        )
        Assert-SequenceEqual `
            -Expected $sdkKeys `
            -Actual $cliKeys `
            -Label 'SDK/CLI semantic keys'
        Assert-SequenceEqual `
            -Expected $sdkKeys `
            -Actual $mcpKeys `
            -Label 'SDK/MCP semantic keys'

        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        if ($toolNames -contains 'hktas_pause' `
                -or $toolNames -contains 'hktas_acquire_control' `
                -or $toolNames -contains 'hktas_set_hero_pose' `
                -or $toolNames -notcontains 'hktas_get_state' `
                -or $toolNames -notcontains `
                    'hktas_propose_movie_patch') {
            throw 'ReadOnly MCP tool visibility is incorrect.'
        }

        $beforeHash = $sdkHash
        $rejected = Invoke-ExternalProcess `
            -FilePath $cliPath `
            -ArgumentList @(
                'automation',
                'call',
                'pause',
                'control.playback',
                '--expected-mode=Running',
                "--bootstrap=$bootstrapPath"
            )
        if ($rejected.exitCode -ne 3) {
            throw 'ReadOnly CLI control was not rejected.'
        }
        Set-Content `
            -LiteralPath (
                Join-Path $case.directory 'cli-control-rejected.txt'
            ) `
            -Value ($rejected.stdout + $rejected.stderr) `
            -Encoding utf8NoBOM
        $after = Wait-SdkSemanticState -Client $sdk
        if ($after.State.SemanticSnapshotSha256 -ne $beforeHash) {
            throw 'ReadOnly rejected control changed semantic state.'
        }

        $cases.Add(
            [pscustomobject]@{
                name = $case.name
                mode = $case.mode
                sessionId = $sdkState.State.SessionId
                manifestSha256 =
                    $sdkState.State.ManifestSha256
                semanticSnapshotSha256 = $sdkHash
                semanticKeyCount = $sdkKeys.Count
                sdk = 'pass'
                cli = 'pass'
                mcp = 'pass'
                controlRejected = $true
            })
        return $case
    }
    finally {
        Dispose-SdkClient -Client $sdk
        Stop-McpBridge
    }
}

function Invoke-ApprovedControlParity {
    $case = Start-GameCase `
        -Name 'approved-control' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $false `
        -InputNeutralGameplay
    $sdk = $null
    $sdkLease = ''
    $sdkScopes = @(
        'control.playback',
        'control.step',
        'control.run-until',
        'control.recording',
        'control.replay-save'
    )
    $records =
        [System.Collections.Generic.List[object]]::new()
    try {
        $sdk = New-SdkClient `
            -ClientId 't15-sdk-approved-control'
        $initial = Wait-SdkState `
            -Client $sdk `
            -ControlMode 'Running'
        if ($initial.State.ActiveCapabilities `
                -notcontains 'pause' `
                -or $initial.State.ActiveCapabilities `
                    -notcontains 'step' `
                -or $initial.State.ActiveCapabilities `
                    -notcontains 'runUntil' `
                -or $initial.State.ActiveCapabilities `
                    -contains 'setHeroPose' `
                -or $initial.State.ActiveCapabilities `
                    -contains 'setPlayerResources') {
            throw 'ApprovedControl/debug=false capability set is incorrect.'
        }

        $lease = Acquire-SdkLease `
            -Client $sdk `
            -Scopes $sdkScopes
        $sdkLease = $lease.leaseId
        $records.Add(
            [ordered]@{
                channel = 'sdk'
                operation = 'acquireControl'
                result = Convert-AutomationResult $lease.result
            })

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforePause = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $pause = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'pause' `
                -Scope 'control.playback' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Running'
            $paused = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused'

            $stepTick = $paused.State.MovieTick
            $step = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'step' `
                -Scope 'control.step' `
                -Arguments @{ count = '1' } `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $stepTick
            $stepped = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -ExactMovieTick ($stepTick + 1)

            $target = $stepped.State.MovieTick + 2
            $runUntil = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'runUntil' `
                -Scope 'control.run-until' `
                -Arguments @{
                    targetMovieTick = [string]$target
                } `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $stepped.State.MovieTick
            $ran = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -MinimumMovieTick $target
            if ($ran.State.MovieTick -ne $target) {
                throw (
                    'SDK runUntil stopped at {0}, expected {1}.' -f `
                        $ran.State.MovieTick, `
                        $target
                )
            }

            $resume = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'resume' `
                -Scope 'control.playback' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $ran.State.MovieTick
            $running = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $records.Add(
                [ordered]@{
                    channel = 'sdk'
                    iteration = $iteration
                    pause = Convert-AutomationResult $pause
                    step = Convert-AutomationResult $step
                    runUntil = Convert-AutomationResult $runUntil
                    resume = Convert-AutomationResult $resume
                    beforeTick = $beforePause.State.MovieTick
                    pausedTick = $paused.State.MovieTick
                    steppedTick = $stepped.State.MovieTick
                    runUntilTarget = $target
                    runUntilObservedTick = $ran.State.MovieTick
                    finalMode = $running.State.RuntimeMode
                })
        }

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })
            $startRecording = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'startRecording' `
                -Scope 'control.recording' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -Fields @{ recordingActive = 'true' })
            $stopRecording = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'stopRecording' `
                -Scope 'control.recording' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Running'
            if (-not $stopRecording.Data.ContainsKey('movieId') `
                    -or [string]::IsNullOrEmpty(
                        $stopRecording.Data['movieId'])) {
                throw 'SDK stopRecording omitted movieId.'
            }
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })

            $startReplay = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'startReplay' `
                -Scope 'control.playback' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Replaying')
            $stopReplay = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'stopReplay' `
                -Scope 'control.playback' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle')
            $records.Add(
                [ordered]@{
                    channel = 'sdk'
                    family = 'record-replay'
                    iteration = $iteration
                    startRecordingCode =
                        $startRecording.ResultCode
                    stopRecordingCode =
                        $stopRecording.ResultCode
                    movieId = $stopRecording.Data['movieId']
                    startReplayCode = $startReplay.ResultCode
                    stopReplayCode = $stopReplay.ResultCode
                    finalPlaybackMode = 'Idle'
                    recordingInactive = $true
                })
        }

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforeSave = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle'
            $label = 't15-sdk-save-{0:D2}' -f $iteration
            $createSave = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'createReplaySave' `
                -Scope 'control.replay-save' `
                -Arguments @{ label = $label } `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode $beforeSave.State.RuntimeMode
            $entry = Wait-ReplaySaveReady `
                -Client $sdk `
                -Label $label
            $beforeRestore = Wait-SdkSemanticState -Client $sdk
            $restore = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'restoreReplaySave' `
                -Scope 'control.replay-save' `
                -Arguments @{
                    replaySaveId = $entry.replaySaveId
                } `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode $beforeRestore.State.RuntimeMode
            $decision = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @(
                    'AwaitingOverwriteApproval',
                    'Paused',
                    'Failed',
                    'Cancelled'
                )
            $approve = $null
            if ((Get-StateField `
                        -SemanticState $decision `
                        -Name 'replaySaveRestorePhase') `
                    -eq 'AwaitingOverwriteApproval') {
                $approve = Invoke-SdkCommand `
                    -Client $sdk `
                    -CommandId 'approveReplaySaveOverwrite' `
                    -Scope 'control.replay-save' `
                    -Arguments @{ approved = 'true' } `
                    -LeaseId $sdkLease `
                    -ExpectedRuntimeMode $decision.State.RuntimeMode `
                    -ExpectedMovieTick $decision.State.MovieTick
            }
            $verified = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Paused', 'Failed', 'Cancelled')
            Assert-VerifiedRestorePause `
                -SemanticState $verified `
                -Channel 'SDK'
            $resumeRestore = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'resumeReplaySaveRestore' `
                -Scope 'control.replay-save' `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode $verified.State.RuntimeMode `
                -ExpectedMovieTick $verified.State.MovieTick
            $completed = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Completed', 'Failed')
            if ((Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestorePhase') `
                    -ne 'Completed' `
                    -or (Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestoreActive') `
                    -ne 'false') {
                throw (
                    'SDK replay restore did not complete: {0}' -f `
                        (Get-StateField `
                            -SemanticState $completed `
                            -Name 'replaySaveRestoreDetail')
                )
            }
            $records.Add(
                [ordered]@{
                    channel = 'sdk'
                    family = 'replay-save'
                    iteration = $iteration
                    replaySaveId = $entry.replaySaveId
                    effectiveMovieTick = $entry.effectiveMovieTick
                    createCode = $createSave.ResultCode
                    restoreCode = $restore.ResultCode
                    overwriteApprovalRequired = $null -ne $approve
                    approveCode = if ($null -eq $approve) {
                        'not-required'
                    }
                    else {
                        $approve.ResultCode
                    }
                    verifiedPhase = 'Paused'
                    strictSemanticEquivalent = $true
                    resumeCode = $resumeRestore.ResultCode
                    finalPhase = 'Completed'
                    restoreInactive = $true
                })
        }

        $released = Release-SdkLease `
            -Client $sdk `
            -Scope $sdkScopes[0] `
            -LeaseId $sdkLease
        $sdkLease = ''
        $records.Add(
            [ordered]@{
                channel = 'sdk'
                operation = 'releaseControl'
                result = Convert-AutomationResult $released
            })

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $before = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $cliPause = Invoke-CliCall `
                -CommandId 'pause' `
                -Scope 'control.playback' `
                -ExpectedRuntimeMode 'Running'
            $paused = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused'
            $tick = $paused.State.MovieTick
            $cliStep = Invoke-CliCall `
                -CommandId 'step' `
                -Scope 'control.step' `
                -Arguments @('count=1') `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $tick
            $stepped = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -ExactMovieTick ($tick + 1)
            $target = $stepped.State.MovieTick + 2
            $cliRunUntil = Invoke-CliCall `
                -CommandId 'runUntil' `
                -Scope 'control.run-until' `
                -Arguments @("targetMovieTick=$target") `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $stepped.State.MovieTick
            $ran = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -MinimumMovieTick $target
            if ($ran.State.MovieTick -ne $target) {
                throw (
                    'CLI runUntil stopped at {0}, expected {1}.' -f `
                        $ran.State.MovieTick, `
                        $target
                )
            }
            $cliResume = Invoke-CliCall `
                -CommandId 'resume' `
                -Scope 'control.playback' `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $ran.State.MovieTick
            $running = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $records.Add(
                [ordered]@{
                    channel = 'cli'
                    iteration = $iteration
                    pauseExitCode = $cliPause.exitCode
                    stepExitCode = $cliStep.exitCode
                    runUntilExitCode = $cliRunUntil.exitCode
                    resumeExitCode = $cliResume.exitCode
                    beforeTick = $before.State.MovieTick
                    pausedTick = $paused.State.MovieTick
                    steppedTick = $stepped.State.MovieTick
                    runUntilTarget = $target
                    runUntilObservedTick = $ran.State.MovieTick
                    finalMode = $running.State.RuntimeMode
                })
        }

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })
            $cliStartRecording = Invoke-CliCall `
                -CommandId 'startRecording' `
                -Scope 'control.recording' `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -Fields @{ recordingActive = 'true' })
            $cliStopRecording = Invoke-CliCall `
                -CommandId 'stopRecording' `
                -Scope 'control.recording' `
                -ExpectedRuntimeMode 'Running'
            $cliMovie = $cliStopRecording.data
            if ([string]::IsNullOrEmpty(
                    $cliMovie.movieId)) {
                throw 'CLI stopRecording omitted movieId.'
            }
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })
            $cliStartReplay = Invoke-CliCall `
                -CommandId 'startReplay' `
                -Scope 'control.playback' `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Replaying')
            $cliStopReplay = Invoke-CliCall `
                -CommandId 'stopReplay' `
                -Scope 'control.playback' `
                -ExpectedRuntimeMode 'Running'
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle')
            $records.Add(
                [ordered]@{
                    channel = 'cli'
                    family = 'record-replay'
                    iteration = $iteration
                    startRecordingExitCode =
                        $cliStartRecording.exitCode
                    stopRecordingExitCode =
                        $cliStopRecording.exitCode
                    movieId = $cliMovie.movieId
                    startReplayExitCode =
                        $cliStartReplay.exitCode
                    stopReplayExitCode =
                        $cliStopReplay.exitCode
                    finalPlaybackMode = 'Idle'
                    recordingInactive = $true
                })
        }

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforeSave = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle'
            $label = 't15-cli-save-{0:D2}' -f $iteration
            $cliCreateSave = Invoke-CliCall `
                -CommandId 'createReplaySave' `
                -Scope 'control.replay-save' `
                -Arguments @("label=$label") `
                -ExpectedRuntimeMode $beforeSave.State.RuntimeMode
            $entry = Wait-ReplaySaveReady `
                -Client $sdk `
                -Label $label
            $beforeRestore = Wait-SdkSemanticState -Client $sdk
            $cliRestore = Invoke-CliCall `
                -CommandId 'restoreReplaySave' `
                -Scope 'control.replay-save' `
                -Arguments @(
                    "replaySaveId=$($entry.replaySaveId)"
                ) `
                -ExpectedRuntimeMode $beforeRestore.State.RuntimeMode
            $decision = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @(
                    'AwaitingOverwriteApproval',
                    'Paused',
                    'Failed',
                    'Cancelled'
                )
            $cliApprove = $null
            if ((Get-StateField `
                        -SemanticState $decision `
                        -Name 'replaySaveRestorePhase') `
                    -eq 'AwaitingOverwriteApproval') {
                $cliApprove = Invoke-CliCall `
                    -CommandId 'approveReplaySaveOverwrite' `
                    -Scope 'control.replay-save' `
                    -Arguments @('approved=true') `
                    -ExpectedRuntimeMode $decision.State.RuntimeMode `
                    -ExpectedMovieTick $decision.State.MovieTick
            }
            $verified = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Paused', 'Failed', 'Cancelled')
            Assert-VerifiedRestorePause `
                -SemanticState $verified `
                -Channel 'CLI'
            $cliResumeRestore = Invoke-CliCall `
                -CommandId 'resumeReplaySaveRestore' `
                -Scope 'control.replay-save' `
                -ExpectedRuntimeMode $verified.State.RuntimeMode `
                -ExpectedMovieTick $verified.State.MovieTick
            $completed = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Completed', 'Failed')
            if ((Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestorePhase') `
                    -ne 'Completed' `
                    -or (Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestoreActive') `
                    -ne 'false') {
                throw (
                    'CLI replay restore did not complete: {0}' -f `
                        (Get-StateField `
                            -SemanticState $completed `
                            -Name 'replaySaveRestoreDetail')
                )
            }
            $records.Add(
                [ordered]@{
                    channel = 'cli'
                    family = 'replay-save'
                    iteration = $iteration
                    replaySaveId = $entry.replaySaveId
                    effectiveMovieTick = $entry.effectiveMovieTick
                    createExitCode = $cliCreateSave.exitCode
                    restoreExitCode = $cliRestore.exitCode
                    overwriteApprovalRequired =
                        $null -ne $cliApprove
                    approveExitCode = if ($null -eq $cliApprove) {
                        'not-required'
                    }
                    else {
                        $cliApprove.exitCode
                    }
                    verifiedPhase = 'Paused'
                    strictSemanticEquivalent = $true
                    resumeExitCode = $cliResumeRestore.exitCode
                    finalPhase = 'Completed'
                    restoreInactive = $true
                })
        }

        Start-McpBridge `
            -LogPath (Join-Path $case.directory 'mcp-control.jsonl')
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        foreach ($required in @(
                'hktas_acquire_control',
                'hktas_pause',
                'hktas_resume',
                'hktas_step',
                'hktas_run_until',
                'hktas_start_recording',
                'hktas_stop_recording',
                'hktas_start_replay',
                'hktas_stop_replay',
                'hktas_create_replay_save',
                'hktas_restore_replay_save',
                'hktas_approve_replay_save_overwrite',
                'hktas_cancel_replay_save_restore',
                'hktas_resume_replay_save_restore',
                'hktas_release_control'
            )) {
            if ($toolNames -notcontains $required) {
                throw "ApprovedControl MCP omitted $required."
            }
        }
        if ($toolNames -contains 'hktas_set_hero_pose' `
                -or $toolNames -contains `
                    'hktas_set_player_resources') {
            throw 'ApprovedControl/debug=false exposed mutation tools.'
        }
        $mcpId = 10
        $acquired = Invoke-McpTool `
            -Id $mcpId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @(
                    'control.playback',
                    'control.step',
                    'control.run-until',
                    'control.recording',
                    'control.replay-save'
                )
                ttlSeconds = 300
            }
        $mcpId++
        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $before = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $mcpPause = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_pause' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $mcpId++
            $paused = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused'
            $tick = $paused.State.MovieTick
            $mcpStep = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_step' `
                -Arguments @{
                    count = 1
                    expectedRuntimeMode = 'Paused'
                    expectedMovieTick = $tick
                }
            $mcpId++
            $stepped = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -ExactMovieTick ($tick + 1)
            $target = $stepped.State.MovieTick + 2
            $mcpRunUntil = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_run_until' `
                -Arguments @{
                    targetMovieTick = $target
                    expectedRuntimeMode = 'Paused'
                    expectedMovieTick = $stepped.State.MovieTick
                }
            $mcpId++
            $ran = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -MinimumMovieTick $target
            if ($ran.State.MovieTick -ne $target) {
                throw (
                    'MCP runUntil stopped at {0}, expected {1}.' -f `
                        $ran.State.MovieTick, `
                        $target
                )
            }
            $mcpResume = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_resume' `
                -Arguments @{
                    expectedRuntimeMode = 'Paused'
                    expectedMovieTick = $ran.State.MovieTick
                }
            $mcpId++
            $running = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running'
            $records.Add(
                [ordered]@{
                    channel = 'mcp'
                    iteration = $iteration
                    pauseCode = $mcpPause.result.
                        structuredContent.resultCode
                    stepCode = $mcpStep.result.
                        structuredContent.resultCode
                    runUntilCode = $mcpRunUntil.result.
                        structuredContent.resultCode
                    resumeCode = $mcpResume.result.
                        structuredContent.resultCode
                    beforeTick = $before.State.MovieTick
                    pausedTick = $paused.State.MovieTick
                    steppedTick = $stepped.State.MovieTick
                    runUntilTarget = $target
                    runUntilObservedTick = $ran.State.MovieTick
                    finalMode = $running.State.RuntimeMode
                })
        }
        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })
            $mcpStartRecording = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_start_recording' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $mcpId++
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -Fields @{ recordingActive = 'true' })
            $mcpStopRecording = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_stop_recording' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $mcpId++
            $mcpMovieId = $mcpStopRecording.result.
                structuredContent.data.movieId
            if ([string]::IsNullOrEmpty($mcpMovieId)) {
                throw 'MCP stopRecording omitted movieId.'
            }
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle' `
                -Fields @{ recordingActive = 'false' })
            $mcpStartReplay = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_start_replay' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $mcpId++
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Replaying')
            $mcpStopReplay = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_stop_replay' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $mcpId++
            [void](Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle')
            $records.Add(
                [ordered]@{
                    channel = 'mcp'
                    family = 'record-replay'
                    iteration = $iteration
                    startRecordingCode = $mcpStartRecording.result.
                        structuredContent.resultCode
                    stopRecordingCode = $mcpStopRecording.result.
                        structuredContent.resultCode
                    movieId = $mcpMovieId
                    startReplayCode = $mcpStartReplay.result.
                        structuredContent.resultCode
                    stopReplayCode = $mcpStopReplay.result.
                        structuredContent.resultCode
                    finalPlaybackMode = 'Idle'
                    recordingInactive = $true
                })
        }
        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforeSave = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Running' `
                -PlaybackMode 'Idle'
            $label = 't15-mcp-save-{0:D2}' -f $iteration
            $mcpCreateSave = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_create_replay_save' `
                -Arguments @{
                    label = $label
                    expectedRuntimeMode =
                        $beforeSave.State.RuntimeMode
                }
            $mcpId++
            $entry = Wait-ReplaySaveReady `
                -Client $sdk `
                -Label $label
            $beforeRestore = Wait-SdkSemanticState -Client $sdk
            $mcpRestore = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_restore_replay_save' `
                -Arguments @{
                    replaySaveId = $entry.replaySaveId
                    expectedRuntimeMode =
                        $beforeRestore.State.RuntimeMode
                }
            $mcpId++
            $decision = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @(
                    'AwaitingOverwriteApproval',
                    'Paused',
                    'Failed',
                    'Cancelled'
                )
            $mcpApprove = $null
            if ((Get-StateField `
                        -SemanticState $decision `
                        -Name 'replaySaveRestorePhase') `
                    -eq 'AwaitingOverwriteApproval') {
                $mcpApprove = Invoke-McpTool `
                    -Id $mcpId `
                    -Name 'hktas_approve_replay_save_overwrite' `
                    -Arguments @{
                        approved = $true
                        expectedRuntimeMode =
                            $decision.State.RuntimeMode
                        expectedMovieTick =
                            $decision.State.MovieTick
                    }
                $mcpId++
            }
            $verified = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Paused', 'Failed', 'Cancelled')
            Assert-VerifiedRestorePause `
                -SemanticState $verified `
                -Channel 'MCP'
            $mcpResumeRestore = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_resume_replay_save_restore' `
                -Arguments @{
                    expectedRuntimeMode =
                        $verified.State.RuntimeMode
                    expectedMovieTick =
                        $verified.State.MovieTick
                }
            $mcpId++
            $completed = Wait-ReplayRestorePhase `
                -Client $sdk `
                -AllowedPhases @('Completed', 'Failed')
            if ((Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestorePhase') `
                    -ne 'Completed' `
                    -or (Get-StateField `
                        -SemanticState $completed `
                        -Name 'replaySaveRestoreActive') `
                    -ne 'false') {
                throw (
                    'MCP replay restore did not complete: {0}' -f `
                        (Get-StateField `
                            -SemanticState $completed `
                            -Name 'replaySaveRestoreDetail')
                )
            }
            $records.Add(
                [ordered]@{
                    channel = 'mcp'
                    family = 'replay-save'
                    iteration = $iteration
                    replaySaveId = $entry.replaySaveId
                    effectiveMovieTick = $entry.effectiveMovieTick
                    createCode = $mcpCreateSave.result.
                        structuredContent.resultCode
                    restoreCode = $mcpRestore.result.
                        structuredContent.resultCode
                    overwriteApprovalRequired =
                        $null -ne $mcpApprove
                    approveCode = if ($null -eq $mcpApprove) {
                        'not-required'
                    }
                    else {
                        $mcpApprove.result.
                            structuredContent.resultCode
                    }
                    verifiedPhase = 'Paused'
                    strictSemanticEquivalent = $true
                    resumeCode = $mcpResumeRestore.result.
                        structuredContent.resultCode
                    finalPhase = 'Completed'
                    restoreInactive = $true
                })
        }
        $releasedMcp = Invoke-McpTool `
            -Id $mcpId `
            -Name 'hktas_release_control'
        $mcpId++

        $records |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'control-parity.json'
                ) `
                -Encoding utf8NoBOM
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-tools.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add(
            [pscustomobject]@{
                name = $case.name
                mode = $case.mode
                sessionId = $initial.State.SessionId
                manifestSha256 =
                    $initial.State.ManifestSha256
                debugMutationEnabled = $false
                sdkPauseStepRunUntilResume =
                    "$ParityIterations/$ParityIterations"
                cliPauseStepRunUntilResume =
                    "$ParityIterations/$ParityIterations"
                mcpPauseStepRunUntilResume =
                    "$ParityIterations/$ParityIterations"
                sdkRecordReplay =
                    "$ParityIterations/$ParityIterations"
                cliRecordReplay =
                    "$ParityIterations/$ParityIterations"
                mcpRecordReplay =
                    "$ParityIterations/$ParityIterations"
                sdkReplaySave =
                    "$ParityIterations/$ParityIterations"
                cliReplaySave =
                    "$ParityIterations/$ParityIterations"
                mcpReplaySave =
                    "$ParityIterations/$ParityIterations"
                mutationToolsHidden = $true
                finalVerificationEligibility =
                    (Get-StateField `
                        -SemanticState (
                            Wait-SdkSemanticState -Client $sdk
                        ) `
                        -Name 'verificationEligibility')
            })
        return $case
    }
    finally {
        if (-not [string]::IsNullOrEmpty($sdkLease) `
                -and $null -ne $sdk) {
            try {
                [void](Release-SdkLease `
                    -Client $sdk `
                    -Scope $sdkScopes[0] `
                    -LeaseId $sdkLease)
            }
            catch {
            }
        }
        Dispose-SdkClient -Client $sdk
        Stop-McpBridge
        Close-RunProcesses
    }
}

function Invoke-TypedMutationParity {
    $case = Start-GameCase `
        -Name 'approved-mutation' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $true `
        -InputNeutralGameplay
    $sdk = $null
    $observer = $null
    $sdkLease = ''
    $sdkScopes = @(
        'control.playback',
        'debug.state.pose',
        'debug.state.resources'
    )
    $records =
        [System.Collections.Generic.List[object]]::new()
    $rejections =
        [System.Collections.Generic.List[object]]::new()
    try {
        $sdk = New-SdkClient `
            -ClientId 't15-sdk-approved-mutation'
        $initial = Wait-SdkState `
            -Client $sdk `
            -ControlMode 'Running' `
            -PlaybackMode 'Idle'
        foreach ($required in @(
                'setHeroPose',
                'setPlayerResources'
            )) {
            if ($initial.State.ActiveCapabilities -notcontains $required) {
                throw "Debug-enabled Runtime omitted $required."
            }
        }
        if ((Get-StateField `
                    -SemanticState $initial `
                    -Name 'verificationEligibility') -ne 'Eligible') {
            throw 'Mutation session did not start verification-eligible.'
        }

        $initialPose = New-HeroPoseMutationCase `
            -SemanticState $initial `
            -Iteration 1
        $noLease = Invoke-SdkCommand `
            -Client $sdk `
            -CommandId 'setHeroPose' `
            -Scope 'debug.state.pose' `
            -Arguments $initialPose.arguments `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $initial.State.MovieTick `
            -AllowFailure
        if ($noLease.ResultCode -ne 'LeaseRequired') {
            throw (
                'Mutation without lease returned {0}, expected LeaseRequired.' -f `
                    $noLease.ResultCode
            )
        }
        $rejections.Add(
            [ordered]@{
                label = 'no-lease'
                resultCode = $noLease.ResultCode
                accepted = $false
            })

        $acquired = Acquire-SdkLease `
            -Client $sdk `
            -Scopes $sdkScopes
        $sdkLease = $acquired.leaseId
        $records.Add(
            [ordered]@{
                channel = 'sdk'
                operation = 'acquireControl'
                result = Convert-AutomationResult $acquired.result
            })

        $unpaused = Invoke-SdkCommand `
            -Client $sdk `
            -CommandId 'setHeroPose' `
            -Scope 'debug.state.pose' `
            -Arguments $initialPose.arguments `
            -LeaseId $sdkLease `
            -ExpectedRuntimeMode 'Running' `
            -ExpectedMovieTick $initial.State.MovieTick `
            -AllowFailure
        if ($unpaused.ResultCode -ne 'PreconditionFailed') {
            throw (
                'Unpaused mutation returned {0}, expected PreconditionFailed.' -f `
                    $unpaused.ResultCode
            )
        }
        $rejections.Add(
            [ordered]@{
                label = 'unpaused'
                resultCode = $unpaused.ResultCode
                accepted = $false
            })

        $pause = Invoke-SdkCommand `
            -Client $sdk `
            -CommandId 'pause' `
            -Scope 'control.playback' `
            -LeaseId $sdkLease `
            -ExpectedRuntimeMode 'Running'
        $paused = Wait-SdkState `
            -Client $sdk `
            -ControlMode 'Paused' `
            -PlaybackMode 'Idle'
        $records.Add(
            [ordered]@{
                channel = 'sdk'
                operation = 'pause'
                result = Convert-AutomationResult $pause
                movieTick = $paused.State.MovieTick
            })

        $validPose = New-HeroPoseMutationCase `
            -SemanticState $paused `
            -Iteration 1
        $rejections.Add(
            (Invoke-SdkRejectedMutationCase `
                -Client $sdk `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.resources' `
                -Arguments $validPose.arguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $paused.State.MovieTick `
                -ExpectedCodes @('CapabilityMismatch') `
                -Label 'wrong-scope'))

        $staleTick = [Math]::Max(
            0,
            $paused.State.MovieTick - 1)
        if ($staleTick -eq $paused.State.MovieTick) {
            throw 'Mutation stale-tick case requires a positive movie tick.'
        }
        $staleTickArguments = $validPose.arguments.Clone()
        $staleTickArguments['expectedMovieTick'] =
            [string]$staleTick
        $rejections.Add(
            (Invoke-SdkRejectedMutationCase `
                -Client $sdk `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.pose' `
                -Arguments $staleTickArguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $staleTick `
                -ExpectedCodes @('PreconditionFailed') `
                -Label 'stale-tick'))

        $staleHashArguments = $validPose.arguments.Clone()
        $staleHashArguments['expectedSnapshotSha256'] =
            '0000000000000000000000000000000000000000000000000000000000000000'
        $rejections.Add(
            (Invoke-SdkRejectedMutationCase `
                -Client $sdk `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.pose' `
                -Arguments $staleHashArguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $paused.State.MovieTick `
                -ExpectedCodes @(
                    'PreconditionFailed',
                    'RuntimeRejected'
                ) `
                -Label 'stale-hash'))

        foreach ($nonFinite in @('NaN', 'Infinity')) {
            $nonFiniteArguments = $validPose.arguments.Clone()
            $nonFiniteArguments['positionX'] = $nonFinite
            $rejections.Add(
                (Invoke-SdkRejectedMutationCase `
                    -Client $sdk `
                    -CommandId 'setHeroPose' `
                    -Scope 'debug.state.pose' `
                    -Arguments $nonFiniteArguments `
                    -LeaseId $sdkLease `
                    -ExpectedRuntimeMode 'Paused' `
                    -ExpectedMovieTick $paused.State.MovieTick `
                    -ExpectedCodes @('InvalidArguments') `
                    -Label "non-finite-$nonFinite"))
        }

        $outOfRangeArguments = $validPose.arguments.Clone()
        $outOfRangeArguments['positionX'] = '10001'
        $rejections.Add(
            (Invoke-SdkRejectedMutationCase `
                -Client $sdk `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.pose' `
                -Arguments $outOfRangeArguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $paused.State.MovieTick `
                -ExpectedCodes @('InvalidArguments') `
                -Label 'pose-out-of-range'))

        $validResources = New-PlayerResourcesMutationCase `
            -SemanticState $paused `
            -Iteration 1
        $invalidResources = $validResources.arguments.Clone()
        $invalidResources['health'] = '0'
        $rejections.Add(
            (Invoke-SdkRejectedMutationCase `
                -Client $sdk `
                -CommandId 'setPlayerResources' `
                -Scope 'debug.state.resources' `
                -Arguments $invalidResources `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $paused.State.MovieTick `
                -ExpectedCodes @('InvalidArguments') `
                -Label 'resources-out-of-range'))

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforePose = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused'
            $poseCase = New-HeroPoseMutationCase `
                -SemanticState $beforePose `
                -Iteration $iteration
            $poseResult = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.pose' `
                -Arguments $poseCase.arguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $beforePose.State.MovieTick
            $afterPose = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforePose.State.MovieTick
            $normalizedPose =
                Convert-AutomationResult $poseResult
            Assert-MutationCommitted `
                -Result $normalizedPose `
                -Before $beforePose `
                -After $afterPose `
                -ExpectedValues $poseCase.expectedValues `
                -Label "SDK hero pose $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'sdk'
                    adapter = 'hero-pose'
                    iteration = $iteration
                    resultCode = $poseResult.ResultCode
                    beforeSha256 =
                        $beforePose.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterPose.State.SemanticSnapshotSha256
                    verificationEligibility =
                        (Get-StateField `
                            -SemanticState $afterPose `
                            -Name 'verificationEligibility')
                })

            $beforeResources = $afterPose
            $resourceCase =
                New-PlayerResourcesMutationCase `
                    -SemanticState $beforeResources `
                    -Iteration $iteration
            $resourceResult = Invoke-SdkCommand `
                -Client $sdk `
                -CommandId 'setPlayerResources' `
                -Scope 'debug.state.resources' `
                -Arguments $resourceCase.arguments `
                -LeaseId $sdkLease `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $beforeResources.State.MovieTick
            $afterResources = Wait-SdkState `
                -Client $sdk `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforeResources.State.MovieTick
            $normalizedResources =
                Convert-AutomationResult $resourceResult
            Assert-MutationCommitted `
                -Result $normalizedResources `
                -Before $beforeResources `
                -After $afterResources `
                -ExpectedValues $resourceCase.expectedValues `
                -Label "SDK player resources $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'sdk'
                    adapter = 'player-resources'
                    iteration = $iteration
                    resultCode = $resourceResult.ResultCode
                    beforeSha256 =
                        $beforeResources.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterResources.State.SemanticSnapshotSha256
                    verificationEligibility =
                        (Get-StateField `
                            -SemanticState $afterResources `
                            -Name 'verificationEligibility')
                })
        }

        $disconnectTimer =
            [Diagnostics.Stopwatch]::StartNew()
        Dispose-SdkClient -Client $sdk
        $sdk = $null
        $sdkLease = ''
        $observer = New-SdkClient `
            -ClientId 't15-mutation-observer'
        $leaseProbe = $null
        $leaseDeadline =
            [DateTimeOffset]::UtcNow.AddSeconds(5)
        do {
            $leaseProbe = Invoke-SdkCommand `
                -Client $observer `
                -CommandId 'acquireControl' `
                -Scope 'debug.state.pose' `
                -Arguments @{
                    scopes = 'debug.state.pose'
                    ttlSeconds = '30'
                } `
                -AllowFailure
            if ($leaseProbe.Success) {
                break
            }
            if ($leaseProbe.ResultCode -ne 'LeaseBusy') {
                throw (
                    'Disconnect lease probe failed: {0}: {1}' -f `
                        $leaseProbe.ResultCode, `
                        $leaseProbe.Detail
                )
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $leaseDeadline)
        if ($null -eq $leaseProbe -or -not $leaseProbe.Success) {
            throw 'SDK disconnect did not release its mutation lease in 5 seconds.'
        }
        $disconnectTimer.Stop()
        $probeLeaseId = $leaseProbe.Data['leaseId']
        [void](Release-SdkLease `
            -Client $observer `
            -Scope 'debug.state.pose' `
            -LeaseId $probeLeaseId)
        $records.Add(
            [ordered]@{
                channel = 'sdk'
                operation = 'disconnect-lease-release'
                releasedWithinMilliseconds =
                    $disconnectTimer.ElapsedMilliseconds
                replacementLeaseAcquired = $true
            })

        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforePose = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused'
            $poseCase = New-HeroPoseMutationCase `
                -SemanticState $beforePose `
                -Iteration $iteration
            $poseArguments = @(
                $poseCase.arguments.GetEnumerator() |
                    Sort-Object Key |
                    ForEach-Object {
                        '{0}={1}' -f $_.Key, $_.Value
                    }
            )
            $cliPose = Invoke-CliCall `
                -CommandId 'setHeroPose' `
                -Scope 'debug.state.pose' `
                -Arguments $poseArguments `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $beforePose.State.MovieTick
            $afterPose = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforePose.State.MovieTick
            $normalizedPose = [ordered]@{
                success = [bool]$cliPose.envelope.success
                resultCode = $cliPose.envelope.resultCode
                detail = $cliPose.envelope.detail
                data = $cliPose.data
            }
            Assert-MutationCommitted `
                -Result $normalizedPose `
                -Before $beforePose `
                -After $afterPose `
                -ExpectedValues $poseCase.expectedValues `
                -Label "CLI hero pose $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'cli'
                    adapter = 'hero-pose'
                    iteration = $iteration
                    exitCode = $cliPose.exitCode
                    resultCode = $cliPose.envelope.resultCode
                    beforeSha256 =
                        $beforePose.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterPose.State.SemanticSnapshotSha256
                })

            $beforeResources = $afterPose
            $resourceCase =
                New-PlayerResourcesMutationCase `
                    -SemanticState $beforeResources `
                    -Iteration $iteration
            $resourceArguments = @(
                $resourceCase.arguments.GetEnumerator() |
                    Sort-Object Key |
                    ForEach-Object {
                        '{0}={1}' -f $_.Key, $_.Value
                    }
            )
            $cliResources = Invoke-CliCall `
                -CommandId 'setPlayerResources' `
                -Scope 'debug.state.resources' `
                -Arguments $resourceArguments `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $beforeResources.State.MovieTick
            $afterResources = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforeResources.State.MovieTick
            $normalizedResources = [ordered]@{
                success = [bool]$cliResources.envelope.success
                resultCode = $cliResources.envelope.resultCode
                detail = $cliResources.envelope.detail
                data = $cliResources.data
            }
            Assert-MutationCommitted `
                -Result $normalizedResources `
                -Before $beforeResources `
                -After $afterResources `
                -ExpectedValues $resourceCase.expectedValues `
                -Label "CLI player resources $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'cli'
                    adapter = 'player-resources'
                    iteration = $iteration
                    exitCode = $cliResources.exitCode
                    resultCode =
                        $cliResources.envelope.resultCode
                    beforeSha256 =
                        $beforeResources.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterResources.State.SemanticSnapshotSha256
                })
        }

        Start-McpBridge `
            -LogPath (
                Join-Path $case.directory 'mcp-mutation.jsonl'
            )
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        foreach ($required in @(
                'hktas_set_hero_pose',
                'hktas_set_player_resources'
            )) {
            if ($toolNames -notcontains $required) {
                throw "Debug-enabled MCP omitted $required."
            }
        }
        $mcpId = 10
        [void](Invoke-McpTool `
            -Id $mcpId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @(
                    'debug.state.pose',
                    'debug.state.resources'
                )
                ttlSeconds = 300
            })
        $mcpId++
        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforePose = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused'
            $poseCase = New-HeroPoseMutationCase `
                -SemanticState $beforePose `
                -Iteration $iteration
            $mcpPose = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_set_hero_pose' `
                -Arguments @{
                    expectedSnapshotSha256 =
                        $beforePose.State.SemanticSnapshotSha256
                    expectedMovieTick =
                        $beforePose.State.MovieTick
                    positionX = [double]$poseCase.targetX
                    positionY = [double]$poseCase.targetY
                    velocityX = [double]$poseCase.velocityX
                    velocityY = [double]$poseCase.velocityY
                }
            $mcpId++
            $afterPose = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforePose.State.MovieTick
            $normalizedPose =
                $mcpPose.result.structuredContent
            Assert-MutationCommitted `
                -Result $normalizedPose `
                -Before $beforePose `
                -After $afterPose `
                -ExpectedValues $poseCase.expectedValues `
                -Label "MCP hero pose $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'mcp'
                    adapter = 'hero-pose'
                    iteration = $iteration
                    resultCode = $normalizedPose.resultCode
                    beforeSha256 =
                        $beforePose.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterPose.State.SemanticSnapshotSha256
                })

            $beforeResources = $afterPose
            $resourceCase =
                New-PlayerResourcesMutationCase `
                    -SemanticState $beforeResources `
                    -Iteration $iteration
            $mcpResources = Invoke-McpTool `
                -Id $mcpId `
                -Name 'hktas_set_player_resources' `
                -Arguments @{
                    expectedSnapshotSha256 =
                        $beforeResources.State.SemanticSnapshotSha256
                    expectedMovieTick =
                        $beforeResources.State.MovieTick
                    health = $resourceCase.health
                    soul = $resourceCase.soul
                }
            $mcpId++
            $afterResources = Wait-SdkState `
                -Client $observer `
                -ControlMode 'Paused' `
                -ExactMovieTick $beforeResources.State.MovieTick
            $normalizedResources =
                $mcpResources.result.structuredContent
            Assert-MutationCommitted `
                -Result $normalizedResources `
                -Before $beforeResources `
                -After $afterResources `
                -ExpectedValues $resourceCase.expectedValues `
                -Label "MCP player resources $iteration"
            $records.Add(
                [ordered]@{
                    channel = 'mcp'
                    adapter = 'player-resources'
                    iteration = $iteration
                    resultCode =
                        $normalizedResources.resultCode
                    beforeSha256 =
                        $beforeResources.State.SemanticSnapshotSha256
                    afterSha256 =
                        $afterResources.State.SemanticSnapshotSha256
                })
        }
        [void](Invoke-McpTool `
            -Id $mcpId `
            -Name 'hktas_release_control')

        $finalState = Wait-SdkState `
            -Client $observer `
            -ControlMode 'Paused' `
            -Fields @{
                verificationEligibility =
                    'NonVerifiableDebugMutation'
            }
        $records |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mutation-parity.json'
                ) `
                -Encoding utf8NoBOM
        $rejections |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mutation-rejections.json'
                ) `
                -Encoding utf8NoBOM
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-tools.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add(
            [pscustomobject]@{
                name = $case.name
                mode = $case.mode
                sessionId = $case.sessionId
                debugMutationEnabled = $true
                sdkHeroPose =
                    "$ParityIterations/$ParityIterations"
                sdkPlayerResources =
                    "$ParityIterations/$ParityIterations"
                cliHeroPose =
                    "$ParityIterations/$ParityIterations"
                cliPlayerResources =
                    "$ParityIterations/$ParityIterations"
                mcpHeroPose =
                    "$ParityIterations/$ParityIterations"
                mcpPlayerResources =
                    "$ParityIterations/$ParityIterations"
                rejectionCases = $rejections.Count
                disconnectLeaseReleased = $true
                finalVerificationEligibility =
                    (Get-StateField `
                        -SemanticState $finalState `
                        -Name 'verificationEligibility')
            })
        return $case
    }
    finally {
        if (-not [string]::IsNullOrEmpty($sdkLease) `
                -and $null -ne $sdk) {
            try {
                [void](Release-SdkLease `
                    -Client $sdk `
                    -Scope $sdkScopes[0] `
                    -LeaseId $sdkLease)
            }
            catch {
            }
        }
        Dispose-SdkClient -Client $sdk
        Dispose-SdkClient -Client $observer
        Stop-McpBridge
        Close-RunProcesses
    }
}

function Invoke-CleanEligibilityRestart {
    $case = Start-GameCase `
        -Name 'mutation-clean-restart' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $false `
        -InputNeutralGameplay
    $sdk = $null
    try {
        $sdk = New-SdkClient `
            -ClientId 't15-sdk-mutation-clean-restart'
        $state = Wait-SdkState `
            -Client $sdk `
            -ControlMode 'Running' `
            -Fields @{ verificationEligibility = 'Eligible' }
        if ($state.State.ActiveCapabilities -contains 'setHeroPose' `
                -or $state.State.ActiveCapabilities -contains
                    'setPlayerResources') {
            throw 'Clean restart exposed disabled mutation capabilities.'
        }

        Start-McpBridge `
            -LogPath (
                Join-Path $case.directory 'mcp-clean-restart.jsonl'
            )
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        if ($toolNames -contains 'hktas_set_hero_pose' `
                -or $toolNames -contains
                    'hktas_set_player_resources') {
            throw 'Clean restart exposed disabled MCP mutation tools.'
        }
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-tools.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add(
            [pscustomobject]@{
                name = $case.name
                mode = $case.mode
                sessionId = $case.sessionId
                debugMutationEnabled = $false
                verificationEligibility = 'Eligible'
                mutationCapabilitiesHidden = $true
                mutationMcpToolsHidden = $true
            })
        return $case
    }
    finally {
        Dispose-SdkClient -Client $sdk
        Stop-McpBridge
        Close-RunProcesses
    }
}

function Get-T15ProcessMemorySample {
    param(
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)]
        [Diagnostics.Stopwatch]$Stopwatch,
        [Parameter(Mandatory)][int]$CompanionProcessId,
        [Parameter(Mandatory)][int]$WatchProcessId
    )

    $companion = Get-Process `
        -Id $CompanionProcessId `
        -ErrorAction SilentlyContinue
    $watch = Get-Process `
        -Id $WatchProcessId `
        -ErrorAction SilentlyContinue
    if ($null -ne $companion) {
        $companion.Refresh()
    }
    if ($null -ne $watch) {
        $watch.Refresh()
    }

    return [pscustomobject][ordered]@{
        timestampUtc =
            [DateTimeOffset]::UtcNow.ToString('O')
        elapsedMilliseconds = $Stopwatch.ElapsedMilliseconds
        phase = $Phase
        companionWorkingSetBytes = if ($null -eq $companion) {
            -1
        }
        else {
            $companion.WorkingSet64
        }
        companionPrivateBytes = if ($null -eq $companion) {
            -1
        }
        else {
            $companion.PrivateMemorySize64
        }
        watchWorkingSetBytes = if ($null -eq $watch) {
            -1
        }
        else {
            $watch.WorkingSet64
        }
        watchPrivateBytes = if ($null -eq $watch) {
            -1
        }
        else {
            $watch.PrivateMemorySize64
        }
    }
}

function Invoke-StateDurabilityMatrix {
    $case = Start-GameCase `
        -Name 'state-durability' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $false `
        -InputNeutralGameplay
    $reader = $null
    $watch = $null
    $watchStdout = Join-Path `
        $case.directory `
        'timeline-watch.jsonl'
    $watchStderr = Join-Path `
        $case.directory `
        'timeline-watch.stderr.log'
    $readCount = if ($Smoke) { 100 } else { 10000 }
    $reconnectCount = if ($Smoke) { 5 } else { 100 }
    $memory =
        [System.Collections.Generic.List[object]]::new()
    $warmupSample = $null
    $stopwatch = [Diagnostics.Stopwatch]::new()
    try {
        $reader = New-SdkClient `
            -ClientId 't15-state-durability-reader'
        $initial = Wait-SdkState `
            -Client $reader `
            -ControlMode 'Running' `
            -PlaybackMode 'Idle'
        $manifestSha256 = $initial.State.ManifestSha256
        if ($initial.State.SessionId -ne $case.sessionId) {
            throw 'State durability reader bound the wrong session.'
        }

        $companionProcess = Get-Process `
                -Name HollowKnightTAS.Companion `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $script:ownedHelperPids -contains $_.Id
            } |
            Select-Object -First 1
        if ($null -eq $companionProcess) {
            throw 'State durability case could not identify Companion.'
        }

        $watchArguments = @(
            'automation',
            'watch',
            '--from=0',
            '--count=200',
            "--duration-seconds=$SubscriptionSeconds",
            "--bootstrap=$bootstrapPath"
        )
        $watch = Start-Process `
            -FilePath $cliPath `
            -ArgumentList $watchArguments `
            -WindowStyle Hidden `
            -RedirectStandardOutput $watchStdout `
            -RedirectStandardError $watchStderr `
            -PassThru
        if ($null -eq $watch) {
            throw 'Timeline watch process did not start.'
        }
        $stopwatch.Start()
        $memory.Add(
            (Get-T15ProcessMemorySample `
                -Phase 'initial' `
                -Stopwatch $stopwatch `
                -CompanionProcessId $companionProcess.Id `
                -WatchProcessId $watch.Id))

        $readStarted = [DateTimeOffset]::UtcNow
        $previousCapturedAt = [DateTimeOffset]::MinValue
        $minimumTick = [long]::MaxValue
        $maximumTick = [long]::MinValue
        $maximumAgeMilliseconds = 0L
        $warmupAt = [Math]::Min(100, $readCount)
        $sampleEvery = [Math]::Max(
            1,
            [int][Math]::Floor($readCount / 20))
        for ($iteration = 1;
                $iteration -le $readCount;
                $iteration++) {
            $state = $reader.GetSemanticStateAsync(
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
            if ($state.State.SessionId -ne $case.sessionId `
                    -or $state.State.ManifestSha256 `
                        -ne $manifestSha256) {
                throw "State read $iteration changed session binding."
            }
            if ($state.State.AgeMilliseconds -gt 5000 `
                    -or $state.State.AgeMilliseconds -lt 0) {
                throw (
                    "State read $iteration exceeded freshness budget: " `
                    + $state.State.AgeMilliseconds
                )
            }
            if ([string]::IsNullOrWhiteSpace(
                    $state.State.TickPhase) `
                    -or $state.State.SemanticSnapshotSha256 `
                        -notmatch '^[0-9a-f]{64}$') {
                throw "State read $iteration returned invalid semantics."
            }
            if ($state.State.CapturedAtUtc `
                    -lt $previousCapturedAt) {
                throw "State read $iteration moved capturedAtUtc backwards."
            }
            $previousCapturedAt = $state.State.CapturedAtUtc
            $minimumTick = [Math]::Min(
                $minimumTick,
                $state.State.MovieTick)
            $maximumTick = [Math]::Max(
                $maximumTick,
                $state.State.MovieTick)
            $maximumAgeMilliseconds = [Math]::Max(
                $maximumAgeMilliseconds,
                $state.State.AgeMilliseconds)

            if ($iteration -eq $warmupAt) {
                $warmupSample = Get-T15ProcessMemorySample `
                    -Phase 'read-warmup' `
                    -Stopwatch $stopwatch `
                    -CompanionProcessId $companionProcess.Id `
                    -WatchProcessId $watch.Id
                $memory.Add($warmupSample)
            }
            elseif (($iteration % $sampleEvery) -eq 0) {
                $memory.Add(
                    (Get-T15ProcessMemorySample `
                        -Phase "read-$iteration" `
                        -Stopwatch $stopwatch `
                        -CompanionProcessId $companionProcess.Id `
                        -WatchProcessId $watch.Id))
            }
        }
        $readElapsedMilliseconds =
            ([DateTimeOffset]::UtcNow - $readStarted).
                TotalMilliseconds

        $reconnectStarted = [DateTimeOffset]::UtcNow
        for ($reconnect = 1;
                $reconnect -le $reconnectCount;
                $reconnect++) {
            $client = $null
            try {
                $client = New-SdkClient `
                    -ClientId (
                        't15-state-reconnect-{0:D3}' -f `
                            $reconnect
                    )
                $state = Wait-SdkSemanticState `
                    -Client $client `
                    -TimeoutSeconds 15
                if ($state.State.SessionId -ne $case.sessionId `
                        -or $state.State.ManifestSha256 `
                            -ne $manifestSha256 `
                        -or $state.State.AgeMilliseconds -gt 5000) {
                    throw (
                        "Reconnect $reconnect returned invalid state."
                    )
                }
            }
            finally {
                Dispose-SdkClient -Client $client
            }
            if (($reconnect % 10) -eq 0 `
                    -or $reconnect -eq $reconnectCount) {
                $memory.Add(
                    (Get-T15ProcessMemorySample `
                        -Phase "reconnect-$reconnect" `
                        -Stopwatch $stopwatch `
                        -CompanionProcessId $companionProcess.Id `
                        -WatchProcessId $watch.Id))
            }
        }
        $reconnectElapsedMilliseconds =
            ([DateTimeOffset]::UtcNow - $reconnectStarted).
                TotalMilliseconds

        $nextProgressAt = 60
        while (-not $watch.HasExited) {
            if ($stopwatch.Elapsed.TotalSeconds `
                    -gt ($SubscriptionSeconds + 120)) {
                throw 'Timeline watch exceeded its duration budget.'
            }
            if ($stopwatch.Elapsed.TotalSeconds `
                    -ge $nextProgressAt) {
                Write-Host (
                    'T15 timeline subscription progress: {0}/{1}s' -f `
                        [int]$stopwatch.Elapsed.TotalSeconds, `
                        $SubscriptionSeconds
                )
                $nextProgressAt += 60
            }
            $memory.Add(
                (Get-T15ProcessMemorySample `
                    -Phase 'subscription' `
                    -Stopwatch $stopwatch `
                    -CompanionProcessId $companionProcess.Id `
                    -WatchProcessId $watch.Id))
            Start-Sleep -Seconds 5
            $watch.Refresh()
        }
        $stopwatch.Stop()
        if ($watch.ExitCode -ne 0) {
            throw (
                'Timeline watch failed with exit code {0}: {1}' -f `
                    $watch.ExitCode, `
                    (Get-Content `
                        -LiteralPath $watchStderr `
                        -Raw)
            )
        }
        if ($stopwatch.Elapsed.TotalSeconds `
                -lt $SubscriptionSeconds) {
            throw (
                'Timeline watch exited early after {0:N3}s.' -f `
                    $stopwatch.Elapsed.TotalSeconds
            )
        }
        $memory.Add(
            (Get-T15ProcessMemorySample `
                -Phase 'completed' `
                -Stopwatch $stopwatch `
                -CompanionProcessId $companionProcess.Id `
                -WatchProcessId $watch.Id))

        $timelineLines = @(
            Get-Content -LiteralPath $watchStdout
        )
        if ($timelineLines.Count -lt 1) {
            throw 'Timeline watch returned no pages.'
        }
        $timelineEntries = 0
        $gapPages = 0
        $lastSequence = -1L
        $firstSequence = -1L
        $lastNextAfterSequence = 0L
        for ($lineIndex = 0;
                $lineIndex -lt $timelineLines.Count;
                $lineIndex++) {
            $envelope = $timelineLines[$lineIndex] |
                ConvertFrom-Json -Depth 100
            if ($envelope.success -ne 'true' `
                    -or $envelope.resultCode -ne 'Ok' `
                    -or $envelope.sessionId -ne $case.sessionId `
                    -or $envelope.manifestSha256 `
                        -ne $manifestSha256) {
                throw (
                    "Timeline page $lineIndex has an invalid envelope."
                )
            }
            $dataJson = [Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String(
                    [string]$envelope.dataBase64))
            $data = $dataJson |
                ConvertFrom-Json -Depth 20
            $entries = @(
                ([string]$data.entries -split "`n") |
                    Where-Object {
                        -not [string]::IsNullOrWhiteSpace($_)
                    }
            )
            if ([int]$data.count -ne $entries.Count) {
                throw (
                    "Timeline page $lineIndex count does not match entries."
                )
            }
            if ($data.gapBeforeWindow -eq 'true') {
                $gapPages++
            }
            foreach ($entry in $entries) {
                $parts = $entry -split '\|', 4
                if ($parts.Count -ne 4) {
                    throw "Timeline entry has an invalid shape: $entry"
                }
                $sequence = [long]::Parse(
                    $parts[0],
                    [Globalization.CultureInfo]::InvariantCulture)
                if ($sequence -le $lastSequence) {
                    throw (
                        'Timeline sequence repeated or moved backwards: ' `
                        + $sequence
                    )
                }
                if ($firstSequence -lt 0) {
                    $firstSequence = $sequence
                }
                $lastSequence = $sequence
                $timelineEntries++
            }
            $nextAfterSequence = [long]$data.nextAfterSequence
            if ($nextAfterSequence -lt $lastNextAfterSequence `
                    -or ($lastSequence -ge 0 `
                        -and $nextAfterSequence `
                            -lt $lastSequence)) {
                throw (
                    "Timeline page $lineIndex regressed its cursor."
                )
            }
            $lastNextAfterSequence = $nextAfterSequence
        }

        if ($null -eq $warmupSample `
                -or $warmupSample.companionPrivateBytes -lt 0) {
            throw 'Companion warm-up memory sample is unavailable.'
        }
        $companionSamples = @(
            $memory |
                Where-Object {
                    $_.companionPrivateBytes -ge 0
                }
        )
        $peakCompanionPrivate =
            ($companionSamples |
                Measure-Object `
                    -Property companionPrivateBytes `
                    -Maximum).Maximum
        $finalCompanionPrivate =
            $companionSamples[-1].companionPrivateBytes
        $peakGrowth =
            $peakCompanionPrivate `
            - $warmupSample.companionPrivateBytes
        $finalGrowth =
            $finalCompanionPrivate `
            - $warmupSample.companionPrivateBytes
        $memory |
            ConvertTo-Json -Depth 20 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'memory-samples.json'
                ) `
                -Encoding utf8NoBOM
        if ($peakGrowth -gt 256MB `
                -or $finalGrowth -gt 64MB) {
            throw (
                'Companion memory exceeded durability budget: ' `
                + "peakGrowth=$peakGrowth finalGrowth=$finalGrowth"
            )
        }

        $summary = [ordered]@{
            name = $case.name
            mode = $case.mode
            sessionId = $case.sessionId
            readCount = $readCount
            readElapsedMilliseconds =
                [Math]::Round($readElapsedMilliseconds, 3)
            reconnectCount = $reconnectCount
            reconnectElapsedMilliseconds =
                [Math]::Round(
                    $reconnectElapsedMilliseconds,
                    3)
            subscriptionRequestedSeconds =
                $SubscriptionSeconds
            subscriptionElapsedSeconds =
                [Math]::Round(
                    $stopwatch.Elapsed.TotalSeconds,
                    3)
            timelinePages = $timelineLines.Count
            timelineEntries = $timelineEntries
            firstSequence = $firstSequence
            lastSequence = $lastSequence
            explicitGapPages = $gapPages
            minimumMovieTick = $minimumTick
            maximumMovieTick = $maximumTick
            maximumAgeMilliseconds =
                $maximumAgeMilliseconds
            warmupCompanionPrivateBytes =
                $warmupSample.companionPrivateBytes
            peakCompanionPrivateBytes =
                $peakCompanionPrivate
            finalCompanionPrivateBytes =
                $finalCompanionPrivate
            peakGrowthBytes = $peakGrowth
            finalGrowthBytes = $finalGrowth
            verificationEligibility =
                (Get-StateField `
                    -SemanticState (
                        Wait-SdkState `
                            -Client $reader `
                            -ControlMode 'Running' `
                            -PlaybackMode 'Idle'
                    ) `
                    -Name 'verificationEligibility')
        }
        $summary |
            ConvertTo-Json -Depth 20 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'state-durability.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add([pscustomobject]$summary)
        return $case
    }
    finally {
        Dispose-SdkClient -Client $reader
        if ($null -ne $watch) {
            try {
                $watch.Refresh()
                if (-not $watch.HasExited) {
                    Stop-Process `
                        -Id $watch.Id `
                        -ErrorAction SilentlyContinue
                    [void]$watch.WaitForExit(5000)
                }
            }
            finally {
                $watch.Dispose()
            }
        }
        Close-RunProcesses
    }
}

function Get-McpStructuredContent {
    param([Parameter(Mandatory)]$Response)

    if ($null -eq $Response.result `
            -or $null -eq $Response.result.structuredContent) {
        throw 'MCP response omitted structuredContent.'
    }

    return $Response.result.structuredContent
}

function Invoke-McpStateRead {
    param([Parameter(Mandatory)][ref]$NextId)

    $response = Invoke-McpTool `
        -Id $NextId.Value `
        -Name 'hktas_get_state'
    $NextId.Value++
    return Get-McpStructuredContent -Response $response
}

function Wait-McpPlaybackIdle {
    param(
        [Parameter(Mandatory)][ref]$NextId,
        [int]$TimeoutSeconds = 15
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    do {
        $last = Invoke-McpStateRead -NextId $NextId
        if ($last.state.runtimeMode -eq 'Running' `
                -and $last.state.fields.playbackMode -eq 'Idle' `
                -and $last.state.fields.recordingActive `
                    -eq 'false') {
            return $last
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw (
        'MCP state did not return to Running/Idle: ' `
        + ($last | ConvertTo-Json -Compress -Depth 10)
    )
}

function Find-McpTimelineMilestone {
    param(
        [Parameter(Mandatory)]$TimelineContent,
        [Parameter(Mandatory)][string]$Checkpoint
    )

    $entries = @(
        ([string]$TimelineContent.data.entries -split "`n") |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_)
            }
    )
    foreach ($entry in $entries) {
        $parts = $entry -split '\|', 4
        if ($parts.Count -ne 4 `
                -or $parts[1] -ne 'milestone') {
            continue
        }
        try {
            $json = [Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String($parts[3]))
            $fields = $json |
                ConvertFrom-Json -Depth 10
            if ($fields.kind -eq 'Checkpoint' `
                    -and $fields.detail -eq $Checkpoint) {
                return [pscustomobject]@{
                    sequence = [long]$parts[0]
                    movieTick = [long]$parts[2]
                    detail = [string]$fields.detail
                }
            }
        }
        catch {
            throw "Timeline milestone payload is invalid: $entry"
        }
    }

    return $null
}

function Invoke-McpScriptedAgentMatrix {
    $case = Start-GameCase `
        -Name 'scripted-ai-mcp' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $false `
        -InputNeutralGameplay
    $records =
        [System.Collections.Generic.List[object]]::new()
    $nextId = 10
    $leaseAcquired = $false
    try {
        Start-McpBridge `
            -LogPath (
                Join-Path $case.directory 'mcp-scripted-agent.jsonl'
            )
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $resources = Send-McpRequest `
            -Id 3 `
            -Method 'resources/list' `
            -Parameters ([ordered]@{})
        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        foreach ($required in @(
                'hktas_get_state',
                'hktas_get_timeline',
                'hktas_get_desync',
                'hktas_get_movie',
                'hktas_validate_movie_patch',
                'hktas_propose_movie_patch',
                'hktas_apply_movie_branch',
                'hktas_start_recording',
                'hktas_stop_recording',
                'hktas_start_replay'
            )) {
            if ($toolNames -notcontains $required) {
                throw "Scripted MCP client is missing $required."
            }
        }

        $acquire = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @(
                    'control.recording',
                    'control.playback',
                    'movie.apply-branch'
                )
                ttlSeconds = 300
            }
        $nextId++
        $leaseAcquired = $true
        $acquireContent =
            Get-McpStructuredContent -Response $acquire
        if (-not $acquireContent.success) {
            throw 'Scripted MCP client could not acquire control.'
        }

        $state = Invoke-McpStateRead -NextId ([ref]$nextId)
        if ($state.state.fields.verificationEligibility `
                -ne 'Eligible') {
            throw 'Scripted MCP workflow started ineligible.'
        }
        $movieResponse = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_get_movie' `
            -AllowFailure
        $nextId++
        $movie = Get-McpStructuredContent `
            -Response $movieResponse
        if (-not $movie.success `
                -or $movie.data.available -ne 'true') {
            $startRecording = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_start_recording' `
                -Arguments @{
                    expectedRuntimeMode =
                        [string]$state.state.runtimeMode
                }
            $nextId++
            Start-Sleep -Milliseconds 250
            $recordingState = Invoke-McpStateRead `
                -NextId ([ref]$nextId)
            if ($recordingState.state.fields.recordingActive `
                    -ne 'true') {
                throw 'MCP baseline recording did not become active.'
            }
            $stopRecording = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_stop_recording' `
                -Arguments @{
                    expectedRuntimeMode =
                        [string]$recordingState.state.runtimeMode
                }
            $nextId++
            [void](Wait-McpPlaybackIdle `
                -NextId ([ref]$nextId))
            $movieResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_movie'
            $nextId++
            $movie = Get-McpStructuredContent `
                -Response $movieResponse
        }
        if (-not $movie.success `
                -or $movie.data.available -ne 'true' `
                -or [string]::IsNullOrWhiteSpace(
                    [string]$movie.data.movieId) `
                -or [string]::IsNullOrWhiteSpace(
                    [string]$movie.data.movieBase64)) {
            throw 'MCP baseline movie is unavailable.'
        }

        $strictUtf8 =
            [Text.UTF8Encoding]::new($false, $true)
        for ($iteration = 1;
                $iteration -le $ParityIterations;
                $iteration++) {
            $beforeState = Invoke-McpStateRead `
                -NextId ([ref]$nextId)
            $timelineBefore = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_timeline' `
                -Arguments @{ count = 200 }
            $nextId++
            $timelineBeforeContent =
                Get-McpStructuredContent `
                    -Response $timelineBefore
            $cursor =
                [long]$timelineBeforeContent.data.
                    nextAfterSequence
            $movieResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_movie'
            $nextId++
            $movie = Get-McpStructuredContent `
                -Response $movieResponse
            $baseMovieId = [string]$movie.data.movieId
            $movieBytes = [Convert]::FromBase64String(
                [string]$movie.data.movieBase64)
            $movieText = $strictUtf8.GetString($movieBytes).
                Replace("`r`n", "`n")
            $separator = $movieText.IndexOf(
                "---`n",
                [StringComparison]::Ordinal)
            if ($separator -lt 0) {
                throw 'Current movie omitted the canonical separator.'
            }
            $checkpoint =
                't15-ai-{0:D2}' -f $iteration
            $insertion =
                'marker "T15 scripted AI {0:D2}"' -f `
                    $iteration
            $insertion += "`ncheckpoint $checkpoint`n"
            $candidateText = $movieText.Insert(
                $separator + 4,
                $insertion)
            $candidateBytes = $strictUtf8.GetBytes(
                $candidateText)
            $candidateBase64 =
                [Convert]::ToBase64String($candidateBytes)
            $invalidBytes = $strictUtf8.GetBytes(
                $candidateText `
                + "frames 1 hold=teleport`n")

            $invalidResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_validate_movie_patch' `
                -Arguments @{
                    candidateMovieBase64 =
                        [Convert]::ToBase64String(
                            $invalidBytes)
                } `
                -AllowFailure
            $nextId++
            $invalid =
                Get-McpStructuredContent `
                    -Response $invalidResponse
            if ($invalid.success `
                    -or $invalid.resultCode -ne 'MovieInvalid') {
                throw (
                    "Scripted AI invalid proposal $iteration " `
                    + 'was not rejected before execution.'
                )
            }

            $proposalResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_propose_movie_patch' `
                -Arguments @{
                    baseMovieId = $baseMovieId
                    candidateMovieBase64 = $candidateBase64
                    reason =
                        "deterministic nonvisual iteration $iteration"
                    expectedMilestone = $checkpoint
                }
            $nextId++
            $proposal =
                Get-McpStructuredContent `
                    -Response $proposalResponse
            $branchMovieId =
                [string]$proposal.data.branchMovieId
            if ($branchMovieId -notmatch '^[0-9a-f]{64}$') {
                throw "Proposal $iteration returned an invalid branch ID."
            }

            $unchangedResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_movie'
            $nextId++
            $unchanged =
                Get-McpStructuredContent `
                    -Response $unchangedResponse
            if ($unchanged.data.movieId -ne $baseMovieId) {
                throw (
                    "Proposal $iteration silently replaced current movie."
                )
            }

            $applyResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_apply_movie_branch' `
                -Arguments @{
                    branchMovieId = $branchMovieId
                    expectedRuntimeMode =
                        [string]$beforeState.state.runtimeMode
                }
            $nextId++
            $apply =
                Get-McpStructuredContent `
                    -Response $applyResponse
            $currentResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_movie'
            $nextId++
            $current =
                Get-McpStructuredContent `
                    -Response $currentResponse
            if ($current.data.movieId -ne $branchMovieId) {
                throw (
                    "Applied branch $iteration does not match proposal."
                )
            }

            $startReplayResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_start_replay' `
                -Arguments @{
                    expectedRuntimeMode = 'Running'
                }
            $nextId++
            $startReplay =
                Get-McpStructuredContent `
                    -Response $startReplayResponse
            $milestone = $null
            $milestoneDeadline =
                [DateTimeOffset]::UtcNow.AddSeconds(15)
            do {
                $timelineResponse = Invoke-McpTool `
                    -Id $nextId `
                    -Name 'hktas_get_timeline' `
                    -Arguments @{
                        afterSequence = $cursor
                        count = 200
                    }
                $nextId++
                $timeline =
                    Get-McpStructuredContent `
                        -Response $timelineResponse
                $milestone = Find-McpTimelineMilestone `
                    -TimelineContent $timeline `
                    -Checkpoint $checkpoint
                $cursor =
                    [long]$timeline.data.nextAfterSequence
                if ($null -eq $milestone) {
                    Start-Sleep -Milliseconds 100
                }
            } while ($null -eq $milestone `
                -and [DateTimeOffset]::UtcNow `
                    -lt $milestoneDeadline)
            if ($null -eq $milestone) {
                throw (
                    "Replay $iteration did not emit $checkpoint."
                )
            }

            $afterState = Wait-McpPlaybackIdle `
                -NextId ([ref]$nextId)
            $desyncResponse = Invoke-McpTool `
                -Id $nextId `
                -Name 'hktas_get_desync' `
                -AllowFailure
            $nextId++
            $desync =
                Get-McpStructuredContent `
                    -Response $desyncResponse
            if ($desync.success `
                    -and $desync.data.available -eq 'true') {
                throw (
                    "Replay $iteration produced structured desync evidence."
                )
            }
            if ($afterState.state.fields.
                    verificationEligibility -ne 'Eligible') {
                throw (
                    "Replay $iteration lost verification eligibility."
                )
            }

            $records.Add(
                [pscustomobject][ordered]@{
                    iteration = $iteration
                    baseMovieId = $baseMovieId
                    branchMovieId = $branchMovieId
                    currentMovieId =
                        [string]$current.data.movieId
                    invalidResultCode =
                        [string]$invalid.resultCode
                    proposalResultCode =
                        [string]$proposal.resultCode
                    applyResultCode =
                        [string]$apply.resultCode
                    replayResultCode =
                        [string]$startReplay.resultCode
                    checkpoint = $checkpoint
                    milestoneSequence =
                        $milestone.sequence
                    milestoneMovieTick =
                        $milestone.movieTick
                    beforeSemanticSha256 =
                        [string]$beforeState.state.
                            semanticSnapshotSha256
                    afterSemanticSha256 =
                        [string]$afterState.state.
                            semanticSnapshotSha256
                    verificationEligibility =
                        [string]$afterState.state.fields.
                            verificationEligibility
                    nonvisualOnly = $true
                })
        }

        $release = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_release_control'
        $nextId++
        $leaseAcquired = $false
        $finalState = Invoke-McpStateRead `
            -NextId ([ref]$nextId)
        if ($finalState.state.runtimeMode -ne 'Running' `
                -or $finalState.state.fields.playbackMode `
                    -ne 'Idle' `
                -or $finalState.state.fields.recordingActive `
                    -ne 'false' `
                -or $finalState.state.fields.
                    verificationEligibility -ne 'Eligible') {
            throw 'Scripted MCP workflow did not finish cleanly.'
        }

        $records |
            ConvertTo-Json -Depth 30 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'scripted-ai-runs.json'
                ) `
                -Encoding utf8NoBOM
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-tools.json'
                ) `
                -Encoding utf8NoBOM
        $resources |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-resources.json'
                ) `
                -Encoding utf8NoBOM
        $summary = [pscustomobject][ordered]@{
            name = $case.name
            mode = $case.mode
            sessionId = $case.sessionId
            iterations =
                "$ParityIterations/$ParityIterations"
            compatibleMcpClientSmoke = $true
            nonvisualOnly = $true
            invalidRejectedBeforeExecution =
                "$ParityIterations/$ParityIterations"
            isolatedProposal =
                "$ParityIterations/$ParityIterations"
            explicitApply =
                "$ParityIterations/$ParityIterations"
            replayMilestone =
                "$ParityIterations/$ParityIterations"
            structuredDesyncCount = 0
            finalVerificationEligibility =
                [string]$finalState.state.fields.
                    verificationEligibility
            leaseReleased = $true
        }
        $cases.Add($summary)
        return $case
    }
    finally {
        if ($leaseAcquired -and $null -ne $script:mcp) {
            try {
                [void](Invoke-McpTool `
                    -Id $nextId `
                    -Name 'hktas_release_control' `
                    -AllowFailure)
            }
            catch {
            }
        }
        Stop-McpBridge
        Close-RunProcesses
    }
}

function Invoke-SecurityMatrix {
    $case = Start-GameCase `
        -Name 'security-matrix' `
        -Mode 'ApprovedControl' `
        -DebugMutationEnabled $false `
        -InputNeutralGameplay
    $observer = $null
    $replacementLeaseId = ''
    $nextId = 10
    try {
        $observer = New-SdkClient `
            -ClientId 't15-security-observer'
        $before = Wait-SdkState `
            -Client $observer `
            -ControlMode 'Running' `
            -PlaybackMode 'Idle'

        Start-McpBridge `
            -LogPath (
                Join-Path $case.directory 'mcp-security.jsonl'
            )
        Initialize-McpBridge
        $tools = Send-McpRequest `
            -Id 2 `
            -Method 'tools/list' `
            -Parameters ([ordered]@{})
        $resources = Send-McpRequest `
            -Id 3 `
            -Method 'resources/list' `
            -Parameters ([ordered]@{})
        $toolNames = @(
            $tools.result.tools |
                ForEach-Object name
        )
        $resourceUris = @(
            $resources.result.resources |
                ForEach-Object uri
        )
        $dangerousPattern =
            '(?i)(shell|process|dll|https?|reflection|address|raw.?save|native.?enable|memory)'
        if (@(
                $toolNames |
                    Where-Object {
                        $_ -match $dangerousPattern
                    }
            ).Count -ne 0) {
            throw 'MCP catalog exposed a dangerous tool family.'
        }
        foreach ($uri in $resourceUris) {
            if (-not $uri.StartsWith(
                    'hktas://session/current/',
                    [StringComparison]::Ordinal) `
                    -or $uri.Contains('..') `
                    -or $uri.Contains('\') `
                    -or $uri.Contains('%')) {
                throw "MCP exposed an unsafe resource URI: $uri"
            }
        }
        $proposeTool = $tools.result.tools |
            Where-Object name -eq 'hktas_propose_movie_patch' |
            Select-Object -First 1
        $acquireTool = $tools.result.tools |
            Where-Object name -eq 'hktas_acquire_control' |
            Select-Object -First 1
        if ($null -eq $proposeTool `
                -or $proposeTool.inputSchema.properties.reason.
                    maxLength -ne 512 `
                -or $null -eq $acquireTool `
                -or $acquireTool.inputSchema.properties.scopes.
                    maxItems -ne 16 `
                -or $acquireTool.inputSchema.properties.scopes.
                    items.maxLength -ne 128) {
            throw 'Installed MCP schemas do not expose hardened bounds.'
        }

        $unsafeUris = @(
            'file:///etc/passwd',
            'file:///C:/Windows/System32/config/SAM',
            'https://example.invalid/secret',
            'hktas://session/current/../../movie',
            'hktas://session/current/state/summary/../movie',
            'hktas://session/current/state/%2e%2e/movie',
            'C:\Users\Public\secret.txt',
            'hktas://session/current/movie?path=../../secret'
        )
        $uriResults =
            [System.Collections.Generic.List[object]]::new()
        foreach ($uri in $unsafeUris) {
            $response = Send-McpRequest `
                -Id $nextId `
                -Method 'resources/read' `
                -Parameters ([ordered]@{ uri = $uri })
            $nextId++
            if ($null -eq $response.error `
                    -or [int]$response.error.code -ne -32602) {
                throw "Unsafe resource URI was not rejected: $uri"
            }
            $uriResults.Add(
                [pscustomobject]@{
                    uri = $uri
                    errorCode = [int]$response.error.code
                })
        }

        $unsafeTools = @(
            'shell',
            'process_start',
            'load_dll',
            'fetch_url',
            'reflection_invoke',
            'write_address',
            'write_raw_save',
            'enable_native'
        )
        $toolResults =
            [System.Collections.Generic.List[object]]::new()
        foreach ($name in $unsafeTools) {
            $response = Send-McpRequest `
                -Id $nextId `
                -Method 'tools/call' `
                -Parameters ([ordered]@{
                    name = $name
                    arguments = [ordered]@{}
                })
            $nextId++
            if ($null -eq $response.error `
                    -or [int]$response.error.code -ne -32602) {
                throw "Unsafe tool was not rejected: $name"
            }
            $toolResults.Add(
                [pscustomobject]@{
                    tool = $name
                    errorCode = [int]$response.error.code
                })
        }

        $invalidArguments =
            [System.Collections.Generic.List[object]]::new()
        $unknownField = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_get_state' `
            -Arguments @{ shell = 'powershell' } `
            -AllowFailure
        $nextId++
        $unknownContent =
            Get-McpStructuredContent -Response $unknownField
        $invalidArguments.Add(
            [pscustomobject]@{
                label = 'unknown-field'
                resultCode =
                    [string]$unknownContent.resultCode
            })

        $oversizedReason = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_propose_movie_patch' `
            -Arguments @{
                baseMovieId = 'none'
                candidateMovieBase64 = 'AA=='
                reason = ('r' * 513)
                expectedMilestone = 'none'
            } `
            -AllowFailure
        $nextId++
        $oversizedContent =
            Get-McpStructuredContent -Response $oversizedReason
        $invalidArguments.Add(
            [pscustomobject]@{
                label = 'oversized-string'
                resultCode =
                    [string]$oversizedContent.resultCode
            })

        $duplicateScopes = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @(
                    'control.playback',
                    'control.playback'
                )
            } `
            -AllowFailure
        $nextId++
        $duplicateContent =
            Get-McpStructuredContent -Response $duplicateScopes
        $invalidArguments.Add(
            [pscustomobject]@{
                label = 'duplicate-array-items'
                resultCode =
                    [string]$duplicateContent.resultCode
            })

        $tooManyScopes = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @(
                    1..17 |
                        ForEach-Object {
                            "control.scope.$_"
                        }
                )
            } `
            -AllowFailure
        $nextId++
        $tooManyContent =
            Get-McpStructuredContent -Response $tooManyScopes
        $invalidArguments.Add(
            [pscustomobject]@{
                label = 'array-too-large'
                resultCode =
                    [string]$tooManyContent.resultCode
            })
        foreach ($result in $invalidArguments) {
            if ($result.resultCode -ne 'InvalidToolInput') {
                throw (
                    'MCP schema rejection did not return ' `
                    + "InvalidToolInput: $($result.label)"
                )
            }
        }

        $oversizedLine =
            '{"jsonrpc":"2.0","id":900,"method":"ping","padding":"' `
            + ('x' * (1024 * 1024)) `
            + '"}'
        $script:mcp.StandardInput.WriteLine($oversizedLine)
        $script:mcp.StandardInput.Flush()
        $oversizedRead =
            $script:mcp.StandardOutput.ReadLineAsync()
        if (-not $oversizedRead.Wait(20000)) {
            throw 'Oversized MCP line did not receive a bounded error.'
        }
        $oversizedResponse =
            $oversizedRead.GetAwaiter().GetResult() |
                ConvertFrom-Json -Depth 20
        if ([int]$oversizedResponse.error.code -ne -32700) {
            throw 'Oversized MCP line returned the wrong error.'
        }
        $recovery = Send-McpRequest `
            -Id $nextId `
            -Method 'ping' `
            -Parameters ([ordered]@{})
        $nextId++
        if ($null -eq $recovery.result) {
            throw 'AgentBridge did not recover after oversized input.'
        }

        $afterNegative = Wait-SdkState `
            -Client $observer `
            -ControlMode 'Running' `
            -PlaybackMode 'Idle'
        if ($afterNegative.State.SemanticSnapshotSha256 `
                -ne $before.State.SemanticSnapshotSha256) {
            throw 'Security rejection matrix changed semantic state.'
        }

        $acquire = Invoke-McpTool `
            -Id $nextId `
            -Name 'hktas_acquire_control' `
            -Arguments @{
                scopes = @('control.playback')
                ttlSeconds = 300
            }
        $nextId++
        $bridgeProcessId = $script:mcp.Id
        $script:mcp.Kill($true)
        [void]$script:mcp.WaitForExit(10000)
        $script:mcp.Dispose()
        $script:mcp = $null
        $script:mcpLog = $null

        $replacementDeadline =
            [DateTimeOffset]::UtcNow.AddSeconds(5)
        $replacement = $null
        do {
            $replacement = Invoke-SdkCommand `
                -Client $observer `
                -CommandId 'acquireControl' `
                -Scope 'control.playback' `
                -Arguments @{
                    scopes = 'control.playback'
                    ttlSeconds = '30'
                } `
                -AllowFailure
            if ($replacement.Success) {
                $replacementLeaseId =
                    [string]$replacement.Data['leaseId']
                break
            }
            Start-Sleep -Milliseconds 10
        } while ([DateTimeOffset]::UtcNow `
            -lt $replacementDeadline)
        if ($null -eq $replacement `
                -or -not $replacement.Success `
                -or [string]::IsNullOrEmpty(
                    $replacementLeaseId)) {
            throw 'Abrupt AgentBridge termination did not release lease.'
        }
        $released = Release-SdkLease `
            -Client $observer `
            -Scope 'control.playback' `
            -LeaseId $replacementLeaseId
        $replacementLeaseId = ''

        $final = Wait-SdkState `
            -Client $observer `
            -ControlMode 'Running' `
            -PlaybackMode 'Idle' `
            -Fields @{
                recordingActive = 'false'
                verificationEligibility = 'Eligible'
            }
        $securityEvidence = [ordered]@{
            unsafeUris = @($uriResults)
            unsafeTools = @($toolResults)
            invalidArguments = @($invalidArguments)
            oversizedLineErrorCode =
                [int]$oversizedResponse.error.code
            recoveredAfterOversizedLine = $true
            semanticHashBefore =
                $before.State.SemanticSnapshotSha256
            semanticHashAfter =
                $afterNegative.State.SemanticSnapshotSha256
            killedBridgeProcessId = $bridgeProcessId
            replacementLeaseAcquired = $true
            replacementLeaseReleased = $true
            finalRuntimeMode = $final.State.RuntimeMode
            finalPlaybackMode =
                (Get-StateField `
                    -SemanticState $final `
                    -Name 'playbackMode')
            finalRecordingActive =
                (Get-StateField `
                    -SemanticState $final `
                    -Name 'recordingActive')
            finalVerificationEligibility =
                (Get-StateField `
                    -SemanticState $final `
                    -Name 'verificationEligibility')
        }
        $securityEvidence |
            ConvertTo-Json -Depth 50 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'security-matrix.json'
                ) `
                -Encoding utf8NoBOM
        $tools |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-tools.json'
                ) `
                -Encoding utf8NoBOM
        $resources |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (
                    Join-Path $case.directory 'mcp-resources.json'
                ) `
                -Encoding utf8NoBOM
        $cases.Add(
            [pscustomobject][ordered]@{
                name = $case.name
                mode = $case.mode
                sessionId = $case.sessionId
                unsafeUriRejections = $uriResults.Count
                unsafeToolRejections = $toolResults.Count
                schemaRejections =
                    $invalidArguments.Count
                oversizedLineRejected = $true
                postOversizeRecovery = $true
                semanticStateUnchanged = $true
                abruptDisconnectLeaseReleased = $true
                finalRuntimeMode = 'Running'
                finalPlaybackMode = 'Idle'
                finalRecordingActive = $false
                finalVerificationEligibility = 'Eligible'
            })
        return $case
    }
    finally {
        if (-not [string]::IsNullOrEmpty(
                $replacementLeaseId) `
                -and $null -ne $observer) {
            try {
                [void](Release-SdkLease `
                    -Client $observer `
                    -Scope 'control.playback' `
                    -LeaseId $replacementLeaseId)
            }
            catch {
            }
        }
        Dispose-SdkClient -Client $observer
        Stop-McpBridge
        Close-RunProcesses
    }
}

try {
    $slotInitial = Get-SlotState
    $modsInitial = Get-ModTreeState

    foreach ($unexpectedPath in @($modsBackup, $modsEmpty)) {
        if (Test-Path -LiteralPath $unexpectedPath) {
            throw "Unexpected T15 Mods recovery path exists: $unexpectedPath"
        }
    }
    Move-Item `
        -LiteralPath $modsDirectory `
        -Destination $modsBackup
    $modsSwapped = $true
    New-Item `
        -ItemType Directory `
        -Path $modsDirectory |
        Out-Null
    Move-Item `
        -LiteralPath (Join-Path $modsBackup 'HollowKnightTAS') `
        -Destination (Join-Path $modsDirectory 'HollowKnightTAS')
    $isolatedEntries = @(
        Get-ChildItem -LiteralPath $modsDirectory -Force
    )
    if ($isolatedEntries.Count -ne 1 `
            -or $isolatedEntries[0].Name -ne 'HollowKnightTAS') {
        throw 'Failed to establish the isolated T15 Mods profile.'
    }

    if ($replayStoreOriginallyExisted) {
        if (Test-Path -LiteralPath $replayStoreBackup) {
            throw "Replay-store backup already exists: $replayStoreBackup"
        }
        Move-Item `
            -LiteralPath $replayStoreRoot `
            -Destination $replayStoreBackup
        $replayStoreMoved = $true
    }
    if ($automationOriginallyExisted) {
        if (Test-Path -LiteralPath $automationBackup) {
            throw "Automation backup already exists: $automationBackup"
        }
        Move-Item `
            -LiteralPath $automationRoot `
            -Destination $automationBackup
        $automationMoved = $true
    }

    Add-Type -Path $sdkCorePath
    Add-Type -Path $sdkClientPath

    foreach ($schema in Get-ChildItem `
            -LiteralPath (Join-Path $repoRoot 'schemas') `
            -Filter '*.json' `
            -File) {
        Get-Content -LiteralPath $schema.FullName -Raw |
            ConvertFrom-Json -Depth 100 |
            Out-Null
    }

    if (-not $SkipOfflineTests) {
        $testLog = Join-Path $EvidenceRoot 'offline-tests.txt'
        $offlineSuites = @(
            [pscustomobject]@{
                project =
                    'tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj'
                filter = ''
            },
            [pscustomobject]@{
                project =
                    'tests\HollowKnightTAS.Companion.Tests\HollowKnightTAS.Companion.Tests.csproj'
                filter = 'FullyQualifiedName~Automation'
            },
            [pscustomobject]@{
                project =
                    'tests\HollowKnightTAS.AgentBridge.Tests\HollowKnightTAS.AgentBridge.Tests.csproj'
                filter = ''
            }
        )
        foreach ($suite in $offlineSuites) {
            $arguments = @(
                'test',
                (Join-Path $repoRoot $suite.project),
                '-c',
                'Debug',
                '--no-restore',
                '--logger',
                'console;verbosity=normal'
            )
            if (-not [string]::IsNullOrEmpty($suite.filter)) {
                $arguments += @('--filter', $suite.filter)
            }
            & dotnet @arguments *>&1 |
                Tee-Object `
                    -FilePath $testLog `
                    -Append
            if ($LASTEXITCODE -ne 0) {
                throw "Offline automation tests failed for $($suite.project) with exit code $LASTEXITCODE."
            }
        }
    }

    if ($MutationOnly) {
        $mutationCase = Invoke-TypedMutationParity
        $cleanRestartCase =
            Invoke-CleanEligibilityRestart
    }
    elseif ($StateSoakOnly) {
        $stateDurabilityCase =
            Invoke-StateDurabilityMatrix
    }
    elseif ($AiOnly) {
        $scriptedAiCase =
            Invoke-McpScriptedAgentMatrix
    }
    elseif ($SecurityOnly) {
        $securityCase =
            Invoke-SecurityMatrix
    }
    else {
        if (-not $ApprovedControlOnly) {
            $disabledCase = Invoke-DisabledDiscovery
            $readOnlyCase = Invoke-ReadOnlyDiscovery
            Close-RunProcesses
        }
        if (-not $DiscoveryOnly) {
            $approvedControlCase =
                Invoke-ApprovedControlParity
        }
    }

    $result = [ordered]@{
        schemaVersion = 1
        campaign = Split-Path -Leaf $EvidenceRoot
        verdict = if ($DiscoveryOnly) {
            'DISCOVERY_PASS'
        }
        elseif ($MutationOnly) {
            'TYPED_MUTATION_PARTIAL_PASS'
        }
        elseif ($StateSoakOnly) {
            'STATE_DURABILITY_PARTIAL_PASS'
        }
        elseif ($AiOnly) {
            'SCRIPTED_AI_PARTIAL_PASS'
        }
        elseif ($SecurityOnly) {
            'SECURITY_PARTIAL_PASS'
        }
        elseif ($ApprovedControlOnly) {
            'CONTROL_PARITY_PARTIAL_PASS'
        }
        else {
            'CONTROL_PARITY_PARTIAL_PASS'
        }
        smoke = [bool]$Smoke
        parityIterations = $ParityIterations
        subscriptionSeconds = $SubscriptionSeconds
        cases = @($cases)
        ordinarySlotsUnchanged = $false
        modsUnchanged = $false
        settingsRestored = $false
        replayStoreRestored = $false
        automationWorkspaceRestored = $false
        isolatedModsProfile = $true
    }
    $result |
        ConvertTo-Json -Depth 100 |
        Set-Content `
            -LiteralPath (Join-Path $EvidenceRoot 'matrix.json') `
            -Encoding utf8NoBOM
}
finally {
    Close-RunProcesses
    [IO.File]::WriteAllBytes(
        $settingsPath,
        $settingsOriginal)
    if ($settingsBackupOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsBackupPath,
            $settingsBackupOriginal)
    }
    elseif (Test-Path -LiteralPath $settingsBackupPath) {
        Remove-Item -LiteralPath $settingsBackupPath -Force
    }

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
        $modsSwapped = $false
        if (Test-Path -LiteralPath $modsEmpty) {
            if (@(
                    Get-ChildItem `
                        -LiteralPath $modsEmpty `
                        -Force
                ).Count -ne 0) {
                throw 'T15 empty validation directory contains unexpected files.'
            }

            Remove-Item -LiteralPath $modsEmpty
        }
    }

    if (Test-Path -LiteralPath $replayStoreRoot) {
        $resolvedReplay =
            [IO.Path]::GetFullPath($replayStoreRoot)
        if (-not $resolvedReplay.StartsWith(
                $persistentPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove replay store outside persistent data.'
        }
        Remove-Item `
            -LiteralPath $resolvedReplay `
            -Recurse `
            -Force
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
        $brokerEvidence = Join-Path `
            $EvidenceRoot `
            'automation-broker-artifacts'
        if (-not (Test-Path -LiteralPath $brokerEvidence)) {
            Copy-Item `
                -LiteralPath (Join-Path $automationRoot 'artifacts') `
                -Destination $brokerEvidence `
                -Recurse
        }
    }
    if (Test-Path -LiteralPath $automationRoot) {
        $resolvedAutomation =
            [IO.Path]::GetFullPath($automationRoot)
        if (-not $resolvedAutomation.StartsWith(
                $localPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove automation root outside LocalApplicationData.'
        }
        Remove-Item `
            -LiteralPath $resolvedAutomation `
            -Recurse `
            -Force
    }
    if ($automationMoved) {
        Move-Item `
            -LiteralPath $automationBackup `
            -Destination $automationRoot
        $automationMoved = $false
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
if (-not [string]::Equals(
        $settingsBackupOriginalSha256,
        $settingsBackupFinalSha256,
        [StringComparison]::Ordinal)) {
    throw 'Settings backup was not restored byte-for-byte.'
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
    throw 'Replay-store existence was not restored.'
}
if ($automationOriginallyExisted -ne (
        Test-Path -LiteralPath $automationRoot -PathType Container
    )) {
    throw 'Automation-workspace existence was not restored.'
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
    throw 'A T15-owned process remained after cleanup.'
}

$auditFiles = @(
    Get-ChildItem `
        -LiteralPath (
            Join-Path $EvidenceRoot 'automation-broker-artifacts'
        ) `
        -Recurse `
        -File `
        -Filter '*.jsonl' `
        -ErrorAction SilentlyContinue
)
$auditLineCount = 0
$auditLeakMatches = 0
$auditLeakPattern =
    '(?i)(authorization\s*[:=]|bearer\s+[A-Za-z0-9._-]+|access[_-]?token|refresh[_-]?token|client[_-]?secret|C:\\Users\\33361)'
foreach ($auditFile in $auditFiles) {
    $auditLines = @(Get-Content -LiteralPath $auditFile.FullName)
    $auditLineCount += $auditLines.Count
    $auditLeakMatches += @(
        $auditLines |
            Select-String -Pattern $auditLeakPattern
    ).Count
}
if ($auditLeakMatches -ne 0) {
    throw 'Automation audit contained credential or absolute-path material.'
}

$result['ordinarySlotsUnchanged'] = $true
$result['modsUnchanged'] = $true
$result['settingsRestored'] = $true
$result['settingsRestoredSha256'] =
    $settingsFinalSha256.ToLowerInvariant()
$result['replayStoreRestored'] = $true
$result['automationWorkspaceRestored'] = $true
$result['auditFiles'] = $auditFiles.Count
$result['auditLines'] = $auditLineCount
$result['auditLeakMatches'] = $auditLeakMatches
$result |
    ConvertTo-Json -Depth 100 |
    Set-Content `
        -LiteralPath (Join-Path $EvidenceRoot 'matrix.json') `
        -Encoding utf8NoBOM

Write-Host (
    'T15 automation matrix completed: {0}' -f $EvidenceRoot
)

[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$LocalRunCount = 10,

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable = 'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory = 'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(60, 300)]
    [int]$MaxRunSeconds = 180,

    [switch]$Smoke,

    [switch]$Resume,

    [Nullable[bool]]$AutoStartCompanion = $null,

    [switch]$SkipDeliberateDivergence,

    [ValidateSet('Ignore', 'Disabled', 'Verified')]
    [string]$NativeObserveExpectation = 'Ignore',

    [ValidateRange(5, 60)]
    [int]$NativeObserveWaitSeconds = 15
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $campaignId = 'local-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N')
    $EvidenceRoot = Join-Path $projectRoot "artifacts\verification\$campaignId"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$effectiveRunCount = if ($Smoke) { 1 } else { $LocalRunCount }
$cliAssembly = Join-Path `
    $projectRoot `
    'src\HollowKnightTAS.Cli\bin\Debug\net8.0\HollowKnightTAS.Cli.dll'
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$sessionRoot = Join-Path $PersistentDataDirectory 'HollowKnightTAS\sessions'
$settingsPath = Join-Path `
    $PersistentDataDirectory `
    'HollowKnightTASMod.GlobalSettings.json'
$settingsBackupPath = $settingsPath + '.bak'
$settingsOriginallyExisted =
    Test-Path -LiteralPath $settingsPath -PathType Leaf
$settingsOriginalBytes =
    if ($settingsOriginallyExisted) {
        [IO.File]::ReadAllBytes($settingsPath)
    }
    else {
        $null
    }
$settingsOriginalSha256 =
    if ($settingsOriginallyExisted) {
        (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
    }
    else {
        $null
    }
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

if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) {
    throw "Build the Debug CLI before running T07: $cliAssembly"
}
if (-not (Test-Path -LiteralPath $SteamExecutable -PathType Leaf)) {
    throw "Steam executable not found: $SteamExecutable"
}
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) {
    throw "TAS session root was not found: $sessionRoot"
}

$resolvedManaged = (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods = (Resolve-Path -LiteralPath $modsDirectory).Path
if (-not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved Mods directory is outside the intended Managed directory.'
}
$tasModDirectory = Join-Path $modsDirectory 'HollowKnightTAS'
if (-not (Test-Path -LiteralPath $tasModDirectory -PathType Container)) {
    throw 'Installed HollowKnightTAS directory was not found.'
}
if (Get-Process -Name 'hollow_knight' -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight is already running.'
}
if (Test-Path -LiteralPath $EvidenceRoot) {
    if (
        -not $Resume `
            -and @(Get-ChildItem -LiteralPath $EvidenceRoot -Force).Count -ne 0
    ) {
        throw "Evidence root must be absent or empty: $EvidenceRoot"
    }
}
else {
    New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
}

$swapId = [Guid]::NewGuid().ToString('N')
$backupDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T07-$swapId.backup"
$emptyDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T07-$swapId.empty"
$gameProcess = $null
$runSummaries = [System.Collections.Generic.List[object]]::new()
$campaignOutput = ''
$comparisonOutput = ''
$comparisonPath = Join-Path $EvidenceRoot 'comparison.json'
$formalLevel = 'NOT_VERIFIED'

function Close-GameNormally {
    if ($null -eq $script:gameProcess) {
        return
    }

    $script:gameProcess.Refresh()
    if (-not $script:gameProcess.HasExited) {
        $requested = $script:gameProcess.CloseMainWindow()
        if ($requested) {
            [void]$script:gameProcess.WaitForExit(20000)
        }
    }
}

function Get-NativeObserveRunState {
    param(
        [Parameter(Mandatory)]
        [string]$EventsPath
    )

    if (-not (Test-Path -LiteralPath $EventsPath -PathType Leaf)) {
        throw "Session event evidence is missing: $EventsPath"
    }

    $events = [System.Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content -LiteralPath $EventsPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $events.Add(($line | ConvertFrom-Json))
        }
        catch {
            # A live writer can expose a partial final line. Final validation
            # runs after process exit and will observe the completed record.
        }
    }

    $service = @(
        $events |
            Where-Object eventType -eq 'companion-service-started'
    )
    $ready = @(
        $events |
            Where-Object {
                $_.eventType -eq 'companion-launch-state' `
                    -and $_.fields.state -eq 'Ready'
            }
    )
    $started = @(
        $events |
            Where-Object {
                $_.eventType -eq 'native-capability-evidence' `
                    -and $_.fields.capabilityId `
                        -eq 'native.process.observe.v1' `
                    -and $_.fields.status -eq 'started'
            }
    )
    $verified = @(
        $events |
            Where-Object {
                $_.eventType -eq 'native-capability-evidence' `
                    -and $_.fields.capabilityId `
                        -eq 'native.process.observe.v1' `
                    -and $_.fields.status -eq 'verified'
            }
    )
    $faulted = @(
        $events |
            Where-Object {
                $_.eventType -eq 'native-capability-evidence' `
                    -and $_.fields.status -eq 'faulted'
            }
    )
    $catalog = @(
        $events |
            Where-Object eventType -eq 'native-capability-catalog'
    )
    $verifiedFields =
        if ($verified.Count -eq 1) {
            $verified[0].fields
        }
        else {
            $null
        }

    return [pscustomobject]@{
        serviceStartedCount = $service.Count
        companionReadyCount = $ready.Count
        nativeCapabilitiesRequested =
            if ($service.Count -eq 1) {
                [string]$service[0].fields.nativeCapabilitiesRequested
            }
            else {
                ''
            }
        startedCount = $started.Count
        verifiedCount = $verified.Count
        faultedCount = $faulted.Count
        catalogMode =
            if ($catalog.Count -ge 1) {
                [string]$catalog[-1].fields.processObserve
            }
            else {
                ''
            }
        attachCyclesCompleted =
            if ($null -ne $verifiedFields) {
                [int]$verifiedFields.attachCyclesCompleted
            }
            else {
                0
            }
        parentProcessVerified =
            if ($null -ne $verifiedFields) {
                [string]$verifiedFields.parentProcessVerified
            }
            else {
                $null
            }
        rawPagesPersisted =
            if ($null -ne $verifiedFields) {
                [string]$verifiedFields.rawPagesPersisted
            }
            else {
                $null
            }
        checkpointStatus =
            if ($null -ne $verifiedFields) {
                [string]$verifiedFields.checkpointStatus
            }
            else {
                $null
            }
    }
}

function Test-NativeObserveExpectation {
    param(
        [Parameter(Mandatory)]
        [object]$State
    )

    if ($NativeObserveExpectation -eq 'Ignore') {
        return $true
    }

    if ($NativeObserveExpectation -eq 'Disabled') {
        return $State.serviceStartedCount -eq 1 `
            -and $State.companionReadyCount -eq 1 `
            -and $State.nativeCapabilitiesRequested -eq 'false' `
            -and $State.startedCount -eq 0 `
            -and $State.verifiedCount -eq 0 `
            -and $State.faultedCount -eq 0 `
            -and $State.catalogMode -eq 'experimental-disabled'
    }

    return $State.serviceStartedCount -eq 1 `
        -and $State.companionReadyCount -eq 1 `
        -and $State.nativeCapabilitiesRequested -eq 'true' `
        -and $State.startedCount -eq 1 `
        -and $State.verifiedCount -eq 1 `
        -and $State.faultedCount -eq 0 `
        -and $State.catalogMode -eq 'requested' `
        -and $State.attachCyclesCompleted -eq 100 `
        -and $State.parentProcessVerified -eq 'true' `
        -and $State.rawPagesPersisted -eq 'false' `
        -and $State.checkpointStatus -eq 'unsupported'
}

function Import-T07Run {
    param(
        [Parameter(Mandatory)]
        [string]$Profile,

        [Parameter(Mandatory)]
        [string]$RunName,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    $runJsonPath = Join-Path $Destination 'run.json'
    $runtimeResultPath = Join-Path $Destination 'runtime\result.json'
    if (
        -not (Test-Path -LiteralPath $runJsonPath -PathType Leaf) `
            -or -not (
                Test-Path `
                    -LiteralPath $runtimeResultPath `
                    -PathType Leaf
            )
    ) {
        throw "Existing T07 evidence is incomplete: $Destination"
    }

    $validateOutput = & dotnet `
        $cliAssembly `
        verification `
        validate `
        $runJsonPath 2>&1 |
        Out-String
    $validateExit = $LASTEXITCODE
    if ($validateExit -ne 0) {
        throw "CLI rejected runtime evidence in $Destination`: $validateOutput"
    }

    $runEvidence = Get-Content -LiteralPath $runJsonPath -Raw |
        ConvertFrom-Json
    $result = Get-Content -LiteralPath $runtimeResultPath -Raw |
        ConvertFrom-Json
    if (-not $result.runPass) {
        throw "Existing runtime evidence did not pass: $Destination"
    }

    $nativeObserveState = $null
    $nativeObservePass = $null
    if ($NativeObserveExpectation -ne 'Ignore') {
        $nativeObserveState =
            Get-NativeObserveRunState `
                -EventsPath (
                    Join-Path `
                        $Destination `
                        'runtime\session-events.jsonl'
                )
        $nativeObservePass =
            Test-NativeObserveExpectation -State $nativeObserveState
        if (-not $nativeObservePass) {
            throw "Native observe expectation $NativeObserveExpectation failed in $Destination"
        }
    }

    $summary = [pscustomobject]@{
        profile = $Profile
        runName = $RunName
        runId = $result.runId
        sessionId = $runEvidence.sessionId
        processInstanceId = $runEvidence.processInstanceId
        manifestSha256 = $runEvidence.manifestSha256
        baselineSha256 = $runEvidence.baselineSha256
        movieId = $runEvidence.movieId
        runSignature = $runEvidence.runSignature
        milestoneCount = @($runEvidence.milestones).Count
        observationCount = $result.observationCount
        suppressedHealthManagerCount =
            $result.suppressedHealthManagerCount
        nativeObserveExpectation = $NativeObserveExpectation
        nativeObservePass = $nativeObservePass
        nativeObserveVerifiedCount =
            if ($null -ne $nativeObserveState) {
                $nativeObserveState.verifiedCount
            }
            else {
                $null
            }
        nativeObserveAttachCycles =
            if ($null -ne $nativeObserveState) {
                $nativeObserveState.attachCyclesCompleted
            }
            else {
                $null
            }
        evidenceDirectory = $Destination
    }
    $script:runSummaries.Add($summary)
    return $summary
}

function Invoke-T07GameRun {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('VERIFY', 'DIVERGENCE')]
        [string]$Profile,

        [Parameter(Mandatory)]
        [string]$RunName,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        throw "Evidence destination already exists: $Destination"
    }

    $profileToken = if ($Profile -eq 'VERIFY') { 'v' } else { 'd' }
    $nameToken = $RunName.ToLowerInvariant()
    if ($nameToken.Length -gt 8) {
        $nameToken = $nameToken.Substring(0, 8)
    }
    $runId = '{0}-{1}-{2}-{3}' -f `
        $profileToken, `
        $nameToken, `
        [DateTimeOffset]::UtcNow.ToString('HHmmssfff'), `
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $beforeSessions = @(
        Get-ChildItem `
            -LiteralPath $sessionRoot `
            -Directory `
            -ErrorAction SilentlyContinue |
            ForEach-Object FullName
    )
    $steamConsoleLog = Join-Path `
        (Split-Path -Parent $SteamExecutable) `
        'logs\console_log.txt'
    $syncFailureCountBefore = if (
        Test-Path -LiteralPath $steamConsoleLog -PathType Leaf
    ) {
        @(
            Select-String `
                -LiteralPath $steamConsoleLog `
                -SimpleMatch `
                'SynchronizingCloud "syncfailed"'
        ).Count
    }
    else {
        0
    }
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
        "--hktas-playback-probe=$Profile",
        "--hktas-playback-probe-run=$runId",
        "--hktas-playback-probe-slot=$TestSaveSlot"
    )
    if ($NativeObserveExpectation -eq 'Ignore') {
        $arguments += '--hktas-playback-probe-exit'
    }
    $launchAttempts = 1
    $lastLaunchAttempt = Get-Date
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $script:gameProcess = $null
    $resultPath = $null
    $sourceDirectory = $null
    $sessionDirectory = $null
    $deadline = (Get-Date).AddSeconds($MaxRunSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if (
            $null -eq $script:gameProcess `
                -and (
                    Test-Path `
                        -LiteralPath $steamConsoleLog `
                        -PathType Leaf
                )
        ) {
            $syncFailureCount = @(
                Select-String `
                    -LiteralPath $steamConsoleLog `
                    -SimpleMatch `
                    'SynchronizingCloud "syncfailed"'
            ).Count
            if ($syncFailureCount -gt $syncFailureCountBefore) {
                throw 'Steam blocked Hollow Knight launch on a cloud-sync warning. Resolve or explicitly confirm the Steam dialog, then resume the campaign.'
            }
        }

        if ($null -eq $script:gameProcess) {
            $script:gameProcess = Get-Process |
                Where-Object {
                    $_.ProcessName -eq 'hollow_knight' `
                        -and $_.StartTime -ge $launchTime.AddSeconds(-2)
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if (
                $null -eq $script:gameProcess `
                    -and $launchAttempts -lt 5 `
                    -and ((Get-Date) - $lastLaunchAttempt).TotalSeconds `
                        -ge 20
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
                    Join-Path $_.FullName "playback\$runId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $sessionDirectory = $newSession.FullName
            $sourceDirectory = Join-Path `
                $sessionDirectory `
                "playback\$runId"
            $candidate = Join-Path $sourceDirectory 'result.json'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $resultPath = $candidate
                break
            }
        }
    }

    if ($null -eq $resultPath) {
        Close-GameNormally
        throw "T07 run timed out: $runId"
    }

    $result = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    if (-not $result.runPass) {
        Close-GameNormally
        throw "Runtime T07 probe failed: $runId"
    }
    if ($NativeObserveExpectation -ne 'Ignore') {
        $nativeSettleDeadline =
            (Get-Date).AddSeconds($NativeObserveWaitSeconds)
        while ((Get-Date) -lt $nativeSettleDeadline) {
            if ($null -ne $script:gameProcess) {
                $script:gameProcess.Refresh()
                if ($script:gameProcess.HasExited) {
                    throw "Game exited before native observe settle completed: $runId"
                }
            }

            Start-Sleep -Milliseconds 250
        }
        Close-GameNormally
    }
    if ($null -ne $script:gameProcess) {
        [void]$script:gameProcess.WaitForExit(30000)
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            Close-GameNormally
            throw "Game did not exit normally after T07 run: $runId"
        }
    }
    $script:gameProcess = $null
    Start-Sleep -Seconds 3

    $verificationSource = Join-Path $sourceDirectory 'verification'
    $runJsonSource = Join-Path $verificationSource 'run.json'
    if (-not (Test-Path -LiteralPath $runJsonSource -PathType Leaf)) {
        throw "Runtime verification evidence is missing: $runJsonSource"
    }
    Copy-Item `
        -LiteralPath $verificationSource `
        -Destination $Destination `
        -Recurse

    $runtimeDestination = Join-Path $Destination 'runtime'
    New-Item -ItemType Directory -Path $runtimeDestination | Out-Null
    Get-ChildItem -LiteralPath $sourceDirectory -File |
        ForEach-Object {
            Copy-Item `
                -LiteralPath $_.FullName `
                -Destination $runtimeDestination
        }
    foreach ($manifestName in @('manifest.json', 'manifest.sha256')) {
        Copy-Item `
            -LiteralPath (Join-Path $sessionDirectory $manifestName) `
            -Destination $runtimeDestination
    }
    $sessionEventsSource =
        Join-Path $sessionDirectory 'events.jsonl'
    if (Test-Path -LiteralPath $sessionEventsSource -PathType Leaf) {
        Copy-Item `
            -LiteralPath $sessionEventsSource `
            -Destination (
                Join-Path $runtimeDestination 'session-events.jsonl'
            )
    }
    elseif ($NativeObserveExpectation -ne 'Ignore') {
        throw "Session event evidence is missing: $sessionEventsSource"
    }

    return Import-T07Run `
        -Profile $Profile `
        -RunName $RunName `
        -Destination $Destination
}

try {
    if ($null -ne $AutoStartCompanion) {
        $settings =
            if ($settingsOriginallyExisted) {
                Get-Content -LiteralPath $settingsPath -Raw |
                    ConvertFrom-Json -AsHashtable -Depth 50
            }
            else {
                [ordered]@{}
            }
        $settings['CompanionEnabled'] = $true
        $settings['AutoStartCompanion'] =
            [bool]$AutoStartCompanion
        $settings['ExitCompanionWithGame'] = $true
        $settings |
            ConvertTo-Json -Depth 50 |
            Set-Content `
                -LiteralPath $settingsPath `
                -Encoding utf8NoBOM
    }

    Move-Item -LiteralPath $modsDirectory -Destination $backupDirectory
    New-Item -ItemType Directory -Path $modsDirectory | Out-Null
    Move-Item `
        -LiteralPath (Join-Path $backupDirectory 'HollowKnightTAS') `
        -Destination (Join-Path $modsDirectory 'HollowKnightTAS')

    $isolatedEntries = @(Get-ChildItem -LiteralPath $modsDirectory)
    if (
        $isolatedEntries.Count -ne 1 `
            -or $isolatedEntries[0].Name -ne 'HollowKnightTAS'
    ) {
        throw 'Failed to establish the isolated T07 Mods profile.'
    }

    for ($index = 1; $index -le $effectiveRunCount; $index++) {
        $runName = 'run-{0:D2}' -f $index
        $destination = Join-Path $EvidenceRoot $runName
        if (Test-Path -LiteralPath $destination) {
            if (-not $Resume) {
                throw "Evidence destination already exists: $destination"
            }
            Import-T07Run `
                -Profile 'VERIFY' `
                -RunName $runName `
                -Destination $destination |
                ConvertTo-Json -Compress
        }
        else {
            Invoke-T07GameRun `
                -Profile 'VERIFY' `
                -RunName $runName `
                -Destination $destination |
                ConvertTo-Json -Compress
        }
    }

    $dependencySemanticProjection = @()
    $dependencySemanticProjectionPass = $null
    if ($SkipDeliberateDivergence) {
        foreach ($index in 1..$effectiveRunCount) {
            $runPath = Join-Path `
                $EvidenceRoot `
                ('run-{0:D2}\run.json' -f $index)
            $run = Get-Content -LiteralPath $runPath -Raw |
                ConvertFrom-Json
            $projection = @(
                $run.milestones |
                    ForEach-Object {
                        '{0}|{1}|{2}' -f `
                            $_.milestoneId, `
                            $_.movieTick, `
                            $_.semanticSha256
                    }
            )
            if ($index -eq 1) {
                $dependencySemanticProjection = $projection
                continue
            }

            if ($projection.Count -ne $dependencySemanticProjection.Count) {
                $message =
                    'Dependency semantic milestone count changed in run-{0:D2}.' -f
                    $index
                throw $message
            }
            for (
                $milestoneIndex = 0;
                $milestoneIndex -lt $projection.Count;
                $milestoneIndex++
            ) {
                $expectedMilestone =
                    [string]$dependencySemanticProjection[$milestoneIndex]
                $actualMilestone =
                    [string]$projection[$milestoneIndex]
                if ($expectedMilestone -cne $actualMilestone) {
                    $message =
                        'Dependency semantic milestone changed in run-{0:D2} at index {1}.' -f
                        $index,
                        $milestoneIndex
                    throw $message
                }
            }
        }
        $dependencySemanticProjectionPass = $true
        $campaignOutput =
            'PER_RUN_CLI_VALIDATED DEPENDENCY_SEMANTIC_MATCH runs={0} milestones={1}' -f
            $effectiveRunCount,
            $dependencySemanticProjection.Count
    }
    else {
        $campaignOutput = & dotnet `
            $cliAssembly `
            verification `
            campaign `
            $EvidenceRoot 2>&1 |
            Out-String
        $campaignExit = $LASTEXITCODE
        if ($campaignExit -ne 0) {
            throw "Local verification campaign failed: $campaignOutput"
        }
        $expectedCampaignLabel = if ($effectiveRunCount -ge 10) {
            'LOCAL_VERIFIED'
        }
        else {
            'MATCHING_UNLABELED'
        }
        if (-not $campaignOutput.StartsWith(
                $expectedCampaignLabel,
                [StringComparison]::Ordinal)) {
            throw "Unexpected campaign label: $campaignOutput"
        }
    }

    $deliberateDivergencePass = $null
    if ($SkipDeliberateDivergence) {
        $comparisonOutput = 'SKIPPED_DEPENDENCY_ORACLE'
    }
    else {
        $divergenceDestination = Join-Path `
            $EvidenceRoot `
            'deliberate-divergence'
        if (Test-Path -LiteralPath $divergenceDestination) {
            if (-not $Resume) {
                throw "Evidence destination already exists: $divergenceDestination"
            }
            Import-T07Run `
                -Profile 'DIVERGENCE' `
                -RunName 'deliberate-divergence' `
                -Destination $divergenceDestination |
                ConvertTo-Json -Compress
        }
        else {
            Invoke-T07GameRun `
                -Profile 'DIVERGENCE' `
                -RunName 'deliberate-divergence' `
                -Destination $divergenceDestination |
                ConvertTo-Json -Compress
        }

        $comparisonOutput = & dotnet `
            $cliAssembly `
            verification `
            compare `
            (Join-Path $EvidenceRoot 'run-01\run.json') `
            (Join-Path $divergenceDestination 'run.json') `
            --out `
            $comparisonPath 2>&1 |
            Out-String
        $comparisonExit = $LASTEXITCODE
        if ($comparisonExit -ne 4) {
            throw "Deliberate divergence did not produce the expected DESYNC exit code 4: exit=$comparisonExit output=$comparisonOutput"
        }

        $comparison =
            Get-Content -LiteralPath $comparisonPath -Raw |
            ConvertFrom-Json
        if (
            $comparison.status -ne 'Desync' `
                -or $comparison.firstDifference.milestoneId `
                    -ne 'checkpoint:divergence-probe' `
                -or [long]$comparison.firstDifference.movieTick -ne 491 `
                -or @(
                    $comparison.firstDifference
                        .actualContext.ledgerWindow
                ).Count -eq 0
        ) {
            throw 'Deliberate divergence report did not identify the expected actionable milestone.'
        }
        $deliberateDivergencePass = $true
    }

    $formalLevel = if ($SkipDeliberateDivergence) {
        'DEPENDENCY_ORACLE'
    }
    elseif ($effectiveRunCount -ge 10) {
        'LOCAL_VERIFIED'
    }
    else {
        'SMOKE_PASS'
    }

    $campaign = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        level = $formalLevel
        localRunCount = $effectiveRunCount
        campaignCli = $campaignOutput.Trim()
        deliberateDivergenceCli = $comparisonOutput.Trim()
        deliberateDivergencePass = $deliberateDivergencePass
        dependencySemanticProjectionPass =
            $dependencySemanticProjectionPass
        dependencySemanticProjection =
            @($dependencySemanticProjection)
        nativeObserveExpectation = $NativeObserveExpectation
        nativeObserveValidationPass =
            if ($NativeObserveExpectation -eq 'Ignore') {
                $null
            }
            else {
                @(
                    $runSummaries |
                        Where-Object {
                            $_.profile -eq 'VERIFY' `
                                -and $_.nativeObservePass
                        }
                ).Count -eq $effectiveRunCount
            }
        portableVerification = 'NOT_RUN'
        runs = @($runSummaries)
    }
    $campaignPath = Join-Path $EvidenceRoot 'campaign.json'
    $campaign |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $campaignPath -Encoding utf8NoBOM

    $reportPath = Join-Path $EvidenceRoot 'report.md'
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# T07 Determinism Verification Report')
    $lines.Add('')
    $lines.Add("- Level: ``$formalLevel``")
    $lines.Add("- Local cold-start runs: $effectiveRunCount")
    $lines.Add("- Campaign CLI: ``$($campaignOutput.Trim())``")
    $lines.Add("- Deliberate divergence: ``$($comparisonOutput.Trim())``")
    $lines.Add("- Native observe expectation: ``$NativeObserveExpectation``")
    $lines.Add('- Portable verification: `NOT_RUN`')
    $lines.Add('')
    $lines.Add('| Run | Session | Process | Signature |')
    $lines.Add('|---|---|---|---|')
    foreach (
        $run in $runSummaries |
            Where-Object { $_.profile -eq 'VERIFY' }
    ) {
        $lines.Add(
            "| $($run.runName) | $($run.sessionId) | $($run.processInstanceId) | ``$($run.runSignature)`` |"
        )
    }
    $lines.Add('')
    $lines.Add(
        'The portability campaign was not executed, so this evidence must not be labelled `PORTABLE_VERIFIED`.'
    )
    $lines |
        Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM

    [pscustomobject]@{
        Level = $formalLevel
        LocalRunCount = $effectiveRunCount
        DeliberateDivergencePass =
            $deliberateDivergencePass
        PortableVerification = 'NOT_RUN'
        EvidenceRoot = $EvidenceRoot
        CampaignPath = $campaignPath
        ComparisonPath =
            if ($SkipDeliberateDivergence) {
                $null
            }
            else {
                $comparisonPath
            }
        ReportPath = $reportPath
    } | ConvertTo-Json
}
finally {
    Close-GameNormally
    if ($null -ne $gameProcess) {
        $gameProcess.Refresh()
        if (-not $gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T07 profile recovery.'
        }
    }

    $isolatedTas = Join-Path $modsDirectory 'HollowKnightTAS'
    if (Test-Path -LiteralPath $isolatedTas) {
        Move-Item `
            -LiteralPath $isolatedTas `
            -Destination (Join-Path $backupDirectory 'HollowKnightTAS')
    }
    if (Test-Path -LiteralPath $modsDirectory) {
        Move-Item -LiteralPath $modsDirectory -Destination $emptyDirectory
    }
    if (Test-Path -LiteralPath $backupDirectory) {
        Move-Item -LiteralPath $backupDirectory -Destination $modsDirectory
    }

    if (
        -not (
            Test-Path `
                -LiteralPath (Join-Path $modsDirectory 'HollowKnightTAS') `
                -PathType Container
        )
    ) {
        throw 'T07 profile recovery failed: HollowKnightTAS was not restored.'
    }
    if (Test-Path -LiteralPath $emptyDirectory) {
        if (@(Get-ChildItem -LiteralPath $emptyDirectory -Force).Count -ne 0) {
            throw 'T07 empty validation directory contains unexpected files.'
        }
        Remove-Item -LiteralPath $emptyDirectory
    }

    if ($settingsOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsPath,
            $settingsOriginalBytes)
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath
    }
    if ($settingsBackupOriginallyExisted) {
        [IO.File]::WriteAllBytes(
            $settingsBackupPath,
            $settingsBackupOriginalBytes)
    }
    elseif (Test-Path -LiteralPath $settingsBackupPath) {
        Remove-Item -LiteralPath $settingsBackupPath
    }

    if (
        $settingsOriginallyExisted `
            -and (
                Get-FileHash `
                    -LiteralPath $settingsPath `
                    -Algorithm SHA256
            ).Hash -ne $settingsOriginalSha256
    ) {
        throw 'T07 global settings were not restored byte-for-byte.'
    }
    if (
        $settingsBackupOriginallyExisted `
            -and (
                Get-FileHash `
                    -LiteralPath $settingsBackupPath `
                    -Algorithm SHA256
            ).Hash -ne $settingsBackupOriginalSha256
    ) {
        throw 'T07 global settings backup was not restored byte-for-byte.'
    }

}

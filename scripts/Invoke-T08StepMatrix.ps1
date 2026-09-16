[CmdletBinding()]
param(
    [ValidateSet(
        'PAUSE_STATIONARY',
        'PAUSE_MOVING',
        'BASELINE',
        'STEP',
        'RESUME',
        'SCENE',
        'FOCUS',
        'FAULT',
        'SHUTDOWN'
    )]
    [string[]]$Profiles = @(
        'PAUSE_STATIONARY',
        'PAUSE_MOVING',
        'BASELINE',
        'STEP',
        'RESUME',
        'SCENE',
        'FOCUS',
        'FAULT',
        'SHUTDOWN'
    ),

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable = 'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory = 'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(60, 300)]
    [int]$MaxRunSeconds = 180,

    [switch]$Resume
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $matrixId = 'local-{0}-{1}' -f `
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
        [Guid]::NewGuid().ToString('N')
    $EvidenceRoot = Join-Path $projectRoot "artifacts\step\$matrixId"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$sessionRoot = Join-Path $PersistentDataDirectory 'HollowKnightTAS\sessions'

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
$backupDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T08-$swapId.backup"
$emptyDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T08-$swapId.empty"
$gameProcess = $null
$summaries = [System.Collections.Generic.List[object]]::new()

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

function Import-T08Run {
    param(
        [Parameter(Mandatory)]
        [string]$Profile,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    $resultPath = Join-Path $Destination 'result.json'
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Existing T08 evidence is incomplete: $Destination"
    }

    $result = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    if (-not $result.runPass) {
        throw "Existing T08 evidence did not pass: $Destination"
    }

    $summary = [pscustomobject]@{
        profile = $Profile
        runId = $result.runId
        sessionId = $result.sessionId
        processInstanceId = $result.processInstanceId
        endpointVerificationSha256 =
            $result.endpointVerificationSha256
        pauseMetricsPresent = $result.pauseMetricsPresent
        pauseDurationSeconds = $result.pauseDurationSeconds
        pauseMovieBefore = $result.pauseMovieBefore
        pauseMovieAfter = $result.pauseMovieAfter
        pauseVisualDelta = $result.pauseVisualDelta
        pauseFixedDelta = $result.pauseFixedDelta
        pauseInputDelta = $result.pauseInputDelta
        stepCount = $result.stepCount
        distinctStepDeltaTupleCount =
            $result.distinctStepDeltaTupleCount
        namingVerdict = $result.namingVerdict
        controlAbortReason = $result.controlAbortReason
        timeRestoreApplicable = $result.timeRestoreApplicable
        timeRestoreEquivalent = $result.timeRestoreEquivalent
        bindingRestoreEquivalent = $result.bindingRestoreEquivalent
        ledgerValid = $result.ledgerValid
        ledgerRecordCount = $result.ledgerRecordCount
        evidenceDirectory = $Destination
    }
    $script:summaries.Add($summary)
    return $summary
}

function Invoke-T08GameRun {
    param(
        [Parameter(Mandatory)]
        [string]$Profile,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        throw "Evidence destination already exists: $Destination"
    }

    $profileToken = $Profile.ToLowerInvariant().Replace('_', '-')
    if ($profileToken.Length -gt 14) {
        $profileToken = $profileToken.Substring(0, 14)
    }
    $runId = '{0}-{1}-{2}' -f `
        $profileToken, `
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
        "--hktas-step-probe=$Profile",
        "--hktas-step-probe-run=$runId",
        "--hktas-step-probe-slot=$TestSaveSlot",
        '--hktas-step-probe-exit'
    )
    $launchAttempts = 1
    $lastLaunchAttempt = Get-Date
    Start-Process `
        -FilePath $SteamExecutable `
        -ArgumentList $arguments `
        -WindowStyle Hidden

    $script:gameProcess = $null
    $processInstanceId = ''
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
                throw 'Steam blocked Hollow Knight launch on a cloud-sync warning. Resolve the Steam dialog, then resume the matrix.'
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
            if ($null -ne $script:gameProcess) {
                $processInstanceId = '{0}-{1}' -f `
                    $script:gameProcess.Id, `
                    $script:gameProcess.StartTime.ToUniversalTime().Ticks
            }
            elseif (
                $launchAttempts -lt 5 `
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
                    Join-Path $_.FullName "step\$runId"
                )
            } |
            Select-Object -First 1
        if ($null -ne $newSession) {
            $sessionDirectory = $newSession.FullName
            $sourceDirectory = Join-Path `
                $sessionDirectory `
                "step\$runId"
            $candidate = Join-Path $sourceDirectory 'result.json'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $resultPath = $candidate
                break
            }
        }
    }

    if ($null -eq $resultPath) {
        Close-GameNormally
        throw "T08 run timed out: $runId"
    }

    $result = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    if (-not $result.runPass) {
        Close-GameNormally
        throw "Runtime T08 probe failed: $runId"
    }
    if ($null -ne $script:gameProcess) {
        [void]$script:gameProcess.WaitForExit(30000)
        $script:gameProcess.Refresh()
        if (-not $script:gameProcess.HasExited) {
            Close-GameNormally
            throw "Game did not exit normally after T08 run: $runId"
        }
    }
    $script:gameProcess = $null
    Start-Sleep -Seconds 3

    Copy-Item `
        -LiteralPath $sourceDirectory `
        -Destination $Destination `
        -Recurse
    foreach ($manifestName in @('manifest.json', 'manifest.sha256')) {
        Copy-Item `
            -LiteralPath (Join-Path $sessionDirectory $manifestName) `
            -Destination $Destination
    }

    $destinationResultPath = Join-Path $Destination 'result.json'
    $destinationResult = Get-Content `
        -LiteralPath $destinationResultPath `
        -Raw |
        ConvertFrom-Json
    $destinationResult |
        Add-Member `
            -NotePropertyName sessionId `
            -NotePropertyValue (Split-Path -Leaf $sessionDirectory)
    $destinationResult |
        Add-Member `
            -NotePropertyName processInstanceId `
            -NotePropertyValue $processInstanceId
    $destinationResult |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -LiteralPath $destinationResultPath `
            -Encoding utf8NoBOM

    return Import-T08Run `
        -Profile $Profile `
        -Destination $Destination
}

try {
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
        throw 'Failed to establish the isolated T08 Mods profile.'
    }

    foreach ($profile in $Profiles) {
        $destination = Join-Path $EvidenceRoot $profile.ToLowerInvariant()
        if (Test-Path -LiteralPath $destination) {
            if (-not $Resume) {
                throw "Evidence destination already exists: $destination"
            }
            Import-T08Run `
                -Profile $profile `
                -Destination $destination |
                ConvertTo-Json -Compress
        }
        else {
            Invoke-T08GameRun `
                -Profile $profile `
                -Destination $destination |
                ConvertTo-Json -Compress
        }
    }

    $baseline = $summaries |
        Where-Object profile -eq 'BASELINE' |
        Select-Object -First 1
    $step = $summaries |
        Where-Object profile -eq 'STEP' |
        Select-Object -First 1
    $endpointParity = $null -ne $baseline `
        -and $null -ne $step `
        -and -not [string]::IsNullOrWhiteSpace(
            $baseline.endpointVerificationSha256
        ) `
        -and [string]::Equals(
            $baseline.endpointVerificationSha256,
            $step.endpointVerificationSha256,
            [StringComparison]::Ordinal
        )
    $allRequested = @($summaries).Count -eq @($Profiles).Count
    $gatePass = $allRequested `
        -and $endpointParity `
        -and @(
            $summaries |
                Where-Object {
                    -not $_.bindingRestoreEquivalent `
                        -or -not $_.ledgerValid `
                        -or (
                            $_.profile -ne 'BASELINE' `
                                -and -not $_.timeRestoreEquivalent
                        )
                }
        ).Count -eq 0
    if (-not $gatePass) {
        throw 'T08 aggregate acceptance conditions were not met.'
    }

    $matrix = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        gatePass = $gatePass
        endpointParity = $endpointParity
        baselineEndpointVerificationSha256 =
            $baseline.endpointVerificationSha256
        stepEndpointVerificationSha256 =
            $step.endpointVerificationSha256
        namingVerdict = $step.namingVerdict
        profiles = @($summaries)
    }
    $matrixPath = Join-Path $EvidenceRoot 'step-matrix.json'
    $matrix |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $matrixPath -Encoding utf8NoBOM

    $reportPath = Join-Path $EvidenceRoot 'report.md'
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# T08 Pause / Controlled Step Matrix')
    $lines.Add('')
    $lines.Add("- Gate pass: ``$gatePass``")
    $lines.Add("- Endpoint parity: ``$endpointParity``")
    $lines.Add("- Naming verdict: ``$($step.namingVerdict)``")
    $lines.Add(
        "- Endpoint verification SHA-256: ``$($step.endpointVerificationSha256)``"
    )
    $lines.Add('')
    $lines.Add(
        '| Profile | Pause seconds | Movie | Visual | Fixed | Input | Steps | Restore | Result |'
    )
    $lines.Add('|---|---:|---|---:|---:|---:|---:|---|---|')
    foreach ($run in $summaries) {
        $movie = if ($run.pauseMetricsPresent) {
            "$($run.pauseMovieBefore)→$($run.pauseMovieAfter)"
        }
        else {
            '-'
        }
        $pauseSeconds = if ($run.pauseMetricsPresent) {
            $run.pauseDurationSeconds
        }
        else {
            '-'
        }
        $visual = if ($run.pauseMetricsPresent) {
            $run.pauseVisualDelta
        }
        else {
            '-'
        }
        $fixed = if ($run.pauseMetricsPresent) {
            $run.pauseFixedDelta
        }
        else {
            '-'
        }
        $input = if ($run.pauseMetricsPresent) {
            $run.pauseInputDelta
        }
        else {
            '-'
        }
        $restore = if ($run.timeRestoreApplicable) {
            $run.timeRestoreEquivalent
        }
        else {
            'N/A'
        }
        $lines.Add(
            "| $($run.profile) | $pauseSeconds | $movie | $visual | $fixed | $input | $($run.stepCount) | $restore | PASS |"
        )
    }
    $lines |
        Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM

    [pscustomobject]@{
        GatePass = $gatePass
        EndpointParity = $endpointParity
        NamingVerdict = $step.namingVerdict
        EvidenceRoot = $EvidenceRoot
        MatrixPath = $matrixPath
        ReportPath = $reportPath
    } | ConvertTo-Json
}
finally {
    Close-GameNormally
    if ($null -ne $gameProcess) {
        $gameProcess.Refresh()
        if (-not $gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T08 profile recovery.'
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
        throw 'T08 profile recovery failed: HollowKnightTAS was not restored.'
    }
    if (Test-Path -LiteralPath $emptyDirectory) {
        if (@(Get-ChildItem -LiteralPath $emptyDirectory -Force).Count -ne 0) {
            throw 'T08 empty validation directory contains unexpected files.'
        }
        Remove-Item -LiteralPath $emptyDirectory
    }
}

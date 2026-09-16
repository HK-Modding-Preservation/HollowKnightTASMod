[CmdletBinding()]
param(
    [ValidateSet(
        'ORIGINAL',
        'EDITED',
        'PHYSICAL',
        'MANUAL',
        'EMERGENCY',
        'SCENE',
        'FAULT',
        'SHADOW',
        'All'
    )]
    [string]$Profile = 'All',

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable = 'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory = 'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(60, 300)]
    [int]$MaxRunSeconds = 180,

    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\playback'
}

$modsDirectory = Join-Path $ManagedDirectory 'Mods'
$resolvedManaged = (Resolve-Path -LiteralPath $ManagedDirectory).Path
$resolvedMods = (Resolve-Path -LiteralPath $modsDirectory).Path
if (-not [string]::Equals(
        (Split-Path -Parent $resolvedMods),
        $resolvedManaged,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved Mods directory is outside the intended Managed directory.'
}
if (-not (Test-Path -LiteralPath $SteamExecutable -PathType Leaf)) {
    throw "Steam executable not found: $SteamExecutable"
}
if (-not (Test-Path -LiteralPath (Join-Path $modsDirectory 'HollowKnightTAS') -PathType Container)) {
    throw 'Installed HollowKnightTAS directory was not found.'
}
if (Get-Process -Name 'hollow_knight' -ErrorAction SilentlyContinue) {
    throw 'Hollow Knight is already running.'
}

New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
$sessionRoot = Join-Path $PersistentDataDirectory 'HollowKnightTAS\sessions'
$swapId = [Guid]::NewGuid().ToString('N')
$backupDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T06-$swapId.backup"
$emptyDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T06-$swapId.empty"
$gameProcess = $null
$runResults = @()

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

try {
    Move-Item -LiteralPath $modsDirectory -Destination $backupDirectory
    New-Item -ItemType Directory -Path $modsDirectory | Out-Null
    Move-Item `
        -LiteralPath (Join-Path $backupDirectory 'HollowKnightTAS') `
        -Destination (Join-Path $modsDirectory 'HollowKnightTAS')

    $isolatedEntries = @(Get-ChildItem -LiteralPath $modsDirectory)
    if ($isolatedEntries.Count -ne 1 -or $isolatedEntries[0].Name -ne 'HollowKnightTAS') {
        throw 'Failed to establish the isolated T06 Mods profile.'
    }

    [string[]]$profiles = if ($Profile -eq 'All') {
        @(
            'ORIGINAL',
            'EDITED',
            'PHYSICAL',
            'MANUAL',
            'EMERGENCY',
            'SCENE',
            'FAULT',
            'SHADOW'
        )
    }
    else {
        @($Profile)
    }

    foreach ($profileId in $profiles) {
        $runCount = if (
            -not $Smoke `
                -and $Profile -eq 'All' `
                -and ($profileId -eq 'ORIGINAL' -or $profileId -eq 'EDITED')
        ) {
            5
        }
        else {
            1
        }

        for ($runIndex = 1; $runIndex -le $runCount; $runIndex++) {
            $runId = '{0}-{1:D2}-{2}' -f `
                $profileId.ToLowerInvariant(), `
                $runIndex, `
                [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
            $beforeSessions = @(
                Get-ChildItem -LiteralPath $sessionRoot -Directory -ErrorAction SilentlyContinue |
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
                "--hktas-playback-probe=$profileId",
                "--hktas-playback-probe-run=$runId",
                "--hktas-playback-probe-slot=$TestSaveSlot",
                '--hktas-playback-probe-exit'
            )
            Start-Process `
                -FilePath $SteamExecutable `
                -ArgumentList $arguments `
                -WindowStyle Hidden

            $gameProcess = $null
            $resultPath = $null
            $probeDirectory = $null
            $deadline = (Get-Date).AddSeconds($MaxRunSeconds)
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 250
                if ($null -eq $gameProcess) {
                    $gameProcess = Get-Process |
                        Where-Object {
                            $_.ProcessName -eq 'hollow_knight' `
                                -and $_.StartTime -ge $launchTime.AddSeconds(-2)
                        } |
                        Sort-Object StartTime -Descending |
                        Select-Object -First 1
                }

                $newSession = Get-ChildItem -LiteralPath $sessionRoot -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $beforeSessions -notcontains $_.FullName } |
                    Sort-Object LastWriteTime -Descending |
                    Where-Object {
                        Test-Path -LiteralPath (
                            Join-Path $_.FullName "playback\$runId"
                        )
                    } |
                    Select-Object -First 1
                if ($null -ne $newSession) {
                    $probeDirectory = Join-Path $newSession.FullName "playback\$runId"
                    $candidateResult = Join-Path $probeDirectory 'result.json'
                    if (Test-Path -LiteralPath $candidateResult) {
                        $resultPath = $candidateResult
                        break
                    }
                }

            }

            if ($null -eq $resultPath) {
                Close-GameNormally
                throw "T06 run timed out: $runId"
            }

            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            $runResults += $result
            $sourceDirectory = Split-Path -Parent $resultPath
            $sessionDirectory = Split-Path -Parent (Split-Path -Parent $sourceDirectory)
            if ($null -ne $gameProcess) {
                [void]$gameProcess.WaitForExit(30000)
                $gameProcess.Refresh()
                if (-not $gameProcess.HasExited) {
                    Close-GameNormally
                    throw "Game did not exit normally after T06 run: $runId"
                }
            }
            $gameProcess = $null

            $evidenceName = (Split-Path -Leaf $sessionDirectory) + '-' + $runId
            $evidenceDestination = Join-Path $EvidenceRoot $evidenceName
            if (Test-Path -LiteralPath $evidenceDestination) {
                throw "Evidence destination already exists: $evidenceDestination"
            }
            Copy-Item -LiteralPath $sourceDirectory -Destination $evidenceDestination -Recurse
            $shadowSource = [string]$result.shadowDirectory
            if (-not [string]::IsNullOrWhiteSpace($shadowSource) `
                -and (Test-Path -LiteralPath $shadowSource -PathType Container)) {
                Copy-Item `
                    -LiteralPath $shadowSource `
                    -Destination (Join-Path $evidenceDestination 'shadow-journal') `
                    -Recurse
            }
            Copy-Item `
                -LiteralPath (Join-Path $sessionDirectory 'manifest.json') `
                -Destination (Join-Path $evidenceDestination 'manifest.json')
            Copy-Item `
                -LiteralPath (Join-Path $sessionDirectory 'manifest.sha256') `
                -Destination (Join-Path $evidenceDestination 'manifest.sha256')

            [pscustomobject]@{
                Profile = $result.profile
                RunId = $result.runId
                PlaybackStopReason = $result.playbackStopReason
                Observations = $result.observationCount
                Mismatches = $result.mismatchCount
                BindingRestore = $result.bindingRestoreEquivalent
                ShadowGap = $result.shadowHasGap
                RunPass = $result.runPass
                Evidence = $evidenceDestination
            } | ConvertTo-Json -Compress
        }
    }
}
finally {
    Close-GameNormally
    if ($null -ne $gameProcess) {
        $gameProcess.Refresh()
        if (-not $gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T06 profile recovery.'
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

    if (-not (Test-Path -LiteralPath (Join-Path $modsDirectory 'HollowKnightTAS'))) {
        throw 'T06 profile recovery failed: HollowKnightTAS was not restored.'
    }
    if (Test-Path -LiteralPath $emptyDirectory) {
        if (@(Get-ChildItem -LiteralPath $emptyDirectory -Force).Count -ne 0) {
            throw 'T06 empty validation directory contains unexpected files.'
        }
        Remove-Item -LiteralPath $emptyDirectory
    }
}

if ($Smoke) {
    $smokePass = $runResults.Count -gt 0 `
        -and @($runResults | Where-Object { -not $_.runPass }).Count -eq 0
    [pscustomobject]@{
        Smoke = $true
        GatePass = $smokePass
        ResultCount = $runResults.Count
        EvidenceRoot = $EvidenceRoot
    } | ConvertTo-Json
    exit $(if ($smokePass) { 0 } else { 4 })
}

& (Join-Path $PSScriptRoot 'Summarize-T06Playback.ps1') -EvidenceRoot $EvidenceRoot
exit $LASTEXITCODE

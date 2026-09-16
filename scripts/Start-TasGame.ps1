#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CompanionDirectory,
    [Parameter(Mandatory)][string]$GamePath,
    [ValidateRange(0, 4)][int]$Slot = 0,
    [switch]$Restart,
    [ValidateRange(10, 180)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-NoRunningGame {
    if (@(Get-Process -Name hollow_knight -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'A game is already running. Save and exit it normally; no restart or termination was attempted.'
    }
}

if (!$Restart) { Assert-NoRunningGame }
if ($Restart -and $Slot -eq 0) { throw 'Restart requires an explicit existing slot (1-4).' }
if ($Restart) {
    $slotFile = Join-Path ([Environment]::GetFolderPath('UserProfile')) "AppData/LocalLow/Team Cherry/Hollow Knight/user$Slot.dat"
    if (!(Test-Path -LiteralPath $slotFile -PathType Leaf)) { throw 'The requested existing slot is missing; the current game was not touched.' }
}
$companionRoot = (Resolve-Path -LiteralPath $CompanionDirectory).Path
$gameExecutable = (Resolve-Path -LiteralPath $GamePath).Path
if ([IO.Path]::GetFileName($gameExecutable) -ine 'hollow_knight.exe') {
    throw 'GamePath must identify hollow_knight.exe.'
}
$cli = Join-Path $companionRoot 'Tools/HollowKnightTAS.Cli.exe'
if ($Slot -ne 0 -and !(Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw 'Packaged CLI is required to load a slot.'
}
Add-Type -Path (Join-Path $companionRoot 'HollowKnightTAS.Core.dll')
Add-Type -Path (Join-Path $companionRoot 'HollowKnightTAS.Companion.dll')
$coldRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HollowKnightTAS/cold-restore'
$identity = [HollowKnightTAS.Companion.Services.ColdRestoreSupervisorIdentity]::LoadOrCreate(
    (Join-Path $coldRoot 'identity'))
$receipts = [HollowKnightTAS.Companion.Services.VerifiedLaunchReceiptStore]::new(
    (Join-Path $coldRoot 'launch-receipts'), $identity.CompanionInstanceId, $identity.ClaimSecret)
$profile = [HollowKnightTAS.Companion.Services.VerifiedStartupProfile]::Load(
    (Join-Path $companionRoot 'ClockStartup'), $gameExecutable)
$launcher = [HollowKnightTAS.Companion.Services.VerifiedGameLauncher]::new($profile, $receipts)
$runId = 'interactive-' + [Guid]::NewGuid().ToString('N')
if ($Restart) {
    # A fresh process supplies a new recording root. Never recycle an old
    # ClockPayload acknowledgement or change the running hero to fake a root.
    $sourceGames = @(Get-Process -Name hollow_knight -ErrorAction SilentlyContinue)
    if ($sourceGames.Count -ne 1) { throw 'Restart requires exactly one existing game; nothing was launched.' }
    $sourceGame = $sourceGames[0]
    if ($sourceGame.Path -ine $gameExecutable) { throw 'The running game is not the requested executable.' }
    Add-Type -Path (Join-Path $companionRoot 'Tools/SDK/HollowKnightTAS.Automation.Client.dll')
    $restartClient = [HollowKnightTAS.Automation.Client.AutomationClient]::new()
    $restartTimeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    try {
        $connect = [HollowKnightTAS.Automation.Client.AutomationConnectOptions]::new()
        $connect.ClientId = 'restart-' + [Guid]::NewGuid().ToString('N')
        $null = $restartClient.ConnectAsync($connect, $restartTimeout.Token).GetAwaiter().GetResult()
        function Invoke-RestartCommand($name, $scope, [hashtable]$fields, $lease = '', $mode = '', $tick = $null) {
            $arguments = [Collections.Generic.Dictionary[string,string]]::new()
            foreach ($key in $fields.Keys) { $arguments.Add($key, [string]$fields[$key]) }
            $requestId = 'request-' + [Guid]::NewGuid().ToString('N')
            $idempotencyKey = 'idempotency-' + [Guid]::NewGuid().ToString('N')
            $command = $restartClient.CreateCommand($name, $scope, $arguments, $lease, $mode, $tick, $requestId, $idempotencyKey)
            $result = $restartClient.ExecuteAsync($command, $restartTimeout.Token).GetAwaiter().GetResult()
            if (!$result.Success) { throw "$name rejected: $($result.ResultCode): $($result.Detail)" }
            return $result.Data
        }
        # This connection remains bound to one session even if Studio rebinds.
        $attestation = Invoke-RestartCommand 'getStartupProfile' 'observe.status' @{}
        if ($attestation['status'] -ne 'Verified' -or
            [int]$attestation['processId'] -ne $sourceGame.Id -or
            [long]$attestation['processStartTimeUtcTicks'] -ne $sourceGame.StartTime.ToUniversalTime().Ticks) {
            throw 'Runtime attestation does not identify the exact source process.'
        }
        $lease = (Invoke-RestartCommand 'acquireControl' 'control.playback' @{scopes='control.playback';ttlSeconds='30'})['leaseId']
        $state = Invoke-RestartCommand 'getState' 'observe.state.summary' @{statusOnly='true'}
        if ($state['playbackMode'] -ne 'Idle') { throw 'Finish active playback before starting another recording session.' }
        if ($state['controlMode'] -eq 'Running') {
            $null = Invoke-RestartCommand 'pause' 'control.playback' @{} $lease 'Running'
            do {
                $restartTimeout.Token.ThrowIfCancellationRequested()
                $state = Invoke-RestartCommand 'getState' 'observe.state.summary' @{statusOnly='true'}
                if ($state['controlMode'] -eq 'Pausing') { Start-Sleep -Milliseconds 50 }
            } while ($state['controlMode'] -eq 'Pausing')
        }
        if ($state['controlMode'] -ne 'Paused') { throw 'The source did not reach a paused boundary.' }
        $sourceTick = [long]$state['movieTick']
        $null = Invoke-RestartCommand 'quitGame' 'control.playback' @{} $lease 'Paused' $(if ($sourceTick -ge 0) { $sourceTick } else { $null })
        # One accepted exit, one exact-process wait, then one launch. A timeout
        # is not permission to kill, retry exit, or launch beside the source.
        $null = $sourceGame.WaitForExitAsync($restartTimeout.Token).GetAwaiter().GetResult()
    }
    finally {
        $null = $restartClient.DisposeAsync().AsTask().GetAwaiter().GetResult()
        $restartTimeout.Dispose()
        $sourceGame.Dispose()
    }
}
# Recheck after package verification. Never terminate an existing game.
Assert-NoRunningGame
$handle = $launcher.LaunchInteractiveAsync($runId, [TimeSpan]::FromSeconds(60),
    [Threading.CancellationToken]::None).GetAwaiter().GetResult()
$handle.ReleaseSupervision()
try {
    $gameProcessId = $handle.ProcessId
    if ($Slot -eq 0) {
        [pscustomobject]@{ status = 'Launched'; processId = $gameProcessId; runId = $runId;
            detail = 'Wait for Runtime authentication before TAS commands.' } | ConvertTo-Json -Compress
        return
    }

    function Read-LaunchState {
        $response = & $cli automation call getState observe.state.summary statusOnly=true | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $response.success -ne 'true') { return $null }
        return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($response.dataBase64)) | ConvertFrom-Json
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $loadAccepted = $false
    $descriptor = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HollowKnightTAS/automation/automation-v1.json'
    do {
        if ($handle.HasExited) { throw "Launched process $gameProcessId exited. No replacement was started." }
        if (!(Test-Path -LiteralPath $descriptor -PathType Leaf)) {
            Start-Sleep -Milliseconds 250
            continue
        }
        $attestation = & $cli automation startup | ConvertFrom-Json
        if ($LASTEXITCODE -eq 0 -and $attestation.PSObject.Properties['runId'] -and
            $attestation.runId -eq $runId -and $attestation.status -eq 'Verified') {
            $state = Read-LaunchState
            if ($null -ne $state -and !$loadAccepted -and $state.controlMode -eq 'Running' -and
                [long]$state.movieTick -eq -1 -and $state.PSObject.Properties['isStableTitleMenu'] -and
                $state.isStableTitleMenu -eq 'true') {
                # Submit exactly once. A timeout after submission is ambiguous,
                # not permission to issue another native load.
                $loadAccepted = $true
                $response = & $cli automation call loadGameSlot control.playback "slot=$Slot" '--expected-mode=Running' | ConvertFrom-Json
                if ($LASTEXITCODE -ne 0 -or $response.success -ne 'true') {
                    throw "Slot load did not confirm success: $($response.detail). Inspect this process; do not resubmit blindly."
                }
            }
            if ($loadAccepted -and $null -ne $state) {
                if ($state.recordingOriginStatus -eq 'Faulted') { throw $state.recordingOriginDetail }
                if ($state.recordingOriginStatus -eq 'Ready') {
                    [pscustomobject]@{ status = 'RecordingOriginReady'; processId = $gameProcessId;
                        runId = $runId; slot = $Slot; movieTick = $state.movieTick } | ConvertTo-Json -Compress
                    return
                }
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Observation timed out for PID $gameProcessId, run $runId, loadSubmitted=$loadAccepted. The game remains running; inspect it, do not relaunch."
}
finally {
    # Ownership was released immediately after successful launch. Observation
    # errors must not kill a game containing user work.
    $handle.Dispose()
}

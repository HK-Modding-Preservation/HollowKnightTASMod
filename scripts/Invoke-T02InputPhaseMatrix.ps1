[CmdletBinding()]
param(
    [ValidateSet('A', 'B', 'C', 'All')]
    [string]$Candidate = 'All',

    [ValidateRange(1, 4)]
    [int]$TestSaveSlot = 2,

    [string]$ManagedDirectory = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',

    [string]$SteamExecutable = 'C:\Program Files (x86)\Steam\steam.exe',

    [string]$PersistentDataDirectory = 'C:\Users\33361\AppData\LocalLow\Team Cherry\Hollow Knight',

    [string]$EvidenceRoot = '',

    [ValidateRange(30, 300)]
    [int]$MaxRunSeconds = 150,

    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\input-phase'
}

if (-not ('HkTasInputHarness.NativeInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace HkTasInputHarness
{
    public static class NativeInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT keyboard;

            [FieldOffset(0)]
            public MOUSEINPUT mouse;

            [FieldOffset(0)]
            public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort virtualKey;
            public ushort scanCode;
            public uint flags;
            public uint time;
            public UIntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int x;
            public int y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint message;
            public ushort parameterLow;
            public ushort parameterHigh;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        private const uint InputKeyboard = 1;
        private const uint KeyUpFlag = 0x0002;

        public static void KeyDown(ushort virtualKey)
        {
            Send(virtualKey, 0);
        }

        public static void KeyUp(ushort virtualKey)
        {
            Send(virtualKey, KeyUpFlag);
        }

        public static void Press(ushort virtualKey)
        {
            KeyDown(virtualKey);
            KeyUp(virtualKey);
        }

        private static void Send(ushort virtualKey, uint flags)
        {
            var input = new INPUT
            {
                type = InputKeyboard,
                data = new InputUnion
                {
                    keyboard = new KEYBDINPUT
                    {
                        virtualKey = virtualKey,
                        flags = flags
                    }
                }
            };
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
            {
                throw new InvalidOperationException("SendInput failed.");
            }
        }
    }
}
'@
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
$backupDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T02-$swapId.backup"
$emptyDirectory = Join-Path $ManagedDirectory "Mods.HKTAS-T02-$swapId.empty"
$gameProcess = $null
$noiseKeysDown = $false
$noiseVirtualKeys = [System.Collections.Generic.List[ushort]]::new()
$results = [System.Collections.Generic.List[object]]::new()

function Convert-KeyNameToVirtualKey {
    param(
        [Parameter(Mandatory)]
        [string]$KeyName
    )

    if ($KeyName.Length -eq 1) {
        $character = [char]::ToUpperInvariant($KeyName[0])
        if (($character -ge 'A' -and $character -le 'Z') `
            -or ($character -ge '0' -and $character -le '9')) {
            return [ushort][int]$character
        }
    }

    $namedKeys = @{
        Left = 0x25
        Up = 0x26
        Right = 0x27
        Down = 0x28
        Space = 0x20
        Return = 0x0D
        Enter = 0x0D
        Escape = 0x1B
        Tab = 0x09
    }
    if ($namedKeys.ContainsKey($KeyName)) {
        return [ushort]$namedKeys[$KeyName]
    }
    if ($KeyName -match '^F([1-9]|1[0-2])$') {
        return [ushort](0x6F + [int]$Matches[1])
    }

    throw "Unsupported keyboard binding name for physical-noise case: $KeyName"
}

function Get-NoiseVirtualKeys {
    param(
        [Parameter(Mandatory)]
        [string]$BindingSnapshotPath
    )

    $snapshot = Get-Content -LiteralPath $BindingSnapshotPath -Raw | ConvertFrom-Json
    $keys = [System.Collections.Generic.List[ushort]]::new()
    foreach ($actionName in @('Left', 'Attack')) {
        $action = @($snapshot.actions | Where-Object action -eq $actionName) |
            Select-Object -First 1
        if ($null -eq $action) {
            throw "Binding snapshot does not contain action $actionName"
        }
        $binding = @(
            $action.bindings |
                Where-Object sourceType -eq 'KeyBindingSource'
        ) | Select-Object -First 1
        if ($null -eq $binding) {
            throw "Action $actionName has no keyboard binding for physical-noise injection."
        }
        $keys.Add((Convert-KeyNameToVirtualKey -KeyName ([string]$binding.name)))
    }
    return $keys.ToArray()
}

function Release-NoiseKeys {
    if ($script:noiseKeysDown) {
        foreach ($virtualKey in $script:noiseVirtualKeys) {
            [HkTasInputHarness.NativeInput]::KeyUp($virtualKey)
        }
        $script:noiseKeysDown = $false
    }
    $script:noiseVirtualKeys.Clear()
}

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
        throw 'Failed to establish the isolated T02 Mods profile.'
    }

    [string[]]$candidates = if ($Smoke) {
        if ($Candidate -eq 'All') { , 'A' } else { , $Candidate }
    }
    elseif ($Candidate -eq 'All') {
        @('A', 'B', 'C')
    }
    else {
        @($Candidate)
    }

    [string[]]$casePlan = if ($Smoke) {
        , 'normal'
    }
    else {
        @(
            'normal',
            'normal',
            'normal',
            'physical-noise',
            'physical-noise',
            'emergency-stop',
            'scene-change',
            'adapter-exception',
            'normal',
            'normal'
        )
    }

    foreach ($candidateId in $candidates) {
        for ($runIndex = 0; $runIndex -lt $casePlan.Count; $runIndex++) {
            $caseName = $casePlan[$runIndex]
            $runId = '{0}-{1}-{2:D2}-{3}' -f `
                $candidateId.ToLowerInvariant(), `
                $caseName, `
                ($runIndex + 1), `
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
                "--hktas-input-probe=$candidateId",
                "--hktas-input-probe-run=$runId",
                "--hktas-input-probe-case=$caseName",
                "--hktas-input-probe-slot=$TestSaveSlot",
                '--hktas-input-probe-exit'
            )
            Start-Process -FilePath $SteamExecutable -ArgumentList $arguments

            $gameProcess = $null
            $resultPath = $null
            $readyPath = $null
            $probeDirectory = $null
            $inputInjected = $false
            $noiseStarted = $null
            $deadline = (Get-Date).AddSeconds($MaxRunSeconds)

            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 100
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
                            Join-Path $_.FullName "input-phase\$runId"
                        )
                    } |
                    Select-Object -First 1
                if ($null -ne $newSession) {
                    $probeDirectory = Join-Path $newSession.FullName "input-phase\$runId"
                    $readyPath = Join-Path $probeDirectory 'probe.ready'
                    $candidateResult = Join-Path $probeDirectory 'result.json'
                    if (Test-Path -LiteralPath $candidateResult) {
                        $resultPath = $candidateResult
                        break
                    }
                }

                if (-not $inputInjected `
                    -and $null -ne $readyPath `
                    -and (Test-Path -LiteralPath $readyPath) `
                    -and $null -ne $gameProcess) {
                    [void][HkTasInputHarness.NativeInput]::SetForegroundWindow(
                        $gameProcess.MainWindowHandle
                    )
                    if ($caseName -eq 'physical-noise') {
                        $bindingSnapshotPath = Join-Path $probeDirectory 'binding-before.json'
                        $noiseVirtualKeys.Clear()
                        foreach ($virtualKey in Get-NoiseVirtualKeys -BindingSnapshotPath $bindingSnapshotPath) {
                            $noiseVirtualKeys.Add($virtualKey)
                            [HkTasInputHarness.NativeInput]::KeyDown($virtualKey)
                        }
                        $noiseKeysDown = $true
                        $noiseStarted = Get-Date
                    }
                    elseif ($caseName -eq 'emergency-stop') {
                        [HkTasInputHarness.NativeInput]::Press(0x77)
                    }
                    $inputInjected = $true
                }

                if ($noiseKeysDown `
                    -and $null -ne $noiseStarted `
                    -and ((Get-Date) - $noiseStarted).TotalSeconds -ge 1.5) {
                    Release-NoiseKeys
                }
            }

            Release-NoiseKeys
            if ($null -eq $resultPath) {
                Close-GameNormally
                throw "T02 run timed out: $runId"
            }

            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            $sourceDirectory = Split-Path -Parent $resultPath
            $sessionDirectory = Split-Path -Parent (Split-Path -Parent $sourceDirectory)
            $evidenceName = (Split-Path -Leaf $sessionDirectory) + '-' + $runId
            $evidenceDestination = Join-Path $EvidenceRoot $evidenceName
            if (Test-Path -LiteralPath $evidenceDestination) {
                throw "Evidence destination already exists: $evidenceDestination"
            }
            Copy-Item -LiteralPath $sourceDirectory -Destination $evidenceDestination -Recurse
            $results.Add($result)

            if ($null -ne $gameProcess) {
                [void]$gameProcess.WaitForExit(20000)
                $gameProcess.Refresh()
                if (-not $gameProcess.HasExited) {
                    Close-GameNormally
                    throw "Game did not exit normally after T02 run: $runId"
                }
            }
            $gameProcess = $null

            [pscustomobject]@{
                Candidate = $result.candidate
                Case = $result.case
                RunId = $result.runId
                StopReason = $result.stopReason
                RunPass = $result.runPass
                Evidence = $evidenceDestination
            } | ConvertTo-Json -Compress
        }
    }
}
finally {
    Release-NoiseKeys
    Close-GameNormally
    if ($null -ne $gameProcess) {
        $gameProcess.Refresh()
        if (-not $gameProcess.HasExited) {
            throw 'Hollow Knight is still running; refusing T02 profile recovery.'
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
        throw 'T02 profile recovery failed: HollowKnightTAS was not restored.'
    }
    if (Test-Path -LiteralPath $emptyDirectory) {
        if (@(Get-ChildItem -LiteralPath $emptyDirectory -Force).Count -ne 0) {
            throw 'T02 empty validation directory contains unexpected files.'
        }
        Remove-Item -LiteralPath $emptyDirectory
    }
}

if (-not $Smoke) {
    & (Join-Path $PSScriptRoot 'Summarize-T02InputPhase.ps1') -EvidenceRoot $EvidenceRoot
    exit $LASTEXITCODE
}

[pscustomobject]@{
    Smoke = $true
    RunCount = $results.Count
    AllRunsPass = @($results | Where-Object { -not $_.runPass }).Count -eq 0
    EvidenceRoot = $EvidenceRoot
} | ConvertTo-Json -Depth 4

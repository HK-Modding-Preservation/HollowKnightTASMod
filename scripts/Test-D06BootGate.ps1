[CmdletBinding()]
param([string]$BundleRoot = 'artifacts/d06-startup/probe-bundle', [switch]$FrameGate)
$ErrorActionPreference = 'Stop'
if (Get-Process hollow_knight -ErrorAction SilentlyContinue) {
    throw 'An existing game is running; probe will not replace it.'
}
$token = [Guid]::NewGuid().ToString('N')
$ready = [Threading.EventWaitHandle]::new($false, 'ManualReset', "Local\HKTAS.Boot.$token.Ready")
$proceed = [Threading.EventWaitHandle]::new($false, 'ManualReset', "Local\HKTAS.Boot.$token.Continue")
$injector = $null
$step = $null
$mapping = $null
$view = $null
try {
    if ($FrameGate) {
        $step = [Threading.EventWaitHandle]::new($false, 'AutoReset', "Local\HKTAS.Boot.$token.Step")
        $mapping = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew("Local\HKTAS.Boot.$token.State", 16)
        $view = $mapping.CreateViewAccessor()
    }
    $start = [Diagnostics.ProcessStartInfo]::new(
        [IO.Path]::GetFullPath((Join-Path $BundleRoot 'HollowKnightTAS.ClockInjector.exe')))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['HKTAS_BOOT_GATE_TOKEN'] = $token
    $start.Environment['HKTAS_BOOT_GATE_OWNER'] = $PID.ToString()
    $start.Environment.Remove('HKTAS_BOOT_FRAME_GATE') | Out-Null
    if ($FrameGate) { $start.Environment['HKTAS_BOOT_FRAME_GATE'] = '1' }
    $runId = 'd06-gate-' + $token
    $argsJson = ConvertTo-Json -Compress -InputObject @("--hktas-reference-run=$runId")
    $start.ArgumentList.Add('--launch-game=D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe')
    $start.ArgumentList.Add('--launch-arguments-base64=' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($argsJson)))
    $injector = [Diagnostics.Process]::Start($start)
    $stdout = $injector.StandardOutput.ReadToEndAsync()
    $stderr = $injector.StandardError.ReadToEndAsync()
    $ack = $ready.WaitOne(15000)
    Write-Output "nativeGateAcknowledged=$ack"
    if (-not $ack) { throw 'Native startup gate did not acknowledge within 15 seconds.' }
    foreach ($sample in 1..3) {
        Get-Process hollow_knight | Select-Object Id, MainWindowHandle, MainWindowTitle, CPU
        Start-Sleep -Milliseconds 500
    }
    if ($FrameGate) {
        if ($view.ReadInt32(0) -ne 0 -or $view.ReadInt32(4) -ne 1 -or $view.ReadInt32(12) -ne 1) {
            throw 'First PlayerLoop was not held at zero completed frames.'
        }
        Write-Output "firstFrameHeld=true;threadId=$($view.ReadInt32(8))"
        foreach ($expected in 1..3) {
            $ready.Reset() | Out-Null
            $step.Set() | Out-Null
            if (-not $ready.WaitOne(15000)) { throw "Step $expected did not reach the next frame boundary." }
            $completed = $view.ReadInt32(0)
            Start-Sleep -Milliseconds 200
            if ($completed -ne $expected -or $view.ReadInt32(0) -ne $expected -or $view.ReadInt32(4) -ne 1) {
                throw "Step mismatch: expected $expected; got $completed."
            }
            Write-Output "stepCompleted=$completed;held=true"
        }
    }
    $proceed.Set() | Out-Null
    Write-Output 'nativeGateReleased=true'
    if (-not $injector.WaitForExit(15000)) { throw 'Injector did not finish after gate release.' }
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    if ($injector.ExitCode -ne 0) { throw "Injector exit code $($injector.ExitCode)" }
} finally {
    $proceed.Set() | Out-Null
    $ready.Dispose()
    $proceed.Dispose()
    if ($view) { $view.Dispose() }
    if ($mapping) { $mapping.Dispose() }
    if ($step) { $step.Dispose() }
    if ($injector) { $injector.Dispose() }
}

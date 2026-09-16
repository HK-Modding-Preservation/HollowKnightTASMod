[CmdletBinding()]
param([string]$BundleRoot = 'artifacts/d06-startup/probe-bundle')
$ErrorActionPreference = 'Stop'
if (Get-Process hollow_knight -ErrorAction SilentlyContinue) {
    throw 'An existing game is running; probe will not replace it.'
}
$token = [Guid]::NewGuid().ToString('N')
$ready = [Threading.EventWaitHandle]::new($false, 'ManualReset', "Local\HKTAS.Boot.$token.Ready")
$proceed = [Threading.EventWaitHandle]::new($false, 'ManualReset', "Local\HKTAS.Boot.$token.Continue")
$injector = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new(
        [IO.Path]::GetFullPath((Join-Path $BundleRoot 'HollowKnightTAS.ClockInjector.exe')))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['HKTAS_BOOT_GATE_TOKEN'] = $token
    $start.Environment['HKTAS_BOOT_GATE_OWNER'] = $PID.ToString()
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
    if ($injector) { $injector.Dispose() }
}

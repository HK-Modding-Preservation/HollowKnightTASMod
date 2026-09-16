#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModArchive,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'Output already exists; nothing overwritten.' }
$files = [ordered]@{
    'HollowKnightTAS.zip' = (Resolve-Path -LiteralPath $ModArchive).Path
    'demo/false-knight-ea42-3042.hktas' = (Join-Path $repo 'fixtures/t16/false-knight-ea42-3042.hktas')
    'demo/false-knight-ea42-3042.hktas-save' = (Join-Path $repo 'artifacts/d03-live-current/false-knight-ea42-3042.hktas-save')
    'START-HERE.md' = (Join-Path $repo 'packaging/DELIVERY.md')
    'docs/USER-GUIDE.md' = (Join-Path $repo 'docs/USER-GUIDE.md')
    'docs/DELIVERY-STATUS.md' = (Join-Path $repo 'docs/DELIVERY-STATUS.md')
    'docs/ai/MCP-v1.md' = (Join-Path $repo 'docs/ai/MCP-v1.md')
    'docs/ai/AI-TAS-Workflow-v1.md' = (Join-Path $repo 'docs/ai/AI-TAS-Workflow-v1.md')
    'packaging/README.md' = (Join-Path $repo 'packaging/README.md')
    'scripts/Transfer-ReplaySave.ps1' = (Join-Path $repo 'scripts/Transfer-ReplaySave.ps1')
    'scripts/Start-TasGame.ps1' = (Join-Path $repo 'scripts/Start-TasGame.ps1')
    'scripts/Invoke-AuthoringBatch.ps1' = (Join-Path $repo 'scripts/Invoke-AuthoringBatch.ps1')
}
$expected = @{
    'HollowKnightTAS.zip' = '6b0c8d4cb14242bdd088fce906b3e2ef0778cfd88e7a437c8cc74a97cf767ebd'
    'demo/false-knight-ea42-3042.hktas' = 'b689af0d86b221c41c4612c4044bf3da497f2c725678fb26fd172d9a6debef9e'
    'demo/false-knight-ea42-3042.hktas-save' = 'a33d2951b19e206288c7967deb34e5693f36c762c1799c52dd0b3b6ec7fd9a14'
}
$hashes = [ordered]@{}
foreach ($name in $files.Keys) {
    $hashes[$name] = (Get-FileHash -LiteralPath $files[$name] -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected.ContainsKey($name) -and $hashes[$name] -cne $expected[$name]) { throw "Frozen input mismatch: $name" }
}
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in $files.Keys) {
            $level = if ($name.EndsWith('.zip')) { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$name], $name, $level)
        }
        $entry = $zip.CreateEntry('SHA256SUMS.txt')
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { foreach ($name in $hashes.Keys) { $writer.WriteLine("$($hashes[$name])  $name") } }
        finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $stream.Flush($true)
} finally { $stream.Dispose() }
# Verify archived bytes, not just the source files, without extracting a second copy.
$zip = [IO.Compression.ZipFile]::OpenRead($output)
try {
    if ($zip.Entries.Count -ne $files.Count + 1) { throw 'Unexpected delivery entry count.' }
    foreach ($name in $hashes.Keys) {
        $entry = $zip.GetEntry($name)
        if (!$entry) { throw "Missing delivery entry: $name" }
        $inputStream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant() }
        finally { $inputStream.Dispose() }
        if ($actual -cne $hashes[$name]) { throw "Delivery entry hash mismatch: $name" }
    }
} finally { $zip.Dispose() }
[pscustomobject]@{ status='Verified'; archive=$output; bytes=(Get-Item -LiteralPath $output).Length; sha256=(Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant(); files=$files.Count } | ConvertTo-Json -Compress

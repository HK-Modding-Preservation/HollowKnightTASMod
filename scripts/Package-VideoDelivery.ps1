#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModArchive,
    [Parameter(Mandatory)][string]$MoviePath,
    [Parameter(Mandatory)][string]$SaveArchivePath,
    [Parameter(Mandatory)][string]$VideoPath,
    [Parameter(Mandatory)][string]$GuidePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$ExpectedModSha256,
    [Parameter(Mandatory)][string]$ExpectedMovieSha256,
    [Parameter(Mandatory)][string]$ExpectedSaveSha256,
    [Parameter(Mandatory)][string]$ExpectedVideoSha256
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-InputFile {
    param([string]$Path, [string]$Name)
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if (-not [IO.File]::Exists($resolved)) { throw "Input is not a file: $Name" }
    return $resolved
}

function Normalize-ExpectedHash {
    param([string]$Hash, [string]$Name)
    $normalized = $Hash.Trim().ToLowerInvariant()
    if ($normalized -notmatch '^[0-9a-f]{64}$') { throw "Expected SHA-256 is invalid: $Name" }
    return $normalized
}

$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.File]::Exists($output) -or [IO.Directory]::Exists($output)) {
    throw 'Output already exists; nothing overwritten.'
}

$files = [ordered]@{
    'HollowKnightTAS.zip' = Resolve-InputFile $ModArchive 'HollowKnightTAS.zip'
    'demo/false-knight.hktas' = Resolve-InputFile $MoviePath 'demo/false-knight.hktas'
    'demo/false-knight.hktas-save' = Resolve-InputFile $SaveArchivePath 'demo/false-knight.hktas-save'
    'video/false-knight.mp4' = Resolve-InputFile $VideoPath 'video/false-knight.mp4'
    'START-HERE.md' = Resolve-InputFile $GuidePath 'START-HERE.md'
    'scripts/Transfer-ReplaySave.ps1' = Resolve-InputFile (Join-Path $repo 'scripts/Transfer-ReplaySave.ps1') 'scripts/Transfer-ReplaySave.ps1'
    'scripts/Start-TasGame.ps1' = Resolve-InputFile (Join-Path $repo 'scripts/Start-TasGame.ps1') 'scripts/Start-TasGame.ps1'
}
$expected = [ordered]@{
    'HollowKnightTAS.zip' = Normalize-ExpectedHash $ExpectedModSha256 'HollowKnightTAS.zip'
    'demo/false-knight.hktas' = Normalize-ExpectedHash $ExpectedMovieSha256 'demo/false-knight.hktas'
    'demo/false-knight.hktas-save' = Normalize-ExpectedHash $ExpectedSaveSha256 'demo/false-knight.hktas-save'
    'video/false-knight.mp4' = Normalize-ExpectedHash $ExpectedVideoSha256 'video/false-knight.mp4'
}

$hashes = [ordered]@{}
foreach ($name in $files.Keys) {
    $hashes[$name] = (Get-FileHash -LiteralPath $files[$name] -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected.Contains($name) -and $hashes[$name] -cne $expected[$name]) {
        throw "Expected input hash mismatch: $name"
    }
}

$parent = [IO.Path]::GetDirectoryName($output)
if ($parent) { $null = [IO.Directory]::CreateDirectory($parent) }
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in $files.Keys) {
            $extension = [IO.Path]::GetExtension($name).ToLowerInvariant()
            $level = if ($extension -in @('.zip', '.mp4', '.hktas-save')) {
                [IO.Compression.CompressionLevel]::NoCompression
            } else {
                [IO.Compression.CompressionLevel]::Optimal
            }
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$name], $name, $level)
        }
        $entry = $zip.CreateEntry('SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try {
            foreach ($name in $hashes.Keys) { $writer.WriteLine("$($hashes[$name])  $name") }
        } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $stream.Flush($true)
} finally { $stream.Dispose() }

# Verify archived bytes, not just source files, without extracting a second copy.
$zip = [IO.Compression.ZipFile]::OpenRead($output)
try {
    if ($zip.Entries.Count -ne $files.Count + 1) { throw 'Unexpected delivery entry count.' }
    foreach ($name in $hashes.Keys) {
        $entry = $zip.GetEntry($name)
        if ($null -eq $entry) { throw "Missing delivery entry: $name" }
        $inputStream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant() }
        finally { $inputStream.Dispose() }
        if ($actual -cne $hashes[$name]) { throw "Delivery entry hash mismatch: $name" }
    }
    $sumsEntry = $zip.GetEntry('SHA256SUMS.txt')
    if ($null -eq $sumsEntry) { throw 'Missing delivery entry: SHA256SUMS.txt' }
    $reader = [IO.StreamReader]::new($sumsEntry.Open(), [Text.Encoding]::UTF8, $false)
    try {
        $sumLines = @()
        while ($null -ne ($line = $reader.ReadLine())) { $sumLines += $line }
    } finally { $reader.Dispose() }
    $expectedLines = @($hashes.Keys | ForEach-Object { "$($hashes[$_])  $_" })
    if (($sumLines -join "`n") -cne ($expectedLines -join "`n")) { throw 'SHA256SUMS.txt does not match archived entries.' }
} finally { $zip.Dispose() }

[pscustomobject]@{
    status = 'Verified'
    archive = $output
    bytes = (Get-Item -LiteralPath $output).Length
    sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    files = $files.Count + 1
} | ConvertTo-Json -Compress

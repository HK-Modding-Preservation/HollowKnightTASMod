#requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$ModDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$ExpectedRuntimeSha256,
    [Parameter(Mandatory)][string]$ExpectedCoreSha256,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [string]$BundleToolPath = (Join-Path $PSScriptRoot '../src/HollowKnightTAS.BundleTool/bin/Release/net8.0/HollowKnightTAS.BundleTool.dll'),
    [string]$PublicKeyPath = (Join-Path $PSScriptRoot '../.local/signing/companion-public.json'),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Package an already verified installation. Never build or install game assemblies.
$root = (Resolve-Path -LiteralPath $ModDirectory).Path
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Mod directory must not be a reparse point.'
}
$output = [IO.Path]::GetFullPath($OutputPath)
if (!$VerifyOnly -and (Test-Path -LiteralPath $output)) { throw "Refusing to overwrite: $output" }
$files = [ordered]@{}
$hashes = @{}
function Add-PackageFile([string]$name, [string]$path, [string]$expected = '') {
    if ($files.Contains($name)) { throw "Duplicate package entry: $name" }
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Reparse point rejected: $name"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -and $actual -ne $expected.ToLowerInvariant()) { throw "Hash mismatch: $name" }
    $files.Add($name, $path)
    $hashes.Add($name, $actual)
}
foreach ($expected in @($ExpectedRuntimeSha256, $ExpectedCoreSha256, $ExpectedManifestSha256)) {
    if ($expected -cnotmatch '^[0-9a-f]{64}$') { throw 'Expected hashes must be lowercase SHA-256.' }
}
Add-PackageFile 'HollowKnightTAS.dll' (Join-Path $root 'HollowKnightTAS.dll') $ExpectedRuntimeSha256
Add-PackageFile 'HollowKnightTAS.Core.dll' (Join-Path $root 'HollowKnightTAS.Core.dll') $ExpectedCoreSha256
Add-PackageFile 'companion.manifest.json' (Join-Path $root 'companion.manifest.json') $ExpectedManifestSha256
$manifest = Get-Content -LiteralPath $files['companion.manifest.json'] -Raw | ConvertFrom-Json
# Reuse the installer's signature, schema, path and reparse-point verifier.
# This invokes a prebuilt tool only; it does not build any project.
if (!(Test-Path -LiteralPath $BundleToolPath -PathType Leaf)) { throw 'Build BundleTool separately before packaging.' }
& dotnet $BundleToolPath verify $PublicKeyPath $root $files['companion.manifest.json']
if ($LASTEXITCODE -ne 0) { throw 'Companion bundle verification failed.' }
foreach ($file in $manifest.files) {
    $name = [string]$file.path
    if (!$name.StartsWith('Companion/', [StringComparison]::Ordinal) -or
        $name.Contains('\') -or $name.Contains(':') -or $name -match '(^|/)\.{1,2}(/|$)') {
        throw "Unsafe manifest entry: $name"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $root $name))
    if (!$path.StartsWith($root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw "Entry escapes Mod directory: $name" }
    Add-PackageFile $name $path ([string]$file.sha256)
}
Add-PackageFile 'docs/USER-MANUAL.md' (Join-Path $PSScriptRoot '../docs/USER-MANUAL.md')
Add-PackageFile 'docs/AI-CONTROL.md' (Join-Path $PSScriptRoot '../docs/AI-CONTROL.md')
Add-PackageFile 'docs/BUILD.md' (Join-Path $PSScriptRoot '../docs/BUILD.md')
if (!$VerifyOnly) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
    $stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $files.Keys) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$name], $name,
                    [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

# Verify the bytes in the archive, not just the input directory.
$archive = [IO.Compression.ZipFile]::OpenRead($output)
try {
    if ($archive.Entries.Count -ne $files.Count) { throw 'Archive entry count mismatch.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if (!$seen.Add($entry.FullName) -or !$hashes.ContainsKey($entry.FullName)) {
            throw "Duplicate or undeclared archive entry: $($entry.FullName)"
        }
        $inputStream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant() }
        finally { $inputStream.Dispose() }
        if ($actual -ne $hashes[$entry.FullName]) { throw "Archive hash mismatch: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
[pscustomobject]@{ status='Verified'; path=$output; entries=$files.Count;
    bytes=(Get-Item -LiteralPath $output).Length;
    sha256=(Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant() } | ConvertTo-Json

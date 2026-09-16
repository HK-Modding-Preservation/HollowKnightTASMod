#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Export', 'Verify', 'Import')][string]$Mode,
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$CoreAssemblyPath,
    [string]$StoreRoot,
    [string]$ReplaySaveId,
    [string]$ExpectedArchiveSha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Resolve-Path -LiteralPath $CoreAssemblyPath).Path
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Path]::GetFullPath($ArchivePath)
$maxArchive = 256MB
$maxTotal = 512MB
$maxObject = 96MB
function Read-BoundedEntry($entry, [long]$limit) {
    if ($entry.Length -lt 0 -or $entry.Length -gt $limit) { throw "Archive entry exceeds bounds: $($entry.FullName)" }
    $inputStream = $entry.Open()
    try {
        $memory = [IO.MemoryStream]::new()
        try {
            $buffer = [byte[]]::new(65536)
            while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                if ($memory.Length + $count -gt $limit) { throw 'Expanded entry exceeds bounds.' }
                $memory.Write($buffer, 0, $count)
            }
            if ($memory.Length -ne $entry.Length) { throw 'Archive entry length mismatch.' }
            return ,$memory.ToArray()
        } finally { $memory.Dispose() }
    } finally { $inputStream.Dispose() }
}
function Write-Entry($zip, [string]$name, [byte[]]$bytes) {
    $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}
function Get-ObjectBytes([string]$hash) {
    if (!$objects.ContainsKey($hash)) { throw "Missing replay-save object: $hash" }
    return ,$objects[$hash]
}
if ($Mode -eq 'Export') {
    if (!$StoreRoot -or !$ReplaySaveId) { throw 'Export requires StoreRoot and ReplaySaveId.' }
    if (Test-Path -LiteralPath $archive) { throw 'Output already exists; choose another archive path.' }
    $store = [HollowKnightTAS.Core.ReplaySave.ContentAddressedReplaySaveStore]::new([IO.Path]::GetFullPath($StoreRoot))
    $loaded = $store.Load($ReplaySaveId)
    if (!$loaded.Success -or !$loaded.Package) { throw "Source save validation failed: $($loaded.Error)" }
    $package = $loaded.Package
    # Automatic retention is never allowed to delete destination entries during import.
    if ($package.Descriptor.Reason.ToString() -eq 'AutomaticInterval') {
        throw 'Preserve this automatic save as a manual save before exporting it.'
    }
    $parent = [IO.Path]::GetDirectoryName($archive)
    if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw 'Output directory must already exist.' }
    $temporary = Join-Path $parent ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $file = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew)
        try {
            $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                Write-Entry $zip 'descriptor.json' ([HollowKnightTAS.Core.ReplaySave.ReplaySaveDescriptorCodec]::Serialize($package.Descriptor))
                foreach ($hash in ($package.Objects.Keys | Sort-Object)) {
                    Write-Entry $zip "objects/$hash" $package.Objects[$hash]
                }
            } finally { $zip.Dispose() }
        } finally { $file.Dispose() }
        if ((Get-Item -LiteralPath $temporary).Length -gt $maxArchive) { throw 'Archive exceeds size limit.' }
        [IO.File]::Move($temporary, $archive)
    } finally { if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) } }
    [pscustomobject]@{ status='Exported'; replaySaveId=$ReplaySaveId; archive=$archive; sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() } | ConvertTo-Json -Compress
    return
}
if (!(Test-Path -LiteralPath $archive -PathType Leaf) -or (Get-Item -LiteralPath $archive).Length -gt $maxArchive) {
    throw 'Archive is missing or exceeds size limit.'
}
$archiveHash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
if ($ExpectedArchiveSha256 -and $ExpectedArchiveSha256 -cne $archiveHash) { throw 'Archive SHA-256 mismatch.' }
$objects = [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$descriptorBytes = $null
$file = [IO.File]::OpenRead($archive)
try {
    $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if ($zip.Entries.Count -gt 20000) { throw 'Too many archive entries.' }
        [long]$total = 0
        foreach ($entry in $zip.Entries) {
            if (!$names.Add($entry.FullName)) { throw 'Duplicate archive entry.' }
            $total += $entry.Length
            if ($total -gt $maxTotal) { throw 'Expanded archive exceeds size limit.' }
            if ($entry.FullName -ceq 'descriptor.json') {
                $descriptorBytes = Read-BoundedEntry $entry 256KB
            } elseif ($entry.FullName -cmatch '^objects/([0-9a-f]{64})$') {
                $hash = $Matches[1]
                $bytes = Read-BoundedEntry $entry $maxObject
                if ([HollowKnightTAS.Core.Cryptography.Sha256Utility]::ComputeHex($bytes) -cne $hash) { throw 'Object SHA-256 mismatch.' }
                $objects.Add($hash, $bytes)
            } else { throw "Unsupported archive path: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
} finally { $file.Dispose() }
if (!$descriptorBytes) { throw 'Missing descriptor.' }
$descriptor = [HollowKnightTAS.Core.ReplaySave.ReplaySaveDescriptorCodec]::Deserialize($descriptorBytes)
if ($descriptor.Reason.ToString() -eq 'AutomaticInterval') { throw 'Automatic-retention archives cannot be imported.' }
$segments = [Collections.Generic.List[byte[]]]::new()
foreach ($hash in $descriptor.JournalSegmentObjectSha256s) { $segments.Add((Get-ObjectBytes $hash)) }
$commit = [HollowKnightTAS.Core.ReplaySave.ReplaySaveCommit]::Create(
    $descriptor.ReplaySaveId, $descriptor.Label, $descriptor.Reason,
    $descriptor.RequestedAtUtc, $descriptor.CreatedAtUtc, $descriptor.RequestedAtMovieTick, $descriptor.EffectiveMovieTick,
    (Get-ObjectBytes $descriptor.ManifestSha256), (Get-ObjectBytes $descriptor.BaselineObjectSha256),
    (Get-ObjectBytes $descriptor.MovieObjectSha256), $segments,
    (Get-ObjectBytes $descriptor.SemanticSnapshotSha256), (Get-ObjectBytes $descriptor.LedgerSummarySha256),
    $descriptor.SceneName, $descriptor.SceneEpoch, $descriptor.AutoRetentionCount)
if ($descriptor.LifecycleObjectSha256) {
    $log = [HollowKnightTAS.Core.ReplaySave.ReplayLifecycleLog]::Deserialize((Get-ObjectBytes $descriptor.LifecycleObjectSha256))
    $commit = $commit.WithLifecycle($log, $objects)
}
$canonical = [HollowKnightTAS.Core.ReplaySave.ReplaySaveDescriptorCodec]::Serialize($commit.Descriptor)
if ([Convert]::ToBase64String($canonical) -cne [Convert]::ToBase64String($descriptorBytes)) { throw 'Descriptor reconstruction mismatch.' }
if ($commit.Objects.Count -ne $objects.Count) { throw 'Archive contains unreferenced objects.' }
if ($Mode -eq 'Import') {
    if (!$StoreRoot) { throw 'Import requires an explicit destination StoreRoot.' }
    # Store transactions are single-process: do not race the running product.
    if (Get-Process hollow_knight,HollowKnightTAS.Companion -ErrorAction SilentlyContinue) {
        throw 'Close Hollow Knight and TAS Studio normally before importing. No destination was opened.'
    }
    $store = [HollowKnightTAS.Core.ReplaySave.ContentAddressedReplaySaveStore]::new([IO.Path]::GetFullPath($StoreRoot))
    $existing = $store.Load($descriptor.ReplaySaveId)
    if ($existing.Success) {
        $old = [HollowKnightTAS.Core.ReplaySave.ReplaySaveDescriptorCodec]::Serialize($existing.Package.Descriptor)
        if ([Convert]::ToBase64String($old) -cne [Convert]::ToBase64String($canonical)) { throw 'Existing save ID has different content; nothing overwritten.' }
        $status = 'AlreadyPresent'
    } else {
        $result = $store.Commit($commit)
        if (!$result.Success) { throw "Import failed without overwriting existing entries: $($result.Error)" }
        $status = 'Imported'
    }
} else { $status = 'Verified' }
[pscustomobject]@{status=$status;replaySaveId=$descriptor.ReplaySaveId;movieTick=$descriptor.EffectiveMovieTick;manifestSha256=$descriptor.ManifestSha256;archiveSha256=$archiveHash;objectCount=$objects.Count} | ConvertTo-Json -Compress

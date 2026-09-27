#requires -Version 7.2
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$version = '8.0.1'
$archiveHash = 'e2aaeaa0fdbc397d4794828086424d4aaa2102cef1fb6874f6ffd29c0b88b673'
$url = "https://github.com/GyanD/codexffmpeg/releases/download/$version/ffmpeg-$version-essentials_build.zip"
$cache = Join-Path $PSScriptRoot '../.local/ffmpeg'
$archive = Join-Path $cache "ffmpeg-$version-essentials_build.zip"
[IO.Directory]::CreateDirectory($cache) | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest $url -OutFile ($archive + '.download')
    if ((Get-FileHash -LiteralPath ($archive + '.download')).Hash.ToLowerInvariant() -ne $archiveHash) {
        throw 'FFmpeg download checksum mismatch.'
    }
    Move-Item -LiteralPath ($archive + '.download') -Destination $archive -Force
}
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $archiveHash) {
    throw "FFmpeg cache checksum mismatch: $archive"
}
# Extract only the executable and upstream provenance/license, never unrelated tools.
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($name in @('bin/ffmpeg.exe', 'LICENSE', 'README.txt')) {
        $entry = $zip.GetEntry("ffmpeg-$version-essentials_build/$name")
        if ($null -eq $entry) { throw "Missing FFmpeg archive entry: $name" }
        $target = Join-Path $Destination ([IO.Path]::GetFileName($name))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
@"
HollowKnightTAS bundles unmodified FFmpeg $version essentials by Gyan Doshi.
FFmpeg runs as a separate process only during video export.
License: GPL-3.0-or-later; see LICENSE and upstream README.txt.
Binary archive: $url
Archive SHA-256: $archiveHash
FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/894da5ca7d
Upstream build information and external library versions: README.txt
Upstream distribution: https://www.gyan.dev/ffmpeg/builds/
When redistributing, provide access to the corresponding source and build
materials for FFmpeg and its enabled dependencies under their licenses.
"@ | Set-Content -LiteralPath (Join-Path $Destination 'NOTICE.txt') -Encoding utf8
& (Join-Path $Destination 'ffmpeg.exe') -version | Set-Content -LiteralPath (Join-Path $Destination 'build-configuration.txt') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Bundled FFmpeg cannot execute on this machine.' }

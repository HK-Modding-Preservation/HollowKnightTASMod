[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TracePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$GameVersion = '1.5.78.11833',

    [string]$ApiVersion = '1.5.78.11833-77'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TracePath = [IO.Path]::GetFullPath($TracePath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $TracePath -PathType Leaf)) {
    throw "Reference trace is missing: $TracePath"
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Candidate movie already exists: $OutputPath"
}
$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$actions = @(
    @{ Bit = 1; Name = 'left' },
    @{ Bit = 2; Name = 'right' },
    @{ Bit = 4; Name = 'up' },
    @{ Bit = 8; Name = 'down' },
    @{ Bit = 16; Name = 'jump' },
    @{ Bit = 32; Name = 'attack' },
    @{ Bit = 64; Name = 'dash' },
    @{ Bit = 128; Name = 'cast' },
    @{ Bit = 256; Name = 'quickcast' },
    @{ Bit = 512; Name = 'superdash' },
    @{ Bit = 1024; Name = 'dreamnail' }
)
$allGameplay = 2047
$runs = [Collections.Generic.List[object]]::new()
$expectedTick = 0
$currentHeld = -1
$currentAxisX = 0
$currentAxisY = 0
$currentCount = 0

function Add-Run {
    if ($script:currentCount -le 0) {
        return
    }
    $script:runs.Add([pscustomobject]@{
        Held = $script:currentHeld
        AxisX = $script:currentAxisX
        AxisY = $script:currentAxisY
        Count = $script:currentCount
    })
}

foreach ($line in [IO.File]::ReadLines($TracePath)) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    $frame = $line | ConvertFrom-Json
    if ([int]$frame.logicalTick -ne $expectedTick) {
        throw (
            'Reference trace logical ticks are not contiguous: expected=' `
            + $expectedTick `
            + '; actual=' `
            + [int]$frame.logicalTick)
    }
    $fields = @{}
    foreach ($field in $frame.fields) {
        $fields[[string]$field.key] = [string]$field.displayValue
    }
    foreach ($required in @('input.held', 'input.axisX', 'input.axisY')) {
        if (-not $fields.ContainsKey($required)) {
            throw "Reference trace is missing required field: $required"
        }
    }
    $held = [int]$fields['input.held']
    if (($held -band (-bnot $allGameplay)) -ne 0) {
        throw "Reference trace contains unknown input bits at tick $expectedTick."
    }
    $axisX = [int]$fields['input.axisX']
    $axisY = [int]$fields['input.axisY']
    if (($held -band 3) -ne 0) {
        $axisX = 0
    }
    if (($held -band 12) -ne 0) {
        $axisY = 0
    }
    if ($currentCount -gt 0 `
            -and ($held -ne $currentHeld `
                -or $axisX -ne $currentAxisX `
                -or $axisY -ne $currentAxisY)) {
        Add-Run
        $currentCount = 0
    }
    if ($currentCount -eq 0) {
        $currentHeld = $held
        $currentAxisX = $axisX
        $currentAxisY = $axisY
    }
    $currentCount++
    $expectedTick++
}
Add-Run

if ($expectedTick -le 0) {
    throw 'Reference trace contains no frames.'
}
if (($runs | Measure-Object -Property Count -Sum).Sum -ne $expectedTick) {
    throw 'Generated movie runs do not cover the complete reference trace.'
}

$builder = [Text.StringBuilder]::new(4096)
[void]$builder.AppendLine('hktas 1')
[void]$builder.AppendLine("game $GameVersion")
[void]$builder.AppendLine("api $ApiVersion")
[void]$builder.AppendLine(
    'manifest-sha256 ' + ('0' * 64))
[void]$builder.AppendLine('baseline none none')
[void]$builder.AppendLine('tick-unit input')
[void]$builder.AppendLine('---')
[void]$builder.AppendLine(
    'marker "T24 input copied from committed no-TAS HeroActions"')
[void]$builder.AppendLine('assert scene.name == "GG_Workshop"')
[void]$builder.AppendLine('checkpoint seated-start')
foreach ($run in $runs) {
    $names = @(
        foreach ($action in $actions) {
            if (($run.Held -band $action.Bit) -ne 0) {
                $action.Name
            }
        }
    )
    $hold = if ($names.Count -eq 0) { '-' } else { $names -join ',' }
    [void]$builder.Append("frames $($run.Count) hold=$hold")
    if ($run.AxisX -ne 0 -or $run.AxisY -ne 0) {
        [void]$builder.Append(" x=$($run.AxisX) y=$($run.AxisY)")
    }
    [void]$builder.AppendLine()
}
[void]$builder.AppendLine('checkpoint regression-end')

[IO.File]::WriteAllText(
    $OutputPath,
    $builder.ToString().Replace("`r`n", "`n"),
    [Text.UTF8Encoding]::new($false, $true))

[pscustomobject]@{
    TracePath = $TracePath
    TraceSha256 = (Get-FileHash `
        -LiteralPath $TracePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    OutputPath = $OutputPath
    MovieSha256 = (Get-FileHash `
        -LiteralPath $OutputPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    ExpandedTicks = $expectedTick
    Runs = $runs.Count
}

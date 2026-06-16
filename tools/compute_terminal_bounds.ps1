$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'

function Get-MapPixels($gameX, $gameZ, $rotation, $transform) {
    $lat = $gameZ; $lng = $gameX
    if ($rotation -ne 0) {
        $angle = $rotation * [Math]::PI / 180.0
        $cos = [Math]::Cos($angle); $sin = [Math]::Sin($angle)
        $rotLng = $lng * $cos - $lat * $sin
        $rotLat = $lng * $sin + $lat * $cos
        $lat = $rotLat; $lng = $rotLng
    }
    $px = $transform[0] * $lng + $transform[1]
    $py = ($transform[2] * -1) * $lat + $transform[3]
    return @($px, $py)
}

function Get-Normalized($gameX, $gameZ, $bounds, $rotation, $transform) {
    $corners = @(
        @($bounds[0][0], $bounds[0][1]),
        @($bounds[1][0], $bounds[0][1]),
        @($bounds[0][0], $bounds[1][1]),
        @($bounds[1][0], $bounds[1][1])
    )
    $pxs = @(); $pys = @()
    foreach ($c in $corners) {
        $p = Get-MapPixels $c[0] $c[1] $rotation $transform
        $pxs += $p[0]; $pys += $p[1]
    }
    $minPx = ($pxs | Measure-Object -Minimum).Minimum
    $maxPx = ($pxs | Measure-Object -Maximum).Maximum
    $minPy = ($pys | Measure-Object -Minimum).Minimum
    $maxPy = ($pys | Measure-Object -Maximum).Maximum
    $p = Get-MapPixels $gameX $gameZ $rotation $transform
    $nx = ($p[0] - $minPx) / ($maxPx - $minPx)
    $ny = ($p[1] - $minPy) / ($maxPy - $minPy)
    return @($nx, $ny)
}

$q = '{ maps { normalizedName spawns { position { x z } } } }'
$t = ((Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body (@{query=$q}|ConvertTo-Json)).data.maps | Where-Object normalizedName -eq 'terminal')
$xs = $t.spawns | ForEach-Object { [double]$_.position.x }
$zs = $t.spawns | ForEach-Object { [double]$_.position.z }
$minX = ($xs | Measure-Object -Minimum).Minimum
$maxX = ($xs | Measure-Object -Maximum).Maximum
$minZ = ($zs | Measure-Object -Minimum).Minimum
$maxZ = ($zs | Measure-Object -Maximum).Maximum

$padX = 40; $padZ = 40
$newBounds = @(
    @([math]::Round($maxX + $padX, 0), [math]::Round($minZ - $padZ, 0)),
    @([math]::Round($minX - $padX, 0), [math]::Round($maxZ + $padZ, 0))
)

$transform = @(0.20, 0, 0.20, 0)
$rotation = 180

Write-Output "Computed Terminal bounds from spawns:"
Write-Output "  [[$($newBounds[0][0]), $($newBounds[0][1])], [$($newBounds[1][0]), $($newBounds[1][1])]]"

$sample = $t.spawns[0].position
$norm = Get-Normalized $sample.x $sample.z $newBounds $rotation $transform
Write-Output ("Sample spawn normalized: ({0:F3}, {1:F3})" -f $norm[0], $norm[1])

$oldBounds = @(@(463, -580), @(-433, 475))
$oldNorm = Get-Normalized $sample.x $sample.z $oldBounds $rotation $transform
Write-Output ("OLD bounds normalized: ({0:F3}, {1:F3})" -f $oldNorm[0], $oldNorm[1])

@{
    bounds = $newBounds
    transform = $transform
    coordinateRotation = $rotation
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $toolsDataDir 'terminal_bounds_computed.json') -Encoding utf8

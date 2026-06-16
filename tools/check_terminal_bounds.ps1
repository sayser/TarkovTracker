$ErrorActionPreference = 'Stop'
$q = @'
{
  maps {
    name
    normalizedName
  }
}
'@
# Check static maps.json from github we already have - bounds are [[463,-580],[-433,475]]

# Compute bounds from spawn extent with padding
$q2 = '{ maps { normalizedName spawns { position { x z } } } }'
$t = ((Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body (@{query=$q2}|ConvertTo-Json)).data.maps | Where-Object normalizedName -eq 'terminal')
$xs = $t.spawns | ForEach-Object { [double]$_.position.x }
$zs = $t.spawns | ForEach-Object { [double]$_.position.z }
$minX = ($xs | Measure-Object -Minimum).Minimum
$maxX = ($xs | Measure-Object -Maximum).Maximum
$minZ = ($zs | Measure-Object -Minimum).Minimum
$maxZ = ($zs | Measure-Object -Maximum).Maximum
Write-Output "Spawn extents: X [$minX, $maxX], Z [$minZ, $maxZ]"

# Test transform: tarkov.dev uses transform [0.20, 0, 0.20, 0] at zoom 0
# Leaflet: px = 2^0 * (0.20 * rotLng + 0), py = 2^0 * (-0.20 * rotLat + 0)
# With rotation 180: rotLat = -lat, rotLng = -lng (need to verify rotation formula)

function Get-Pixels($x, $z, $rotation) {
    $lat = $x; $lng = $z
    if ($rotation -ne 0) {
        $angle = $rotation * [Math]::PI / 180.0
        $cos = [Math]::Cos($angle); $sin = [Math]::Sin($angle)
        $rotLng = $lng * $cos - $lat * $sin
        $rotLat = $lng * $sin + $lat * $cos
        $lat = $rotLat; $lng = $rotLng
    }
    $px = 0.20 * $lng
    $py = -0.20 * $lat
    return @($px, $py)
}

$oldBounds = @(@(463, -580), @(-433, 475))
Write-Output ''
Write-Output 'Pixel extents with CURRENT bounds (tarkov.dev):'
foreach ($corner in @(@(463,-580), @(-433,-580), @(463,475), @(-433,475))) {
    $p = Get-Pixels $corner[0] $corner[1] 180
    Write-Output ("  game ({0},{1}) -> px ({2:F1},{3:F1})" -f $corner[0],$corner[1],$p[0],$p[1])
}

$sampleSpawn = $t.spawns[0].position
$pSpawn = Get-Pixels $sampleSpawn.x $sampleSpawn.z 180
Write-Output ("Sample spawn ({0},{1}) -> px ({2:F1},{3:F1})" -f $sampleSpawn.x,$sampleSpawn.z,$pSpawn[0],$pSpawn[1])

# Proposed bounds from spawn data
$padX = 50; $padZ = 50
$newBounds = @(@($maxX + $padX, $minZ - $padZ), @($minX - $padX, $maxZ + $padZ))
Write-Output ''
Write-Output "Proposed bounds from spawns: [[$($newBounds[0][0]), $($newBounds[0][1])], [$($newBounds[1][0]), $($newBounds[1][1])]]"

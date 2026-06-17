# Refreshes boss metadata and spawn data from tarkov.dev (matches tarkov.dev boss markers).
# Usage: powershell -ExecutionPolicy Bypass -File Tools\fetch-boss-data.ps1

$ErrorActionPreference = 'Stop'
$configDir = Join-Path $PSScriptRoot '..\Config'

$query = @'
{
  maps {
    normalizedName
    name
    bosses {
      boss { name normalizedName }
      spawnLocations { name spawnKey chance }
    }
    spawns {
      zoneName
      sides
      categories
      position { x y z }
    }
  }
}
'@

$body = @{ query = $query } | ConvertTo-Json
$response = Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body

# Map API normalizedName -> app config key (maps.json keys / NormalizeMapName(locale.en))
$apiToAppKey = @{
    'factory'            = 'factory'
    'night-factory'      = 'factory'
    'customs'            = 'customs'
    'woods'              = 'woods'
    'lighthouse'         = 'lighthouse'
    'shoreline'          = 'shoreline'
    'reserve'            = 'reserve'
    'interchange'        = 'interchange'
    'streets-of-tarkov'  = 'streetsoftarkov'
    'the-lab'            = 'thelab'
    'ground-zero-21'     = 'groundzero'
    'ground-zero'        = 'groundzero'
    'terminal'           = 'terminal'
    'the-labyrinth'      = 'labyrinth'
}

$bossMarkers = @{}
$spawnsByAppKey = @{}
$bossSpawnCounts = @{}

$appDisplayNames = @{
    'factory'           = 'Factory'
    'customs'           = 'Customs'
    'woods'             = 'Woods'
    'lighthouse'        = 'Lighthouse'
    'shoreline'         = 'Shoreline'
    'reserve'           = 'Reserve'
    'interchange'       = 'Interchange'
    'streetsoftarkov'   = 'Streets of Tarkov'
    'thelab'            = 'The Lab'
    'groundzero'        = 'Ground Zero 21+'
    'terminal'          = 'Terminal'
    'labyrinth'         = 'The Labyrinth'
}

foreach ($map in $response.data.maps) {
    $appKey = $apiToAppKey[$map.normalizedName]
    if (-not $appKey) { continue }

    if (-not $bossMarkers.ContainsKey($appKey)) {
        $bossMarkers[$appKey] = @()
    }

    foreach ($boss in $map.bosses) {
        foreach ($loc in $boss.spawnLocations) {
            $bossMarkers[$appKey] += [PSCustomObject]@{
                bossName       = $boss.boss.name
                normalizedName = $boss.boss.normalizedName
                locationName   = $loc.name
                zoneName       = $loc.spawnKey
                spawnChance    = [double]$loc.chance
            }
        }
    }

    if (-not $spawnsByAppKey.ContainsKey($appKey)) {
        $spawnsByAppKey[$appKey] = @()
    }

    foreach ($spawn in $map.spawns) {
        $spawnsByAppKey[$appKey] += [PSCustomObject]@{
            zoneName   = $spawn.zoneName
            sides      = @($spawn.sides)
            categories = @($spawn.categories)
            position   = [PSCustomObject]@{
                x = [double]$spawn.position.x
                y = [double]$spawn.position.y
                z = [double]$spawn.position.z
            }
        }
    }

    $bossCount = @($map.spawns | Where-Object { $_.categories -contains 'boss' }).Count
    if ($bossCount -gt 0) {
        if (-not $bossSpawnCounts.ContainsKey($appKey)) {
            $bossSpawnCounts[$appKey] = 0
        }
        $bossSpawnCounts[$appKey] += $bossCount
    }
}

function Get-SpawnDedupKey($spawn) {
    $x = [math]::Round([double]$spawn.position.x, 2)
    $y = [math]::Round([double]$spawn.position.y, 2)
    $z = [math]::Round([double]$spawn.position.z, 2)
    return ('{0}|{1}|{2}|{3}' -f $spawn.zoneName, $x, $y, $z)
}

$spawnsRoot = @{ data = @{ maps = @() } }

foreach ($appKey in ($spawnsByAppKey.Keys | Sort-Object)) {
    $deduped = @()
    $seen = @{}

    foreach ($spawn in $spawnsByAppKey[$appKey]) {
        $key = Get-SpawnDedupKey $spawn
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $deduped += $spawn
    }

    $displayName = if ($appDisplayNames.ContainsKey($appKey)) { $appDisplayNames[$appKey] } else { $appKey }
    $spawnsRoot.data.maps += [PSCustomObject]@{
        name   = $displayName
        spawns = $deduped
    }
}

# Keep cultist-priest on factory (Night Factory only in-game, shown on our single Factory map).

$bossMarkers | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $configDir 'tarkov_boss_spawn_markers.json') -Encoding UTF8
$spawnsRoot | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $configDir 'tarkov_spawns_raw.json') -Encoding UTF8

Write-Output 'Updated Config/tarkov_boss_spawn_markers.json and Config/tarkov_spawns_raw.json'
Write-Output ''
Write-Output 'Boss spawn points per map (should match tarkov.dev boss toggle):'
$bossSpawnCounts.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Output ('  {0}: {1}' -f $_.Name, $_.Value)
}

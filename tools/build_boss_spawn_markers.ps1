$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'
$spawnsPath = Join-Path $configDir 'tarkov_spawns_raw.json'
$outputPath = Join-Path $configDir 'tarkov_boss_spawn_markers.json'

function Normalize-MapName([string]$name) {
    return -join ($name.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
}

function Resolve-MapKey([string]$mapName) {
    switch (Normalize-MapName $mapName) {
        'nightfactory' { return 'factory' }
        'groundzero21' { return 'groundzero' }
        'lab' { return 'thelab' }
        'streetsoftarkov' { return 'streetsoftarkov' }
        'thelabyrinth' { return 'thelabyrinth' }
        default { return Normalize-MapName $mapName }
    }
}

$query = @'
{
  maps {
    name
    bosses {
      boss { name normalizedName }
      spawnLocations { spawnKey name chance }
    }
  }
}
'@

$body = @{ query = $query } | ConvertTo-Json
$api = Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body
if ($api.errors) { $api.errors | ConvertTo-Json; exit 1 }

$spawnsRoot = Get-Content $spawnsPath -Raw | ConvertFrom-Json
$spawnsByMap = @{}
foreach ($map in $spawnsRoot.data.maps) {
    $key = Resolve-MapKey $map.name
    if (-not $spawnsByMap.ContainsKey($key)) {
        $spawnsByMap[$key] = @()
    }
    $spawnsByMap[$key] += $map.spawns
}

$appMaps = @('factory','customs','woods','shoreline','interchange','thelab','reserve','lighthouse','streetsoftarkov','groundzero','terminal','thelabyrinth')
$byMap = @{}

foreach ($map in $api.data.maps) {
    $mapKey = Resolve-MapKey $map.name
    if ($appMaps -notcontains $mapKey) { continue }
    if (-not $map.bosses) { continue }

    $mapSpawns = if ($spawnsByMap.ContainsKey($mapKey)) { $spawnsByMap[$mapKey] } else { @() }

    foreach ($bossEntry in $map.bosses) {
        if (-not $bossEntry.spawnLocations) { continue }
        foreach ($loc in $bossEntry.spawnLocations) {
            $spawnKey = $loc.spawnKey
            $match = $mapSpawns | Where-Object {
                $_.zoneName -eq $spawnKey -and $_.categories -contains 'boss'
            } | Select-Object -First 1

            if (-not $match) {
                $match = $mapSpawns | Where-Object { $_.zoneName -eq $spawnKey } | Select-Object -First 1
            }

            if (-not $match -or -not $match.position) {
                Write-Warning "No spawn position for $($map.name) / $($bossEntry.boss.name) / $spawnKey"
                continue
            }

            $marker = [ordered]@{
                bossName      = $bossEntry.boss.name
                normalizedName = $bossEntry.boss.normalizedName
                locationName  = $loc.name
                zoneName      = $spawnKey
                spawnChance   = [double]$loc.chance
                x             = [double]$match.position.x
                y             = [double]$match.position.y
                z             = [double]$match.position.z
            }

            if (-not $byMap.ContainsKey($mapKey)) {
                $byMap[$mapKey] = New-Object System.Collections.Generic.List[object]
            }
            $byMap[$mapKey].Add($marker)
        }
    }
}

$sorted = [ordered]@{}
foreach ($key in ($byMap.Keys | Sort-Object)) {
    $sorted[$key] = @($byMap[$key] | Sort-Object bossName, locationName, zoneName)
}

$sorted | ConvertTo-Json -Depth 5 | Set-Content $outputPath -Encoding utf8

Write-Output 'Boss spawn markers built:'
foreach ($key in ($sorted.Keys | Sort-Object)) {
    Write-Output ("  {0}: {1}" -f $key, $sorted[$key].Count)
}
Write-Output ("Total: {0}" -f (($sorted.Values | ForEach-Object { $_.Count }) | Measure-Object -Sum).Sum)
